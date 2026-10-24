import { describe, expect, it } from 'vitest';
import { DAY_LENGTH, TICKS_PER_HOUR, clockText, darkness, dayOf, hourOf, isNight } from '../src/sim/time';

/** The tick at which a given day and hour is reached (game starts at 06:00 on day 1). */
const at = (day: number, hour: number) => (day - 1) * DAY_LENGTH + (hour - 6) * TICKS_PER_HOUR;

describe('world clock', () => {
  it('starts at 06:00 on day 1', () => {
    expect(dayOf(0)).toBe(1);
    expect(hourOf(0)).toBe(6);
    expect(clockText(0)).toBe('06:00');
  });

  it('advances 1 hour per TICKS_PER_HOUR and rolls over at midnight', () => {
    expect(clockText(at(1, 13.5))).toBe('13:30');
    expect(dayOf(at(1, 23.99))).toBe(1);
    expect(dayOf(at(1, 24))).toBe(2);
    expect(clockText(at(1, 24))).toBe('00:00');
    expect(dayOf(at(3, 12))).toBe(3);
  });

  it('night is 21:00 to 05:00', () => {
    expect(isNight(at(1, 20.99))).toBe(false);
    expect(isNight(at(1, 21))).toBe(true);
    expect(isNight(at(2, 4.99))).toBe(true);
    expect(isNight(at(2, 5))).toBe(false);
  });

  it('darkness fades in over dusk and out over dawn', () => {
    expect(darkness(at(1, 12))).toBe(0);
    expect(darkness(at(1, 19))).toBe(0);
    expect(darkness(at(1, 20))).toBeCloseTo(0.5);
    expect(darkness(at(1, 22))).toBe(1);
    expect(darkness(at(2, 6))).toBeCloseTo(0.5);
    expect(darkness(at(2, 7))).toBe(0);
  });
});
