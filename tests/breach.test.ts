import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { STRUCTURE_HEALTH, structureDamage } from '../src/sim/breach';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { TICKS_PER_HOUR } from '../src/sim/time';

/** 23:00 on day 1, just past a spawn boundary. */
const NIGHT = 17 * TICKS_PER_HOUR + 1;

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 1500): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = {}, atNight = true) {
  const state = loadScenario({ seed: 7, map });
  if (atNight) state.tick = NIGHT;
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

const damageAt = (state: SimState, x: number, y: number) => structureDamage(state, x, y);
const lastEvents = (state: SimState) => state.events.map((e) => e.message);

// A survivor shut in a small hut with one door at (5, 3).
const HUT = ['z..........', '...#####...', '...#.s.#...', '...##D##...', '...........'];

describe('breaching', () => {
  it('a zombie batters the door down, leaves a rebuild plan, and gets in', () => {
    const state = setup(HUT);
    const s = state.survivors[0]!;
    runUntil(state, () => state.grid.tiles[3 * 11 + 5] === 'floor');
    expect(lastEvents(state)).toContain('Zombies broke through the door at (5, 3)');
    expect(state.blueprints).toMatchObject([{ x: 5, y: 3, target: 'door' }]);
    expect(damageAt(state, 5, 3)).toBe(0);
    runUntil(state, () => s.health < 1000);
  });

  it('a door takes 10 blows', () => {
    const state = setup(HUT);
    runUntil(state, () => damageAt(state, 5, 3) > 0);
    const first = state.tick;
    runUntil(state, () => state.grid.tiles[3 * 11 + 5] === 'floor');
    // 400 / 40 = 10 blows, one every 10 ticks: the first lands at `first`, the last 90 ticks later.
    expect(state.tick - first).toBeGreaterThanOrEqual((STRUCTURE_HEALTH.door / 40 - 1) * 10);
    expect(state.tick - first).toBeLessThanOrEqual((STRUCTURE_HEALTH.door / 40 - 1) * 10 + 2);
  });

  it('with no door at all, it goes through a wall', () => {
    const state = setup(['z..........', '...#####...', '...#.s.#...', '...#####...', '...........']);
    runUntil(state, () => state.events.some((e) => e.message.startsWith('Zombies broke through the wall')), 3000);
    expect(state.blueprints[0]!.target).toBe('wall');
  });

  it('with nobody about, zombies wander and break nothing', () => {
    const state = setup(['z....', '.##D#', '.#..#', '.####']);
    for (let i = 0; i < 400; i++) tick(state);
    expect(state.grid.damage.every((d) => d === 0)).toBe(true);
  });

  it('survivors repair damaged doors from beside them', () => {
    const state = setup(['#D#', 's..'], { build: 3 }, false);
    state.grid.damage[1] = 250;
    tick(state);
    expect(state.jobs).toMatchObject([{ type: 'build', x: 1, y: 0, blueprintId: null }]);
    runUntil(state, () => state.survivors[0]!.note === 'repaired door at (1, 0)');
    expect(damageAt(state, 1, 0)).toBe(0);
    expect(state.jobs).toEqual([]);
  });

  it('a repair job is dropped when its door is gone, and the repairer moves on', () => {
    const state = setup(['#D#', '...', '...', 's..'], { build: 3 }, false);
    state.grid.damage[1] = 250;
    tick(state);
    const s = state.survivors[0]!;
    expect(s.jobId).not.toBeNull();
    applyCommand(state, { type: 'clearTile', x: 1, y: 0 });
    tick(state);
    expect(state.jobs).toEqual([]);
    expect(s.jobId).toBeNull();
    expect(checkInvariants(state)).toEqual([]);
  });

  it('a broken door is rebuilt with wood like any other', () => {
    const state = setup(HUT.map((r, y) => (y === 4 ? 'w..........' : r)));
    runUntil(state, () => state.grid.tiles[3 * 11 + 5] === 'floor');
    state.zombies = []; // pretend the guards dealt with it
    state.survivors[0]!.priorities = { ...NO_JOBS, build: 3, haul: 3 };
    runUntil(state, () => state.grid.tiles[3 * 11 + 5] === 'door', 2000);
  });

  it('a repairer slows a breach but cannot hold the door alone', () => {
    const time = (jobs: Partial<typeof NO_JOBS>) => {
      const state = setup(HUT, jobs);
      return runUntil(state, () => state.grid.tiles[3 * 11 + 5] === 'floor', 3000);
    };
    expect(time({ build: 3 })).toBeGreaterThan(time({}) + 50);
  });

  it('removing a damaged wall clears its damage, and a new wall starts whole', () => {
    const state = setup(['s#.'], {}, false);
    state.grid.damage[1] = 500;
    applyCommand(state, { type: 'clearTile', x: 1, y: 0 });
    expect(damageAt(state, 1, 0)).toBe(0);
    applyCommand(state, { type: 'placeWall', x: 1, y: 0 });
    expect(state.grid.damage[1]).toBe(0);
    expect(checkInvariants(state)).toEqual([]);
  });
});
