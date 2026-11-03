import { describe, expect, it } from 'vitest';
import { NORMAL, PRESETS, eventChance, isSettings, spawnFind } from '../src/sim/difficulty';
import { loadScenario } from '../src/sim/scenario';
import { EVENT_CHANCE, cropGrows, isCold } from '../src/sim/seasons';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
import { WEAPON_KILLS } from '../src/sim/weapons';
import { createWorld } from '../src/sim/worldgen';

const SIZE = 128;
const crateLoot = (s: ReturnType<typeof createWorld>) => s.furniture.filter((f) => f.kind === 'crate').reduce((n, f) => n + f.loot, 0);

describe('difficulty', () => {
  it('normal settings make the same world as before settings existed', () => {
    expect(createWorld(7, SIZE, NORMAL)).toEqual(createWorld(7, SIZE));
  });

  it('every preset is valid', () => {
    for (const p of Object.values(PRESETS)) expect(isSettings(p.settings)).toBe(true);
    expect(isSettings({ ...NORMAL, zombies: 5 })).toBe(false);
  });

  it('a solo start is one person, under your control, with less to go on', () => {
    const state = createWorld(7, SIZE, { ...NORMAL, start: 'solo' });
    expect(state.survivors).toHaveLength(1);
    expect(state.control?.survivorId).toBe(state.survivors[0]!.id);
    expect(state.items.filter((i) => i.kind === 'food').length).toBeLessThan(createWorld(7, SIZE).items.filter((i) => i.kind === 'food').length);
  });

  it('later in the apocalypse there is less left, and finds are worn', () => {
    expect(crateLoot(createWorld(7, SIZE, { ...NORMAL, progression: 3 }))).toBeLessThan(crateLoot(createWorld(7, SIZE)));
    expect(crateLoot(createWorld(7, SIZE, { ...NORMAL, progression: 0 }))).toBeGreaterThan(crateLoot(createWorld(7, SIZE)));
    const state = loadScenario({ seed: 1, map: ['s..'] });
    state.settings.progression = 3;
    expect(spawnFind(state, 'weapon', 1, 0).wear).toBe(Math.floor(WEAPON_KILLS / 2));
    expect(spawnFind(state, 'food', 2, 0).wear).toBe(0);
  });

  it('more zombies a night', () => {
    const night = (zombies: number) => {
      const state = loadScenario({ seed: 1, map: ['s....'] });
      state.settings.zombies = zombies;
      state.tick = 15 * TICKS_PER_HOUR; // 21:00
      tick(state);
      return state.zombiesTonight!;
    };
    expect(night(300)).toBeGreaterThan(night(100));
  });

  it('extreme weather brings more events; a nuclear winter halves crop growth and makes every night cold', () => {
    const state = loadScenario({ seed: 1, map: ['s..'] });
    state.settings.weather = 3;
    expect(eventChance(state, EVENT_CHANCE)).toBeGreaterThan(EVENT_CHANCE);
    state.tick = DAY_LENGTH * 5 + 15 * TICKS_PER_HOUR; // a summer night
    const s = state.survivors[0]!;
    expect(isCold(state, s)).toBe(false);
    state.settings.winter = true;
    expect(isCold(state, s)).toBe(true);
    let grown = 0;
    for (let t = 0; t < 10; t++, state.tick++) grown += cropGrows(state);
    expect(grown).toBe(5);
  });
});
