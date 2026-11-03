// Group orders: what the people with you do when you aren't running a colony.
//
//   follow  keep close to whoever you're steering (control.ts), a couple of tiles behind
//   hold    stand at a spot and keep it
//   settle  (no order) the colony sim: jobs by priority, as usual
//
// Under an order a survivor still eats, drinks, treats wounds, arms and sleeps,
// but only takes the jobs that keep people alive (guarding, firefighting, doctoring),
// and only near where they're meant to be. No reading or rec-room visits either.
// Following with nobody under control means waiting where you are.

import { controlled } from './control';
import type { Point } from './grid';
import type { JobType } from './jobTypes';
import { findPath } from './pathfinding';
import type { SimState } from './state';
import { walk, type Survivor } from './survivors';

export type Order = { kind: 'follow' } | { kind: 'hold'; x: number; y: number };

/** The only job types taken under an order. */
export const ORDER_JOBS: readonly JobType[] = ['guard', 'firefight', 'doctor'];
/** Jobs further than this (Manhattan) from where they're meant to be are left alone. */
export const ORDER_REACH = 8;
/** Followers start walking once they're this far behind. */
export const FOLLOW_GAP = 3;
/** Shouting "follow me" reaches people this close. */
export const SHOUT_RANGE = 12;

/** Where an ordered survivor is meant to be, or null with no order (or nobody to follow). */
export function orderAnchor(state: SimState, s: Survivor): Point | null {
  if (!s.order) return null;
  if (s.order.kind === 'hold') return { x: s.order.x, y: s.order.y };
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
  const at = orderAnchor(state, s);
  if (!at) return false;
  const gap = s.order.kind === 'follow' ? FOLLOW_GAP : 0;
  const far = Math.max(Math.abs(at.x - s.pos.x), Math.abs(at.y - s.pos.y));
  if (far <= gap) return false;
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
  return !o ? 'settled' : o.kind === 'follow' ? 'following' : `holding (${o.x}, ${o.y})`;
}
