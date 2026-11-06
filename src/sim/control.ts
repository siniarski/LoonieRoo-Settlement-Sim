// Direct control (the control prototype): the player takes over one survivor.
//
// While controlled, a survivor does nothing on their own: no jobs, meals, sleep
// or leisure. Their needs still drain, zombies still go for them, and they still
// bleed and heal. Input arrives as commands like everything else (rule 5):
//   steer   the held direction (sent when the keys change, not every frame)
//   swing   attack towards a tile: the zombie in front, else a shot with a loaded gun
//   work    repair, barricade, reinforce or build at the tile in front (handiwork.ts)
//   use     pick up, put on, eat, search, treat yourself (medicine, a bandage), drink (a well,
//           a rain collector, the river) or
//           sleep (a bed; your own tile with nothing on it is the floor) at a tile next to you
// Movement is free (motion.ts): the held direction moves your centre a little each tick, at
// your tile pace (roads, a bad leg...), the same speed any way you go, sliding along walls.
// Your tile (pos) is whatever tile you're over.
// Searching, eating, drinking and sleeping take time; moving or swinging interrupts them,
// and so does being hurt while asleep.

import { getTile, isOpen } from './grid';
import { HUNGRY } from './food';
import { ENTER_REACH, VEHICLE_RADIUS, drive, leaveVehicle, useVehicle, vehicleOf, vehiclesOf } from './vehicles';
import { maybeAlarm } from './timeline';
import { EDGE_WINDOW, edgeBetween } from './grid';
import { smashGlass } from './breach';
import { fitsInPack, stow, unpack } from './inventory';
import { toggleEdgeDoor } from './doors';
import { toggleDoor } from './commands';
import { RUN_PCT, RUN_TICK_COST, SWING_COST, WINDED_DAMAGE_PCT, WINDED_WAIT, exert, isWinded, panicMiss } from './body';
import { canForage, FORAGE_TICKS, finishBuild, finishForage, hitDeer, repairTick, startWork } from './handiwork';
import { TIRED } from './rest';
import { isNight, slowTick } from './time';
import { wakeComfort } from './wellbeing';
import { boostDamage, swingWait } from './moodlets';
import { freezes } from './resolve';
import { addThought } from './mood';
import { hitResident, meetResident, residentAt, type Resident } from './residents';
import { logEvent } from './events';
import { EAT_TICKS, HOT_MEAL_VALUE, MEAL_VALUE, SATIETY_MAX } from './food';
import { furnitureAt, getFurniture, type Furniture, setScavenge } from './furniture';
import { DRINK_TICKS, HYDRATION_MAX, sourceAt } from './water';
import { BED_REGEN, FLOOR_REGEN, REST_DRAIN, REST_MAX } from './rest';
import { samePoint } from './space';
import { applyRemedy, remediesFor } from './medicine';
import { TREAT_TICKS } from './health';
import type { Point } from './grid';
import { dropItem, getItem, itemAt, removeItem, type Item } from './items';
import { spawnFind } from './difficulty';
import { GUARD_ATTACK_EVERY, GUARD_DAMAGE, SCAVENGE_WORK, clearShot, scavengeFind } from './jobs';
import { makeNoise } from './noise';
import { LIGHT_DAMAGE_PCT, inLight } from './power';
import { random } from './rng';
import { GUARD_DAMAGE_PER_LEVEL, HARDENED_DAMAGE, TOOL_FOR, hasTrait, skillLevel } from './skills';
import { SLOT_OF, isGarment } from './clothing';
import type { SimState } from './state';
import { enterTile, getSurvivor, stepPace, type Survivor } from './survivors';
import { REACH, SUB, SWING_REACH, centre, distance2, fits, followTile, inReach, inWindowFrame, nearestInArc, slide, tileOf, toward } from './motion';
import { GUN_DAMAGE, GUN_EVERY, GUN_RANGE, blowDamage, loadedGun, wearWeapon } from './weapons';
import { damageZombie, knockBack, type Zombie } from './zombies';

