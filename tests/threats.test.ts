import { describe, expect, it } from 'vitest';
import { MIN_THREAT, threatScale, updateDirector } from '../src/sim/director';
import { logEvent } from '../src/sim/events';
import { isPassable } from '../src/sim/pathfinding';
import { loadScenario } from '../src/sim/scenario';
import { EVENT_CHANCE, SEASON_DAYS, isCold } from '../src/sim/seasons';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH } from '../src/sim/time';
import { VARIANTS, giveVariant } from '../src/sim/zombieKinds';
import { addZombie, moverOf } from '../src/sim/zombies';

describe('zombie kinds', () => {
  it('none before their night; a spread of them later, with their own health', () => {
    const state = loadScenario({ seed: 1, map: ['.'] });
    const early = Array.from({ length: 200 }, () => {
      const z = addZombie(state, 0, 0, 200, 'static');
      giveVariant(z, 1);
      return z.variant;
    });
    expect(new Set(early)).toEqual(new Set(['walker']));
    const late = Array.from({ length: 400 }, () => {
      const z = addZombie(state, 0, 0, 200, 'static');
      giveVariant(z, 10);
      return z;
    });
    const kinds = new Set(late.map((z) => z.variant));
    expect(kinds).toEqual(new Set(['walker', 'runner', 'screamer', 'brute', 'crawler']));
    const brute = late.find((z) => z.variant === 'brute')!;
    expect(brute.maxHealth).toBe((200 * VARIANTS.brute.healthPct) / 100);
  });

  it('crawlers get under barricades; walkers do not', () => {
    const state = loadScenario({ seed: 1, map: ['.=.'] });
    const z = addZombie(state, 0, 0, 200, 'static');
    expect(isPassable(state.grid, 1, 0, moverOf(z))).toBe(false);
    z.variant = 'crawler';
    expect(isPassable(state.grid, 1, 0, moverOf(z))).toBe(true);
  });

  it('a screamer screams when it spots someone, waking the sleepers round it', () => {
    const state = loadScenario({ seed: 1, map: ['s' + '.'.repeat(29)] });
    state.survivors[0]!.priorities.guard = 0;
    const z = addZombie(state, 15, 0, 200, 'static');
    z.variant = 'screamer';
    state.dormant = [{ id: 'd1', x: 29, y: 0 }];
    tick(state);
    expect(z.screamed).toBe(true);
    expect(state.dormant).toEqual([]);
  });
});

describe('weather events', () => {
  it('the top of the dawn roll is an event; a blizzard is cold by day', () => {
    expect(EVENT_CHANCE).toBeGreaterThan(0);
    const state = loadScenario({ seed: 1, map: ['s..'] });
    const s = state.survivors[0]!;
    state.tick = DAY_LENGTH / 2;
    expect(isCold(state, s)).toBe(false);
    state.weather = 'blizzard';
    expect(isCold(state, s)).toBe(true);
    // Over a year, some events come up.
    const year = loadScenario({ seed: 5, map: ['...'] });
    const seen = new Set<string>();
    for (let d = 1; d < 4 * SEASON_DAYS * 3; d++) {
      year.tick = d * DAY_LENGTH;
      tick(year);
      seen.add(year.weather);
    }
    expect([...seen].some((w) => w === 'storm' || w === 'heatwave' || w === 'blizzard')).toBe(true);
  });
});

describe('the storyteller', () => {
  it('eases off after deaths, and leans on a rich colony', () => {
    const state = loadScenario({ seed: 1, map: ['s' + 'f'.repeat(29), 'f'.repeat(30), 'f'.repeat(30), 'f'.repeat(30)] });
    expect(threatScale(state)).toBeCloseTo(1.1);
    for (let i = 0; i < 5; i++) logEvent(state, `Bea died of testing`);
    expect(threatScale(state)).toBe(MIN_THREAT);
  });

  it('leaves a cache when the colony is on its knees', () => {
    const state = loadScenario({ seed: 1, map: ['s.....'] });
    state.tick = 2 * DAY_LENGTH;
    logEvent(state, 'Bea died of testing');
    logEvent(state, 'Cal died of testing');
    updateDirector(state);
    expect(state.items.filter((i) => i.kind === 'food').length).toBeGreaterThan(0);
  });
});
