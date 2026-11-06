// Wild animals: deer to hunt, and a small food chain round them.
//
//   deer    most mornings (a hash of the day) a deer wanders in at the edge of explored ground,
//           browses about (a step every DEER_STEP_EVERY ticks), keeps clear of people, and leaves
//           after DEER_STAYS. VENISON when brought down.
//   rabbits nibble about on open ground near your people and bolt from anything that moves near
//           (people, wolves, bears, the dead) within FLEE. Where there are two or more close
//           together they breed (a chance each hour), up to RABBITS_NEAR round each of yours.
//   wolves  come in packs (WOLF_PACK) out of the wild, and get hungry (hunger, on slowTick ticks).
//           Hungry, they hunt the nearest rabbit or deer (killing it feeds them); starving with
//           nothing to hunt, they come for people. Anyone who gets within WOLF_CLOSE is attacked
//           whatever their hunger (they don't like being crowded), and so is anyone who hurts one.
//           A pack follows its leader; a wolf badly hurt runs.
//   bears   rare (BEAR_CHANCE a day), slow and very tough. They wander, eat what they catch, and
//           maul anyone who comes within BEAR_CLOSE.
// Only near your people: animals turn up round them (a hash of the hour and the survivor) and are
// gone once nobody of yours is within WILD_RADIUS (they wandered off), so a bigger world costs
// nothing extra. Rolls are hashes (hash.ts), not the sim RNG, so adding wildlife shifts nothing else.
// An animal that attacks someone is marked for hunting (the Hunt order's mark): hunters go for it.
// Hunting and hitting them: handiwork.ts hitAnimal (yours), work.ts (colonists'): MEAT by kind.

import { placedAt } from './motion';
import { logEvent } from './events';
import { getTile, neighbours4, type Point } from './grid';
import { hashRoll, idNumber } from './hash';
import { addThought } from './mood';
import { HIT_PANIC, frighten } from './body';
import { crossable } from './pathfinding';
import { OUTDOORS, labelAt } from './rooms';
import { manhattan, nearestTile } from './space';
import type { SimState } from './state';
import type { Movement, Survivor } from './survivors';
import { DAY_LENGTH, TICKS_PER_HOUR, dayOf, isNight, onClock, slowTick, tickOfDay } from './time';
import { seasonOf } from './seasons';
import { frontierSpot } from './wanderers';
import { zombiesWithin } from './zombies';

export type AnimalKind = 'deer' | 'rabbit' | 'wolf' | 'bear';

export interface Animal {
  id: string;
  kind: AnimalKind;
  pos: Point;
  /** Where exactly, in motion.ts SUB units (pos is the tile under it); and where at the tick's start. */
  at: Point;
  was: Point;
  move: Movement | null;
  /** Marked by the Hunt order (or for attacking someone). */
  hunted: boolean;
  /** Deer leave after a while; the rest leave when nobody of yours is near (WILD_RADIUS). */
  leavesTick: number;
  /** Rabbits, wolves, bears: health left (deer go down to a lucky blow: HUNT_CHANCE). */
  health?: number;
  /** Wolves and bears: how hungry, 0 just fed. */
  hunger?: number;
  /** Wolves: the pack (its leader's id). */
  packId?: string;
  /** Who it's after this tick: an animal's id or a survivor's (shown in the debug view). */
  targetId?: string | null;
  /** A survivor it's out to get: who hurt it (or its pack), or who a starving wolf has picked. */
  angryAt?: string | null;
  /** After a warning bite it keeps away from people until this tick. */
  backOffUntil?: number;
  /** No bite before this tick. */
  nextBite?: number;
  /** When it last bit one of yours (menace: guards go for it a while after). */
  bitTick?: number;
}

/** How long after biting someone a wolf or bear counts as a menace to fight off. */
export const MENACE_TICKS = 300;

/** A wolf or bear that's bitten someone lately, or is out for someone: colonists on guard go for it (jobs.ts). */
export function isMenace(state: SimState, a: Animal): boolean {
  if (a.kind !== 'wolf' && a.kind !== 'bear') return false;
  return !!a.angryAt || (a.bitTick !== undefined && state.tick - a.bitTick <= MENACE_TICKS);
}

export const DEER_CHANCE = 0.6;
export let DEER_STAYS = 2 * DAY_LENGTH;
onClock(() => (DEER_STAYS = 2 * DAY_LENGTH));
export const DEER_STEP_EVERY = 20;
export const DEER_TICKS_PER_TILE = 5;
/** Deer keep this far from survivors when they can (they're wary, not fast). */
export const DEER_WARY = 3;
export const HUNT_WORK = 20;
export const VENISON = 3;

