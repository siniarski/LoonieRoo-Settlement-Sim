import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { MEALS_PER_SURVIVOR } from '../src/sim/crafting';
import { HUNGRY, MEAL_VALUE } from '../src/sim/food';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
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

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = { cook: 3 }) {
  const state = loadScenario({ seed: 5, map });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

const count = (state: SimState, kind: string) => state.items.filter((i) => i.kind === kind).length;

describe('cooking', () => {
  it('a cook carries raw food to the stove and makes hot meals, up to two per survivor', () => {
    const state = setup(['s.o.ffff', '........']);
    runUntil(state, () => state.survivors[0]!.note?.startsWith('cooked a hot meal') === true);
    expect(state.jobs.some((j) => j.type === 'cook') || count(state, 'meal') === 1).toBe(true);
    runUntil(state, () => count(state, 'meal') === MEALS_PER_SURVIVOR);
    for (let i = 0; i < 150; i++) tick(state);
    expect(count(state, 'meal')).toBe(MEALS_PER_SURVIVOR);
    expect(count(state, 'food')).toBe(4 - MEALS_PER_SURVIVOR);
  });

  it('a hungry survivor picks a hot meal over raw food: it fills more and lifts the mood', () => {
    const state = setup(['f...s...h'], {});
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'setSatiety', survivorId: s.id, satiety: HUNGRY - 1 });
    runUntil(state, () => s.meal === null && s.satiety > HUNGRY);
    expect(count(state, 'meal')).toBe(0);
    expect(count(state, 'food')).toBe(1);
    expect(s.satiety).toBeGreaterThan(HUNGRY - 1 + MEAL_VALUE); // more than raw food could have given (capped at the max)
    expect(s.thoughts.map((t) => t.kind)).toContain('hotMeal');
  });

  it('a removed stove gives its food back', () => {
    const state = setup(['s.o.ff', '......']);
    const stove = state.furniture[0]!;
    runUntil(state, () => stove.stored === 1);
    applyCommand(state, { type: 'clearTile', x: 2, y: 0 });
    tick(state);
    expect(count(state, 'food')).toBe(2);
    expect(checkInvariants(state)).toEqual([]);
  });

  it('survivors build stoves', () => {
    const state = setup(['O..', 's.w'], { build: 3, haul: 3 });
    runUntil(state, () => state.furniture.some((f) => f.kind === 'stove'));
  });
});
