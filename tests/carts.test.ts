import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { killSurvivor } from '../src/sim/needs';
import { labelAt, type RoomPurpose } from '../src/sim/rooms';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = {}, rooms: { x: number; y: number; purpose: RoomPurpose }[] = []) {
  const state = loadScenario({ seed: 17, map, rooms });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

const STORE = ['#######.......', '#.....D.s.....', '#.....#.......', '#######..wwff.'];

describe('carts', () => {
  it('a hauler with a cart carries several things in one trip', () => {
    const state = setup(STORE.map((r, i) => (i === 1 ? r.replace('s.', 'sP') : r)), { haul: 3 }, [{ x: 1, y: 1, purpose: 'storage' }]);
    const s = state.survivors[0]!;
    runUntil(state, () => s.cart !== null, 200);
    runUntil(state, () => s.cartLoad.length > 0, 400);
    const load = s.cartLoad.length;
    expect(load).toBeGreaterThanOrEqual(2);
    runUntil(state, () => state.items.filter((i) => i.kind !== 'cart').every((i) => i.pos && labelAt(state, i.pos.x, i.pos.y) >= 0), 1000);
    expect(s.note).toMatch(/more from the cart/);
  });

  it('a hauler without one carries one at a time', () => {
    const state = setup(STORE, { haul: 3 }, [{ x: 1, y: 1, purpose: 'storage' }]);
    const s = state.survivors[0]!;
    runUntil(state, () => s.carrying !== null, 200);
    expect(s.cartLoad).toEqual([]);
  });

  it('drops the load and the cart when they die', () => {
    const state = setup(STORE.map((r, i) => (i === 1 ? r.replace('s.', 'sP') : r)), { haul: 3 }, [{ x: 1, y: 1, purpose: 'storage' }]);
    const s = state.survivors[0]!;
    runUntil(state, () => s.cartLoad.length > 0, 600);
    killSurvivor(state, s, 'testing');
    expect(state.items.every((i) => i.pos !== null && i.carriedBy === null)).toBe(true);
    expect(checkInvariants(state)).toEqual([]);
  });
});

describe('stockpile filters', () => {
  it('a storage room told to refuse food gets none hauled to it', () => {
    const state = setup(STORE, { haul: 3 }, [{ x: 1, y: 1, purpose: 'storage' }]);
    expect(applyCommand(state, { type: 'setStorageFilter', x: 2, y: 2, category: 'food', accept: false }).ok).toBe(true);
    expect(applyCommand(state, { type: 'setStorageFilter', x: 2, y: 2, category: 'food', accept: false })).toEqual({ ok: true, changed: false });
    runUntil(state, () => state.items.filter((i) => i.kind === 'wood').every((i) => i.pos && labelAt(state, i.pos.x, i.pos.y) >= 0), 1500);
    for (let i = 0; i < 300; i++) tick(state);
    expect(state.items.filter((i) => i.kind === 'food').every((i) => i.pos && labelAt(state, i.pos.x, i.pos.y) < 0)).toBe(true);
    expect(applyCommand(state, { type: 'setStorageFilter', x: 8, y: 2, category: 'food', accept: false }).ok).toBe(false);
  });
});