/** Food from each kind brought down. */
export const MEAT: Record<AnimalKind, number> = { deer: VENISON, rabbit: 1, wolf: 2, bear: 6 };
export const HEALTH: Record<Exclude<AnimalKind, 'deer'>, number> = { rabbit: 20, wolf: 150, bear: 700 };
/** Ticks a tile, by kind. */
export const PACE: Record<AnimalKind, number> = { deer: DEER_TICKS_PER_TILE, rabbit: 3, wolf: 4, bear: 7 };
export const WILD_RADIUS = 70;
/** Rabbits, wolves and bears only on maps at least this big (a yard isn't the wild). */
export const WILD_MAP = 64;
/** Animals turn up at least this far from all your people. */
export const SPAWN_CLEAR = 14;
export const FLEE = 4;
export const RABBITS_NEAR = 4;
/** How far round someone (or a rabbit) the rabbits are counted for those caps. */
export const RABBIT_RANGE = 40;
export const RABBIT_CHANCE = 0.5;
export const BREED_CHANCE = 0.15;
export const WOLF_PACK = 3;
/** Ticks a tile for a wolf running something down (quicker than a rabbit). */
export const WOLF_SPRINT = 2;
/** Chance an hour (at night and in winter: twice each) that a pack comes in near your people with none about. */
export const WOLF_CHANCE = 0.01;
/** Hunger (slowTick ticks) at which wolves hunt prey, and at which they'll come for people. */
export let WOLF_HUNGRY = DAY_LENGTH / 2;
export let WOLF_STARVING = 2 * DAY_LENGTH;
/** A pack moves on after this long. */
export let PACK_STAYS = DAY_LENGTH;
onClock(() => {
  WOLF_HUNGRY = DAY_LENGTH / 2;
  WOLF_STARVING = 2 * DAY_LENGTH;
  PACK_STAYS = DAY_LENGTH;
});
export const WOLF_CLOSE = 2;
/** Wolves that aren't starving keep this far from people. */
export const WOLF_WARY = 6;
/** After a warning bite, how long it keeps clear. */
export const BACK_OFF = 80;
export const WOLF_DAMAGE = 30;
export const WOLF_BITE_EVERY = 12;
export const BEAR_CHANCE = 0.08;
export const BEAR_CLOSE = 3;
export const BEAR_DAMAGE = 90;
export const BEAR_BITE_EVERY = 16;
/** How far a predator looks for prey, or a starving one for people. */
export const HUNT_SIGHT = 15;

export function getAnimal(state: SimState, id: string): Animal | undefined {
  return state.animals.find((a) => a.id === id);
}

export function addDeer(state: SimState, x: number, y: number): Animal {
  const a: Animal = { id: `d${state.nextId++}`, kind: 'deer', pos: { x, y }, ...placedAt({ x, y }), move: null, hunted: false, leavesTick: state.tick + DEER_STAYS };
  state.animals.push(a);
  return a;
}

/** A rabbit, wolf or bear at a tile. */
export function addAnimal(state: SimState, kind: Exclude<AnimalKind, 'deer'>, x: number, y: number, packId?: string): Animal {
  const a: Animal = {
    id: `w${(state.wildId = (state.wildId ?? 0) + 1)}`, kind, pos: { x, y }, ...placedAt({ x, y }), move: null, hunted: false, leavesTick: Number.MAX_SAFE_INTEGER,
    health: HEALTH[kind], hunger: 0, targetId: null, nextBite: 0, ...(packId ? { packId } : {}),
  };
  if (kind === 'wolf' && !packId) a.packId = a.id;
  state.animals.push(a);
  return a;
}

/** Open ground an animal can stand on. */
function ground(state: SimState, x: number, y: number): boolean {
  return getTile(state.grid, x, y) === 'floor';
}

/** A step from one tile to the next that an animal can take (no walls between). */
function canStep(state: SimState, a: Point, b: Point): boolean {
  return ground(state, b.x, b.y) && crossable(state.grid, a.x, a.y, b.x, b.y, 'zombie');
}

