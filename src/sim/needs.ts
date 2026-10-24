// Needs: rest/sleep and hunger/food.
//
// Rest drains while awake. Below TIRED a survivor drops whatever job they
// have (through releaseSurvivor, so nothing is left reserved or carried),
// reserves the nearest reachable free bed (first come, first served), walks
// there, and sleeps until fully rested. With no free reachable bed they
// sleep on the floor where they stand, recovering more slowly.
//
// Satiety drains all the time. Below HUNGRY, if there is reachable free
// food, a survivor drops their job, reserves the nearest food item, walks to
// it, and eats it where it lies. With no food they keep working and look
// again every MEAL_RETRY_TICKS. Eating comes before sleeping; sleepers aren't
// woken to eat.
//
// Anything that goes wrong (bed or food gone, path blocked) falls back to
// re-planning or the floor, so a needy survivor is never stuck.
//
// Constants live in rest.ts and food.ts.

import { EAT_TICKS, HUNGRY, MEAL_RETRY_TICKS, MEAL_VALUE, SATIETY_DRAIN, SATIETY_MAX } from './food';
import { furnitureAt, type Furniture } from './furniture';
import { getItem, removeItem, type Item } from './items';
import { releaseSurvivor } from './jobs';
import { canReach, findPathToAny } from './pathfinding';
import { BED_REGEN, FLOOR_REGEN, REST_DRAIN, REST_MAX, TIRED } from './rest';
import { manhattan, samePoint } from './space';
import type { SimState } from './state';
import { walk, type Survivor } from './survivors';

export function isTired(s: Survivor): boolean {
  return s.rest < TIRED;
}

export function isHungry(s: Survivor): boolean {
  return s.satiety < HUNGRY;
}

function getBed(state: SimState, id: string): Furniture | undefined {
  return state.furniture.find((f) => f.id === id && f.kind === 'bed');
}

/** Drops sleep and meal plans whose bed or food has gone. Runs at the start of each tick. */
export function reconcileNeeds(state: SimState): void {
  for (const s of state.survivors) {
    if (s.meal) {
      const item = getItem(state, s.meal.itemId);
      if (!item || item.reservedBy !== s.id || !item.pos) cancelMeal(state, s, 'food gone');
    }
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

/**
 * Interrupts work for a need before movement, so a tired or hungry hauler
 * doesn't finish a long walk first. Hunger only interrupts if there is food
 * to go to.
 */
export function interruptForNeeds(state: SimState, s: Survivor): void {
  if (!s.jobId || s.sleep || s.meal) return;
  if (isTired(s)) {
    releaseSurvivor(state, s, 'too tired to work');
  } else if (isHungry(s) && state.tick >= s.nextMealTick) {
    if (nearestFreeFood(state, s)) releaseSurvivor(state, s, 'hungry');
    else s.nextMealTick = state.tick + MEAL_RETRY_TICKS;
  }
}

// --- Hunger -----------------------------------------------------------------

/**
 * Runs when the survivor is standing still, before sleep and work. Returns
 * true if eating handled this tick.
 */
export function updateMeal(state: SimState, s: Survivor): boolean {
  if (s.sleep) return false; // sleepers aren't woken to eat
  if (!s.meal && !startMeal(state, s)) return false;
  const meal = s.meal!;
  const item = getItem(state, meal.itemId);
  if (!item?.pos || item.reservedBy !== s.id) {
    cancelMeal(state, s, 'food gone');
    return false;
  }
  if (!samePoint(s.pos, item.pos)) {
    const path = findPathToAny(state.grid, s.pos, [item.pos]);
    if (!path || path.length === 0) {
      cancelMeal(state, s, "can't reach the food");
      s.nextMealTick = state.tick + MEAL_RETRY_TICKS;
      return false;
    }
    walk(s, path, state.walkabilityVersion);
    return true;
  }
  meal.progress += 1;
  if (meal.progress === 1) s.note = `eating at (${item.pos.x}, ${item.pos.y})`;
  if (meal.progress < EAT_TICKS) return true;
  removeItem(state, item);
  s.satiety = Math.min(SATIETY_MAX, s.satiety + MEAL_VALUE);
  s.meal = null;
  s.note = 'ate a meal';
  return true;
}

function startMeal(state: SimState, s: Survivor): boolean {
  if (!isHungry(s) || state.tick < s.nextMealTick) return false;
  const food = nearestFreeFood(state, s);
  if (!food?.pos) {
    s.nextMealTick = state.tick + MEAL_RETRY_TICKS;
    return false;
  }
  if (s.jobId) releaseSurvivor(state, s, 'hungry');
  food.reservedBy = s.id;
  s.meal = { itemId: food.id, progress: 0 };
  s.note = `going to eat at (${food.pos.x}, ${food.pos.y})`;
  return true;
}

/** Nearest unreserved food on the ground that this survivor can reach. */
function nearestFreeFood(state: SimState, s: Survivor): Item | undefined {
  return state.items
    .filter((i): i is Item & { pos: NonNullable<Item['pos']> } => i.kind === 'food' && i.pos !== null && i.reservedBy === null)
    .sort((a, b) => manhattan(s.pos, a.pos) - manhattan(s.pos, b.pos))
    .find((i) => canReach(state.grid, state.walkabilityVersion, s.pos, [i.pos]));
}

/** Gives up the reserved food (if any). Safe to call when not eating. */
export function cancelMeal(state: SimState, s: Survivor, note: string): void {
  if (!s.meal) return;
  const item = getItem(state, s.meal.itemId);
  if (item && item.reservedBy === s.id) item.reservedBy = null;
  s.meal = null;
  s.goal = null;
  s.path = [];
  s.pathVersion = -1;
  s.note = note;
}

/** Satiety drains every tick, awake or asleep. */
export function updateSatiety(s: Survivor): void {
  s.satiety = Math.max(0, s.satiety - SATIETY_DRAIN);
}

// --- Sleep ------------------------------------------------------------------

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