export interface Control {
  survivorId: string;
  /** Held direction, each -1, 0 or 1. */
  dx: number;
  dy: number;
  /** Last direction moved or swung (never 0, 0), one of eight: the tile in front is the one you use or work. */
  facing: Point;
  /** Where you're looking (the mouse), each part within AIM_MAX; swings land in an arc round it. Unset: facing. */
  aim?: Point;
  /** No swing or shot before this tick. */
  nextSwing: number;
  /** Tick of the last swing, -1 if none (the renderer draws its arc). */
  swungTick?: number;
  /** Holding Shift: a tick quicker a tile, for endurance (body.ts). */
  running?: boolean;
  /** Sneaking (C toggles): half pace, no running, hard to see and hear (sight.ts). */
  sneaking?: boolean;
  /** A timed action in progress: searching, eating, drinking, sleeping, treating, building, repairing, foraging. */
  busy: { what: 'search' | 'eat' | 'drink' | 'sleep' | 'treat' | 'build' | 'repair' | 'forage'; targetId: string; progress: number; duration: number } | null;
}

/** You drink from a tap you use only if you're this far from full (else you search the counter). */
export const TAP_THIRST = 200;

/** Sneaking: this much of your pace (percent). */
export const SNEAK_PCT = 50;
/** A blow to one of the dead that isn't after you (sneaking up, or it's busy with someone else) does this much (percent). */
export const SNEAK_ATTACK_PCT = 300;

/** Each zombie within reach slows you this much (percent), to no less than CLUTCHED_MIN. */
export const CLUTCH_PCT = 20;
export const CLUTCHED_MIN = 40;

/** How many of the dead are within reach of someone, clutching at them (not staggered ones). */
export function clutchedBy(state: SimState, s: Survivor): number {
  let n = 0;
  for (const z of state.zombies) if (z.kind !== 'raider' && state.tick >= z.staggerUntil && inReach(s, z, REACH, state.grid)) n++;
  return n;
}

/** The largest part of an aim direction (a circle of radius AIM_MAX, in 32 steps from the UI). */
export const AIM_MAX = 64;

/** Look this way: sets the aim, and the eight-way facing nearest it. Returns null if changed, 'same', or why not. */
export function aim(c: Control, x: number, y: number): string | null {
  if (!Number.isInteger(x) || !Number.isInteger(y) || Math.abs(x) > AIM_MAX || Math.abs(y) > AIM_MAX || (x === 0 && y === 0)) return 'aim takes a direction';
  if (c.aim && c.aim.x === x && c.aim.y === y) return 'same';
  c.aim = { x, y };
  // Eight ways: a part counts if it's at least tan(22.5 deg) of the larger (5 / 12 is close, in integers).
  const big = Math.max(Math.abs(x), Math.abs(y));
  c.facing = { x: 12 * Math.abs(x) >= 5 * big ? Math.sign(x) : 0, y: 12 * Math.abs(y) >= 5 * big ? Math.sign(y) : 0 };
  return null;
}

/** Which way a swing goes: the aim, else the facing. */
function swingDir(c: Control): Point {
  return c.aim ?? c.facing;
}

export function controlled(state: SimState): Survivor | undefined {
  return state.control ? getSurvivor(state, state.control.survivorId) : undefined;
}

export function isControlled(state: SimState, s: Survivor): boolean {
  return state.control?.survivorId === s.id;
}

/** Called once a tick for the controlled survivor, in place of their own behaviour. */
export function updateControlled(state: SimState, s: Survivor): void {
  const c = state.control!;
  // Behind the wheel, the keys are the pedals and the wheel (vehicles.ts).
  if (vehicleOf(state, s)) {
    c.busy = null;
    drive(state, s, c.dx, c.dy);
    return;
  }
  s.move = null;
  followTile(s);
  if (c.dx !== 0 || c.dy !== 0) {
    c.busy = null;
    // Aiming with the mouse, you walk one way and look another; else you face where you walk.
    if (!c.aim) c.facing = { x: c.dx, y: c.dy };
    const run = c.running === true && !c.sneaking && !isWinded(s);
    if (run) exert(s, RUN_TICK_COST);
    // The dead within reach drag at you: each takes CLUTCH_PCT off, down to CLUTCHED_MIN.
    // Climbing through a window frame: two fifths of your pace (and no running).
    const climb = inWindowFrame(state.grid, s.at);
    const pct = Math.max(CLUTCHED_MIN, 100 - CLUTCH_PCT * clutchedBy(state, s)) * (run && !climb ? RUN_PCT : 100) * (climb ? 2 : 5) / 5 * (c.sneaking ? SNEAK_PCT : 100) / 100;
    const walk = Math.round(SUB / stepPace(state, s, s.pos));
    const v = toward(c.dx, c.dy, Math.round((walk * pct) / 10000));
    // Caught in a closed doorway (taken over mid-step, say): walk out as a colonist would.
    s.at = slide(state.grid, s.at, v.x, v.y, fits(state.grid, s.at, 'free') ? 'free' : 'survivor');
    const tile = tileOf(s.at);
    if (tile.x !== s.pos.x || tile.y !== s.pos.y) {
      s.pos = tile;
      enterTile(state, s);
    }
    return;
  }
  if (c.busy) updateBusy(state, s, c);
}

