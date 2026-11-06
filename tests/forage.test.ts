import { describe, expect, it } from 'vitest';
import { HUNGRY } from '../src/sim/food';
import { spawnItem } from '../src/sim/items';
import { loadScenario } from '../src/sim/scenario';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, isNight } from '../src/sim/time';

const MAP = Array.from({ length: 9 }, (_, y) => (y === 4 ? '....s....' : '.........'));
function hungry() {
  const state = loadScenario({ seed: 3, map: MAP });
  state.tick = DAY_LENGTH / 2; // noon, spring
  const s = state.survivors[0]!;
  for (const k of Object.keys(s.priorities)) s.priorities[k as 'guard'] = 0;
  s.satiety = HUNGRY - 10;
  return { state, s };
}

describe('colonists forage', () => {
  it('hungry, with no food anywhere, they forage until something turns up, and eat it', () => {
    const { state, s } = hungry();
    let foraged = false;
    for (let t = 0; t < 3000 && s.satiety < HUNGRY; t++) {
      tick(state);
      if (s.forage) foraged = true;
    }
    expect(foraged).toBe(true);
    expect(s.satiety).toBeGreaterThanOrEqual(HUNGRY); // found some, and ate it
    expect(Object.keys(state.foraged).length).toBeGreaterThan(0);
  });

  it('not with food to go to, and not at night', () => {
    const fed = hungry();
    spawnItem(fed.state, 'food', 0, 0);
    tick(fed.state);
    expect(fed.s.forage ?? null).toBeNull();
    const dark = hungry();
    while (!isNight(dark.state.tick)) dark.state.tick += 10;
    for (let t = 0; t < 20; t++) tick(dark.state);
    expect(dark.s.forage ?? null).toBeNull();
  });
});