export function updateAnimals(state: SimState): void {
  const day = dayOf(state.tick);
  if (tickOfDay(state.tick) === 2 * TICKS_PER_HOUR && hashRoll(day, 0xdee7) < DEER_CHANCE && state.survivors.length > 0) {
    const spot = frontierSpot(state, new Set());
    if (spot) {
      addDeer(state, spot.x, spot.y);
      logEvent(state, `Deer seen at (${spot.x}, ${spot.y}) (the Hunt order marks them)`);
    }
  }
  if (state.tick % TICKS_PER_HOUR === 0 && state.survivors.length > 0 && state.grid.width >= WILD_MAP && state.grid.height >= WILD_MAP) hourly(state);
  for (const a of [...state.animals]) {
    if (!state.animals.includes(a)) continue; // eaten this tick
    if (state.tick >= a.leavesTick) {
      state.animals.splice(state.animals.indexOf(a), 1);
      continue;
    }
    if (a.hunger !== undefined && slowTick(state.tick)) a.hunger += 1;
    if (a.move) {
      a.move.progress += 1;
      if (a.move.progress < a.move.duration) continue;
      a.pos = a.move.to;
      a.move = null;
    }
    if (!ground(state, a.pos.x, a.pos.y) && !offGround(state, a)) continue;
    if (a.kind === 'deer') deer(state, a);
    else if (a.kind === 'rabbit') rabbit(state, a);
    else predator(state, a);
  }
}

/** Built over (or a wall dropped on it): out to the nearest open ground, or gone (false). */
function offGround(state: SimState, a: Animal): boolean {
  const out = nearestTile(state.grid, a.pos, (x, y) => ground(state, x, y), 60);
  if (!out) {
    state.animals.splice(state.animals.indexOf(a), 1);
    return false;
  }
  a.pos = out;
  Object.assign(a, placedAt(out));
  return true;
}

function step(a: Animal, to: Point, pace = PACE[a.kind]): void {
  a.move = { from: { ...a.pos }, to: { ...to }, progress: 0, duration: pace };
}

/** The neighbour that's furthest from (or nearest to) a point, of those an animal can step to. */
function stepAway(state: SimState, a: Animal, from: Point, toward = false): Point | null {
  const options = neighbours4(state.grid, a.pos.x, a.pos.y).filter((p) => canStep(state, a.pos, p));
  if (options.length === 0) return null;
  options.sort((p, q) => (toward ? manhattan(p, from) - manhattan(q, from) : manhattan(q, from) - manhattan(p, from)));
  return options[0]!;
}

function wander(state: SimState, a: Animal, every: number): void {
  if (state.tick % every !== 0) return;
  const options = neighbours4(state.grid, a.pos.x, a.pos.y).filter((p) => canStep(state, a.pos, p));
  if (options.length > 0) step(a, options[Math.floor(hashRoll(idNumber(a.id), state.tick) * options.length)]!);
}

function deer(state: SimState, a: Animal): void {
  if (state.tick % DEER_STEP_EVERY !== 0) return;
  const options = neighbours4(state.grid, a.pos.x, a.pos.y).filter((p) => ground(state, p.x, p.y));
  if (options.length === 0) return;
  // Away from anyone close; otherwise a browsing step (a hash, not the sim RNG).
  const near = state.survivors.find((s) => manhattan(s.pos, a.pos) <= DEER_WARY);
  const to = near
    ? options.sort((p, q) => manhattan(q, near.pos) - manhattan(p, near.pos))[0]!
    : options[Math.floor(hashRoll(idNumber(a.id), state.tick) * options.length)]!;
  step(a, to);
}

function rabbit(state: SimState, a: Animal): void {
  const threat =
    state.survivors.find((s) => manhattan(s.pos, a.pos) <= FLEE)?.pos ??
    state.animals.find((o) => (o.kind === 'wolf' || o.kind === 'bear') && manhattan(o.pos, a.pos) <= FLEE)?.pos ??
    (zombiesWithin(state, a.pos, FLEE) > 0 ? state.zombies.find((z) => manhattan(z.pos, a.pos) <= FLEE)?.pos : undefined); // the bucketed count first: the dead are many
  if (threat) {
    const to = stepAway(state, a, threat);
    if (to) step(a, to);
    return;
  }
  wander(state, a, 12);
}

