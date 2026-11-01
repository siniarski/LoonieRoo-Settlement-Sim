// Hunger constants and types. Kept separate from needs.ts so survivors.ts
// can use them without an import cycle (same pattern as rest.ts).
// All values are integers so the sim stays exactly deterministic.

import type { Point } from './grid';

export const SATIETY_MAX = 3000;
/** Below this a survivor stops working and goes to eat. */
export const HUNGRY = 750;
/** Satiety lost per tick, awake or asleep (3000 → 750 takes 2250 ticks, 3.75 min at 1x). */
export const SATIETY_DRAIN = 1;
/** Satiety from eating one food item (capped at SATIETY_MAX). */
export const MEAL_VALUE = 2000;
/** A hot meal (cooked at a stove) fills more. */
export const HOT_MEAL_VALUE = 2600;
/** Ticks it takes to eat. */
export const EAT_TICKS = 30;
/** A hungry survivor who found no food looks again after this many ticks. */
export const MEAL_RETRY_TICKS = 20;

export interface Meal {
  /** The reserved food item. */
  itemId: string;
  /** Ticks spent eating so far (0 while still walking to it). */
  progress: number;
  /** Mess hall seat the food is carried to, or null to eat where the food lies. */
  seat: Point | null;
}

/** Survivors start staggered by index (100%, 80%, 60%, 90%, then repeat), offset from rest. */
export function initialSatiety(index: number): number {
  return Math.floor((SATIETY_MAX * [100, 80, 60, 90][index % 4]!) / 100);
}
