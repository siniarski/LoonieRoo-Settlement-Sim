// Fog of war and exploring.
//
// grid.explored marks tiles some survivor has seen (within SIGHT_RADIUS).
// Scenario maps start fully explored; generated worlds start dark except
// round the camp. Nothing can be designated on unexplored ground, but
// survivors may walk into it, and that is how it gets explored.
//
// Explore areas are boxes the player wants scouted. Each gets one scout job
// (jobs.ts), which keeps walking to the nearest unexplored walkable tile in
// the box until none is left; then the area is done and removed.

import { inBounds, isWalkable, tileIndex, type Point } from './grid';
import { canReach } from './pathfinding';
import { manhattan } from './space';
import type { SimState } from './state';

export const SIGHT_RADIUS = 8;
/** Explore boxes are capped at this many tiles a side. */
export const MAX_EXPLORE_SIDE = 64;

export interface ExploreArea {
  id: string;
  x0: number;
  y0: number;
  x1: number;
  y1: number;
}

export function isExplored(state: SimState, x: number, y: number): boolean {
  return inBounds(state.grid, x, y) && state.grid.explored[tileIndex(state.grid, x, y)] === 1;
}

export function revealAround(state: SimState, p: Point, r = SIGHT_RADIUS): void {
  const { grid } = state;
  for (let y = Math.max(0, p.y - r); y <= Math.min(grid.height - 1, p.y + r); y++) {
    for (let x = Math.max(0, p.x - r); x <= Math.min(grid.width - 1, p.x + r); x++) {
      if ((x - p.x) ** 2 + (y - p.y) ** 2 <= r * r) grid.explored[y * grid.width + x] = 1;
    }
  }
}

/** Everyone sees around them, every tick. */
export function updateFog(state: SimState): void {
  for (const s of state.survivors) revealAround(state, s.pos);
}

export function getExploreArea(state: SimState, id: string): ExploreArea | undefined {
  return state.exploreAreas.find((a) => a.id === id);
}

/** Unexplored, walkable tiles in the area. */
function unexploredIn(state: SimState, a: ExploreArea): Point[] {
  const out: Point[] = [];
  for (let y = a.y0; y <= a.y1; y++) {
    for (let x = a.x0; x <= a.x1; x++) if (!isExplored(state, x, y) && isWalkable(state.grid, x, y)) out.push({ x, y });
  }
  return out;
}

/** The nearest unexplored tile in the area that `from` can walk to, or null when the area is done. */
export function scoutTarget(state: SimState, a: ExploreArea, from: Point): Point | null {
  const targets = unexploredIn(state, a).sort((p, q) => manhattan(from, p) - manhattan(from, q) || p.y - q.y || p.x - q.x);
  return targets.find((p) => canReach(state.grid, state.walkabilityVersion, from, [p])) ?? null;
}

/** Drops areas with nothing left to see. */
export function reconcileExploreAreas(state: SimState): void {
  state.exploreAreas = state.exploreAreas.filter((a) => unexploredIn(state, a).length > 0);
}
