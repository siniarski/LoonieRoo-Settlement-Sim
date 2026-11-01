import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { CARRY, SEARCH_TICKS } from '../src/sim/expeditions';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { TICKS_PER_TILE } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';

/** A colony at the left, a ruin of two crates (5 finds each) 30 tiles east, marked as found. */
function setup(sleepers = 0) {
  const state = loadScenario({ seed: 4, map: ['s.s' + '.'.repeat(27) + 'c.c', 'b.b' + '.'.repeat(30)] });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS };
  state.pointsOfInterest.push({ id: 'p1', x: 30, y: 0, label: 'ruin with crates' });
  for (let i = 0; i < sleepers; i++) state.dormant.push({ id: `d${i}`, x: 31, y: 1 });
  return state;
}

const send = (state: SimState, ids: string[]) => applyCommand(state, { type: 'sendExpedition', survivorIds: ids, poiId: 'p1' });

describe('expeditions', () => {
  it('take survivors off the map, and bring them back with what the ruin held', () => {
    const state = setup();
    const ash = state.survivors[0]!;
    state.furniture.find((f) => f.kind === 'bed')!.owner = ash.id; // their bed stays theirs while they're away
    expect(send(state, [ash.id])).toEqual({ ok: true, changed: true });
    expect(state.survivors.map((s) => s.name)).not.toContain(ash.name);
    expect(checkInvariants(state)).toEqual([]);
    const due = state.expeditions[0]!.returnTick;
    expect(due - state.tick).toBe(2 * 30 * TICKS_PER_TILE + SEARCH_TICKS);
    while (state.tick < due) tick(state);
    tick(state);
    expect(state.survivors.map((s) => s.name)).toContain(ash.name);
    expect(ash.pos).toEqual({ x: 0, y: 0 });
    // One member carries CARRY of the ten finds; the crates keep the rest.
    expect(state.items).toHaveLength(CARRY);
    expect(state.furniture.filter((f) => f.kind === 'crate').reduce((a, f) => a + f.loot, 0)).toBe(10 - CARRY);
    expect(state.pointsOfInterest).toEqual([]);
    expect(checkInvariants(state)).toEqual([]);
  });

  it('a ruin full of sleepers is dangerous; its sleepers are cleared either way', () => {
    const outcomes = new Set<string>();
    for (let seed = 1; seed <= 12; seed++) {
      const state = setup(8);
      state.rng = seed * 7777;
      const [a, b] = state.survivors;
      send(state, [a!.id, b!.id]);
      const due = state.expeditions[0]!.returnTick;
      while (state.tick <= due) tick(state);
      expect(state.dormant).toEqual([]);
      expect(checkInvariants(state)).toEqual([]);
      const back = state.survivors.length;
      const hurt = state.survivors.some((s) => s.health < 1000);
      outcomes.add(back < 2 ? 'loss' : hurt ? 'hurt' : 'fine');
    }
    expect(outcomes.has('loss') || outcomes.has('hurt')).toBe(true);
  });

  it('refuses nobody, strangers, or an unknown place', () => {
    const state = setup();
    expect(send(state, []).ok).toBe(false);
    expect(send(state, ['s999']).ok).toBe(false);
    expect(applyCommand(state, { type: 'sendExpedition', survivorIds: [state.survivors[0]!.id], poiId: 'nope' }).ok).toBe(false);
  });
});
