// Zombies: crude night pressure (milestone 7).
//
// Each night brings one more zombie than the last (2 on the first night, plus
// one per SURVIVORS_PER_EXTRA_ZOMBIE survivors), each a little tougher,
// spread evenly through the night (in groups from night 4), each on an outdoor map-edge tile (sim
// RNG). Each shambles toward the nearest
// survivor it can reach, and hits an adjacent survivor every ATTACK_EVERY
// ticks. Zombies only walk on floor: walls and doors stop them. With nobody
// reachable, a zombie heads for the nearest survivor anyway, through doors
// if it can and through walls if it must, battering whatever is in the way
// (breach.ts). With no survivors at all they wander. At dawn they all
// shamble off. Guards (jobs.ts) hunt them down.

import { damageStructure, isStructure } from './breach';
import { logEvent } from './events';
import { furnitureAt, removeFurniture } from './furniture';
import { addBlueprint } from './designations';
import { addThought } from './mood';
import { hasTrait } from './skills';
import { INFECTION_CHANCE, maybeInfect } from './infection';
import { armourDamage, wearing } from './clothing';
import { leaveCorpse } from './corpses';
import { FIREBOMB_CHANCE, ignite } from './fire';
import { hashRoll, idNumber } from './hash';
import { LAMP_DETER, litLamps } from './power';
import { hitDog } from './dogs';
import { VARIANTS, giveVariant, type Variant } from './zombieKinds';
import { makeNoise } from './noise';
import { threatScale } from './director';
import { maybeCapture } from './prisoners';
import { maybeSample, siegeFactor } from './endgame';
import { HARDENED_KILLS, earnTrait } from './skills';
import { RAIDER_DAMAGE } from './visitors';
import { getTile, neighbours4, type Point } from './grid';
import { canReach, findPathToAny, isPassable } from './pathfinding';
import { pick, randomInt } from './rng';
import { isExplored } from './fog';
import { OUTDOORS, labelAt } from './rooms';
import { manhattan, nearestTile, samePoint } from './space';
import type { SimState } from './state';
import type { Movement, Survivor } from './survivors';
import { NIGHT_TICKS, isNight, nightNumber, ticksIntoNight } from './time';

/** A night-1 zombie's health; later nights add ZOMBIE_HEALTH_PER_NIGHT each (balance pass 2; 20 -> 10 in pass 3). */
export const ZOMBIE_HEALTH = 200;
export const ZOMBIE_HEALTH_PER_NIGHT = 10;
/** One more zombie each night per this many survivors, counted at nightfall (balance pass 2). */
export const SURVIVORS_PER_EXTRA_ZOMBIE = 4;
/** Slower than survivors (4), so guards can catch them and runners can escape. */
export const ZOMBIE_TICKS_PER_TILE = 6;
export const ZOMBIE_DAMAGE = 40;
/** What a spike trap does to the zombie that steps on it. */
export const TRAP_DAMAGE = 100;
export const ZOMBIE_ATTACK_EVERY = 10;
/** Zombies on night 1; every night after brings one more. (Was 4: too much for night one.) */
export const FIRST_NIGHT_ZOMBIES = 2;
/** Safety cap on zombies alive at once (not a balance lever). */
export const MAX_ZOMBIES = 60;
/** Static and herd zombies only go for survivors this close (Manhattan); night zombies go for anyone. */
export const AGGRO_RADIUS = 20;
/** Night zombies arrive in the dark this far (Manhattan, roughly) from a survivor, past explored ground. */
export const FRONTIER_MIN = 20;
export const FRONTIER_MAX = 32;

/** night: arrives after dark, leaves at dawn. static: woke up in a ruin (herds.ts). herd: broke off a passing herd. */
export type ZombieKind = 'night' | 'static' | 'herd' | 'raider';
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
  /** Health when it arrived (later nights bring tougher ones). */
  maxHealth: number;
  /** Ticks until the next attack is allowed. */
  cooldown: number;
  /** Re-plan no earlier than this tick (or sooner, when the path runs out). */
  nextThinkTick: number;
  kind: ZombieKind;
  /** Herd zombies: where the herd was going. They carry on there when nobody's near. */
  heading: Point | null;
  /** What sort of zombie (zombieKinds.ts). Raiders are always walkers. */
  variant: Variant;
  /** Screamers: has it screamed yet? */
  screamed: boolean;
}

export function getZombie(state: SimState, id: string): Zombie | undefined {
  return state.zombies.find((z) => z.id === id);
}

