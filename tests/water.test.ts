import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { FOOD_SHELF, FUEL_MAX, FUEL_TICKS, LAMP_DETER, SPOIL_CHECK_EVERY, isPowered, litLamps } from '../src/sim/power';
import { loadScenario } from '../src/sim/scenario';
import { SEASON_DAYS } from '../src/sim/seasons';
import type { RoomPurpose } from '../src/sim/rooms';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
import { COLLECTOR_FILL_EVERY, HYDRATION_MAX, THIRSTY } from '../src/sim/water';
import { createWorld, START_WATER } from '../src/sim/worldgen';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = {}, rooms: { x: number; y: number; purpose: RoomPurpose }[] = []) {
  const state = loadScenario({ seed: 11, map, rooms });
  state.harsh = true;
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

describe('thirst', () => {
  it('only drains in a harsh game', () => {
    const state = setup(['s..']);
    state.harsh = false;
    for (let i = 0; i < 50; i++) tick(state);
    expect(state.survivors[0]!.hydration).toBe(HYDRATION_MAX);
  });

  it('a thirsty survivor drops their job and drinks at a well before the river', () => {
    const state = setup(['%.......s.....W', '...............'], { clean: 3 });
    const s = state.survivors[0]!;
    s.hydration = THIRSTY - 1;
    runUntil(state, () => s.hydration >= HYDRATION_MAX - 1, 400);
    expect(s.pos.x).toBeGreaterThan(10);
  });

  it('drinks from the river when there is nothing cleaner', () => {
    const state = setup(['%.....s']);
    const s = state.survivors[0]!;
    s.hydration = 10;
    runUntil(state, () => s.hydration >= HYDRATION_MAX - 1, 400);
    expect(s.pos.x).toBe(1);
  });

  it('rain collectors fill in the rain and run dry when drunk from', () => {
    const state = setup(['s.R']);
    const c = state.furniture[0]!;
    expect(c.loot).toBe(0);
    state.weather = 'rain';
    state.tick = COLLECTOR_FILL_EVERY * 10 - 1;
    for (let i = 0; i < COLLECTOR_FILL_EVERY * 2 + 1; i++) tick(state);
    expect(c.loot).toBe(2);
    state.weather = 'clear';
    state.survivors[0]!.hydration = 1;
    runUntil(state, () => state.survivors[0]!.hydration > 1, 200);
    expect(c.loot).toBe(1);
  });

  it('a new world camp has a rain collector with water in it', () => {
    const state = createWorld(3, 64);
    expect(state.harsh).toBe(true);
    expect(state.furniture.find((f) => f.kind === 'collector')?.loot).toBe(START_WATER);
  });
});

describe('spoilage', () => {
  it('food rots in time in a harsh game, twice as slow in storage, not in winter', () => {
    const map = ['f.......', '####D###', '#f.....#', '#......#', '########'];
    const state = setup(map, {}, [{ x: 1, y: 3, purpose: 'storage' }]);
    state.survivors = [];
    for (let i = 0; i < FOOD_SHELF / SPOIL_CHECK_EVERY + 1; i++) {
      state.tick = (i + 1) * SPOIL_CHECK_EVERY;
      tick(state);
    }
    // (Spring: one tick of age per tick.) The one outside rotted; the stored one is half way.
    expect(state.items.map((i) => i.pos)).toEqual([{ x: 1, y: 2 }]);
    const winter = setup(['f..']);
    winter.tick = 3 * SEASON_DAYS * DAY_LENGTH;
    for (let i = 0; i < 10; i++) {
      winter.tick += SPOIL_CHECK_EVERY - 1;
      tick(winter);
    }
    expect(winter.items[0]!.wear).toBe(0);
  });

  it('a powered fridge keeps food round it', () => {
    const state = setup(['f.F.E', 'w....']);
    state.survivors = [];
    state.furniture.find((f) => f.kind === 'generator')!.stored = 2;
    for (let i = 0; i < 20; i++) {
      state.tick = (i + 1) * SPOIL_CHECK_EVERY;
      tick(state);
    }
    expect(state.items.find((i) => i.kind === 'food')!.wear).toBe(0);
  });
});

describe('power', () => {
  it('haulers fuel a generator; it burns wood while lamps need it at night', () => {
    const state = setup(['s.E.l', 'wwww.', 'ww...'], { haul: 3 });
    const g = state.furniture.find((f) => f.kind === 'generator')!;
    runUntil(state, () => g.stored === FUEL_MAX, 2000);
    expect(isPowered(state, { x: 4, y: 0 })).toBe(true);
    expect(litLamps(state)).toEqual([]); // by day
    state.survivors[0]!.priorities = { ...NO_JOBS };
    state.tick = 15 * TICKS_PER_HOUR + 1; // 21:00
    state.zombiesTonight = 0;
    expect(litLamps(state)).toHaveLength(1);
    for (let i = 0; i < FUEL_TICKS; i++) tick(state);
    expect(g.stored).toBe(FUEL_MAX - 1);
  });

  it('night zombies keep away from a lit lamp', () => {
    // Open ground only near the survivor (too close to arrive) or near the lamp.
    const arrivals = (fuel: number) => {
      const state = setup(['s' + '.'.repeat(19) + 'l.E' + '.'.repeat(7)]);
      state.harsh = false;
      state.furniture.find((f) => f.kind === 'generator')!.stored = fuel;
      state.tick = 15 * TICKS_PER_HOUR;
      for (let i = 0; i < 9 * TICKS_PER_HOUR; i++) tick(state);
      return state.events.filter((e) => e.message.includes('appeared')).length;
    };
    expect(20 - LAMP_DETER).toBeLessThan(8);
    expect(arrivals(0)).toBeGreaterThan(0);
    expect(arrivals(FUEL_MAX)).toBe(0);
  });
});
