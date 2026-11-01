import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { isExplored } from '../src/sim/fog';
import { HERD_TICKS_PER_TILE, MATERIALIZE_RADIUS, WAKE_RADIUS, WARN_RADIUS, direction } from '../src/sim/herds';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
import { createWorld, START_CLEAR_RADIUS } from '../src/sim/worldgen';
import { AGGRO_RADIUS, FRONTIER_MIN, addZombie } from '../src/sim/zombies';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}`);
}

/** Open ground, one survivor at the left, doing nothing. Midday, so no night arrivals. */
function field(width = 80, height = 9) {
  const map = Array.from({ length: height }, (_, y) => (y === 4 ? 's' + '.'.repeat(width - 1) : '.'.repeat(width)));
  const state = loadScenario({ seed: 3, map });
  state.survivors[0]!.priorities = { ...NO_JOBS };
  state.tick = 6 * TICKS_PER_HOUR; // noon, day 1
  return state;
}

describe('dormant zombies', () => {
  it(`stay as data until a survivor comes within ${WAKE_RADIUS}, then get up`, () => {
    const state = field();
    state.dormant.push({ id: 'd1', x: 30, y: 4 });
    for (let i = 0; i < 50; i++) tick(state);
    expect(state.zombies).toEqual([]);
    state.survivors[0]!.pos = { x: 30 - WAKE_RADIUS, y: 4 };
    for (let i = 0; i < 10; i++) tick(state);
    expect(state.dormant).toEqual([]);
    expect(state.zombies).toMatchObject([{ kind: 'static' }]);
  });

  it(`once awake, only go for survivors within ${AGGRO_RADIUS}, and stay out in the day`, () => {
    const state = field();
    const z = addZombie(state, 60, 4, 200, 'static');
    for (let i = 0; i < 60; i++) tick(state);
    expect(z.targetId).toBeNull();
    state.survivors[0]!.pos = { x: z.pos.x - AGGRO_RADIUS + 4, y: z.pos.y }; // it may have wandered a little
    for (let i = 0; i < 20; i++) tick(state);
    expect(z.targetId).toBe(state.survivors[0]!.id);
  });

  it('a new world sleeps zombies in ruins, none near the start', () => {
    const world = createWorld(7, 128);
    expect(world.dormant.length).toBeGreaterThan(0);
    const start = world.survivors[0]!.pos;
    for (const d of world.dormant) expect(Math.hypot(d.x - start.x, d.y - start.y)).toBeGreaterThan(START_CLEAR_RADIUS + 16);
  });
});

describe('herds', () => {
  it('cross the map as a marker, a tile every few ticks, and leave at the far side', () => {
    const state = field(40, 60);
    state.survivors[0]!.pos = { x: 0, y: 0 };
    state.herds.push({ id: 'h1', x: 39, y: 59, to: { x: 39, y: 30 }, size: 4, nextStepTick: state.tick, heard: false });
    for (let i = 0; i < 10 * HERD_TICKS_PER_TILE; i++) tick(state);
    expect(state.herds[0]!.y).toBe(59 - 10);
    runUntil(state, () => state.herds.length === 0, 40 * HERD_TICKS_PER_TILE);
    expect(state.zombies).toEqual([]); // never came near anyone
  });

  it(`break up into real zombies within ${MATERIALIZE_RADIUS} of a survivor, and walk on where the herd was going`, () => {
    const state = field(120, 9);
    state.survivors[0]!.pos = { x: 0, y: 0 };
    state.herds.push({ id: 'h1', x: 60, y: 8, to: { x: 119, y: 8 }, size: 4, nextStepTick: state.tick, heard: false });
    state.survivors[0]!.pos = { x: 60 - MATERIALIZE_RADIUS, y: 8 };
    tick(state);
    expect(state.herds).toEqual([]);
    expect(state.zombies).toHaveLength(4);
    expect(state.zombies.every((z) => z.kind === 'herd' && z.heading?.x === 119)).toBe(true);
    // Move the survivor far away: the herd walks on east and off the map.
    state.survivors[0]!.pos = { x: 0, y: 0 };
    runUntil(state, () => state.zombies.length === 0, 1500);
  });

  it('set out from day 2 (sim RNG: the same seed, the same herd)', () => {
    const herd = () => {
      const state = field(80, 80);
      state.tick = DAY_LENGTH + 2 * TICKS_PER_HOUR - 1; // day 2, 07:59
      for (let day = 0; day < 4 && state.herds.length === 0; day++) {
        state.tick = DAY_LENGTH * (day + 1) + 2 * TICKS_PER_HOUR - 1;
        tick(state);
        tick(state);
      }
      return JSON.stringify(state.herds);
    };
    expect(herd()).not.toBe('[]');
    expect(herd()).toBe(herd());
  });
});

describe('hearing a herd coming', () => {
  it(`warns once, with a direction, when it comes within ${WARN_RADIUS}`, () => {
    const state = field(120, 9);
    state.survivors[0]!.pos = { x: 10, y: 4 };
    state.herds.push({ id: 'h1', x: 119, y: 4, to: { x: 0, y: 4 }, size: 5, nextStepTick: state.tick, heard: false });
    runUntil(state, () => state.herds[0]?.heard === true, 2000);
    expect(state.herds[0]!.x).toBe(10 + WARN_RADIUS);
    expect(state.events.at(-1)!.message).toBe(`A herd of 5 is coming from the east (${10 + WARN_RADIUS}, 4)`);
    const warnings = () => state.events.filter((e) => e.message.startsWith('A herd of 5 is coming')).length;
    runUntil(state, () => state.herds.length === 0, 2000);
    expect(warnings()).toBe(1);
  });

  it('names directions the way a compass does', () => {
    expect([direction(10, 0), direction(0, -10), direction(-10, 10), direction(3, -10), direction(0, 0)]).toEqual(['east', 'north', 'south-west', 'north', 'nearby']);
  });
});

describe('night zombies on a dark map', () => {
  it('arrive in the dark past explored ground, and leave at dawn while others stay', () => {
    const state = field(80, 80);
    state.grid.explored.fill(0);
    state.survivors[0]!.pos = { x: 40, y: 40 };
    tick(state);
    addZombie(state, 0, 0, 200, 'static');
    state.tick = 15 * TICKS_PER_HOUR; // nightfall
    runUntil(state, () => state.zombies.some((z) => z.kind === 'night'), 3000);
    const z = state.zombies.find((zz) => zz.kind === 'night')!;
    expect(isExplored(state, z.pos.x, z.pos.y)).toBe(false);
    expect(Math.abs(z.pos.x - 40) + Math.abs(z.pos.y - 40)).toBeGreaterThanOrEqual(FRONTIER_MIN - 8);
    runUntil(state, () => !state.zombies.some((zz) => zz.kind === 'night'), DAY_LENGTH);
    expect(state.zombies.some((zz) => zz.kind === 'static')).toBe(true);
  });
});
