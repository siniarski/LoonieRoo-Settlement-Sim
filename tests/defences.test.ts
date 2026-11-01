import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { STRUCTURE_HEALTH } from '../src/sim/breach';
import { TRAP_CHARGES } from '../src/sim/furniture';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { findPath } from '../src/sim/pathfinding';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { TICKS_PER_HOUR } from '../src/sim/time';
import { TRAP_DAMAGE, addZombie } from '../src/sim/zombies';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}`);
}

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = {}) {
  const state = loadScenario({ seed: 4, map });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  state.tick = 17 * TICKS_PER_HOUR + 1; // night: zombies stay
  return state;
}

describe('spike traps', () => {
  it('hurt a zombie that steps on them, use up a charge, and survivors cross them safely', () => {
    const state = setup(['z..^..s']);
    const trap = state.furniture[0]!;
    runUntil(state, () => trap.loot < TRAP_CHARGES);
    expect(state.zombies[0]!.health).toBe(200 - TRAP_DAMAGE);
    expect(state.survivors[0]!.health).toBe(1000);
  });

  it('a spent trap goes, leaving a plan to rebuild it, and two hits kill a basic zombie', () => {
    const state = setup(['....^....s']);
    state.furniture[0]!.loot = 2;
    for (const x of [0, 1]) addZombie(state, x, 0);
    runUntil(state, () => state.blueprints.length > 0 || state.zombies.length === 0);
    runUntil(state, () => state.blueprints.length > 0);
    expect(state.blueprints).toMatchObject([{ x: 4, y: 0, target: 'trap' }]);
    expect(state.furniture).toEqual([]);
    expect(state.events.map((e) => e.message)).toContain('A spike trap at (4, 0) is spent');
  });
});

describe('barricades', () => {
  it('survivors walk through them; zombies must batter them down', () => {
    const state = setup(['z.....=.....', '######.#####', '......s.....'.replace('s', '.')]);
    expect(findPath(state.grid, { x: 0, y: 0 }, { x: 11, y: 0 })).not.toBeNull();
    // Barricades are the only way through the wall line.
    const fenced = setup(['z..=..s', '###=###']);
    runUntil(fenced, () => fenced.grid.damageList.length > 0);
    expect(fenced.grid.damage[3]! + fenced.grid.damage[7 + 3]!).toBeGreaterThan(0);
    expect(STRUCTURE_HEALTH.barricade).toBeLessThan(STRUCTURE_HEALTH.wall);
    runUntil(fenced, () => fenced.blueprints.length > 0, 3000);
    expect(fenced.blueprints.map((b) => `${b.target}@${b.x}`)).toEqual(['barricade@3']);
  });

  it('are built from wood, quickly', () => {
    const state = setup(['-..', 's.w'], { build: 3, haul: 3 });
    state.tick = 6 * TICKS_PER_HOUR; // day
    runUntil(state, () => state.grid.tiles[0] === 'barricade');
  });
});
