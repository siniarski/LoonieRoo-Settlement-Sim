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

import { isCold } from './seasons';
import { EAT_TICKS, HOT_MEAL_VALUE, HUNGRY, MEAL_RETRY_TICKS, MEAL_VALUE, SATIETY_DRAIN, SATIETY_MAX } from './food';
import { logEvent } from './events';
import { furnitureAt, type Furniture } from './furniture';
import {
  AWAKE_HEAL_EVERY,
  BANDAGE_HEAL,
  HEALTH_MAX,
  SLEEP_HEAL,
  STARVE_DAMAGE,
  TREAT_RETRY_TICKS,
  TREAT_TICKS,
  isCritical,
  isHurt,
} from './health';
import { dropItem, getItem, removeItem, type Item, type ItemKind } from './items';
import { pickSeat, seatUsable } from './mess';
import { addThought, mournDeath } from './mood';
import { getJob, releaseSurvivor } from './jobs';
import { canReach, findPathToAny } from './pathfinding';
import { BED_REGEN, FLOOR_REGEN, NIGHT_TIRED, REST_DRAIN, REST_MAX, TIRED } from './rest';
import { manhattan, samePoint } from './space';
import type { SimState } from './state';
import { isNight } from './time';
import { zombieNear } from './zombies';
import { walk, type Survivor } from './survivors';
import { cancelArming, dropWeapon, reconcileArming } from './weapons';
import { INFIRMARY_HEAL_FACTOR, applyRemedy, inInfirmary, isBarracksBed, isGuard, isInfirmaryBed, remediesFor } from './medicine';

export function isTired(state: SimState, s: Survivor): boolean {
  return s.rest < (isNight(state.tick) ? NIGHT_TIRED : TIRED);
}

/**
 * A guard finishes the fight: needs don't pull them off a guard job. (Without
 * this, a tired guard flip-flops between hunting and bed as the zombie drifts
 * in and out of DANGER_RADIUS.)
 */
export function onGuardDuty(state: SimState, s: Survivor): boolean {
  // ...unless critically hurt: then they fall back to bed.
  return !isCritical(s.health) && s.jobId !== null && getJob(state, s.jobId)?.type === 'guard';
}

/**
 * Tired with no zombie close enough to make lying down a bad idea, or
 * critically hurt (then bed is where they recover, zombies or not).
 */
export function wantsSleep(state: SimState, s: Survivor): boolean {
  return (isTired(state, s) && !zombieNear(state, s.pos)) || isCritical(s.health);
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
      if (!item || item.reservedBy !== s.id || (!item.pos && item.carriedBy !== s.id)) cancelMeal(state, s, 'food gone');
    }
    if (s.treatment) {
      const item = getItem(state, s.treatment.itemId);
      if (!item || item.reservedBy !== s.id || !item.pos) cancelTreatment(state, s, 'bandage gone');
    }
    reconcileArming(state, s);
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
  if (!s.jobId || s.sleep || s.meal || s.treatment || onGuardDuty(state, s)) return;
  if (isCritical(s.health)) {
    releaseSurvivor(state, s, 'too badly hurt to work');
  } else if (wantsSleep(state, s)) {
    releaseSurvivor(state, s, 'too tired to work');
  } else if (state.tick >= s.nextTreatTick && nearestRemedy(state, s)) {
    releaseSurvivor(state, s, 'going to treat wounds');
  } else if (isHungry(s) && state.tick >= s.nextMealTick) {
    if (nearestFreeItem(state, s, 'food')) releaseSurvivor(state, s, 'hungry');
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
  if (!s.meal && onGuardDuty(state, s)) return false; // eat after the fight
  if (!s.meal && !startMeal(state, s)) return false;
  const meal = s.meal!;
  const item = getItem(state, meal.itemId);
  if (!item || item.reservedBy !== s.id || (!item.pos && item.carriedBy !== s.id)) {
    cancelMeal(state, s, 'food gone');
    return false;
  }
  if (item.pos) {
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
    if (meal.seat && seatUsable(state, s, meal.seat)) {
      // Pick it up and take it to the table.
      item.pos = null;
      item.carriedBy = s.id;
      s.carrying = item.id;
      s.note = `taking food to the mess hall at (${meal.seat.x}, ${meal.seat.y})`;
    } else {
      meal.seat = null;
    }
  }
  if (meal.seat && meal.progress === 0) {
    // Carrying it: head for the seat. If the seat stopped being one, eat right here.
    const path = seatUsable(state, s, meal.seat) ? findPathToAny(state.grid, s.pos, [meal.seat]) : null;
    if (!path) meal.seat = null;
    else if (path.length > 0) {
      walk(s, path, state.walkabilityVersion);
      return true;
    }
  }
  meal.progress += 1;
  if (meal.progress === 1) s.note = `eating ${meal.seat ? 'at a table' : 'standing'} at (${s.pos.x}, ${s.pos.y})`;
  if (meal.progress < EAT_TICKS) return true;
  if (s.carrying === item.id) s.carrying = null;
  removeItem(state, item);
  const hot = item.kind === 'meal';
  s.satiety = Math.min(SATIETY_MAX, s.satiety + (hot ? HOT_MEAL_VALUE : MEAL_VALUE));
  addThought(state, s, meal.seat ? 'ateAtTable' : 'ateWithoutTable');
  if (hot) addThought(state, s, 'hotMeal');
  s.note = meal.seat ? 'ate a meal at a table' : 'ate a meal';
  s.meal = null;
  return true;
}

