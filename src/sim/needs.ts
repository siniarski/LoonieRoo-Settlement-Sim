// Needs: rest and sleep.
//
// Rest drains while awake. Below TIRED a survivor drops whatever job they
// have (through releaseSurvivor, so nothing is left reserved or carried),
// reserves the nearest reachable free bed (first come, first served), walks
// there, and sleeps until fully rested. With no free reachable bed they
// sleep on the floor where they stand, recovering more slowly. Anything that
// goes wrong (bed removed, path blocked) falls back to re-planning or the
// floor, so a tired survivor is never stuck.
//
// Constants live in rest.ts.

import { furnitureAt, type Furniture } from './furniture';
import { releaseSurvivor } from './jobs';
import { canReach, findPathToAny } from './pathfinding';
import { BED_REGEN, FLOOR_REGEN, REST_DRAIN, REST_MAX, TIRED } from './rest';
import { manhattan, samePoint } from './space';
import type { SimState } from './state';
import { walk, type Survivor } from './survivors';

export function isTired(s: Survivor): boolean {
  return s.rest < TIRED;
}

function getBed(state: SimState, id: string): Furniture | undefined {
  return state.furniture.find((f) => f.id === id && f.kind === 'bed');
}

/** Drops sleep plans whose bed has gone (removed, flattened). Runs at the start of each tick. */
export function reconcileSleep(state: SimState): void {
  for (const s of state.survivors) {
    if (!s.sleep?.bedId) continue;
    const bed = getBed(state, s.sleep.bedId);
    if (bed && bed.occupant === s.id) continue;
    // Bed gone: re-plan next tick (another bed, or the floor).
    s.sleep = null;
    s.goal = null;
    s.path = [];
    s.pathVersion = -1;
    s.note = 'bed gone';
  }
}

/** Interrupts work for sleep before movement, so a tired hauler doesn't finish a long walk first. */
export function interruptIfTired(state: SimState, s: Survivor): void {
  if (isTired(s) && !s.sleep && s.jobId) releaseSurvivor(state, s, 'too tired to work');
}

/**
 * Runs when the survivor is standing still. Returns true if sleep handled
 * this tick (so the survivor doesn't also look for work).
 */
export function updateSleep(state: SimState, s: Survivor): boolean {
  if (!s.sleep) {
    if (!isTired(s)) return false;
    startSleep(state, s);
    return true;
  }
  const sleep = s.sleep;
  if (sleep.asleep) return true;

  // Walking to a bed and standing still: either arrived, or the walk failed.
  const bed = sleep.bedId ? getBed(state, sleep.bedId) : undefined;
  if (!bed) {
    sleep.bedId = null;
    sleep.asleep = true;
    s.note = 'asleep on the floor';
    return true;
  }
  if (samePoint(s.pos, bed)) {
    sleep.asleep = true;
    s.note = `asleep in bed at (${bed.x}, ${bed.y})`;
    return true;
  }
  const path = findPathToAny(state.grid, s.pos, [bed]);
  if (path && path.length > 0) {
    walk(s, path, state.walkabilityVersion);
    return true;
  }
  // Can't get there any more: give the bed up and sleep here.
  bed.occupant = null;
  s.sleep = { bedId: null, asleep: true };
  s.note = 'no way to a bed, asleep on the floor';
  return true;
}

function startSleep(state: SimState, s: Survivor): void {
  if (s.jobId) releaseSurvivor(state, s, 'too tired to work');
  const bed = nearestFreeBed(state, s);
  if (!bed) {
    s.sleep = { bedId: null, asleep: true };
    s.note = 'no free bed, asleep on the floor';
    return;
  }
  bed.occupant = s.id;
  s.sleep = { bedId: bed.id, asleep: false };
  s.note = `going to bed at (${bed.x}, ${bed.y})`;
  if (samePoint(s.pos, bed)) {
    s.sleep.asleep = true;
    s.note = `asleep in bed at (${bed.x}, ${bed.y})`;
    return;
  }
  const path = findPathToAny(state.grid, s.pos, [bed]);
  if (path && path.length > 0) walk(s, path, state.walkabilityVersion);
}

/** Nearest unoccupied, reachable bed (Manhattan order, ties by furniture order). */
function nearestFreeBed(state: SimState, s: Survivor): Furniture | undefined {
  return state.furniture
    .filter((f) => f.kind === 'bed' && f.occupant === null)
    .sort((a, b) => manhattan(s.pos, a) - manhattan(s.pos, b))
    .find((f) => canReach(state.grid, state.walkabilityVersion, s.pos, [f]));
}

/** Drains or restores rest; wakes the survivor when full. Runs every tick, after movement and work. */
export function updateRest(state: SimState, s: Survivor): void {
  if (!s.sleep?.asleep) {
    s.rest = Math.max(0, s.rest - REST_DRAIN);
    return;
  }
  const inBed = s.sleep.bedId !== null && furnitureAt(state, s.pos.x, s.pos.y)?.id === s.sleep.bedId;
  s.rest = Math.min(REST_MAX, s.rest + (inBed ? BED_REGEN : FLOOR_REGEN));
  if (s.rest >= REST_MAX) wake(state, s, 'woke up rested');
}

/** Ends sleep (or the walk to bed) and frees the bed. Safe to call when awake. */
export function wake(state: SimState, s: Survivor, note: string): void {
  if (!s.sleep) return;
  const bed = s.sleep.bedId ? getBed(state, s.sleep.bedId) : undefined;
  if (bed && bed.occupant === s.id) bed.occupant = null;
  s.sleep = null;
  s.note = note;
}