function predator(state: SimState, a: Animal): void {
  const hp = a.health ?? 0;
  const hurt = hp < HEALTH[a.kind as 'wolf' | 'bear'] * 0.3;
  // Badly hurt: away from whoever's closest.
  const nearest = closestSurvivor(state, a.pos);
  if (hurt && nearest && manhattan(nearest.pos, a.pos) <= 8) {
    const to = stepAway(state, a, nearest.pos);
    if (to) step(a, to);
    return;
  }
  if ((a.backOffUntil ?? 0) > state.tick && !a.angryAt && nearest && manhattan(nearest.pos, a.pos) <= 8) {
    const to = stepAway(state, a, nearest.pos);
    if (to) step(a, to);
    return;
  }
  const target = chooseTarget(state, a, nearest);
  a.targetId = target?.id ?? null;
  if (!target && a.kind === 'wolf' && nearest && manhattan(nearest.pos, a.pos) <= WOLF_WARY) {
    // Wary of people: off out of the way.
    const to = stepAway(state, a, nearest.pos);
    if (to) step(a, to);
    return;
  }
  if (!target) {
    // A pack follows its leader; leaders (and bears) wander.
    const leader = a.packId && a.packId !== a.id ? state.animals.find((o) => o.id === a.packId) : undefined;
    if (leader && manhattan(leader.pos, a.pos) > 2) {
      const to = stepAway(state, a, leader.pos, true);
      if (to) step(a, to);
      return;
    }
    wander(state, a, a.kind === 'bear' ? 25 : 15);
    return;
  }
  if (manhattan(target.pos, a.pos) <= 1) {
    attack(state, a, target);
    return;
  }
  const to = stepAway(state, a, target.pos, true);
  if (to) step(a, to, a.kind === 'wolf' ? WOLF_SPRINT : PACE[a.kind]); // wolves run their quarry down
}

function closestSurvivor(state: SimState, p: Point): Survivor | undefined {
  let best: Survivor | undefined;
  for (const s of state.survivors) if (!best || manhattan(s.pos, p) < manhattan(best.pos, p)) best = s;
  return best;
}

/**
 * Who a wolf or bear goes for: whoever it's out to get (they hurt it, or it's starving), anyone who
 * comes too close (a warning: one bite, then it backs off), prey when hungry.
 */
function chooseTarget(state: SimState, a: Animal, nearest: Survivor | undefined): Animal | Survivor | undefined {
  const close = a.kind === 'bear' ? BEAR_CLOSE : WOLF_CLOSE;
  const after = a.angryAt ? state.survivors.find((s) => s.id === a.angryAt) : undefined;
  if (after && manhattan(after.pos, a.pos) <= HUNT_SIGHT) return after;
  a.angryAt = null; // got away (or gone)
  if (nearest && manhattan(nearest.pos, a.pos) <= close) return nearest;
  const hunger = a.hunger ?? 0;
  if (a.kind === 'wolf' ? hunger >= WOLF_HUNGRY : hunger >= WOLF_STARVING) {
    let prey: Animal | undefined;
    for (const o of state.animals) {
      if ((o.kind !== 'rabbit' && o.kind !== 'deer') || manhattan(o.pos, a.pos) > HUNT_SIGHT) continue;
      if (!prey || manhattan(o.pos, a.pos) < manhattan(prey.pos, a.pos)) prey = o;
    }
    if (prey) return prey;
  }
  if (a.kind === 'wolf' && hunger >= WOLF_STARVING && nearest && manhattan(nearest.pos, a.pos) <= HUNT_SIGHT) {
    a.angryAt = nearest.id;
    return nearest;
  }
  return undefined;
}

function attack(state: SimState, a: Animal, target: Animal | Survivor): void {
  if (state.tick < (a.nextBite ?? 0)) return;
  const bear = a.kind === 'bear';
  a.nextBite = state.tick + (bear ? BEAR_BITE_EVERY : WOLF_BITE_EVERY);
  if ('kind' in target) {
    // Prey: caught and eaten.
    state.animals.splice(state.animals.indexOf(target), 1);
    a.hunger = 0;
    return;
  }
  const s = target;
  s.health = Math.max(0, s.health - (bear ? BEAR_DAMAGE : WOLF_DAMAGE));
  a.bitTick = state.tick;
  s.lastHurtBy = bear ? 'a bear' : 'wolves';
  s.hurtTick = state.tick;
  s.note = `attacked by a ${a.kind} at (${a.pos.x}, ${a.pos.y})`;
  addThought(state, s, 'attacked');
  frighten(s, HIT_PANIC);
  if (!a.angryAt) a.backOffUntil = state.tick + BACK_OFF; // a warning: now it keeps clear
  if (!a.hunted) {
    a.hunted = true; // hunters go for it
    logEvent(state, `${bear ? 'A bear' : 'Wolves'} attacked ${s.name} at (${a.pos.x}, ${a.pos.y})!`);
  }
}

/** Someone of yours hurt it: it goes for them (wolves and bears), and its pack too. */
export function provoke(state: SimState, a: Animal, by: Survivor): void {
  if (a.kind !== 'wolf' && a.kind !== 'bear') return;
  for (const o of state.animals) {
    if (o !== a && !(a.packId && o.packId === a.packId)) continue;
    o.angryAt = by.id;
    o.backOffUntil = 0;
  }
}