function updateBusy(state: SimState, s: Survivor, c: Control): void {
  const busy = c.busy!;
  if (busy.what === 'sleep') return sleepTick(state, s, c, busy);
  if (!slowTick(state.tick)) return;
  busy.progress += 1;
  if (busy.what === 'repair') {
    if (!repairTick(state, busy.targetId)) return;
    c.busy = null;
    s.note = 'repaired it';
    return;
  }
  if (busy.progress < busy.duration) return;
  c.busy = null;
  if (busy.what === 'build') {
    s.note = finishBuild(state, s, busy.targetId);
    return;
  }
  if (busy.what === 'forage') {
    s.note = finishForage(state, s);
    return;
  }
  if (busy.what === 'eat') {
    const item = getItem(state, busy.targetId);
    if (!item || (item.carriedBy !== s.id && !item.pos)) return;
    s.satiety = Math.min(SATIETY_MAX, s.satiety + (item.kind === 'meal' ? HOT_MEAL_VALUE : MEAL_VALUE));
    if (s.carrying === item.id) s.carrying = null;
    unpack(s, item.id);
    removeItem(state, item);
    s.note = `ate ${item.kind === 'meal' ? 'a hot meal' : 'some food'}`;
    return;
  }
  if (busy.what === 'treat') {
    const item = getItem(state, busy.targetId);
    if (!item || (item.carriedBy !== s.id && !item.pos)) return;
    applyRemedy(s, item.kind);
    if (s.carrying === item.id) s.carrying = null;
    unpack(s, item.id);
    removeItem(state, item);
    s.note = item.kind === 'medicine' ? 'took the medicine' : 'bandaged up';
    return;
  }
  if (busy.what === 'drink') {
    const [x, y] = busy.targetId.split(',').map(Number) as [number, number];
    const source = sourceAt(state, x, y);
    if (!source) return;
    s.hydration = HYDRATION_MAX;
    if (source === 'collector') furnitureAt(state, x, y)!.loot -= 1;
    s.note = 'had a drink';
    return;
  }
  const crate = getFurniture(state, busy.targetId);
  if (!crate || crate.loot <= 0 || !nextTo(s.pos, crate)) return;
  if (crate.kind === 'tree') {
    spawnFind(state, 'wood', s.pos.x, s.pos.y);
    crate.loot -= 1;
    s.note = 'chopped some wood';
    return;
  }
  // A container: everything left in it turns up at once, and stays in it to take (inventory.ts).
  const found: string[] = [];
  while (crate.loot > 0) {
    const item = spawnFind(state, scavengeFind(random(state), crate.table), s.pos.x, s.pos.y);
    item.pos = null;
    item.inside = crate.id;
    crate.loot -= 1;
    found.push(item.kind);
  }
  setScavenge(state, crate, false);
  s.note = found.length ? `found ${found.join(', ')}` : 'nothing in it';
}

/** Asleep: rest comes back (faster in a bed) until full, or until something hurts you. */
function sleepTick(state: SimState, s: Survivor, c: Control, busy: NonNullable<Control['busy']>): void {
  if (s.hurtTick === state.tick) {
    c.busy = null;
    s.note = 'woken by an attack!';
    return;
  }
  if (!slowTick(state.tick)) return;
  // updateRest still drains a tick's worth; this makes up for it.
  s.rest = Math.min(REST_MAX, s.rest + REST_DRAIN + (busy.targetId === 'floor' ? FLOOR_REGEN : BED_REGEN));
  busy.progress = s.rest;
  if (s.rest < REST_MAX) return;
  c.busy = null;
  addThought(state, s, busy.targetId === 'floor' ? 'sleptOnFloor' : 'sleptInBed');
  if (busy.targetId !== 'floor') addThought(state, s, 'wellRested');
  wakeComfort(state, s, busy.targetId === 'floor' ? null : busy.targetId);
  s.note = 'woke up rested';
}

/** Within one tile, diagonals included. */
function nextTo(a: Point, b: Point): boolean {
  return Math.max(Math.abs(a.x - b.x), Math.abs(a.y - b.y)) <= 1;
}