function startMeal(state: SimState, s: Survivor): boolean {
  if (!isHungry(s) || state.tick < s.nextMealTick) return false;
  // A hot meal if there's one to be had, else raw food.
  const food = nearestFreeItem(state, s, 'meal') ?? nearestFreeItem(state, s, 'food');
  if (!food?.pos) {
    s.nextMealTick = state.tick + MEAL_RETRY_TICKS;
    return false;
  }
  if (s.jobId) releaseSurvivor(state, s, 'hungry');
  cancelArming(state, s, 'hungry');
  cancelTreatment(state, s, 'hungry'); // eat first; the bandage can wait
  food.reservedBy = s.id;
  s.meal = { itemId: food.id, progress: 0, seat: pickSeat(state, s, food.pos) };
  s.note = `going to eat at (${food.pos.x}, ${food.pos.y})`;
  return true;
}

/** Nearest unreserved item of a kind, on the ground, that this survivor can reach. */
export function nearestFreeItem(state: SimState, s: Survivor, kind: ItemKind): Item | undefined {
  return state.items
    .filter((i): i is Item & { pos: NonNullable<Item['pos']> } => i.kind === kind && i.pos !== null && i.reservedBy === null)
    .sort((a, b) => manhattan(s.pos, a.pos) - manhattan(s.pos, b.pos))
    .find((i) => canReach(state.grid, state.walkabilityVersion, s.pos, [i.pos]));
}

/** Gives up the reserved food (if any). Safe to call when not eating. */
export function cancelMeal(state: SimState, s: Survivor, note: string): void {
  if (!s.meal) return;
  const item = getItem(state, s.meal.itemId);
  if (item && s.carrying === item.id) {
    // Was carrying it to the mess hall: put it down here.
    s.carrying = null;
    dropItem(state, item, s.pos);
  }
  if (item && item.reservedBy === s.id) item.reservedBy = null;
  s.meal = null;
  s.goal = null;
  s.path = [];
  s.pathVersion = -1;
  s.note = note;
}

// --- Treating wounds -------------------------------------------------------

/**
 * Runs when standing still, after eating and before sleep. A hurt survivor
 * walks to the nearest free bandage and applies it where it lies.
 */
export function updateTreatment(state: SimState, s: Survivor): boolean {
  if (s.sleep) return false;
  if (!s.treatment && onGuardDuty(state, s)) return false; // after the fight
  if (!s.treatment && !startTreatment(state, s)) return false;
  const treatment = s.treatment!;
  const item = getItem(state, treatment.itemId);
  if (!item?.pos || item.reservedBy !== s.id) {
    cancelTreatment(state, s, 'bandage gone');
    return false;
  }
  if (!samePoint(s.pos, item.pos)) {
    const path = findPathToAny(state.grid, s.pos, [item.pos]);
    if (!path || path.length === 0) {
      cancelTreatment(state, s, "can't reach the bandage");
      s.nextTreatTick = state.tick + TREAT_RETRY_TICKS;
      return false;
    }
    walk(s, path, state.walkabilityVersion);
    return true;
  }
  treatment.progress += 1;
  if (treatment.progress === 1) s.note = `bandaging wounds at (${item.pos.x}, ${item.pos.y})`;
  if (treatment.progress < TREAT_TICKS) return true;
  removeItem(state, item);
  applyRemedy(s, item.kind);
  s.treatment = null;
  s.note = item.kind === 'medicine' ? 'took medicine for the infection' : 'treated wounds with a bandage';
  return true;
}

/** The nearest free thing that would help: medicine if infected, else a bandage if hurt. */
function nearestRemedy(state: SimState, s: Survivor): Item | undefined {
  for (const kind of remediesFor(s)) {
    const found = nearestFreeItem(state, s, kind);
    if (found) return found;
  }
  return undefined;
}

function startTreatment(state: SimState, s: Survivor): boolean {
  if (remediesFor(s).length === 0 || state.tick < s.nextTreatTick) return false;
  const bandage = nearestRemedy(state, s);
  if (!bandage?.pos) {
    s.nextTreatTick = state.tick + TREAT_RETRY_TICKS;
    return false;
  }
  if (s.jobId) releaseSurvivor(state, s, 'going to treat wounds');
  cancelArming(state, s, 'going to treat wounds');
  bandage.reservedBy = s.id;
  s.treatment = { itemId: bandage.id, progress: 0 };
  s.note = `going for ${bandage.kind === 'medicine' ? 'medicine' : 'a bandage'} at (${bandage.pos.x}, ${bandage.pos.y})`;
  return true;
}

