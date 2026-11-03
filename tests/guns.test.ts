import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { checkInvariants } from '../src/sim/invariants';
import { clearShot } from '../src/sim/jobs';
import { NO_JOBS } from '../src/sim/jobTypes';
import { NOISE, makeNoise } from '../src/sim/noise';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { TICKS_PER_HOUR } from '../src/sim/time';
import { TOWER_SIGHT, onTower } from '../src/sim/towers';
import { AMMO_ROUNDS, GUN_RANGE } from '../src/sim/weapons';
import { addZombie } from '../src/sim/zombies';
import { createWorld } from '../src/sim/worldgen';
import { SIGHT_RADIUS, isExplored } from '../src/sim/fog';
import { addFurniture } from '../src/sim/furniture';

/** 23:00 on day 1, just past a spawn boundary. */
const NIGHT = 17 * TICKS_PER_HOUR + 1;

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = {}) {
  const state = loadScenario({ seed: 3, map });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

describe('guns', () => {
  it('a guard takes a gun over a machete, then fetches ammo for it', () => {
    const state = setup(['/.s..G..a'], { guard: 3 });
    const s = state.survivors[0]!;
    runUntil(state, () => s.weapon !== null);
    const gun = state.items.find((i) => i.id === s.weapon)!;
    expect(gun.kind).toBe('gun');
    runUntil(state, () => gun.wear > 0);
    expect(gun.wear).toBe(AMMO_ROUNDS);
    expect(state.items.some((i) => i.kind === 'ammo')).toBe(false);
    expect(s.note).toBe('loaded their gun');
  });

  it('a loaded gun shoots from range (no walking up to it), using rounds', () => {
    const state = setup(['s.........', '..........'], { guard: 3 });
    const s = state.survivors[0]!;
    state.items.push({ id: 'g1', kind: 'gun', pos: null, carriedBy: s.id, reservedBy: null, wear: 3, tag: null });
    s.weapon = 'g1';
    state.tick = NIGHT;
    state.zombiesTonight = 0;
    addZombie(state, GUN_RANGE, 0, 200, 'static');
    runUntil(state, () => state.zombies.length === 0);
    expect(s.pos.x).toBeLessThanOrEqual(1);
    expect(state.items[0]!.wear).toBe(1);
    expect(s.note).toMatch(/^shot a zombie/);
  });

  it('walls block a shot', () => {
    const state = loadScenario({ seed: 1, map: ['s.#.z'] });
    expect(clearShot(state.grid, { x: 0, y: 0 }, { x: 4, y: 0 })).toBe(false);
    expect(clearShot(state.grid, { x: 0, y: 0 }, { x: 1, y: 0 })).toBe(true);
  });
});

describe('noise', () => {
  it('wakes dormant zombies within its radius, not beyond', () => {
    const state = setup(['s' + '.'.repeat(39)]);
    state.dormant = [
      { id: 'd1', x: 5, y: 0 },
      { id: 'd2', x: 35, y: 0 },
    ];
    makeNoise(state, { x: 0, y: 0 }, 'fighting');
    expect(state.dormant.map((d) => d.id)).toEqual(['d2']);
    expect(state.zombies).toHaveLength(1);
    makeNoise(state, { x: 10, y: 0 }, 'gunfire');
    expect(state.dormant).toEqual([]);
    expect(NOISE.gunfire).toBeGreaterThan(NOISE.fighting);
  });

  it('turns a herd towards it', () => {
    const state = setup(['s' + '.'.repeat(79)]);
    state.herds = [{ id: 'h1', x: 50, y: 0, to: { x: 79, y: 0 }, size: 3, nextStepTick: 0, heard: false }];
    makeNoise(state, { x: 30, y: 0 }, 'gunfire');
    expect(state.herds[0]!.to).toEqual({ x: 10, y: 0 });
  });

  it('finishing a build is noisy', () => {
    const state = setup(['s.+w']);
    state.survivors[0]!.priorities = { ...NO_JOBS, build: 3, haul: 3 };
    state.dormant = [{ id: 'd1', x: 0, y: 0 }];
    // Asleep in the ground right under them: only the hammering wakes it (not being near: that's every 10 ticks).
    runUntil(state, () => state.blueprints.length === 0, 400);
    expect(state.dormant).toEqual([]);
  });
});

describe('watchtowers', () => {
  it('an idle guard climbs a free tower at night and keeps watch', () => {
    const state = setup(['s.....H'], { guard: 3 });
    state.tick = NIGHT;
    state.zombiesTonight = 0;
    const s = state.survivors[0]!;
    runUntil(state, () => onTower(state, s));
    tick(state);
    expect(s.note).toBe('keeping watch');
  });

  it('sees further from a tower', () => {
    const state = createWorld(5, 64);
    const s = state.survivors[0]!;
    // An unexplored tile beyond normal sight but within a tower's.
    let far: { x: number; y: number } | undefined;
    for (let dy = -TOWER_SIGHT; dy <= TOWER_SIGHT && !far; dy++) {
      for (let dx = -TOWER_SIGHT; dx <= TOWER_SIGHT && !far; dx++) {
        const d2 = dx * dx + dy * dy;
        if (d2 > SIGHT_RADIUS * SIGHT_RADIUS && d2 <= (TOWER_SIGHT - 1) ** 2 && !isExplored(state, s.pos.x + dx, s.pos.y + dy)) far = { x: s.pos.x + dx, y: s.pos.y + dy };
      }
    }
    expect(far).toBeDefined();
    if (!far) return;
    addFurniture(state, 'tower', s.pos.x, s.pos.y);
    s.priorities = { ...NO_JOBS };
    tick(state);
    expect(isExplored(state, far.x, far.y)).toBe(true);
  });
});