/** The facing direction (a step of -1..1 each way) from a tile towards another. */
function towards(from: Point, to: Point): Point | null {
  const dx = Math.sign(to.x - from.x);
  const dy = Math.sign(to.y - from.y);
  return dx === 0 && dy === 0 ? null : { x: dx, y: dy };
}

/** Swing (or shoot) towards a tile. Returns what happened, or null if nothing could be done. */
export function swing(state: SimState, s: Survivor, target: Point): string | null {
  const c = state.control!;
  if (state.tick < c.nextSwing || s.move || vehicleOf(state, s)) return null;
  const face = towards(s.pos, target);
  if (face) c.facing = face;
  c.busy = null;
  const near = zombieToHit(state, s.at, swingDir(c));
  const wait = swingWait(s, GUARD_ATTACK_EVERY) + (isWinded(s) ? WINDED_WAIT : 0);
  if (near && freezes(state, s)) {
    c.nextSwing = state.tick + wait;
    return 'froze';
  }
  if (near && panicMiss(state, s)) {
    c.nextSwing = state.tick + wait;
    c.swungTick = state.tick;
    exert(s, SWING_COST);
    return 'missed: your hands are shaking';
  }
  if (near) {
    c.nextSwing = state.tick + wait;
    c.swungTick = state.tick;
    exert(s, SWING_COST);
    makeNoise(state, s.pos, 'fighting');
    const full = boostDamage(s, blowDamage(s, GUARD_DAMAGE) + GUARD_DAMAGE_PER_LEVEL * skillLevel(s, 'guard') + (hasTrait(s, 'hardened') ? HARDENED_DAMAGE : 0));
    const winded = isWinded(s) ? Math.round((full * WINDED_DAMAGE_PCT) / 100) : full;
    // It never saw you coming: a sneak attack.
    const unaware = near.kind !== 'raider' && near.targetId !== s.id;
    const blow = unaware ? Math.round((winded * SNEAK_ATTACK_PCT) / 100) : winded;
    const killed = damageZombie(state, near, inLight(state, s.pos) ? Math.round((blow * LIGHT_DAMAGE_PCT) / 100) : blow, s);
    if (killed) wearWeapon(state, s);
    else knockBack(state, near, s.at);
    return killed ? (unaware ? 'killed it before it knew you were there' : 'killed it') : unaware ? 'hit it from behind' : 'hit it';
  }
  const person = residentToHit(state, s.at, swingDir(c));
  if (person) {
    c.nextSwing = state.tick + GUARD_ATTACK_EVERY;
    c.swungTick = state.tick;
    makeNoise(state, s.pos, 'fighting');
    const blow = blowDamage(s, GUARD_DAMAGE) + GUARD_DAMAGE_PER_LEVEL * skillLevel(s, 'guard');
    return hitResident(state, person, blow, s) ? `killed ${person.name}` : `hit ${person.name}`;
  }
  // A deer next to you, or one you're aiming at with a loaded gun (handiwork.ts).
  const deer = nearestInArc(state.animals, s.at, swingDir(c), SWING_REACH, state.grid)
    ?? (loadedGun(state, s) ? state.animals.find((a) => a.pos.x === target.x && a.pos.y === target.y && Math.abs(a.pos.x - s.pos.x) + Math.abs(a.pos.y - s.pos.y) <= GUN_RANGE && clearShot(state.grid, s.pos, a.pos)) : undefined);
  if (deer) {
    const shot = !inReach(deer, s, SWING_REACH);
    c.nextSwing = state.tick + (shot ? GUN_EVERY : swingWait(s, GUARD_ATTACK_EVERY));
    if (shot) {
      loadedGun(state, s)!.wear -= 1;
      makeNoise(state, s.pos, 'gunfire');
    }
    return hitDeer(state, s, deer.id);
  }
  const gun = loadedGun(state, s);
  const shotAt = gun ? state.zombies.find((z) => z.pos.x === target.x && z.pos.y === target.y) : undefined;
  if (gun && shotAt && Math.abs(shotAt.pos.x - s.pos.x) + Math.abs(shotAt.pos.y - s.pos.y) <= GUN_RANGE && clearShot(state.grid, s.pos, shotAt.pos)) {
    const blocking = inTheWay(state, s, shotAt.pos);
    if (blocking) return `held fire: ${blocking.name} is in the way`;
    c.nextSwing = state.tick + GUN_EVERY;
    gun.wear -= 1;
    makeNoise(state, s.pos, 'gunfire');
    const dmg = boostDamage(s, GUN_DAMAGE + GUARD_DAMAGE_PER_LEVEL * skillLevel(s, 'guard') + (hasTrait(s, 'hardened') ? HARDENED_DAMAGE : 0));
    return damageZombie(state, shotAt, dmg, s) ? 'shot it dead' : 'shot it';
  }
  // A swing at nothing still takes the time, and the breath.
  c.nextSwing = state.tick + wait;
  c.swungTick = state.tick;
  exert(s, SWING_COST);
  return 'swung at nothing';
}

