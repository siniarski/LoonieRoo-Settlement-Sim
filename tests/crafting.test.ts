import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { craftStock, wantedCraft } from '../src/sim/crafting';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 1500): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = { craft: 3 }) {
  const state = loadScenario({ seed: 8, map });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

const target = (state: SimState, kind: 'weapon' | 'bandage', count: number) => applyCommand(state, { type: 'setCraftTarget', kind, count });
const count = (state: SimState, kind: string) => state.items.filter((i) => i.kind === kind).length;

describe('crafting', () => {
  it('no target, no craft jobs', () => {
    const state = setup(['s.k.ww']);
    tick(state);
    expect(state.jobs).toEqual([]);
  });

  it('a crafter carries wood to the bench piece by piece, then makes a weapon', () => {
    const state = setup(['s.k.ww']);
    target(state, 'weapon', 1);
    tick(state);
    expect(state.jobs).toMatchObject([{ type: 'craft', x: 2, y: 0 }]);
    const bench = state.furniture[0]!;
    runUntil(state, () => bench.stored === 1);
    expect(state.survivors[0]!.note).toBe('brought wood to the workbench (1/2)');
    runUntil(state, () => state.survivors[0]!.note === 'crafted a weapon at (2, 0)');
    expect(count(state, 'weapon')).toBe(1);
    expect(count(state, 'wood')).toBe(0);
    expect(bench.stored).toBe(0);
  });

  it('stops at the target and makes what is short, in order', () => {
    const state = setup(['s.k.wwww', '........']);
    target(state, 'weapon', 1);
    target(state, 'bandage', 2);
    runUntil(state, () => wantedCraft(state) === null, 3000);
    expect([count(state, 'weapon'), count(state, 'bandage'), count(state, 'wood')]).toEqual([1, 2, 0]);
    for (let i = 0; i < 50; i++) tick(state);
    expect(state.jobs).toEqual([]);
  });

  it("wielded weapons don't count as stock", () => {
    const state = setup(['s/k.', '....'], { guard: 3 });
    runUntil(state, () => state.survivors[0]!.weapon !== null);
    expect(craftStock(state, 'weapon')).toBe(0);
    target(state, 'weapon', 1);
    expect(wantedCraft(state)).toBe('weapon');
  });

  it('with no wood the job waits, unclaimed, and nobody gets stuck', () => {
    const state = setup(['s.k...']);
    target(state, 'bandage', 1);
    for (let i = 0; i < 100; i++) tick(state);
    expect(state.jobs).toMatchObject([{ type: 'craft', reservedBy: null }]);
    // Wood turns up (as if a scavenger brought it back).
    state.items.push({ id: 'i900', kind: 'wood', pos: { x: 5, y: 0 }, carriedBy: null, reservedBy: null, wear: 0 });
    runUntil(state, () => count(state, 'bandage') === 1);
  });

  it('removing a bench gives its stored wood back and frees the crafter', () => {
    const state = setup(['s.k.ww', '......']);
    target(state, 'weapon', 1);
    const bench = state.furniture[0]!;
    runUntil(state, () => bench.stored === 1);
    applyCommand(state, { type: 'clearTile', x: 2, y: 0 });
    tick(state);
    expect(state.furniture).toEqual([]);
    expect(count(state, 'wood')).toBe(2);
    expect(state.survivors[0]!.jobId).toBeNull();
    expect(checkInvariants(state)).toEqual([]);
  });

  it('lowering the target mid-craft releases the crafter; the wood stays in the bench', () => {
    const state = setup(['s.k.ww']);
    target(state, 'weapon', 1);
    const bench = state.furniture[0]!;
    runUntil(state, () => bench.stored === 1);
    target(state, 'weapon', 0);
    tick(state);
    expect(state.jobs).toEqual([]);
    expect(state.survivors[0]!.jobId).toBeNull();
    expect(bench.stored).toBe(1);
  });

  it('targets are validated', () => {
    const state = setup(['s.k']);
    expect(target(state, 'weapon', -1).ok).toBe(false);
    expect(target(state, 'weapon', 21).ok).toBe(false);
    expect(target(state, 'weapon', 2.5).ok).toBe(false);
    expect(target(state, 'weapon', 3).ok).toBe(true);
  });

  it('survivors build workbenches from wood', () => {
    const state = setup(['K..', 's.w'], { build: 3, haul: 3 });
    runUntil(state, () => state.furniture.some((f) => f.kind === 'workbench'));
  });
});
