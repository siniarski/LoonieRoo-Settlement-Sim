import { afterEach, describe, expect, it } from 'vitest';
import { THOUGHTS, addThought } from '../src/sim/mood';
import { INFECTION_TURN } from '../src/sim/infection';
import { loadScenario } from '../src/sim/scenario';
import { workStep } from '../src/sim/skills';
import { tick } from '../src/sim/tick';
import { BASE_TICKS_PER_HOUR, DAY_LENGTH, HOURS_PER_DAY, dayOf, setDayScale } from '../src/sim/time';

// The clock scale is module state (time.ts); put it back for the other tests.
afterEach(() => setDayScale(1));

describe('day length', () => {
  it('a world with days twice as long: the clock and every duration stretch', () => {
    const state = loadScenario({ seed: 1, map: ['s..'] });
    state.settings.dayScale = 2;
    tick(state);
    expect(DAY_LENGTH).toBe(2 * BASE_TICKS_PER_HOUR * HOURS_PER_DAY);
    expect(dayOf(BASE_TICKS_PER_HOUR * HOURS_PER_DAY)).toBe(1); // what was a day is half of one
    expect(INFECTION_TURN).toBe(2 * DAY_LENGTH);
    expect(THOUGHTS.sawDeath.duration).toBe(2 * DAY_LENGTH);
    const s = state.survivors[0]!;
    addThought(state, s, 'hotMeal');
    expect(s.thoughts[0]!.until - state.tick).toBe(DAY_LENGTH / 2);
  });

  it('needs and work run at half the rate per tick, so a day holds as much', () => {
    const run = (scale: number) => {
      const state = loadScenario({ seed: 1, map: ['s..'] });
      state.settings.dayScale = scale;
      const s = state.survivors[0]!;
      const before = s.satiety;
      for (let t = 0; t < 400; t++) tick(state);
      return before - s.satiety;
    };
    expect(run(2)).toBe(run(1) / 2);
  });

  it('back to normal days: back to the usual numbers', () => {
    const state = loadScenario({ seed: 1, map: ['s..'] });
    state.settings.dayScale = 3;
    tick(state);
    state.settings.dayScale = 1;
    tick(state);
    expect(DAY_LENGTH).toBe(BASE_TICKS_PER_HOUR * HOURS_PER_DAY);
    expect(THOUGHTS.sawDeath.duration).toBe(2 * DAY_LENGTH);
    expect(workStep).toBeTypeOf('function');
  });
});