/** Gives up the reserved bandage (if any). Safe to call when not treating. */
export function cancelTreatment(state: SimState, s: Survivor, note: string): void {
  if (!s.treatment) return;
  const item = getItem(state, s.treatment.itemId);
  if (item && item.reservedBy === s.id) item.reservedBy = null;
  s.treatment = null;
  s.goal = null;
  s.path = [];
  s.pathVersion = -1;
  s.note = note;
}

/** Satiety drains every tick, awake or asleep. */
export function updateSatiety(s: Survivor): void {
  s.satiety = Math.max(0, s.satiety - SATIETY_DRAIN);
}

// --- Health -----------------------------------------------------------------

/**
 * Starving (satiety 0) costs health every tick; sleep heals, and so does
 * being awake and fed, more slowly. Returns the cause if the survivor died
 * this tick; the caller removes them (after the survivor loop, so the array
 * isn't spliced mid-iteration).
 */
export function updateHealth(state: SimState, s: Survivor): string | null {
  if (s.satiety === 0) {
    s.health = Math.max(0, s.health - STARVE_DAMAGE);
  } else if (isCold(state, s)) {
    // No healing in the cold (seasons.ts takes its toll separately).
  } else if (s.health > 0 && s.sleep?.asleep) {
    s.health = Math.min(HEALTH_MAX, s.health + SLEEP_HEAL * (inInfirmary(state, s) ? INFIRMARY_HEAL_FACTOR : 1));
  } else if (s.health > 0 && !isHungry(s) && state.tick % AWAKE_HEAL_EVERY === 0) {
    s.health = Math.min(HEALTH_MAX, s.health + 1);
  }
  if (s.health > 0) return null;
  return s.satiety === 0 ? 'starvation' : (s.lastHurtBy ?? 'injuries');
}

/** Removes a survivor, releasing everything they hold (job, carried item, bed, food, weapon). */
export function killSurvivor(state: SimState, s: Survivor, cause: string): void {
  cancelMeal(state, s, cause);
  if (s.jobId || s.carrying) releaseSurvivor(state, s, cause);
  wake(state, s, cause);
  cancelTreatment(state, s, cause);
  cancelArming(state, s, cause);
  dropWeapon(state, s);
  for (const f of state.furniture) if (f.owner === s.id) f.owner = null;
  const i = state.survivors.indexOf(s);
  if (i >= 0) state.survivors.splice(i, 1);
  logEvent(state, `${s.name} died of ${cause}`);
  mournDeath(state, s);
}

// --- Sleep ------------------------------------------------------------------

/**
 * Runs when the survivor is standing still. Returns true if sleep handled
 * this tick (so the survivor doesn't also look for work).
 */
export function updateSleep(state: SimState, s: Survivor): boolean {
  if (!s.sleep) {
    if (!wantsSleep(state, s) || onGuardDuty(state, s)) return false;
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
  // Your first ordinary bed becomes yours (infirmary beds stay shared).
  if (bed.owner === null && !isInfirmaryBed(state, bed) && !state.furniture.some((f) => f.owner === s.id)) bed.owner = s.id;
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
/**
 * Which bed: never someone else's. The hurt try infirmary beds first, everyone else
 * saves them for the hurt; then your own bed; then guards take barracks beds and
 * others leave them; then nearest.
 */
function nearestFreeBed(state: SimState, s: Survivor): Furniture | undefined {
  const hurt = isHurt(s.health);
  const guard = isGuard(s);
  const rank = (f: Furniture) =>
    (isInfirmaryBed(state, f) === hurt ? 0 : 4) + (f.owner === s.id ? 0 : 2) + (isBarracksBed(state, f) === guard ? 0 : 1);
  return state.furniture
    .filter((f) => f.kind === 'bed' && f.occupant === null && (f.owner === null || f.owner === s.id))
    .sort((a, b) => rank(a) - rank(b) || manhattan(s.pos, a) - manhattan(s.pos, b))
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
  // Stay in bed until healed enough to be up, but never sleep through hunger
  // (a starving sleeper would lose health instead of healing).
  if (s.rest >= REST_MAX && (!isHurt(s.health) || isHungry(s))) {
    addThought(state, s, s.sleep.bedId ? 'sleptInBed' : 'sleptOnFloor');
    wake(state, s, 'woke up rested');
  }
}

/** Ends sleep (or the walk to bed) and frees the bed. Safe to call when awake. */
/** Guards asleep in a barracks bed wake when a zombie comes within DANGER_RADIUS (others only when hit). */
export function wakeLightSleepers(state: SimState): void {
  for (const s of state.survivors) {
    if (!s.sleep?.asleep || !s.sleep.bedId || !isGuard(s) || isCritical(s.health)) continue;
    const bed = getBed(state, s.sleep.bedId);
    if (bed && isBarracksBed(state, bed) && zombieNear(state, s.pos)) wake(state, s, 'woken for guard duty');
  }
}

export function wake(state: SimState, s: Survivor, note: string): void {
  if (!s.sleep) return;
  const bed = s.sleep.bedId ? getBed(state, s.sleep.bedId) : undefined;
  if (bed && bed.occupant === s.id) bed.occupant = null;
  s.sleep = null;
  s.note = note;
}
