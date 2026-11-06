// Foraging for colonists: going hungry with no food to go to, they look for some themselves.
//
// A colonist with no job, by day, when the colony has less than a meal each lying about (free food,
// reachable or not; generated worlds only) or they're hungry with none to reach, walks to the nearest open
// grass or old field (outdoors, not picked over today: handiwork.ts canForage) within
// FORAGE_SEARCH tiles and forages there for FORAGE_TICKS (on slowTick ticks, like work). What
// turns up is dropped at their feet, and they eat it as they would any food (needs.ts). One who
// is hungry (below FORAGE_HUNGER) drops their job to do it (needs.ts interruptForNeeds).
// It's subsistence, not stockpiling: they stop once there's a meal each, and a tile gives once a day.
// Not at night, and not in winter. When there's nowhere to go they look again after
// FORAGE_RETRY_TICKS (survivor.nextForageTick).

import { GROUNDS, isWalkable, tileIndex } from './grid';
import { FORAGE_TICKS, canForage, finishForage } from './handiwork';
import { HUNGRY } from './food';
import { canReach, findPathToAny } from './pathfinding';
import { isIndoors } from './rooms';
import { seasonOf } from './seasons';
import { nearestTile, samePoint } from './space';
import type { SimState } from './state';
import { walk, type Survivor } from './survivors';
import { dayOf, isNight, slowTick } from './time';

/** How far (tiles searched) a colonist looks for somewhere to forage. */
export const FORAGE_SEARCH = 1500;
export const FORAGE_RETRY_TICKS = 200;
/** Below this (hungry), a colonist with no food to go to drops their job to forage by day. */
export const FORAGE_HUNGER = HUNGRY;

/** Is it a time anyone can forage at all (by day, not in winter)? */
export function forageTime(state: SimState): boolean {
  return !isNight(state.tick) && seasonOf(state.tick) !== 'winter';
}

/** Where this colonist can go to forage: the nearest open grass or field not picked over today, or null. */
function spot(state: SimState, s: Survivor): { x: number; y: number } | null {
  const { grid } = state;
  const today = dayOf(state.tick);
  const open = (x: number, y: number) => {
    const g = GROUNDS[grid.ground[tileIndex(grid, x, y)]!];
    return (g === 'grass' || g === 'field') && isWalkable(grid, x, y) && state.foraged[String(tileIndex(grid, x, y))] !== today && !isIndoors(state, x, y);
  };
  const p = nearestTile(grid, s.pos, open, FORAGE_SEARCH);
  return p && canReach(grid, state.walkabilityVersion, s.pos, [p]) ? p : null;
}

/**
 * Runs with the other needs, while standing still. Returns true if foraging took this tick.
 * `hasFood`: is there food they could go and eat (then they don't forage)?
 */
export function updateForage(state: SimState, s: Survivor, hasFood: () => boolean): boolean {
  if (s.sleep) return false;
  const f = s.forage;
  if (f) {
    if (!forageTime(state)) {
      s.forage = null;
      return false;
    }
    if (!samePoint(s.pos, f)) {
      const path = findPathToAny(state.grid, s.pos, [f]);
      if (!path || path.length === 0) {
        s.forage = null;
        return false;
      }
      walk(s, path, state.walkabilityVersion);
      s.note = `going to forage at (${f.x}, ${f.y})`;
      return true;
    }
    if (canForage(state, s) !== null) {
      s.forage = null;
      return false;
    }
    if (slowTick(state.tick)) f.progress += 1;
    s.note = 'foraging';
    if (f.progress < FORAGE_TICKS) return true;
    s.note = finishForage(state, s);
    s.forage = null;
    return true;
  }
  if (s.jobId || !forageTime(state) || state.tick < (s.nextForageTick ?? 0)) return false;
  // (Generated worlds only: the harsh rules. On a hand-made map, only the hungry go.)
  const short = state.harsh && state.items.filter((i) => (i.kind === 'food' || i.kind === 'meal') && i.pos !== null && i.reservedBy === null).length < state.survivors.length;
  if (!short && (s.satiety >= HUNGRY || hasFood())) return false;
  const p = spot(state, s);
  if (!p) {
    s.nextForageTick = state.tick + FORAGE_RETRY_TICKS;
    return false;
  }
  s.forage = { x: p.x, y: p.y, progress: 0 };
  s.note = `going to forage at (${p.x}, ${p.y})`;
  return true;
}
