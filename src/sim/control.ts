// Direct control (the control prototype): the player takes over one survivor.
//
// While controlled, a survivor does nothing on their own: no jobs, meals, sleep
// or leisure. Their needs still drain, zombies still go for them, and they still
// bleed and heal. Input arrives as commands like everything else (rule 5):
//   steer   the held direction (sent when the keys change, not every frame)
//   swing   attack towards a tile: the zombie in front, else a shot with a loaded gun
//   use     pick up, put on, eat, or search what's on a tile next to you
// Movement stays tile to tile (rule 10), in eight directions; a diagonal costs
// 1.5 steps and can't cut a corner. A blocked diagonal slides along the wall.
// Searching and eating take time; moving or swinging interrupts them.

import { hitResident, meetResident, residentAt, type Resident } from './residents';
import { logEvent } from './events';
import { EAT_TICKS, HOT_MEAL_VALUE, MEAL_VALUE, SATIETY_MAX } from './food';
import { getFurniture, type Furniture } from './furniture';
import { inBounds, type Point } from './grid';
import { dropItem, getItem, itemAt, removeItem, type Item } from './items';
import { spawnFind } from './difficulty';
import { GUARD_ATTACK_EVERY, GUARD_DAMAGE, SCAVENGE_WORK, clearShot, scavengeFind } from './jobs';
import { makeNoise } from './noise';
import { isPassable } from './pathfinding';
import { LIGHT_DAMAGE_PCT, inLight } from './power';
import { random } from './rng';
import { GUARD_DAMAGE_PER_LEVEL, HARDENED_DAMAGE, TOOL_FOR, hasTrait, skillLevel } from './skills';
import { SLOT_OF, isGarment } from './clothing';
import type { SimState } from './state';
import { advanceStep, getSurvivor, stepPace, type Survivor } from './survivors';
import { GUN_DAMAGE, GUN_EVERY, GUN_RANGE, blowDamage, loadedGun, wearWeapon } from './weapons';
import { damageZombie, type Zombie } from './zombies';

/** A diagonal step takes this many percent of a straight one. */
export const DIAGONAL_PCT = 150;

export interface Control {
  survivorId: string;
  /** Held direction, each -1, 0 or 1. */
  dx: number;
  dy: number;
  /** Last direction moved or swung (never 0, 0). */
  facing: Point;
  /** No swing or shot before this tick. */
  nextSwing: number;
  /** A timed action in progress: searching a crate or tree, or eating. */
  busy: { what: 'search' | 'eat'; targetId: string; progress: number; duration: number } | null;
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
  if (!advanceStep(state, s)) return;
  if (c.dx !== 0 || c.dy !== 0) {
    c.busy = null;
    const to = stepTarget(state, s.pos, c.dx, c.dy);
    if (!to) return;
    const dx = to.x - s.pos.x;
    const dy = to.y - s.pos.y;
    c.facing = { x: dx, y: dy };
    const pace = stepPace(state, s, to);
    s.move = { from: { ...s.pos }, to, progress: 0, duration: dx !== 0 && dy !== 0 ? Math.round((pace * DIAGONAL_PCT) / 100) : pace };
    return;
  }
  if (c.busy) updateBusy(state, s, c);
}

/** Where a step in (dx, dy) lands: straight on, or sliding along a wall for a blocked diagonal. */
export function stepTarget(state: SimState, from: Point, dx: number, dy: number): Point | null {
  const open = (x: number, y: number) => inBounds(state.grid, x, y) && isPassable(state.grid, x, y, 'survivor');
  const straight = { x: from.x + dx, y: from.y + dy };
  if (dx === 0 || dy === 0) return open(straight.x, straight.y) ? straight : null;
  const sideX = open(from.x + dx, from.y);
  const sideY = open(from.x, from.y + dy);
  // No corner cutting: both sides must be open to go diagonally.
  if (sideX && sideY && open(straight.x, straight.y)) return straight;
  if (sideX) return { x: from.x + dx, y: from.y };
  if (sideY) return { x: from.x, y: from.y + dy };
  return null;
}

