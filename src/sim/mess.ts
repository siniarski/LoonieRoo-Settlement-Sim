// The mess hall: a room with tables. A seat is a free floor tile next to a
// table in a complete mess hall. Hungry survivors carry their food to the
// nearest free seat and eat there (a mood boost; see mood.ts). One eater per seat.

import { neighbours4, isWalkable, type Point } from './grid';
import { furnitureAt, ofKind } from './furniture';
import { canReach } from './pathfinding';
import { completeRooms, labelAt } from './rooms';
import { manhattan, samePoint } from './space';
import type { SimState } from './state';
import type { Survivor } from './survivors';

/** Is this tile a seat that `s` may use (nobody else is headed for or sitting at it)? */
export function seatUsable(state: SimState, s: Survivor, p: Point): boolean {
  return isSeat(state, p, completeRooms(state, 'messhall')) && !seatTaken(state, s, p);
}

function isSeat(state: SimState, p: Point, halls: Set<number>): boolean {
  const room = labelAt(state, p.x, p.y);
  if (!halls.has(room) || !isWalkable(state.grid, p.x, p.y)) return false;
  if (furnitureAt(state, p.x, p.y)) return false;
  return ofKind(state, 'table').some((f) => manhattan(f, p) === 1 && labelAt(state, f.x, f.y) === room);
}

function seatTaken(state: SimState, s: Survivor, p: Point): boolean {
  return state.survivors.some((o) => o !== s && o.meal?.seat != null && samePoint(o.meal.seat, p));
}

/** Nearest free seat reachable from `from` (where the food is), or null if there is none. */
export function pickSeat(state: SimState, s: Survivor, from: Point): Point | null {
  const halls = completeRooms(state, 'messhall');
  if (halls.size === 0) return null;
  const seats: Point[] = [];
  for (const index of [...halls].sort((a, b) => a - b)) {
    for (const table of ofKind(state, 'table')) {
      if (labelAt(state, table.x, table.y) !== index) continue;
      for (const p of neighbours4(state.grid, table.x, table.y)) {
        if (!seats.some((q) => samePoint(q, p)) && isSeat(state, p, halls) && !seatTaken(state, s, p)) seats.push(p);
      }
    }
  }
  seats.sort((a, b) => manhattan(from, a) - manhattan(from, b) || a.y - b.y || a.x - b.x);
  return seats.find((p) => canReach(state.grid, state.walkabilityVersion, from, [p])) ?? null;
}
