// World clock, derived purely from state.tick (nothing extra to store or
// keep in sync). A day is DAY_LENGTH ticks; the game starts at START_HOUR.

/** Ticks per in-game hour. 200 ticks = 20 s at 1x. */
export const TICKS_PER_HOUR = 200;
export const HOURS_PER_DAY = 24;
/** 4800 ticks: 8 minutes at 1x, 2 minutes at 4x. */
export const DAY_LENGTH = TICKS_PER_HOUR * HOURS_PER_DAY;
export const START_HOUR = 6;

/** Night is from NIGHT_START (inclusive) to NIGHT_END (exclusive), wrapping midnight. */
export const NIGHT_START = 21;
export const NIGHT_END = 5;
/** Hours over which it gets dark (before NIGHT_START) and light (after NIGHT_END). */
export const TWILIGHT_HOURS = 2;

/** Ticks since the very first midnight. */
function clockTicks(tick: number): number {
  return tick + START_HOUR * TICKS_PER_HOUR;
}

/** Day number, starting at 1. */
export function dayOf(tick: number): number {
  return Math.floor(clockTicks(tick) / DAY_LENGTH) + 1;
}

/** Fractional hour of the day, 0 <= h < 24. */
export function hourOf(tick: number): number {
  return (clockTicks(tick) % DAY_LENGTH) / TICKS_PER_HOUR;
}

export function isNight(tick: number): boolean {
  const h = hourOf(tick);
  return h >= NIGHT_START || h < NIGHT_END;
}

/** 0 in full day, 1 in full night, ramping linearly through dusk and dawn. For rendering. */
export function darkness(tick: number): number {
  const h = hourOf(tick);
  if (isNight(tick)) return 1;
  const duskStart = NIGHT_START - TWILIGHT_HOURS;
  if (h >= duskStart) return (h - duskStart) / TWILIGHT_HOURS;
  if (h < NIGHT_END + TWILIGHT_HOURS) return 1 - (h - NIGHT_END) / TWILIGHT_HOURS;
  return 0;
}

/** "06:30" */
export function clockText(tick: number): string {
  const h = hourOf(tick);
  const hours = Math.floor(h);
  const minutes = Math.floor((h - hours) * 60);
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`;
}
