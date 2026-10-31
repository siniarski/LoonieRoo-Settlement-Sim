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

// --- Injuries ---------------------------------------------------------------
//
//   above HEALTH_HURT       normal
//   HEALTH_CRITICAL..HURT   hurt: walk HURT_STEP_TICKS per tile, work at 3/4 speed
//   below HEALTH_CRITICAL   critical: drop everything (even guarding) and rest in bed
//                           until rested and back above HEALTH_HURT

/** Below 60%: hurt. Also the level a resting survivor must regain before getting up. */
export const HEALTH_HURT = 600;
/** Below 30%: critical. */
export const HEALTH_CRITICAL = 300;
/** Ticks per tile when hurt (normal is 4): 25% slower. */
export const HURT_STEP_TICKS = 5;
/** Hurt survivors skip one work tick in this many (3/4 speed). */
export const HURT_WORK_SKIP_EVERY = 4;

/** Health a bandage restores, and ticks it takes to apply one. */
export const BANDAGE_HEAL = 300;
export const TREAT_TICKS = 20;
/** A hurt survivor who found no bandage looks again after this many ticks. */
export const TREAT_RETRY_TICKS = 20;

export function isHurt(health: number): boolean {
  return health < HEALTH_HURT;
}

export function isCritical(health: number): boolean {
  return health < HEALTH_CRITICAL;
}

/** Does work advance this tick? Slowed survivors (hurt or miserable) lose one tick in HURT_WORK_SKIP_EVERY. */
export function worksThisTick(tick: number, slowed: boolean): boolean {
  return !slowed || tick % HURT_WORK_SKIP_EVERY !== 0;
}
