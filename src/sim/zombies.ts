// Zombies: crude night pressure (milestone 7).
//
// Each night brings one more zombie than the last (2 on the first night),
// spread evenly through the night, each on an outdoor map-edge tile (sim
// RNG). Each shambles toward the nearest
// survivor it can reach, and hits an adjacent survivor every ATTACK_EVERY
// ticks. Zombies only walk on floor: walls and doors stop them. With nobody
// reachable, a zombie heads for the nearest survivor anyway, through doors
// if it can and through walls if it must, battering whatever is in the way
// (breach.ts). With no survivors at all they wander. At dawn they all
// shamble off. Guards (jobs.ts) hunt them down.

import { damageStructure, isStructure } from './breach';
import { logEvent } from './events';
import { getTile, neighbours4, type Point } from './grid';
import { canReach, findPathToAny, isPassable } from './pathfinding';
import { pick } from './rng';
import { OUTDOORS, labelAt } from './rooms';
import { manhattan, nearestTile, samePoint } from './space';
import type { SimState } from './state';
import type { Movement, Survivor } from './survivors';
import { NIGHT_TICKS, isNight, nightNumber, ticksIntoNight } from './time';

export const ZOMBIE_HEALTH = 200;
/** Slower than survivors (4), so guards can catch them and runners can escape. */
export const ZOMBIE_TICKS_PER_TILE = 6;
export const ZOMBIE_DAMAGE = 40;
export const ZOMBIE_ATTACK_EVERY = 10;
/** Zombies on night 1; every night after brings one more. (Was 4: too much for night one.) */
export const FIRST_NIGHT_ZOMBIES = 2;
/** Safety cap on zombies alive at once (not a balance lever). */
export const MAX_ZOMBIES = 20;
/** Zombies never spawn this close (Manhattan) to a survivor. */
export const SPAWN_MIN_DISTANCE = 8;
/** Zombies re-pick their target and path this often. */
export const REPATH_EVERY = 15;
/** Survivors won't go to bed with a zombie this close (Manhattan); see needs.ts. */
export const DANGER_RADIUS = 10;

export interface Zombie {
  id: string;
  pos: Point;
  move: Movement | null;
  path: Point[];
  /** Survivor being chased, or null when wandering. */
  targetId: string | null;
  health: number;
  /** Ticks until the next attack is allowed. */
  cooldown: number;
  /** Re-plan no earlier than this tick (or sooner, when the path runs out). */
  nextThinkTick: number;
}

export function getZombie(state: SimState, id: string): Zombie | undefined {
  return state.zombies.find((z) => z.id === id);
}

export function addZombie(state: SimState, x: number, y: number): Zombie {
  const z: Zombie = {
    id: `z${state.nextId++}`,
    pos: { x, y },
    move: null,
    path: [],
    targetId: null,
    health: ZOMBIE_HEALTH,
    cooldown: 0,
    nextThinkTick: 0,
  };
  state.zombies.push(z);
  return z;
}

export function removeZombie(state: SimState, z: Zombie): void {
  const i = state.zombies.indexOf(z);
  if (i >= 0) state.zombies.splice(i, 1);
}

/** Any zombie within `radius` (Manhattan) of a point. */
export function zombieNear(state: SimState, p: Point, radius = DANGER_RADIUS): boolean {
  return state.zombies.some((z) => manhattan(z.pos, p) <= radius);
}

export function zombiesForNight(night: number): number {
  return FIRST_NIGHT_ZOMBIES + night - 1;
}

/**
 * Is this tick one of tonight's spawn times? Spawns are spread evenly over
 * the night, half an interval in: night 1 (2 zombies) spawns at 23:00 and
 * 03:00.
 */
export function isSpawnTick(tick: number): boolean {
  const night = nightNumber(tick);
  if (night === null) return false;
  const count = zombiesForNight(night);
  const interval = Math.floor(NIGHT_TICKS / count);
  const t = ticksIntoNight(tick) - Math.floor(interval / 2);
  return t >= 0 && t % interval === 0 && t / interval < count;
}

/** Spawning, the dawn retreat, and every zombie's move or attack. Runs first each tick. */
export function updateZombies(state: SimState): void {
  if (!isNight(state.tick)) {
    if (state.zombies.length > 0) {
      const n = state.zombies.length;
      state.zombies = [];
      logEvent(state, `${n} zombie${n === 1 ? '' : 's'} shambled off at dawn`);
    }
    return;
  }
  const night = nightNumber(state.tick)!;
  if (ticksIntoNight(state.tick) === 0) logEvent(state, `Night ${night} falls: ${zombiesForNight(night)} zombies expected`);
  if (isSpawnTick(state.tick) && state.zombies.length < MAX_ZOMBIES) spawnAtEdge(state);
  for (const z of state.zombies) updateZombie(state, z);
}

