import { describe, expect, it } from 'vitest';
import { NO_JOBS } from '../src/sim/jobTypes';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { checkInvariants } from '../src/sim/invariants';
import { BED_REGEN, FLOOR_REGEN, REST_MAX, TIRED, initialRest } from '../src/sim/rest';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import type { Survivor } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[]) {
  const state = loadScenario({ seed: 1, map });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS };
  return state;
}

const tire = (state: SimState, s: Survivor, rest = TIRED - 1) =>
  applyCommand(state, { type: 'setRest', survivorId: s.id, rest });

describe('rest and sleep', () => {
  it('starts staggered and drains by 1 per tick while awake', () => {
    expect([0, 1, 2, 3, 4].map(initialRest)).toEqual([2000, 1700, 1400, 1100, 2000]);
    const state = setup(['s....']);
    for (let i = 0; i < 10; i++) tick(state);
    expect(state.survivors[0]!.rest).toBe(REST_MAX - 10);
  });

  it('a tired survivor walks to the nearest free bed, sleeps, and wakes rested', () => {
    const state = setup(['s.....b..b']);
    const s = state.survivors[0]!;
    tire(state, s);
    tick(state);
    expect(s.note).toBe('going to bed at (6, 0)');
    expect(state.furniture[0]!.occupant).toBe(s.id);
    runUntil(state, () => s.sleep?.asleep === true);
    expect(s.pos).toEqual({ x: 6, y: 0 });
    const before = s.rest;
    tick(state);
    expect(s.rest).toBe(before + BED_REGEN);
    runUntil(state, () => s.sleep === null);
    expect(s.rest).toBe(REST_MAX);
    expect(s.note).toBe('woke up rested');
    expect(state.furniture[0]!.occupant).toBeNull();
  });

  it('with no free bed, sleeps on the floor and recovers more slowly', () => {
    const state = setup(['s.b.s']);
    const [a, b] = state.survivors as [Survivor, Survivor];
    tire(state, a);
    tire(state, b);
    tick(state);
    // Ash claimed the only bed first (survivor order); Bea sleeps where she stands.
    expect(a.sleep?.bedId).toBe(state.furniture[0]!.id);
    expect(b.sleep).toEqual({ bedId: null, asleep: true });
    expect(b.note).toBe('no free bed, asleep on the floor');
    const before = b.rest;
    tick(state);
    expect(b.rest).toBe(before + FLOOR_REGEN);
  });

  it('two tired survivors never claim the same bed', () => {
    const state = setup(['s..bb..s']);
    for (const s of state.survivors) tire(state, s);
    runUntil(state, (st) => st.survivors.every((s) => s.sleep?.asleep));
    const beds = state.survivors.map((s) => s.sleep!.bedId);
    expect(new Set(beds).size).toBe(2);
    expect(beds).not.toContain(null);
  });

  it('getting tired mid-job drops the job and the wood, then sleeps', () => {
    const state = loadScenario({ seed: 1, map: ['s.w........b'] });
    const s = state.survivors[0]!;
    s.priorities.clean = 0;
    applyCommand(state, { type: 'designateBuild', x: 9, y: 0, structure: 'wall' });
    runUntil(state, () => s.carrying !== null);
    tire(state, s);
    tick(state);
    expect(s.jobId).toBeNull();
    expect(s.carrying).toBeNull();
    expect(s.sleep).not.toBeNull();
    expect(state.jobs[0]!.reservedBy).toBeNull();
    expect(state.items[0]!.pos).not.toBeNull();
  });

  it('an unreachable bed is skipped in favour of the floor', () => {
    const state = setup(['s..###', '...#b#', '...###']);
    tire(state, state.survivors[0]!);
    tick(state);
    expect(state.survivors[0]!.sleep).toEqual({ bedId: null, asleep: true });
  });

  it('if the bed is removed mid-sleep, the survivor re-plans instead of getting stuck', () => {
    const state = setup(['s.b...b']);
    const s = state.survivors[0]!;
    tire(state, s);
    runUntil(state, () => s.sleep?.asleep === true);
    applyCommand(state, { type: 'clearTile', x: 2, y: 0 }); // flattens the bed
    runUntil(state, () => s.sleep?.asleep === true && s.sleep.bedId === state.furniture[0]?.id);
    expect(s.pos).toEqual({ x: 6, y: 0 });
  });

  it('if the way to bed gets walled off, the survivor gives the bed up and sleeps on the floor', () => {
    const state = setup(['s.......b']);
    const s = state.survivors[0]!;
    tire(state, s);
    runUntil(state, () => s.pos.x === 2);
    applyCommand(state, { type: 'placeWall', x: 5, y: 0 });
    runUntil(state, () => s.sleep?.asleep === true);
    expect(s.sleep!.bedId).toBeNull();
    expect(state.furniture[0]!.occupant).toBeNull();
  });

  it('a direct order wakes a sleeper and frees the bed; still tired, they go back to bed after', () => {
    const state = setup(['s.b...', '......']);
    const s = state.survivors[0]!;
    tire(state, s);
    runUntil(state, () => s.sleep?.asleep === true);
    applyCommand(state, { type: 'moveSurvivor', survivorId: s.id, x: 5, y: 1 });
    expect(s.sleep).toBeNull();
    expect(state.furniture[0]!.occupant).toBeNull();
    runUntil(state, () => s.sleep?.asleep === true);
    expect(s.pos).toEqual({ x: 2, y: 0 });
  });

  it('setRest validates its input', () => {
    const state = setup(['s']);
    expect(applyCommand(state, { type: 'setRest', survivorId: 's1', rest: -1 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'setRest', survivorId: 's1', rest: 2001 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'setRest', survivorId: 's1', rest: 1.5 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'setRest', survivorId: 's9', rest: 10 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'setRest', survivorId: 's1', rest: 10 })).toEqual({ ok: true, changed: true });
  });
});
