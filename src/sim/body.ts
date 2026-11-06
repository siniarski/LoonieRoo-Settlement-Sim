// The body, 0..100 each: endurance, panic, body heat, wetness. Updated every tick
// (rates per tick, so they slow with longer days: time.ts slowTick).
//
//   endurance  swings and running (RUN_PCT of walking speed, RUN_TICK_COST a tick) spend it; it
//              comes back at rest. Under WINDED: swings come later and land softer, and no running.
//   panic      rises with the dead close by (faster alone, or with little resolve; slower
//              when steeled), and with every hit taken; ebbs when none are near. Over
//              PANICKING: some swings miss, and it weighs on mood.
//   body heat  falls fast while exposed (seasons.ts isCold: a winter night or blizzard outdoors,
//              no coat, no warmth; or soaked through outdoors at night), recovers otherwise.
//              Under COLD: the "cold" thought; under FREEZING: health goes too.
//   wetness    builds out in rain or snow (slowly under a coat), dries indoors or by warmth.
//              Over SOAKED: the "soaked" thought.

import { wearing } from './clothing';
import { zombiesWithin } from './zombies';
import { hashRoll, idNumber } from './hash';
import { addThought } from './mood';
import { isWarmed } from './power';
import { isSteeled, isBreaking } from './resolve';
import { isIndoors } from './rooms';
import { isCold, isWet } from './seasons';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { isNight, slowTick } from './time';

export const BODY_MAX = 100;
export const WINDED = 25;
export const PANICKING = 60;
export const COLD = 30;
export const FREEZING = 10;
export const SOAKED = 50;
/** Wet enough that a night outdoors chills you even out of winter. */
export const DRENCHED = 60;
export const SWING_COST = 10;
export const GUARD_BLOW_COST = 5;
/** Running: this many percent of walking speed, for this much endurance a tick (about 10 s from full to winded). */
export const RUN_PCT = 175;
export const RUN_TICK_COST = 1;
export const HIT_PANIC = 10;
/** Panic per zombie close by, per check. */
const PANIC_PER_ZOMBIE = 3;
const PANIC_RANGE = 6;
const PANIC_EVERY = 10;
/** While winded: extra ticks between swings, and damage in percent. */
export const WINDED_WAIT = 5;
export const WINDED_DAMAGE_PCT = 80;
/** While panicking: the chance a swing misses. */
export const PANIC_MISS = 0.25;
export const PANIC_MOOD = -10;

const clamp = (n: number) => Math.max(0, Math.min(BODY_MAX, n));

/** Exposed to the cold right now: the season's cold, or soaked through outdoors at night. */
export function chilled(state: SimState, s: Survivor): boolean {
  if (isCold(state, s)) return true;
  return s.wetness > DRENCHED && isNight(state.tick) && !isIndoors(state, s.pos.x, s.pos.y) && !isWarmed(state, s.pos);
}

export function updateBody(state: SimState, s: Survivor): void {
  if (!slowTick(state.tick)) return;
  // Endurance comes back at rest (twice as fast asleep).
  if (s.endurance < BODY_MAX && state.tick % 4 === 0) s.endurance = clamp(s.endurance + (s.sleep?.asleep ? 2 : 1));
  // Wetness.
  const outdoors = !isIndoors(state, s.pos.x, s.pos.y);
  const coat = wearing(state, s, 'coat') !== undefined;
  if (outdoors && isWet(state.weather)) {
    if (state.tick % (coat ? 12 : 3) === 0) s.wetness = clamp(s.wetness + 1);
  } else if (s.wetness > 0 && state.tick % (isWarmed(state, s.pos) ? 2 : 5) === 0) s.wetness = clamp(s.wetness - 1);
  // Body heat.
  if (chilled(state, s)) s.bodyHeat = clamp(s.bodyHeat - 1);
  else if (s.bodyHeat < 70 && state.tick % 2 === 0) s.bodyHeat = clamp(s.bodyHeat + 1);
  if (s.bodyHeat < COLD) addThought(state, s, 'cold');
  if (s.wetness > SOAKED) addThought(state, s, 'soaked');
  // Panic.
  if (state.tick % PANIC_EVERY !== 0) return;
  const near = zombiesWithin(state, s.pos, PANIC_RANGE);
  if (near === 0) {
    s.panic = clamp(s.panic - 4);
    return;
  }
  const alone = !state.survivors.some((o) => o !== s && Math.max(Math.abs(o.pos.x - s.pos.x), Math.abs(o.pos.y - s.pos.y)) <= PANIC_RANGE);
  const factor = (alone ? 1.5 : 1) * (isSteeled(s) ? 0.5 : isBreaking(s) ? 1.5 : 1);
  s.panic = clamp(s.panic + Math.round(near * PANIC_PER_ZOMBIE * factor));
}

export function isWinded(s: Survivor): boolean {
  return s.endurance < WINDED;
}

export function isPanicking(s: Survivor): boolean {
  return s.panic > PANICKING;
}

/** Spend endurance (a swing, a step at a run). */
export function exert(s: Survivor, cost: number): void {
  s.endurance = clamp(s.endurance - cost);
}

export function frighten(s: Survivor, by: number): void {
  s.panic = clamp(s.panic + by);
}

/** Panicking: does this swing miss? (A hash, not the sim RNG.) */
export function panicMiss(state: SimState, s: Survivor): boolean {
  return isPanicking(s) && hashRoll(idNumber(s.id), state.tick, 0x9a1) < PANIC_MISS;
}

/** Mood factors from the body. */
export function bodyFactors(s: Survivor): [string, number][] {
  return isPanicking(s) ? [['panicking', PANIC_MOOD]] : [];
}
