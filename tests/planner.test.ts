import { describe, expect, it } from 'vitest';
import { NO_JOBS } from '../src/sim/jobTypes';
import { dumpText } from '../src/debug/text';
import { applyCommand, type Command } from '../src/sim/commands';
import { checkInvariants } from '../src/sim/invariants';
import { planFurnish } from '../src/sim/planner';
import { designationStatus } from '../src/sim/rooms';
import { loadScenario, type Scenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';

/** A 6x3 room (18 tiles: capacity 4 beds) with a door at the bottom, and survivors outside. */
const HOUSE = [
  '..........',
  '.########.',
  '.#......#.',
  '.#......#.',
  '.#......#.',
  '.###D####.',
  '..s.s.....',
  '..........',
];

function setup(map = HOUSE, rooms: NonNullable<Scenario['rooms']> = [{ x: 3, y: 3, purpose: 'housing' }]) {
  return loadScenario({ seed: 4, map, rooms });
}

const beds = (cmds: Command[]) => cmds.map((c) => (c.type === 'designateBuild' && c.structure === 'bed' ? `${c.x},${c.y}` : 'other'));

describe('furnish planner', () => {
  it('explains when there is no room, or no purpose', () => {
    const state = setup(HOUSE, []);
    expect(planFurnish(state, 0, 0)).toEqual({ commands: [], notes: ['Not an enclosed room: close it in with walls and a door first.'] });
    expect(planFurnish(state, 3, 3).notes).toEqual(['This room has no purpose yet: make it Housing or Storage first.']);
  });

  it('plans one bed per survivor, corners first, never next to the door', () => {
    const state = setup();
    const plan = planFurnish(state, 3, 3);
    expect(beds(plan.commands)).toEqual(['2,2', '7,2']);
    expect(plan.notes).toEqual(['Planned 2 beds (room holds 4, colony of 2).']);
    // A bigger colony fills the room, but never past capacity, and the tile by the door stays clear.
    for (let i = 0; i < 4; i++) applyCommand(state, { type: 'spawnSurvivor', x: 5 + i, y: 7 });
    const full = beds(planFurnish(state, 3, 3).commands);
    expect(full).toHaveLength(4);
    expect(full).not.toContain('4,4');
  });

  it('skips tiles with items, furniture, plans, or plots', () => {
    const map = HOUSE.map((row, y) => (y === 2 ? '.#w....~#.' : row));
    const state = setup(map);
    expect(beds(planFurnish(state, 3, 3).commands)).toEqual(['2,4', '7,4']);
  });

  it('counts beds already here and in other housing', () => {
    const map = [...HOUSE.slice(0, 7), '.####.....', '.#bb#.....', '.##D#.....', '..........'];
    const state = setup(map, [
      { x: 3, y: 3, purpose: 'housing' },
      { x: 2, y: 8, purpose: 'housing' },
    ]);
    applyCommand(state, { type: 'spawnSurvivor', x: 8, y: 7 });
    // Colony of 3, two beds in the other house: one more needed here.
    expect(beds(planFurnish(state, 3, 3).commands)).toEqual(['2,2']);
  });

  it('planning twice adds nothing once the beds are planned', () => {
    const state = setup();
    for (const c of planFurnish(state, 3, 3).commands) applyCommand(state, c);
    expect(planFurnish(state, 3, 3)).toEqual({ commands: [], notes: ['Already has 2 beds: enough for the colony.'] });
  });

  it('storage needs nothing; a missing door is explained', () => {
    const storage = setup(HOUSE, [{ x: 3, y: 3, purpose: 'storage' }]);
    expect(planFurnish(storage, 3, 3)).toEqual({ commands: [], notes: ['Storage needs no furniture yet.'] });
    const noDoor = setup(HOUSE.map((row, y) => (y === 5 ? '.########.' : row)));
    const plan = planFurnish(noDoor, 3, 3);
    expect(plan.notes[0]).toBe('Needs a door: build one in its wall.');
    expect(plan.commands).toHaveLength(2);
  });

  it('is pure: planning never changes state', () => {
    const state = setup();
    const before = JSON.stringify(state);
    planFurnish(state, 3, 3);
    expect(JSON.stringify(state)).toBe(before);
  });

  it('end to end: survivors build the planned beds and the housing is complete', () => {
    const map = HOUSE.map((row, y) => (y === 7 ? '..wwww....' : row));
    const state: SimState = setup(map);
    for (const s of state.survivors) s.priorities = { ...NO_JOBS, build: 3, haul: 3 };
    for (const c of planFurnish(state, 3, 3).commands) applyCommand(state, c);
    for (let t = 0; t < 2000 && state.furniture.length < 2; t++) {
      tick(state);
      expect(checkInvariants(state), dumpText(state)).toEqual([]);
    }
    expect(state.furniture.map((f) => `${f.x},${f.y}`).sort()).toEqual(['2,2', '7,2']);
    expect(designationStatus(state, state.roomDesignations[0]!).complete).toBe(true);
  });
});