function spawnAtEdge(state: SimState): void {
  const { width, height } = state.grid;
  const edge: Point[] = [];
  for (let x = 0; x < width; x++) edge.push({ x, y: 0 }, { x, y: height - 1 });
  for (let y = 1; y < height - 1; y++) edge.push({ x: 0, y }, { x: width - 1, y });
  const candidates = edge.filter(
    (p) =>
      getTile(state.grid, p.x, p.y) === 'floor' &&
      labelAt(state, p.x, p.y) === OUTDOORS &&
      !state.survivors.some((s) => manhattan(s.pos, p) < SPAWN_MIN_DISTANCE),
  );
  if (candidates.length === 0) return;
  const p = pick(state, candidates);
  addZombie(state, p.x, p.y);
  logEvent(state, `A zombie appeared at (${p.x}, ${p.y})`);
}

function updateZombie(state: SimState, z: Zombie): void {
  z.cooldown = Math.max(0, z.cooldown - 1);

  // Standing next to someone: attack instead of moving.
  if (!z.move) {
    const adjacent = state.survivors.filter((s) => manhattan(s.pos, z.pos) <= 1);
    const victim = adjacent.find((s) => s.id === z.targetId) ?? adjacent[0];
    if (victim) {
      if (z.cooldown === 0) hit(state, z, victim);
      return;
    }
  }

  if (z.move) {
    z.move.progress += 1;
    if (z.move.progress < z.move.duration) return;
    z.pos = z.move.to;
    z.move = null;
  }

  if (state.tick >= z.nextThinkTick || z.path.length === 0) {
    plan(state, z);
    z.nextThinkTick = state.tick + REPATH_EVERY;
  }
  const next = z.path[0];
  if (!next) return;
  if (!isPassable(state.grid, next.x, next.y, 'zombie')) {
    // A door or wall on the way in: batter it. Anything else, re-plan next tick.
    if (isStructure(getTile(state.grid, next.x, next.y)) && manhattan(z.pos, next) === 1) {
      if (z.cooldown === 0) {
        damageStructure(state, next.x, next.y, ZOMBIE_DAMAGE);
        z.cooldown = ZOMBIE_ATTACK_EVERY;
      }
      return;
    }
    z.path = [];
    return;
  }
  z.path.shift();
  z.move = { from: { ...z.pos }, to: next, progress: 0, duration: ZOMBIE_TICKS_PER_TILE };
}

/** Chase the nearest reachable survivor; failing that, break in to the nearest one; or wander one tile at random. */
function plan(state: SimState, z: Zombie): void {
  const { grid, walkabilityVersion: v } = state;
  const goalsFor = (s: Survivor) => [s.pos, ...neighbours4(grid, s.pos.x, s.pos.y)];
  const target = state.survivors
    .slice()
    .sort((a, b) => manhattan(z.pos, a.pos) - manhattan(z.pos, b.pos))
    .find((s) => canReach(grid, v, z.pos, goalsFor(s), 'zombie'));
  z.targetId = target?.id ?? null;
  if (target) {
    z.path = findPathToAny(grid, z.pos, goalsFor(target), 'zombie') ?? [];
    return;
  }
  // Nobody reachable: go for the nearest survivor through doors, or failing that through walls.
  const victim = state.survivors.slice().sort((a, b) => manhattan(z.pos, a.pos) - manhattan(z.pos, b.pos))[0];
  if (victim) {
    const path = findPathToAny(grid, z.pos, goalsFor(victim), 'survivor') ?? findPathToAny(grid, z.pos, goalsFor(victim), 'breacher');
    if (path && path.length > 0) {
      z.targetId = victim.id;
      z.path = path;
      return;
    }
  }
  const options = neighbours4(grid, z.pos.x, z.pos.y).filter((p) => isPassable(grid, p.x, p.y, 'zombie'));
  z.path = options.length > 0 ? [pick(state, options)] : [];
}

function hit(state: SimState, z: Zombie, s: Survivor): void {
  s.health = Math.max(0, s.health - ZOMBIE_DAMAGE);
  s.lastHurtBy = 'a zombie attack';
  s.hurtTick = state.tick;
  s.note = `attacked by a zombie at (${z.pos.x}, ${z.pos.y})`;
  z.cooldown = ZOMBIE_ATTACK_EVERY;
}

/** A guard's blow. Returns true if the zombie died (and was removed). */
export function damageZombie(state: SimState, z: Zombie, amount: number, by: Survivor): boolean {
  z.health = Math.max(0, z.health - amount);
  if (z.health > 0) return false;
  removeZombie(state, z);
  logEvent(state, `${by.name} killed a zombie`);
  return true;
}

/** Moves zombies off a tile that stopped being floor (wall or door built on it). */
export function evictZombies(state: SimState, tile: Point): void {
  for (const z of state.zombies) {
    const onIt = samePoint(z.pos, tile) || (z.move !== null && samePoint(z.move.to, tile));
    if (!onIt) continue;
    const to = nearestTile(state.grid, tile, (x, y) => isPassable(state.grid, x, y, 'zombie'));
    if (!to) continue;
    z.pos = to;
    z.move = null;
    z.path = [];
  }
}
