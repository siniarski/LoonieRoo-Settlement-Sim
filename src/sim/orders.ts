// Group orders: what the people with you do when you aren't running a colony.
//
//   follow  keep close to whoever you're steering (control.ts), a couple of tiles behind
//   hold    stand at a spot and keep it
//   loot    search every crate in a room (or around a spot outdoors), then come back to you
//   settle  (no order) the colony sim: jobs by priority, as usual
//
// Under an order a survivor still eats, drinks, treats wounds, arms and sleeps,
// but only takes the jobs that keep people alive (guarding, firefighting, doctoring),
// and only near where they're meant to be. No reading or rec-room visits either.
// Following with nobody under control means waiting where you are. Followers hurry when
// they fall behind, and step out of the way when they're standing in front of you.

import { controlled } from './control';
import type { Point } from './grid';
import type { JobType } from './jobTypes';
import { findPath, findPathToAny, isPassable } from './pathfinding';
import { neighbours4 } from './grid';
import type { Furniture } from './furniture';
import { roomAt } from './rooms';
import { SCAVENGE_WORK } from './jobs';
import { scavengeFind } from './work';
import { spawnFind } from './difficulty';
import { random } from './rng';
import type { SimState } from './state';
import { walk, type Survivor } from './survivors';

export type Order =
  | { kind: 'follow' }
  | { kind: 'hold'; x: number; y: number }
  | { kind: 'loot'; x: number; y: number; crateId: string | null; progress: number };

/** The only job types taken under an order. */
export const ORDER_JOBS: readonly JobType[] = ['guard', 'firefight', 'doctor'];
/** Jobs further than this (Manhattan) from where they're meant to be are left alone. */
export const ORDER_REACH = 8;
/** Followers start walking once they're this far behind. */
export const FOLLOW_GAP = 3;
/** Shouting "follow me" reaches people this close. */
export const SHOUT_RANGE = 12;
/** Followers further behind than this walk a tick quicker per tile. */
export const HURRY_GAP = FOLLOW_GAP + 3;
/** Followers keep off this many tiles straight in front of you. */
export const FIRE_LANE = 4;
/** Outdoors, looting takes the crates within this many tiles of the spot. */
export const LOOT_RADIUS = 5;

/** Where an ordered survivor is meant to be, or null with no order (or nobody to follow). */
export function orderAnchor(state: SimState, s: Survivor): Point | null {
  if (!s.order) return null;
  if (s.order.kind !== 'follow') return { x: s.order.x, y: s.order.y };
  const leader = controlled(state);
  return leader && leader.id !== s.id ? leader.pos : null;
}

/** Can this survivor take a job at (x, y) under their order? */
export function withinOrder(state: SimState, s: Survivor, p: Point): boolean {
  if (!s.order) return true;
  const at = orderAnchor(state, s) ?? s.pos;
  return Math.abs(p.x - at.x) + Math.abs(p.y - at.y) <= ORDER_REACH;
}

/**
 * Runs when an ordered survivor is standing still with no need pressing:
 * walk back into place if they've drifted. Returns true if that took the tick.
 */
export function keepOrder(state: SimState, s: Survivor): boolean {
  if (!s.order || s.jobId) return false;
  if (s.order.kind === 'loot') return loot(state, s, s.order);
  const at = orderAnchor(state, s);
  if (!at) return false;
  const gap = s.order.kind === 'follow' ? FOLLOW_GAP : 0;
  const far = Math.max(Math.abs(at.x - s.pos.x), Math.abs(at.y - s.pos.y));
  if (far <= gap) return s.order.kind === 'follow' && stepAside(state, s);
  const path = findPath(state.grid, s.pos, at);
  if (!path || path.length === 0) {
    s.note = s.order.kind === 'follow' ? "can't reach you" : "can't get back to their post";
    return false;
  }
  // Followers stop a step short, so they don't stand on your toes.
  walk(s, s.order.kind === 'follow' ? path.slice(0, -1) : path, state.walkabilityVersion);
  s.note = s.order.kind === 'follow' ? 'following you' : 'going back to their post';
  return true;
}

