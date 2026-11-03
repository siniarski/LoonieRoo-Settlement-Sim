// Watchtowers: furniture a guard stands on.
//
// From a tower a survivor sees TOWER_SIGHT tiles instead of SIGHT_RADIUS and
// shoots TOWER_RANGE_BONUS tiles further. At night, a guard with nothing to
// do climbs the nearest free tower and keeps watch (jobs.ts). Any tower at
// all lets the colony hear herds coming from further off (herds.ts).

import { furnitureAt } from './furniture';
import { findPathToAny } from './pathfinding';
import { manhattan, samePoint } from './space';
import type { SimState } from './state';
import { walk, type Survivor } from './survivors';
import { TICKS_PER_HOUR, isNight } from './time';
import { VIGILANT_HOURS, earnTrait } from './skills';

export const TOWER_SIGHT = 16;
export const TOWER_RANGE_BONUS = 3;
/** Herds are heard this many times further off when the colony has a tower. */
export const TOWER_WARN_FACTOR = 1.5;

export function onTower(state: SimState, s: Survivor): boolean {
  return !s.move && furnitureAt(state, s.pos.x, s.pos.y)?.kind === 'tower';
}

export function hasTower(state: SimState): boolean {
  return state.furniture.some((f) => f.kind === 'tower');
}

/** Hourly, for every survivor: a night up a tower counts towards being vigilant. */
export function countWatch(state: SimState, s: Survivor): void {
  if (state.tick % TICKS_PER_HOUR !== 0 || !isNight(state.tick) || !onTower(state, s)) return;
  s.watchHours += 1;
  if (s.watchHours === VIGILANT_HOURS) earnTrait(state, s, 'vigilant', `${VIGILANT_HOURS} hours on watch`);
}

/**
 * An idle guard at night: stay up the tower, or head for the nearest free one.
 * Returns true if that's what they're doing.
 */
export function keepWatch(state: SimState, s: Survivor): boolean {
  if (!isNight(state.tick) || s.priorities.guard === 0) return false;
  if (onTower(state, s)) {
    s.note = 'keeping watch';
    return true;
  }
  const taken = (x: number, y: number) =>
    state.survivors.some((o) => o !== s && ((o.pos.x === x && o.pos.y === y) || (o.goal?.x === x && o.goal.y === y)));
  const towers = state.furniture
    .filter((f) => f.kind === 'tower' && !taken(f.x, f.y))
    .sort((a, b) => manhattan(s.pos, a) - manhattan(s.pos, b));
  for (const t of towers.slice(0, 4)) {
    if (samePoint(s.pos, t)) continue;
    const path = findPathToAny(state.grid, s.pos, [t]);
    if (!path || path.length === 0) continue;
    walk(s, path, state.walkabilityVersion);
    s.note = `climbing the watchtower at (${t.x}, ${t.y})`;
    return true;
  }
  return false;
}
