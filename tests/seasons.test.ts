import { describe, expect, it } from 'vitest';
import { GROW_TICKS } from '../src/sim/farming';
import { NO_JOBS } from '../src/sim/jobTypes';
import { loadScenario } from '../src/sim/scenario';
import { SEASON_DAYS, cropGrows, seasonOf } from '../src/sim/seasons';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';

const dayStart = (day: number) => (day - 1) * DAY_LENGTH;

function growthOver(state: SimState, ticks: number): number {
  const plot = state.farm[0]!;
  plot.planted = true;
  plot.growth = 0;
  for (let i = 0; i < ticks; i++) tick(state);
  return plot.growth;
}

function field(day: number) {
  const state = loadScenario({ seed: 2, map: ['~...s'] });
  state.survivors[0]!.priorities = { ...NO_JOBS };
  state.tick = dayStart(day) + 1;
  return state;
}

describe('seasons', () => {
  it(`turn every ${SEASON_DAYS} days: spring, summer, autumn, winter, and round again`, () => {
    expect([1, 6, 11, 16, 21].map((d) => seasonOf(dayStart(d)))).toEqual(['spring', 'summer', 'autumn', 'winter', 'spring']);
  });

  it('crops grow fully in spring, half in autumn, not at all in winter', () => {
    expect(growthOver(field(1), 100)).toBe(100);
    expect(growthOver(field(11), 100)).toBe(50);
    expect(growthOver(field(16), 100)).toBe(0);
    expect(GROW_TICKS).toBeGreaterThan(100);
  });

  it('rain makes crops grow half as fast again', () => {
    const state = field(2);
    state.weather = 'rain';
    expect(cropGrows({ ...state, tick: 2 })).toBe(2);
    expect(cropGrows({ ...state, tick: 3 })).toBe(1);
  });
});

describe('weather and cold', () => {
  it('is rolled at dawn from day 2, the same for the same seed', () => {
    const roll = () => {
      const state = field(1);
      const out: string[] = [];
      for (let d = 2; d <= 8; d++) {
        state.tick = dayStart(d);
        tick(state);
        out.push(state.weather);
      }
      return out.join();
    };
    expect(roll()).toBe(roll());
  });

  it('a winter night outdoors costs health and mood; indoors is warm', () => {
    const run = (map: string[]) => {
      const state = loadScenario({ seed: 2, map });
      state.survivors[0]!.priorities = { ...NO_JOBS };
      state.tick = dayStart(16) + 16 * TICKS_PER_HOUR; // winter, 22:00
      for (let i = 0; i < 200; i++) tick(state);
      return state.survivors[0]!;
    };
    const outside = run(['s....']);
    expect(outside.health).toBeLessThan(1000);
    expect(outside.thoughts.map((t) => t.kind)).toContain('cold');
    const inside = run(['####', '#s.#', '#..#', '##D#']);
    expect(inside.thoughts.map((t) => t.kind)).not.toContain('cold');
  });
});