export function describeOrder(o: Order | null): string {
  return !o ? 'settled' : o.kind === 'follow' ? 'following' : o.kind === 'loot' ? `looting (${o.x}, ${o.y})` : `holding (${o.x}, ${o.y})`;
}

/** A follower falling behind hurries (stepPace in survivors.ts). */
export function hurries(state: SimState, s: Survivor): boolean {
  if (s.order?.kind !== 'follow') return false;
  const leader = controlled(state);
  return leader !== undefined && leader !== s && Math.max(Math.abs(leader.pos.x - s.pos.x), Math.abs(leader.pos.y - s.pos.y)) > HURRY_GAP;
}

/** Standing in your line of fire: step to a free tile beside it. Returns true if they moved. */
function stepAside(state: SimState, s: Survivor): boolean {
  const c = state.control;
  const leader = controlled(state);
  if (!c || !leader) return false;
  const lane = (p: Point) => {
    for (let k = 1; k <= FIRE_LANE; k++) if (p.x === leader.pos.x + c.facing.x * k && p.y === leader.pos.y + c.facing.y * k) return true;
    return false;
  };
  if (!lane(s.pos)) return false;
  const side = [{ x: -c.facing.y, y: c.facing.x }, { x: c.facing.y, y: -c.facing.x }]
    .map((d) => ({ x: s.pos.x + Math.sign(d.x), y: s.pos.y + Math.sign(d.y) }))
    .find((p) => isPassable(state.grid, p.x, p.y, 'survivor') && !lane(p));
  if (!side) return false;
  walk(s, [side], state.walkabilityVersion);
  s.note = 'out of your line of fire';
  return true;
}

/** The crates left to loot for a loot order: in the same room as the spot, or around it outdoors. */
function crates(state: SimState, o: { x: number; y: number }): Furniture[] {
  const room = roomAt(state, o.x, o.y);
  return state.furniture.filter(
    (f) => f.kind === 'crate' && f.loot > 0 && (room ? roomAt(state, f.x, f.y)?.index === room.index : Math.max(Math.abs(f.x - o.x), Math.abs(f.y - o.y)) <= LOOT_RADIUS),
  );
}

/** One tick of a loot order: walk to the next crate and search it; with none left, follow again. */
function loot(state: SimState, s: Survivor, o: Extract<Order, { kind: 'loot' }>): boolean {
  const left = crates(state, o);
  let crate = left.find((f) => f.id === o.crateId);
  if (!crate) {
    crate = left.sort((a, b) => Math.abs(a.x - s.pos.x) + Math.abs(a.y - s.pos.y) - (Math.abs(b.x - s.pos.x) + Math.abs(b.y - s.pos.y)))[0];
    o.crateId = crate?.id ?? null;
    o.progress = 0;
  }
  if (!crate) {
    s.order = { kind: 'follow' };
    s.note = 'nothing left to loot, coming back';
    return false;
  }
  if (Math.abs(crate.x - s.pos.x) + Math.abs(crate.y - s.pos.y) !== 1) {
    const path = findPathToAny(state.grid, s.pos, neighbours4(state.grid, crate.x, crate.y));
    if (!path || path.length === 0) {
      s.order = { kind: 'follow' };
      s.note = "can't get to the crates";
      return false;
    }
    walk(s, path, state.walkabilityVersion);
    s.note = `going to loot (${crate.x}, ${crate.y})`;
    return true;
  }
  o.progress += 1;
  s.note = 'searching';
  if (o.progress < SCAVENGE_WORK) return true;
  o.progress = 0;
  const kind = spawnFind(state, scavengeFind(random(state), crate.table), s.pos.x, s.pos.y).kind;
  crate.loot -= 1;
  if (crate.loot === 0) crate.scavenge = false;
  s.note = `found ${kind}`;
  return true;
}
