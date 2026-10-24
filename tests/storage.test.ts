import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { checkInvariants } from '../src/sim/invariants';
import { isStoreJob } from '../src/sim/jobs';
import { designationStatus, labelAt, roomAt } from '../src/sim/rooms';
import { loadScenario, type Scenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 800): void {
  for (let t = 0; t < max; t++) {
    if (done(state)) return;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

// A storage room (left) and a bedroom (right), with wood lying outside.
const MAP = [
  '................',
  '.######.######..',
  '.#....#.#....#..',
  '.#....#.#....#..',
  '.##D###.###D##..',
  '................',
  '..s....www......',
  '................',
];
const STORAGE = { x: 3, y: 2, purpose: 'storage' as const };
const HOUSING = { x: 10, y: 2, purpose: 'housing' as const };

function setup(rooms: NonNullable<Scenario['rooms']>, map = MAP) {
  const state = loadScenario({ seed: 3, map, rooms });
  for (const s of state.survivors) s.priorities.clean = 0;
  return state;
}

const inStorage = (state: SimState) =>
  state.items.filter((i) => i.pos && roomAt(state, i.pos.x, i.pos.y) === roomAt(state, STORAGE.x, STORAGE.y)).length;

describe('storage rooms', () => {
  it('loose wood gets hauled into a complete storage room', () => {
    const state = setup([STORAGE]);
    expect(designationStatus(state, state.roomDesignations[0]!).complete).toBe(true);
    runUntil(state, (s) => inStorage(s) === 3);
    expect(state.jobs.filter(isStoreJob)).toEqual([]);
    expect(state.survivors[0]!.note).toMatch(/^stored wood at/);
  });

  it('an incomplete storage room takes nothing', () => {
    const map = MAP.map((row, y) => (y === 4 ? row.replace('.##D###', '.######') : row)); // no door
    const state = setup([STORAGE], map);
    expect(designationStatus(state, state.roomDesignations[0]!).complete).toBe(false);
    for (let i = 0; i < 100; i++) tick(state);
    expect(state.jobs).toEqual([]);
    expect(inStorage(state)).toBe(0);
  });

  it('two haulers never aim for the same storage tile', () => {
    const map = MAP.map((row, y) => (y === 6 ? '..s.s..www......' : row));
    const state = setup([STORAGE], map);
    for (let i = 0; i < 300; i++) {
      tick(state);
      const dests = state.jobs.filter((j) => j.reservedBy && j.dest).map((j) => `${j.dest!.x},${j.dest!.y}`);
      expect(new Set(dests).size).toBe(dests.length);
    }
    expect(inStorage(state)).toBe(3);
  });

  it('builders take wood back out of storage', () => {
    const state = setup([STORAGE]);
    runUntil(state, (s) => inStorage(s) === 3);
    applyCommand(state, { type: 'designateBuild', x: 12, y: 6, structure: 'wall' });
    runUntil(state, (s) => s.grid.tiles[6 * s.grid.width + 12] === 'wall');
    expect(inStorage(state)).toBe(2);
  });

  it('if storage stops being complete mid-haul, the hauler drops the wood and stops', () => {
    const state = setup([STORAGE]);
    runUntil(state, (s) => s.survivors[0]!.carrying !== null);
    applyCommand(state, { type: 'placeWall', x: 3, y: 4 }); // wall over the only door
    tick(state);
    const s = state.survivors[0]!;
    expect(s.carrying).toBeNull();
    expect(s.jobId).toBeNull();
    expect(state.jobs).toEqual([]);
    expect(checkInvariants(state)).toEqual([]);
  });
});

describe('beds and housing', () => {
  it('a bed gets built from a designation and completes the housing', () => {
    const state = setup([HOUSING]);
    const status = () => designationStatus(state, state.roomDesignations[0]!);
    expect(status().requirements.find((r) => !r.met)?.label).toBe('at least 1 bed');
    applyCommand(state, { type: 'designateBuild', x: 10, y: 2, structure: 'bed' });
    runUntil(state, (s) => s.furniture.length === 1);
    expect(status()).toMatchObject({ complete: true, beds: 1 });
    expect(dumpText(state).split('\n')[2]![10]).toBe('b');
  });

  it('removing a bed gives its wood back', () => {
    const map = MAP.map((row, y) => (y === 2 ? '.#....#.#.b..#..' : row));
    const state = setup([HOUSING], map);
    applyCommand(state, { type: 'designateRemove', x: 10, y: 2 });
    runUntil(state, (s) => s.furniture.length === 0);
    expect(state.items).toHaveLength(4);
  });

  it('beds only go on empty floor, and items are never dropped on them', () => {
    const map = MAP.map((row, y) => (y === 2 ? '.#....#.#.b..#..' : row));
    const state = setup([], map);
    expect(applyCommand(state, { type: 'designateBuild', x: 10, y: 2, structure: 'wall' }).ok).toBe(false);
    expect(applyCommand(state, { type: 'designateBuild', x: 1, y: 1, structure: 'bed' }).ok).toBe(false);
    expect(labelAt(state, 10, 2)).toBeGreaterThanOrEqual(0);
  });
});
