// Rest constants and types. Kept separate from needs.ts so survivors.ts can
// use them without an import cycle (same pattern as jobTypes.ts).
// All values are integers so the sim stays exactly deterministic.

export const REST_MAX = 2000;
/** Below this a survivor stops working and goes to sleep. */
export const TIRED = 500;
/** At night survivors turn in much earlier (below 75%), so sleep follows the clock. */
export const NIGHT_TIRED = 1500;
/** Rest lost per tick while awake (2000 → 500 takes 1500 ticks, 2.5 min at 1x). */
export const REST_DRAIN = 1;
/** Rest gained per tick asleep in a bed / on the floor. */
export const BED_REGEN = 4;
export const FLOOR_REGEN = 2;

export interface Sleep {
  /** The reserved bed, or null when sleeping on the floor. */
  bedId: string | null;
  /** False while still walking to the bed. */
  asleep: boolean;
}

/** Survivors start staggered by index (100%, 85%, 70%, 55%, then repeat) so they don't all tire together. */
export function initialRest(index: number): number {
  return REST_MAX - Math.floor((REST_MAX * 15 * (index % 4)) / 100);
}