/** Every hour: rabbits about your people, breeding; now and then a wolf pack or (rarely) a bear; and animals far from everyone gone. */
function hourly(state: SimState): void {
  const hour = Math.floor(state.tick / TICKS_PER_HOUR);
  const winter = seasonOf(state.tick) === 'winter';
  // Gone: wandered off, far from everyone.
  state.animals = state.animals.filter((a) => a.kind === 'deer' || state.survivors.some((s) => manhattan(s.pos, a.pos) <= WILD_RADIUS));
  state.survivors.forEach((s, k) => {
    const roll = (salt: number) => hashRoll(hour, idNumber(s.id), salt);
    const near = (kind: AnimalKind, r: number) => state.animals.filter((a) => a.kind === kind && manhattan(a.pos, s.pos) <= r);
    // Rabbits: a few about (fewer in winter).
    if (near('rabbit', RABBIT_RANGE).length < (winter ? 2 : RABBITS_NEAR) && roll(1) < RABBIT_CHANCE) spawnNear(state, s.pos, 'rabbit', roll(2), roll(3));
    // Wolves: a pack now and then, more at night and in winter, if none's about.
    const wolfChance = WOLF_CHANCE * (isNight(state.tick) ? 2 : 1) * (winter ? 2 : 1);
    if (k === 0 && near('wolf', 60).length === 0 && roll(4) < wolfChance) {
      const lead = spawnNear(state, s.pos, 'wolf', roll(5), roll(6));
      if (lead) {
        for (let n = 1; n < WOLF_PACK; n++) {
          const p = nearestTile(state.grid, lead.pos, (x, y) => ground(state, x, y) && !state.animals.some((o) => o.pos.x === x && o.pos.y === y), 50);
          if (p) addAnimal(state, 'wolf', p.x, p.y, lead.id);
        }
        // They come in hungry (in winter, nearer starving), and move on after a while.
        for (const o of state.animals) {
          if (o.packId !== lead.id) continue;
          o.hunger = winter ? WOLF_STARVING - WOLF_HUNGRY : WOLF_HUNGRY;
          o.leavesTick = state.tick + PACK_STAYS;
        }
        logEvent(state, `Wolves howling out past (${lead.pos.x}, ${lead.pos.y})`);
      }
    }
    // A bear: rare, one at a time.
    if (k === 0 && tickOfDay(state.tick) === 6 * TICKS_PER_HOUR && !state.animals.some((a) => a.kind === 'bear') && roll(7) < BEAR_CHANCE) {
      const b = spawnNear(state, s.pos, 'bear', roll(8), roll(9));
      if (b) logEvent(state, `A bear's tracks near (${b.pos.x}, ${b.pos.y}). Keep your distance`);
    }
  });
  // Breeding: two or more rabbits close together, now and then another.
  for (const a of [...state.animals]) {
    if (a.kind !== 'rabbit' || winter) continue;
    const close = state.animals.filter((o) => o.kind === 'rabbit' && manhattan(o.pos, a.pos) <= 8).length;
    const about = state.animals.filter((o) => o.kind === 'rabbit' && manhattan(o.pos, a.pos) <= RABBIT_RANGE).length;
    if (close < 2 || about >= RABBITS_NEAR * 2 || hashRoll(hour, idNumber(a.id), 0xb7) >= BREED_CHANCE) continue;
    const p = nearestTile(state.grid, a.pos, (x, y) => ground(state, x, y) && !(x === a.pos.x && y === a.pos.y), 30);
    if (p) addAnimal(state, 'rabbit', p.x, p.y);
  }
}

/** An animal out in the open 18-34 tiles from a point (by hash), or null. */
function spawnNear(state: SimState, from: Point, kind: Exclude<AnimalKind, 'deer'>, r1: number, r2: number): Animal | null {
  const angle = r1 * Math.PI * 2;
  const dist = 18 + Math.floor(r2 * 16);
  const { width, height } = state.grid;
  const want = { x: Math.max(0, Math.min(width - 1, Math.round(from.x + Math.cos(angle) * dist))), y: Math.max(0, Math.min(height - 1, Math.round(from.y + Math.sin(angle) * dist))) };
  // Out of sight of everyone (SPAWN_CLEAR): small maps, with nowhere that far, get none.
  const clear = (x: number, y: number) => state.survivors.every((s) => manhattan(s.pos, { x, y }) >= SPAWN_CLEAR);
  const p = nearestTile(state.grid, want, (x, y) => ground(state, x, y) && clear(x, y) && labelAt(state, x, y) === OUTDOORS && !state.animals.some((o) => o.pos.x === x && o.pos.y === y), 200);
  return p ? addAnimal(state, kind, p.x, p.y) : null;
}
