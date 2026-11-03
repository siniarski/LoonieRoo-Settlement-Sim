import { describe, expect, it } from 'vitest';
import { alerts, daysToWinter, foodDays, stockCounts } from '../src/sim/alerts';
import { ignite } from '../src/sim/fire';
import { loadScenario } from '../src/sim/scenario';
import { SEASON_DAYS } from '../src/sim/seasons';
import { DAY_LENGTH } from '../src/sim/time';
import { addZombie } from '../src/sim/zombies';
import { cloneState } from '../src/sim/state';

describe('alerts', () => {
  it('worst first: zombies, fire and the infected before hunger and visitors', () => {
    const state = loadScenario({ seed: 1, map: ['s.b......', '.........'] });
    const s = state.survivors[0]!;
    s.infection = 10;
    s.satiety = 100;
    addZombie(state, 6, 1, 200, 'static');
    ignite(state, 8, 0);
    const list = alerts(state);
    const levels = list.map((a) => a.level);
    expect(levels).toEqual([...levels].sort((a, b) => ['danger', 'warn', 'info'].indexOf(a) - ['danger', 'warn', 'info'].indexOf(b)));
    expect(list.filter((a) => a.level === 'danger').map((a) => a.text)).toEqual([
      '1 zombie near the colony',
      'Fire! 1 tile burning',
      expect.stringMatching(/^Ash is infected/),
      'Food for less than a day (1 to feed)',
    ]);
    expect(list.some((a) => a.text === 'Ash is hungry')).toBe(true);
  });

  it('warns about winter when the food will not last it', () => {
    const state = loadScenario({ seed: 1, map: ['ssb.ffffff'] });
    state.tick = (2 * SEASON_DAYS + 2) * DAY_LENGTH; // autumn, day 13
    expect(daysToWinter(state.tick)).toBe(3);
    expect(foodDays(state)).toBeLessThan(SEASON_DAYS);
    expect(alerts(state).some((a) => a.text.startsWith('Winter in 3 days'))).toBe(true);
  });

  it('never changes state', () => {
    const state = loadScenario({ seed: 1, map: ['s.ffw', '.....'] });
    state.harsh = true;
    const before = JSON.stringify(cloneState(state));
    alerts(state);
    stockCounts(state);
    expect(JSON.stringify(state)).toBe(before);
    expect(stockCounts(state)).toEqual([
      ['food', 2],
      ['wood', 1],
    ]);
  });
});