/** A resident (residents.ts) in the arc in front of you, within reach. */
function residentToHit(state: SimState, from: Point, facing: Point): Resident | undefined {
  return nearestInArc(state.residents, from, facing, SWING_REACH, state.grid);
}

/** Someone of yours standing between you and a tile (Bresenham, like clearShot). */
function inTheWay(state: SimState, s: Survivor, to: Point): Survivor | undefined {
  let { x, y } = s.pos;
  const dx = Math.abs(to.x - x);
  const dy = -Math.abs(to.y - y);
  const sx = x < to.x ? 1 : -1;
  const sy = y < to.y ? 1 : -1;
  let err = dx + dy;
  for (;;) {
    const e2 = 2 * err;
    if (e2 >= dy) {
      err += dy;
      x += sx;
    }
    if (e2 <= dx) {
      err += dx;
      y += sy;
    }
    if (x === to.x && y === to.y) return undefined;
    const o = state.survivors.find((v) => v !== s && v.pos.x === x && v.pos.y === y);
    if (o) return o;
  }
}

/** The zombie in front, else any zombie next to you (straight or diagonal), in a fixed order. */
/** The zombie a swing lands on: the nearest in the arc in front of you, within reach (motion.ts). */
function zombieToHit(state: SimState, from: Point, facing: Point): Zombie | undefined {
  return nearestInArc(state.zombies, from, facing, SWING_REACH, state.grid);
}

/** Use what's on a tile next to (or under) you. Returns what happened, or null if there was nothing to do. */
export function use(state: SimState, s: Survivor, target: Point): string | null {
  const c = state.control!;
  // In a car, using anything gets you out.
  if (vehicleOf(state, s)) return leaveVehicle(state, s);
  if (s.move || !nextTo(s.pos, target)) return null;
  // A car there: fill it up, or get in.
  const car = vehiclesOf(state).find((v) => distance2(v.at, centre(target)) <= (VEHICLE_RADIUS + SUB / 2) ** 2 && distance2(v.at, s.at) <= ENTER_REACH ** 2);
  if (car) {
    c.busy = null;
    return useVehicle(state, s, car);
  }
  const face = towards(s.pos, target);
  if (face) c.facing = face;
  const person = residentAt(state, target);
  if (person) return meetResident(state, s, person) ?? `talked with ${person.name}`;
  // An edge door between you and the tile in front.
  const edgeDoor = toggleEdgeDoor(state, s.pos, target);
  if (edgeDoor) return edgeDoor;
  // Glass between you and it: smash it (loud: breach.ts smashGlass).
  const pane = edgeBetween(state.grid, s.pos.x, s.pos.y, target.x, target.y);
  if (pane >= 0 && state.grid.edges[pane] === EDGE_WINDOW && !state.grid.edgeBoarded.includes(pane)) {
    smashGlass(state, pane);
    maybeAlarm(state, target, pane);
    return 'smashed the window';
  }
  if (getTile(state.grid, target.x, target.y) === 'door') {
    const was = isOpen(state.grid, target.x, target.y);
    return toggleDoor(state, target.x, target.y) ?? (was ? 'closed the door' : 'opened the door');
  }
  const item = itemAt(state, target.x, target.y);
  // A remedy you need is used, not picked up: on the ground next to you, or the one you carry (use your own tile).
  const carriedRemedy = s.carrying && samePoint(target, s.pos) ? getItem(state, s.carrying) : undefined;
  const remedy = [item, carriedRemedy].find((i) => i && !i.reservedBy && remediesFor(s).includes(i.kind));
  if (remedy) {
    c.busy = { what: 'treat', targetId: remedy.id, progress: 0, duration: TREAT_TICKS };
    return remedy.kind === 'medicine' ? 'taking medicine' : 'bandaging';
  }
  if (item && !item.reservedBy) return take(state, s, c, item);
  // A tap (timeline.ts) with the water on: a drink, if you could do with one; else it's a counter to search.
  if (sourceAt(state, target.x, target.y) === 'tap' && s.hydration < HYDRATION_MAX - TAP_THIRST) {
    c.busy = { what: 'drink', targetId: `${target.x},${target.y}`, progress: 0, duration: DRINK_TICKS };
    return 'drinking from the tap';
  }
  const crate = state.furniture.find((f) => f.x === target.x && f.y === target.y && (f.kind === 'crate' || f.kind === 'tree'));
  if (crate) return search(s, c, crate);
  const source = sourceAt(state, target.x, target.y);
  if (source) {
    c.busy = { what: 'drink', targetId: `${target.x},${target.y}`, progress: 0, duration: DRINK_TICKS };
    return 'drinking';
  }
  const bed = furnitureAt(state, target.x, target.y);
  if (bed?.kind === 'bed' && samePoint(bed, s.pos)) return lieDown(s, c, bed.id);
  if (bed?.kind === 'bed') {
    // Climb in first: one step onto it, then use it again (or E) to sleep.
    s.pos = { x: bed.x, y: bed.y };
    s.at = centre(s.pos);
    return 'getting into bed';
  }
  // Nothing there: put down what you're carrying.
  const carried = s.carrying ? getItem(state, s.carrying) : undefined;
  if (carried) {
    s.carrying = null;
    dropItem(state, carried, target);
    return `put down the ${carried.kind}`;
  }
  // Nothing at all underfoot: sleep rough at night or if tired (unless you're hungry and there's
  // foraging to be done here: hunger first, by day), else forage if you can.
  if (samePoint(target, s.pos)) {
    const why = canForage(state, s);
    if (isNight(state.tick) || (s.rest < TIRED && (why !== null || s.satiety >= HUNGRY))) return lieDown(s, c, 'floor');
    if (why) return why;
    c.busy = { what: 'forage', targetId: 'here', progress: 0, duration: FORAGE_TICKS };
    return 'foraging';
  }
  return null;
}

