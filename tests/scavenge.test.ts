import { describe, expect, it } from 'vitest';
import { NO_JOBS } from '../src/sim/jobTypes';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { CRATE_LOOT } from '../src/sim/furniture';
import { checkInvariants } from '../src/sim/invariants';
import { SCAVENGE_WORK } from '../src/sim/jobs';
import { roomAt } from '../src/sim/rooms';
import { loadScenario, type Scenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 3000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

/** Survivors only scavenge (and haul, when asked), so other jobs don't interfere. */
function setup(map: string[], rooms: NonNullable<Scenario['rooms']> = [], haul = 0) {
  const state = loadScenario({ seed: 7, map, rooms });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, scavenge: 3, haul: haul as 0 | 3 };
  return state;
}

/** Marks every crate on the map, like painting the Scavenge tool over it. */
function markAll(state: SimState) {
  for (const f of state.furniture) applyCommand(state, { type: 'designateScavenge', x: f.x, y: f.y });
}

const crates = (state: SimState) => state.furniture.filter((f) => f.kind === 'crate');

describe('scavenging', () => {
  it('crates do nothing until marked', () => {
    const state = setup(['s...c']);
    for (let i = 0; i < 50; i++) tick(state);
    expect(state.jobs).toEqual([]);
    expect(crates(state)[0]!.loot).toBe(CRATE_LOOT);
  });

  it('a marked crate is searched from next to it and yields one find per search', () => {
    const state = setup(['s...c']);
    markAll(state);
    tick(state);
    expect(state.jobs.map((j) => j.type)).toEqual(['scavenge']);
    runUntil(state, (st) => st.items.length === 1);
    const s = state.survivors[0]!;
    expect(s.pos).toEqual({ x: 3, y: 0 });
    expect(s.note).toMatch(/^scavenged (food|wood) from \(4, 0\), 4 left$/);
    expect(crates(state)[0]!.loot).toBe(CRATE_LOOT - 1);
  });

  it('keeps searching over time until the crate is empty, then unmarks it', () => {
    const state = setup(['s...c', '.....']);
    markAll(state);
    const ticks = runUntil(state, (st) => crates(st)[0]!.loot === 0);
    expect(ticks).toBeGreaterThanOrEqual(CRATE_LOOT * SCAVENGE_WORK);
    expect(state.items).toHaveLength(CRATE_LOOT);
    expect(crates(state)[0]!.scavenge).toBe(false);
    for (let i = 0; i < 20; i++) tick(state);
    expect(state.jobs).toEqual([]);
  });

  it('finds are food or wood, rolled with the sim RNG (same seed, same finds)', () => {
    const run = () => {
      const state = setup(['s..c', '....', '..c.']);
      markAll(state);
      runUntil(state, (st) => crates(st).every((c) => c.loot === 0));
      return state.items.map((i) => i.kind);
    };
    const kinds = run();
    expect(kinds).toHaveLength(2 * CRATE_LOOT);
    expect(new Set(kinds)).toEqual(new Set(['food', 'wood']));
    expect(run()).toEqual(kinds);
  });

  it('painting over an area marks every crate in it; empty crates and beds are ignored', () => {
    const state = setup(['c.c.b', '.....', 's....']);
    crates(state)[1]!.loot = 0;
    for (let x = 0; x < 5; x++) applyCommand(state, { type: 'designateScavenge', x, y: 0 });
    expect(state.furniture.map((f) => f.scavenge)).toEqual([true, false, false]);
    expect(state.commandLog).toHaveLength(1);
  });

  it('two survivors never search the same crate at once', () => {
    const state = setup(['s..c..s', '.......']);
    markAll(state);
    for (let i = 0; i < 300; i++) {
      tick(state);
      const searching = state.survivors.filter((s) => s.jobId).length;
      expect(searching).toBeLessThanOrEqual(1);
    }
  });

  it('cancel unmarks a crate and stops the search', () => {
    const state = setup(['s...c']);
    markAll(state);
    runUntil(state, (st) => st.survivors[0]!.jobId !== null);
    applyCommand(state, { type: 'cancelDesignation', x: 4, y: 0 });
    tick(state);
    expect(state.survivors[0]!.jobId).toBeNull();
    expect(state.jobs).toEqual([]);
    expect(crates(state)[0]!.scavenge).toBe(false);
  });

  it('finds get hauled back to storage', () => {
    const map = [
      '................',
      '.####...........',
      '.#..#........c..',
      '.#..#...........',
      '.##D#...........',
      's...............',
    ];
    const state = setup(map, [{ x: 2, y: 2, purpose: 'storage' }], 3);
    markAll(state);
    const storageRoom = roomAt(state, 2, 2)!;
    runUntil(state, (st) => crates(st)[0]!.loot === 0 && st.items.every((i) => i.pos && roomAt(st, i.pos.x, i.pos.y) === storageRoom));
    // Everything found is in storage, minus anything the survivor got hungry enough to eat on the way.
    expect(state.items.length).toBeGreaterThanOrEqual(CRATE_LOOT - 1);
  });

  it('removing a crate gives wood back', () => {
    const state = loadScenario({ seed: 7, map: ['s..c'] });
    for (const s of state.survivors) s.priorities = { ...NO_JOBS, build: 3 };
    applyCommand(state, { type: 'designateRemove', x: 3, y: 0 });
    runUntil(state, (st) => st.furniture.length === 0);
    expect(state.items.map((i) => i.kind)).toEqual(['wood']);
  });
});
