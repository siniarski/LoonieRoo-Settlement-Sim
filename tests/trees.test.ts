import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { TREE_REGROW_TICKS, TREE_WOOD } from '../src/sim/furniture';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { groupSize, spawnsAt, zombiesForNight } from '../src/sim/zombies';
import { DAY_LENGTH, nightNumber } from '../src/sim/time';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}`);
}

describe('trees', () => {
  it('a marked tree is chopped for wood only, stays marked, and grows back', () => {
    const state = loadScenario({ seed: 3, map: ['s..Y', '....'] });
    state.survivors[0]!.priorities = { ...NO_JOBS, scavenge: 3 };
    const tree = state.furniture[0]!;
    expect(tree.loot).toBe(TREE_WOOD);
    expect(applyCommand(state, { type: 'designateScavenge', x: 3, y: 0 })).toEqual({ ok: true, changed: true });
    runUntil(state, () => tree.loot === 0);
    expect(state.items.map((i) => i.kind)).toEqual(['wood', 'wood', 'wood']);
    expect(state.survivors[0]!.note).toBe('chopped wood from (3, 0), 0 left');
    expect(tree.scavenge).toBe(true);
    // Regrowth comes on the day boundary; then the marked tree is chopped again.
    state.tick = TREE_REGROW_TICKS;
    tick(state);
    expect(tree.loot).toBe(1);
    runUntil(state, () => state.items.filter((i) => i.kind === 'wood').length === 4);
  });

  it('unmarked trees are left alone; Cancel unmarks', () => {
    const state = loadScenario({ seed: 3, map: ['s..Y'] });
    state.survivors[0]!.priorities = { ...NO_JOBS, scavenge: 3 };
    for (let i = 0; i < 100; i++) tick(state);
    expect(state.items).toEqual([]);
    applyCommand(state, { type: 'designateScavenge', x: 3, y: 0 });
    expect(applyCommand(state, { type: 'cancelDesignation', x: 3, y: 0 })).toEqual({ ok: true, changed: true });
    expect(state.furniture[0]!.scavenge).toBe(false);
  });
});

describe('zombie groups', () => {
  const arrivals = (night: number) => {
    const out: number[] = [];
    for (let t = (night - 1) * DAY_LENGTH; t < (night + 1) * DAY_LENGTH; t++) {
      if (nightNumber(t) === night && spawnsAt(t) > 0) out.push(spawnsAt(t));
    }
    return out;
  };

  it('come singly for three nights, then in pairs, then threes; the total per night is unchanged', () => {
    expect([1, 3, 4, 6, 7].map(groupSize)).toEqual([1, 1, 2, 2, 3]);
    expect(arrivals(1)).toEqual([1, 1]);
    expect(arrivals(3)).toEqual([1, 1, 1, 1]);
    expect(arrivals(4)).toEqual([2, 2, 1]);
    expect(arrivals(7)).toEqual([3, 3, 2]);
    for (const n of [1, 4, 7, 10]) expect(arrivals(n).reduce((a, b) => a + b, 0)).toBe(zombiesForNight(n));
  });

  it('a group arrives together on neighbouring outdoor tiles', () => {
    const map = Array.from({ length: 21 }, (_, y) => (y === 10 ? `${'.'.repeat(20)}s${'.'.repeat(20)}` : '.'.repeat(41)));
    const state = loadScenario({ seed: 5, map });
    state.survivors[0]!.priorities = { ...NO_JOBS };
    const first = Array.from({ length: 2 * DAY_LENGTH }, (_, i) => 3 * DAY_LENGTH + i).find((t) => spawnsAt(t) === 2)!;
    state.tick = first;
    tick(state);
    expect(state.zombies).toHaveLength(2);
    const [a, b] = state.zombies;
    expect(Math.abs(a!.pos.x - b!.pos.x) + Math.abs(a!.pos.y - b!.pos.y)).toBe(1);
    expect(state.events.at(-1)!.message).toMatch(/^A group of 2 zombies appeared at/);
  });
});
