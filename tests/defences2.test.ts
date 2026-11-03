import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { STRUCTURE_HEALTH, damageStructure } from '../src/sim/breach';
import { NIGHT_SIGHT, PIT_TICKS, TURRET_EVERY, isReinforced, seen } from '../src/sim/defences';
import { burnable } from '../src/sim/fire';
import { getTile } from '../src/sim/grid';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { TICKS_PER_HOUR } from '../src/sim/time';
import { addZombie } from '../src/sim/zombies';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}`);
}

const NIGHT = 17 * TICKS_PER_HOUR + 1;

describe('reinforcing', () => {
  it('builders reinforce a wall with scrap: it takes far more battering and does not burn', () => {
    const state = loadScenario({ seed: 6, map: ['s.....', '..#..5'] });
    state.survivors[0]!.priorities = { ...NO_JOBS, build: 3, haul: 3 };
    expect(applyCommand(state, { type: 'designateBuild', x: 2, y: 1, structure: 'reinforce' }).ok).toBe(true);
    runUntil(state, () => isReinforced(state, 2, 1), 600);
    expect(state.items.some((i) => i.kind === 'scrap')).toBe(false);
    expect(burnable(state, 2, 1)).toBe(false);
    expect(damageStructure(state, 2, 1, STRUCTURE_HEALTH.wall)).toBe(false);
    expect(getTile(state.grid, 2, 1)).toBe('wall');
  });
});

describe('turrets and pits', () => {
  it('a loaded turret shoots zombies in range', () => {
    const state = loadScenario({ seed: 6, map: ['6........'] });
    const t = state.furniture[0]!;
    t.stored = 1;
    t.storedKind = 'ammo';
    state.tick = TURRET_EVERY * 10;
    addZombie(state, 5, 0, 100, 'static');
    for (let i = 0; i < TURRET_EVERY + 1; i++) tick(state);
    expect(state.zombies).toEqual([]);
  });

  it('a zombie stepping into a pit is held there', () => {
    const state = loadScenario({ seed: 6, map: ['s.0.......'] });
    state.survivors[0]!.priorities = { ...NO_JOBS };
    const z = addZombie(state, 3, 0, 200, 'static');
    runUntil(state, () => z.stuckUntil > 0, 100);
    const at = { ...z.pos };
    for (let i = 0; i < PIT_TICKS - 5; i++) tick(state);
    expect(z.pos).toEqual(at);
  });
});

describe('darkness', () => {
  it('in a harsh game at night, only what someone can see can be fought', () => {
    const state = loadScenario({ seed: 6, map: ['s' + '.'.repeat(19)] });
    state.tick = NIGHT;
    expect(seen(state, { x: 15, y: 0 })).toBe(true); // (not harsh)
    state.harsh = true;
    expect(seen(state, { x: NIGHT_SIGHT, y: 0 })).toBe(true);
    expect(seen(state, { x: 15, y: 0 })).toBe(false);
  });
});
