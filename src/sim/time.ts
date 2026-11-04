// World clock, derived purely from state.tick (nothing extra to store or
// keep in sync). A day is DAY_LENGTH ticks; the game starts at START_HOUR.
//
// Day length is a world setting (settings.dayScale, 1-3): a longer day stretches the clock and
// every duration measured in hours or days, and slows what runs at a rate per tick (needs,
// crops, work) by the same factor, so a day holds about as many meals, sleeps and finished jobs.
// Walking (and zombies) keep their pace: a longer day just covers more ground.
// tick() sets the scale from state each tick (setDayScale); modules whose constants derive from
// the day register with onClock to recompute them. These are live ES module bindings.

/** Ticks per in-game hour at day scale 1. 200 ticks = 20 s at 1x. */
export const BASE_TICKS_PER_HOUR = 200;
export const HOURS_PER_DAY = 24;
let scale = 1;
const listeners: (() => void)[] = [];
/** Ticks per in-game hour (scaled). */
export let TICKS_PER_HOUR = BASE_TICKS_PER_HOUR;
/** 4800 ticks at scale 1: 8 minutes at 1x, 2 minutes at 4x. */
export let DAY_LENGTH = TICKS_PER_HOUR * HOURS_PER_DAY;

/** Recompute something derived from the day now and whenever the scale changes. */
export function onClock(recompute: () => void): void {
  listeners.push(recompute);
  recompute();
}

export function dayScale(): number {
  return scale;
}

export function setDayScale(n: number): void {
  if (n === scale) return;
  scale = n;
  TICKS_PER_HOUR = BASE_TICKS_PER_HOUR * n;
  DAY_LENGTH = TICKS_PER_HOUR * HOURS_PER_DAY;
  NIGHT_TICKS = (HOURS_PER_DAY - NIGHT_START + NIGHT_END) * TICKS_PER_HOUR;
  for (const f of listeners) f();
}

/** Things that run at a rate per tick (needs, crops, work) run on one tick in every `scale`. */
export function slowTick(tick: number): boolean {
  return tick % scale === 0;
}
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

/** Ticks since midnight, 0 <= t < DAY_LENGTH. */
export function tickOfDay(tick: number): number {
  return clockTicks(tick) % DAY_LENGTH;
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

/** Ticks in one night (21:00 to 05:00): 1600. */
export let NIGHT_TICKS = (HOURS_PER_DAY - NIGHT_START + NIGHT_END) * TICKS_PER_HOUR;

/** The night in progress (1 = the first night, starting 21:00 on day 1), or null by day. */
export function nightNumber(tick: number): number | null {
  if (!isNight(tick)) return null;
  return hourOf(tick) >= NIGHT_START ? dayOf(tick) : dayOf(tick) - 1;
}

/** Ticks since the current night began at 21:00. Only meaningful at night. */
export function ticksIntoNight(tick: number): number {
  const h = hourOf(tick);
  const hoursIn = h >= NIGHT_START ? h - NIGHT_START : h + HOURS_PER_DAY - NIGHT_START;
  return Math.round(hoursIn * TICKS_PER_HOUR);
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