function updateBusy(state: SimState, s: Survivor, c: Control): void {
  const busy = c.busy!;
  busy.progress += 1;
  if (busy.progress < busy.duration) return;
  c.busy = null;
  if (busy.what === 'eat') {
    const item = getItem(state, busy.targetId);
    if (!item || (item.carriedBy !== s.id && !item.pos)) return;
    s.satiety = Math.min(SATIETY_MAX, s.satiety + (item.kind === 'meal' ? HOT_MEAL_VALUE : MEAL_VALUE));
    if (s.carrying === item.id) s.carrying = null;
    removeItem(state, item);
    s.note = `ate ${item.kind === 'meal' ? 'a hot meal' : 'some food'}`;
    return;
  }
  const crate = getFurniture(state, busy.targetId);
  if (!crate || crate.loot <= 0 || !nextTo(s.pos, crate)) return;
  const kind = crate.kind === 'tree' ? 'wood' : scavengeFind(random(state));
  spawnFind(state, kind, s.pos.x, s.pos.y);
  crate.loot -= 1;
  if (crate.loot === 0 && crate.kind === 'crate') crate.scavenge = false;
  s.note = crate.kind === 'tree' ? 'chopped some wood' : `found ${kind}`;
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
  if (state.tick < c.nextSwing || s.move) return null;
  const face = towards(s.pos, target);
  if (face) c.facing = face;
  c.busy = null;
  const near = zombieToHit(state, s.pos, c.facing);
  if (near) {
    c.nextSwing = state.tick + GUARD_ATTACK_EVERY;
    makeNoise(state, s.pos, 'fighting');
    const blow = blowDamage(s, GUARD_DAMAGE) + GUARD_DAMAGE_PER_LEVEL * skillLevel(s, 'guard') + (hasTrait(s, 'hardened') ? HARDENED_DAMAGE : 0);
    const killed = damageZombie(state, near, inLight(state, s.pos) ? Math.round((blow * LIGHT_DAMAGE_PCT) / 100) : blow, s);
    if (killed) wearWeapon(state, s);
    return killed ? 'killed it' : 'hit it';
  }
  const person = residentToHit(state, s.pos, c.facing);
  if (person) {
    c.nextSwing = state.tick + GUARD_ATTACK_EVERY;
    makeNoise(state, s.pos, 'fighting');
    const blow = blowDamage(s, GUARD_DAMAGE) + GUARD_DAMAGE_PER_LEVEL * skillLevel(s, 'guard');
    return hitResident(state, person, blow, s) ? `killed ${person.name}` : `hit ${person.name}`;
  }
  const gun = loadedGun(state, s);
  const shotAt = gun ? state.zombies.find((z) => z.pos.x === target.x && z.pos.y === target.y) : undefined;
  if (gun && shotAt && Math.abs(shotAt.pos.x - s.pos.x) + Math.abs(shotAt.pos.y - s.pos.y) <= GUN_RANGE && clearShot(state.grid, s.pos, shotAt.pos)) {
    const blocking = inTheWay(state, s, shotAt.pos);
    if (blocking) return `held fire: ${blocking.name} is in the way`;
    c.nextSwing = state.tick + GUN_EVERY;
    gun.wear -= 1;
    makeNoise(state, s.pos, 'gunfire');
    const dmg = GUN_DAMAGE + GUARD_DAMAGE_PER_LEVEL * skillLevel(s, 'guard') + (hasTrait(s, 'hardened') ? HARDENED_DAMAGE : 0);
    return damageZombie(state, shotAt, dmg, s) ? 'shot it dead' : 'shot it';
  }
  // A swing at nothing still takes the time.
  c.nextSwing = state.tick + GUARD_ATTACK_EVERY;
  return 'swung at nothing';
}

/** A resident (residents.ts) in front of you, or right next to you. */
function residentToHit(state: SimState, pos: Point, facing: Point): Resident | undefined {
  const ahead = residentAt(state, { x: pos.x + facing.x, y: pos.y + facing.y });
  if (ahead) return ahead;
  return state.residents.find((r) => Math.abs(r.pos.x - pos.x) + Math.abs(r.pos.y - pos.y) === 1);
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
function zombieToHit(state: SimState, pos: Point, facing: Point): Zombie | undefined {
  const at = (x: number, y: number) => state.zombies.find((z) => z.pos.x === x && z.pos.y === y);
  const ahead = at(pos.x + facing.x, pos.y + facing.y);
  if (ahead) return ahead;
  for (const [dx, dy] of [[0, -1], [1, 0], [0, 1], [-1, 0], [1, -1], [1, 1], [-1, 1], [-1, -1]] as const) {
    const z = at(pos.x + dx, pos.y + dy);
    if (z) return z;
  }
  return undefined;
}

/** Use what's on a tile next to (or under) you. Returns what happened, or null if there was nothing to do. */
export function use(state: SimState, s: Survivor, target: Point): string | null {
  const c = state.control!;
  if (s.move || !nextTo(s.pos, target)) return null;
  const face = towards(s.pos, target);
  if (face) c.facing = face;
  const person = residentAt(state, target);
  if (person) return meetResident(state, s, person) ?? `talked with ${person.name}`;
  const item = itemAt(state, target.x, target.y);
  if (item && !item.reservedBy) return take(state, s, c, item);
  const crate = state.furniture.find((f) => f.x === target.x && f.y === target.y && (f.kind === 'crate' || f.kind === 'tree'));
  if (crate) return search(s, c, crate);
  // Nothing there: put down what you're carrying.
  const carried = s.carrying ? getItem(state, s.carrying) : undefined;
  if (carried) {
    s.carrying = null;
    dropItem(state, carried, target);
    return `put down the ${carried.kind}`;
  }
  return null;
}

function take(state: SimState, s: Survivor, c: Control, item: Item): string {
  if (item.kind === 'food' || item.kind === 'meal') {
    c.busy = { what: 'eat', targetId: item.id, progress: 0, duration: EAT_TICKS };
    return 'eating';
  }
  const slot = item.kind === 'weapon' || item.kind === 'gun' ? 'weapon' : isGarment(item.kind) ? SLOT_OF[item.kind] : item.kind === 'cart' ? 'cart' : item.kind in TOOL_FOR ? 'tool' : 'carrying';
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
  if (crate.loot <= 0) return crate.kind === 'tree' ? null : 'nothing left in it';
  c.busy = { what: 'search', targetId: crate.id, progress: 0, duration: SCAVENGE_WORK };
  s.note = crate.kind === 'tree' ? 'chopping' : 'searching';
  return s.note;
}

/** The controlled survivor died or left: control ends. */
export function reconcileControl(state: SimState): void {
  if (state.control && !controlled(state)) {
    logEvent(state, 'You are no longer in control of anyone');
    state.control = null;
  }
}