export function addZombie(state: SimState, x: number, y: number, health = ZOMBIE_HEALTH, kind: ZombieKind = 'night'): Zombie {
  const z: Zombie = {
    id: `z${state.nextId++}`,
    pos: { x, y },
    move: null,
    path: [],
    targetId: null,
    health,
    maxHealth: health,
    cooldown: 0,
    nextThinkTick: 0,
    kind,
    heading: null,
    variant: 'walker',
    screamed: false,
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

export function zombiesForNight(night: number, colony = 0): number {
  return FIRST_NIGHT_ZOMBIES + night - 1 + Math.floor(colony / SURVIVORS_PER_EXTRA_ZOMBIE);
}

export function zombieHealthForNight(night: number): number {
  return ZOMBIE_HEALTH + ZOMBIE_HEALTH_PER_NIGHT * (night - 1);
}

/** Tonight's total: fixed at nightfall from the colony's size then. */
function tonight(state: SimState, night: number): number {
  return state.zombiesTonight ?? zombiesForNight(night, state.survivors.length);
}

/** From night 4 zombies come in groups: pairs on nights 4-6, threes on 7-9, and so on. */
export function groupSize(night: number): number {
  return Math.ceil(night / 3);
}

/**
 * How many zombies arrive at this tick (0 on most ticks). Groups are spread
 * evenly over the night, half an interval in: night 1 (2 zombies, singly)
 * spawns at 23:00 and 03:00; night 4 (5 zombies) as 2, 2, then 1.
 */
export function spawnsAt(tick: number, total?: number): number {
  const night = nightNumber(tick);
  if (night === null) return 0;
  const count = total ?? zombiesForNight(night);
  const size = groupSize(night);
  const groups = Math.ceil(count / size);
  const interval = Math.floor(NIGHT_TICKS / groups);
  const t = ticksIntoNight(tick) - Math.floor(interval / 2);
  if (t < 0 || t % interval !== 0 || t / interval >= groups) return 0;
  return Math.min(size, count - (t / interval) * size);
}

/** The first tick of a night. */
export function nightFallsNow(tick: number): boolean {
  return nightNumber(tick) !== null && ticksIntoNight(tick) === 0;
}

/** Raiders open unlocked doors; zombies don't open any; crawlers get under barricades. */
export function moverOf(z: Zombie): 'zombie' | 'raider' | 'crawler' {
  return z.kind === 'raider' ? 'raider' : z.variant === 'crawler' ? 'crawler' : 'zombie';
}

export function isSpawnTick(tick: number): boolean {
  return spawnsAt(tick) > 0;
}

/** Spawning, the dawn retreat, and every zombie's move or attack. Runs first each tick. */
export function updateZombies(state: SimState): void {
  if (!isNight(state.tick)) {
    state.zombiesTonight = null;
    // Only the night's arrivals (and raiders) leave; zombies from ruins and herds stay out in the day.
    const leaves = (z: Zombie) => z.kind === 'night' || z.kind === 'raider';
    const n = state.zombies.filter(leaves).length;
    if (n > 0) {
      state.zombies = state.zombies.filter((z) => !leaves(z));
      logEvent(state, `${n} zombie${n === 1 ? '' : 's'} shambled off at dawn`);
    }
    updateAll(state);
    return;
  }
  const night = nightNumber(state.tick)!;
  if (ticksIntoNight(state.tick) === 0) {
    // The storyteller scales it by how the colony is doing (director.ts).
    state.zombiesTonight = Math.max(1, Math.round(zombiesForNight(night, state.survivors.length) * threatScale(state) * siegeFactor(state, night)));
    const siege = siegeFactor(state, night) > 1;
    logEvent(state, `Night ${night} falls: ${state.zombiesTonight} zombies expected${siege ? ' (a SIEGE night)' : ''}`);
  }
  const arriving = Math.min(spawnsAt(state.tick, tonight(state, night)), MAX_ZOMBIES - state.zombies.length);
  if (arriving > 0) spawnGroup(state, arriving, zombieHealthForNight(night));
  updateAll(state);
}

function updateAll(state: SimState): void {
  for (const z of state.zombies) updateZombie(state, z);
  // Static zombies that came to look at a noise stop looking.
  for (const z of state.zombies) if (z.kind === 'static' && z.heading && manhattan(z.pos, z.heading) <= 1) z.heading = null;
  // Herd zombies that reached the herd's destination wander off the map.
  state.zombies = state.zombies.filter((z) => !(z.kind === 'herd' && z.heading && manhattan(z.pos, z.heading) <= 1 && !z.move));
}

/** Where tonight's next group arrives: in the dark past explored ground, or (on a fully explored map) at the edge. */
function spawnPoint(state: SimState): Point | null {
  const lamps = litLamps(state);
  const open = (x: number, y: number) =>
    getTile(state.grid, x, y) === 'floor' &&
    labelAt(state, x, y) === OUTDOORS &&
    !state.survivors.some((s) => manhattan(s.pos, { x, y }) < SPAWN_MIN_DISTANCE) &&
    !lamps.some((l) => manhattan(l, { x, y }) < LAMP_DETER);
  if (state.survivors.length > 0 && state.grid.explored.includes(0)) {
    // A few tries: a spot out past someone, then the nearest dark open tile to it.
    for (let attempt = 0; attempt < 8; attempt++) {
      const s = pick(state, state.survivors);
      const dx = randomInt(state, -FRONTIER_MAX, FRONTIER_MAX + 1);
      const dy = randomInt(state, -FRONTIER_MAX, FRONTIER_MAX + 1);
      if (Math.abs(dx) + Math.abs(dy) < FRONTIER_MIN) continue;
      const near = { x: Math.max(0, Math.min(state.grid.width - 1, s.pos.x + dx)), y: Math.max(0, Math.min(state.grid.height - 1, s.pos.y + dy)) };
      const p = nearestTile(state.grid, near, (x, y) => !isExplored(state, x, y) && open(x, y));
      if (p && manhattan(p, near) <= FRONTIER_MAX) return p;
    }
  }
  const { width, height } = state.grid;
  const edge: Point[] = [];
  for (let x = 0; x < width; x++) edge.push({ x, y: 0 }, { x, y: height - 1 });
  for (let y = 1; y < height - 1; y++) edge.push({ x: 0, y }, { x: width - 1, y });
  const candidates = edge.filter((p) => open(p.x, p.y));
  return candidates.length > 0 ? pick(state, candidates) : null;
}

/** A group of `n` night zombies at one spot (the rest on the nearest free outdoor tiles). */
function spawnGroup(state: SimState, n: number, health: number): void {
  const p = spawnPoint(state);
  if (!p) return;
  const night = nightNumber(state.tick) ?? 1;
  giveVariant(addZombie(state, p.x, p.y, health), night);
  for (let i = 1; i < n; i++) {
    const free = (x: number, y: number) =>
      getTile(state.grid, x, y) === 'floor' && labelAt(state, x, y) === OUTDOORS && !state.zombies.some((z) => z.pos.x === x && z.pos.y === y);
    const q = nearestTile(state.grid, p, free) ?? p;
    giveVariant(addZombie(state, q.x, q.y, health), night);
  }
  logEvent(state, n === 1 ? `A zombie appeared at (${p.x}, ${p.y})` : `A group of ${n} zombies appeared at (${p.x}, ${p.y})`);
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
    // Nobody to hit but a dog: the dog, then.
    const dog = state.dogs.find((d) => manhattan(d.pos, z.pos) <= 1);
    if (dog) {
      if (z.cooldown === 0) {
        hitDog(state, dog, z.kind === 'raider' ? RAIDER_DAMAGE : ZOMBIE_DAMAGE);
        z.cooldown = ZOMBIE_ATTACK_EVERY;
      }
      return;
    }
  }

  if (z.move) {
    z.move.progress += 1;
    if (z.move.progress < z.move.duration) return;
    z.pos = z.move.to;
    z.move = null;
    if (springTrap(state, z)) return;
  }

  if (state.tick >= z.nextThinkTick || z.path.length === 0) {
    plan(state, z);
    z.nextThinkTick = state.tick + REPATH_EVERY;
  }
  const next = z.path[0];
  if (!next) return;
  if (!isPassable(state.grid, next.x, next.y, moverOf(z))) {
    // A door or wall on the way in: batter it. Anything else, re-plan next tick.
    if (isStructure(getTile(state.grid, next.x, next.y)) && manhattan(z.pos, next) === 1) {
      if (z.cooldown === 0) {
        // Raiders sometimes throw a firebomb instead.
        if (z.kind === 'raider' && hashRoll(idNumber(z.id), state.tick) < FIREBOMB_CHANCE && ignite(state, next.x, next.y)) {
          logEvent(state, `Raiders firebombed the ${getTile(state.grid, next.x, next.y)} at (${next.x}, ${next.y})`);
        }
        damageStructure(state, next.x, next.y, ZOMBIE_DAMAGE * VARIANTS[z.variant].battering);
        z.cooldown = ZOMBIE_ATTACK_EVERY;
      }
      return;
    }
    z.path = [];
    return;
  }
  z.path.shift();
  z.move = { from: { ...z.pos }, to: next, progress: 0, duration: VARIANTS[z.variant].ticksPerTile };
}

/**
 * Chase the nearest reachable survivor; failing that, break in to the nearest one; failing
 * that, a herd zombie walks on towards where the herd was going, and others wander. Static and
 * herd zombies only notice survivors within AGGRO_RADIUS.
 */
function plan(state: SimState, z: Zombie): void {
  const { grid, walkabilityVersion: v } = state;
  const goalsFor = (s: Survivor) => [s.pos, ...neighbours4(grid, s.pos.x, s.pos.y)];
  const noticed = z.kind === 'night' ? state.survivors : state.survivors.filter((s) => manhattan(z.pos, s.pos) <= AGGRO_RADIUS);
  const target = noticed
    .slice()
    .sort((a, b) => manhattan(z.pos, a.pos) - manhattan(z.pos, b.pos))
    .find((s) => canReach(grid, v, z.pos, goalsFor(s), moverOf(z)));
  z.targetId = target?.id ?? null;
  if (target && z.variant === 'screamer' && !z.screamed) {
    z.screamed = true;
    logEvent(state, `A screamer shrieked at (${z.pos.x}, ${z.pos.y})!`);
    makeNoise(state, z.pos, 'scream');
  }
  if (target) {
    z.path = findPathToAny(grid, z.pos, goalsFor(target), moverOf(z)) ?? [];
    return;
  }
  // Nobody reachable: go for the nearest survivor through doors, or failing that through walls.
  const victim = noticed.slice().sort((a, b) => manhattan(z.pos, a.pos) - manhattan(z.pos, b.pos))[0];
  if (victim) {
    const path = findPathToAny(grid, z.pos, goalsFor(victim), 'survivor') ?? findPathToAny(grid, z.pos, goalsFor(victim), 'breacher');
    if (path && path.length > 0) {
      z.targetId = victim.id;
      z.path = path;
      return;
    }
  }
  z.targetId = null;
  const options = neighbours4(grid, z.pos.x, z.pos.y).filter((p) => isPassable(grid, p.x, p.y, moverOf(z)));
  if (z.heading) {
    // On with the herd: a path if there is one, else the step that gets closest.
    const path = findPathToAny(grid, z.pos, [z.heading], 'zombie');
    if (path && path.length > 0) {
      z.path = path.slice(0, REPATH_EVERY);
      return;
    }
    const best = options.sort((a, b) => manhattan(a, z.heading!) - manhattan(b, z.heading!))[0];
    z.path = best && manhattan(best, z.heading) < manhattan(z.pos, z.heading) ? [best] : [];
    return;
  }
  z.path = options.length > 0 ? [pick(state, options)] : [];
}

function hit(state: SimState, z: Zombie, s: Survivor): void {
  const damage = z.kind === 'raider' ? RAIDER_DAMAGE : VARIANTS[z.variant].damage;
  const armoured = wearing(state, s, 'armour') !== undefined;
  s.health = Math.max(0, s.health - armourDamage(state, s, hasTrait(s, 'tough') ? Math.round((damage * 3) / 4) : damage));
  s.lastHurtBy = z.kind === 'raider' ? 'a raider' : 'a zombie attack';
  s.hurtTick = state.tick;
  s.note = `attacked by a zombie at (${z.pos.x}, ${z.pos.y})`;
  addThought(state, s, 'attacked');
  if (z.kind !== 'raider') maybeInfect(state, s, z.id, armoured ? INFECTION_CHANCE / 2 : INFECTION_CHANCE);
  z.cooldown = ZOMBIE_ATTACK_EVERY;
}

/** Spikes: TRAP_DAMAGE to a zombie stepping on a trap. A spent trap goes, leaving a plan to rebuild it. Returns true if the zombie died. */
function springTrap(state: SimState, z: Zombie): boolean {
  const trap = furnitureAt(state, z.pos.x, z.pos.y);
  if (trap?.kind !== 'trap' || trap.loot <= 0) return false;
  trap.loot -= 1;
  z.health = Math.max(0, z.health - TRAP_DAMAGE);
  if (trap.loot === 0) {
    removeFurniture(state, trap);
    addBlueprint(state, trap.x, trap.y, 'trap');
    logEvent(state, `A spike trap at (${trap.x}, ${trap.y}) is spent`);
  }
  if (z.health > 0) return false;
  removeZombie(state, z);
  leaveCorpse(state, z.pos, null);
  logEvent(state, 'A spike trap killed a zombie');
  return true;
}

/** A guard's blow. Returns true if the zombie died (and was removed). */
export function damageZombie(state: SimState, z: Zombie, amount: number, by: Survivor): boolean {
  z.health = Math.max(0, z.health - amount);
  if (z.health > 0) return false;
  removeZombie(state, z);
  by.kills += 1;
  maybeSample(state, z.id, z.pos);
  if (by.kills === HARDENED_KILLS) earnTrait(state, by, 'hardened', `${HARDENED_KILLS} kills`);
  if (maybeCapture(state, z, by.name)) return true;
  leaveCorpse(state, z.pos, null);
  logEvent(state, `${by.name} killed a ${z.kind === 'raider' ? 'raider' : 'zombie'}`);
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
