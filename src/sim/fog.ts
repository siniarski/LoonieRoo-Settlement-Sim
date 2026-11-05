// Fog of war and exploring.
//
// grid.explored marks tiles some survivor has seen: what is in their field of view (sight.ts:
// walls and shut doors hide what is behind them, glass doesn't, and night shortens it).
// Scenario maps start fully explored; generated worlds start dark except
// round the camp. Nothing can be designated on unexplored ground, but
// survivors may walk into it, and that is how it gets explored.
//
// Explore areas are boxes the player wants scouted. Each gets one scout job
// (jobs.ts), which keeps walking to the nearest unexplored walkable tile in
// the box until none is left; then the area is done and removed. Scouts
// head home at SCOUT_HOME_HOUR and go out again in the morning. A newly
// seen crate with loot marks its ruin as a point of interest.

import { inBounds, isWalkable, tileIndex, type Point } from './grid';
import { logEvent } from './events';
import { furnitureAt } from './furniture';
import { canReach } from './pathfinding';
import { hourOf, isNight } from './time';
import { ensureGeneratedAround } from './worldgen';
import { manhattan } from './space';
import { onTower } from './towers';
import { SIGHT_RADIUS, sightOf, sightRadius } from './sight';
import type { SimState } from './state';

export { SIGHT_RADIUS } from './sight';
/** Explore boxes are capped at this many tiles a side. */
export const MAX_EXPLORE_SIDE = 64;
/** Scouts stop and head home at this hour (night falls at NIGHT_START), and set out again at dawn. */
export const SCOUT_HOME_HOUR = 19;
/** One point of interest per this many tiles: a ruin is marked once, not per crate. */
const POI_SPACING = 10;

/** A marker for something found while exploring (shown on the minimap). */
export interface PointOfInterest {
  id: string;
  x: number;
  y: number;
  label: string;
}

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

/** Reveals a whole circle, walls or not (the camp at the start). */
export function revealAround(state: SimState, p: Point, r = SIGHT_RADIUS): void {
  const { grid } = state;
  for (let y = Math.max(0, p.y - r); y <= Math.min(grid.height - 1, p.y + r); y++) {
    for (let x = Math.max(0, p.x - r); x <= Math.min(grid.width - 1, p.x + r); x++) {
      const i = y * grid.width + x;
      if (grid.explored[i] === 1 || (x - p.x) ** 2 + (y - p.y) ** 2 > r * r) continue;
      grid.explored[i] = 1;
      noteFind(state, x, y);
    }
  }
}

/** A newly seen crate with loot marks its ruin (once per ruin). */
function noteFind(state: SimState, x: number, y: number): void {
  const f = furnitureAt(state, x, y);
  if (f?.kind !== 'crate' || f.loot === 0) return;
  if (state.pointsOfInterest.some((p) => manhattan(p, { x, y }) <= POI_SPACING)) return;
  state.pointsOfInterest.push({ id: `p${state.nextId++}`, x, y, label: 'ruin with crates' });
  logEvent(state, `Found a ruin with crates at (${x}, ${y})`);
}

/** Drops markers whose crates have all been searched. */
export function reconcilePointsOfInterest(state: SimState): void {
  if (state.tick % 50 !== 0 || state.pointsOfInterest.length === 0) return;
  const crates = state.furniture.filter((f) => f.kind === 'crate' && f.loot > 0);
  state.pointsOfInterest = state.pointsOfInterest.filter((p) => crates.some((c) => manhattan(c, p) <= POI_SPACING));
}

/** Late in the day: scouts stop exploring until morning. */
export function scoutingOver(tick: number): boolean {
  return isNight(tick) || hourOf(tick) >= SCOUT_HOME_HOUR;
}

/** What each survivor last looked at: skip the look when nothing it depends on has changed. */
const looked = new WeakMap<SimState, Record<string, string>>();

/**
 * Everyone sees what is in their field of view, every tick. A look is skipped when the
 * survivor, how far they see, the walls and the open doors are all as they were last time
 * (the same field, all of it already explored), so the outcome never depends on the cache.
 */
export function updateFog(state: SimState): void {
  const { grid } = state;
  const last = looked.get(state) ?? {};
  looked.set(state, last);
  let doors: string | undefined;
  for (const s of state.survivors) {
    ensureGeneratedAround(state, s.pos); // the world exists well ahead of what anyone sees
    const r = sightRadius(state, s);
    doors ??= grid.open.join(',') + '|' + grid.edgeOpen.join(',');
    const key = `${s.pos.x},${s.pos.y},${r},${onTower(state, s)},${state.walkabilityVersion},${doors}`;
    if (last[s.id] === key) continue;
    last[s.id] = key;
    for (const i of sightOf(state, s, (j) => grid.explored[j] === 1)) {
      grid.explored[i] = 1;
      noteFind(state, i % grid.width, Math.floor(i / grid.width));
    }
  }
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