/** Work the tile in front: repair, barricade, reinforce or build (handiwork.ts). Returns what happened, or why not (null: no). */
export function work(state: SimState, s: Survivor, target: Point): string {
  const c = state.control!;
  if (s.move) return 'not while moving';
  if (!nextTo(s.pos, target)) return 'too far to reach';
  const face = towards(s.pos, target);
  if (face) c.facing = face;
  const job = startWork(state, s, target);
  if (typeof job === 'string') return job;
  c.busy = { ...job, progress: 0 };
  return job.what === 'repair' ? 'repairing' : 'building';
}

function lieDown(s: Survivor, c: Control, where: string): string {
  if (s.rest >= REST_MAX) return 'not tired';
  c.busy = { what: 'sleep', targetId: where, progress: s.rest, duration: REST_MAX };
  return where === 'floor' ? 'sleeping on the floor' : 'asleep in bed';
}

function take(state: SimState, s: Survivor, c: Control, item: Item): string {
  if (item.kind === 'food' || item.kind === 'meal') {
    c.busy = { what: 'eat', targetId: item.id, progress: 0, duration: EAT_TICKS };
    return 'eating';
  }
  const slot = item.kind === 'weapon' || item.kind === 'gun' ? 'weapon' : isGarment(item.kind) ? SLOT_OF[item.kind] : item.kind === 'cart' ? 'cart' : item.kind in TOOL_FOR ? 'tool' : 'carrying';
  // Anything you'd otherwise carry in your hands goes in the bag if it fits (inventory.ts).
  if (slot === 'carrying' && fitsInPack(state, s, item)) {
    stow(state, s, item);
    return `put the ${item.kind} in your bag`;
  }
  // Swap: whatever was in that slot goes down where the new thing was.
  const old = s[slot] ? getItem(state, s[slot]!) : undefined;
  const where = item.pos!;
  item.pos = null;
  item.carriedBy = s.id;
  s[slot] = item.id;
  if (old) dropItem(state, old, where);
  return slot === 'carrying' ? `picked up the ${item.kind}` : `took the ${item.kind}`;
}

function search(s: Survivor, c: Control, crate: Furniture): string | null {
  if (crate.loot <= 0) return crate.kind === 'tree' ? null : 'searched: what\'s left is in the loot list';
  c.busy = { what: 'search', targetId: crate.id, progress: 0, duration: SCAVENGE_WORK };
  s.note = crate.kind === 'tree' ? 'chopping' : 'searching';
  return s.note;
}

/** The controlled survivor died or left: control ends. */
export function reconcileControl(state: SimState): void {
  if (state.control && !controlled(state)) {
    if (!state.succession) logEvent(state, 'You are no longer in control of anyone');
    state.control = null;
  }
}
