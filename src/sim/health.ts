// Health constants. Kept separate from needs.ts so survivors.ts can use them
// without an import cycle (same pattern as rest.ts and food.ts).
// All values are integers so the sim stays exactly deterministic.

export const HEALTH_MAX = 1000;
/** Health lost per tick while starving (satiety at 0): 1000 ticks, 100 s at 1x, from full to dead. */
export const STARVE_DAMAGE = 1;
/** Health regained per tick while asleep. */
export const SLEEP_HEAL = 1;
/** Awake and fed (not hungry): heal 1 every this many ticks. */
export const AWAKE_HEAL_EVERY = 5;
/** Below this, health shows red. */
export const HEALTH_LOW = 300;
