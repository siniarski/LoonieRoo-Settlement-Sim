import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { isWalkable } from '../src/sim/grid';
import { loadScenario } from '../src/sim/scenario';
import { TICKS_PER_TILE } from '../src/sim/survivors';
import { step } from '../src/sim/tick';

/** Movement tests switch jobs off, so survivors don't wander off to clean tracked dirt. */
function only(map: string[]) {
  const state = loadScenario({ seed: 0, map });
  for (const s of state.survivors) s.priorities = { build: 0, haul: 0, clean: 0 };
  const survivor = state.survivors[0]!;
  return { state, survivor: () => state.survivors[0]!, id: survivor.id };
}

describe('survivors', () => {
  it('spawn from scenario maps with ids in reading order', () => {
    const state = loadScenario({ seed: 0, map: ['s..', '..s'] });
    expect(state.survivors.map((s) => [s.id, s.pos])).toEqual([
      ['s1', { x: 0, y: 0 }],
      ['s2', { x: 2, y: 1 }],
    ]);
    expect(dumpText(state)).toBe('s..\n..s');
  });

  it('walk one tile every TICKS_PER_TILE ticks and stop at the goal', () => {
    const { state, survivor, id } = only(['s....']);
    applyCommand(state, { type: 'moveSurvivor', survivorId: id, x: 4, y: 0 });

    // First tick plans and starts the first step; each tile then takes TICKS_PER_TILE.
    step(state, 4 * TICKS_PER_TILE);
    expect(survivor().pos).toEqual({ x: 3, y: 0 });
    step(state, 1);
    expect(survivor().pos).toEqual({ x: 4, y: 0 });
    expect(survivor().goal).toBeNull();
    expect(survivor().move).toBeNull();
    expect(survivor().note).toBe('arrived at (4, 0)');
  });

  it('interpolation data is consistent while moving', () => {
    const { state, survivor, id } = only(['s..']);
    applyCommand(state, { type: 'moveSurvivor', survivorId: id, x: 2, y: 0 });
    step(state, 2);
    const move = survivor().move!;
    expect(move.from).toEqual({ x: 0, y: 0 });
    expect(move.to).toEqual({ x: 1, y: 0 });
    expect(move.progress).toBe(1);
    expect(move.duration).toBe(TICKS_PER_TILE);
  });

  it('repaths when a wall is placed across the route', () => {
    const map = [
      's.......',
      '........',
    ];
    const { state, survivor, id } = only(map);
    applyCommand(state, { type: 'moveSurvivor', survivorId: id, x: 7, y: 0 });
    step(state, 3);
    expect(applyCommand(state, { type: 'placeWall', x: 4, y: 0 }).ok).toBe(true);
    const notes = new Set<string>();
    for (let i = 0; i < 200 && survivor().goal; i++) {
      step(state, 1);
      notes.add(survivor().note ?? '');
      expect(isWalkable(state.grid, survivor().pos.x, survivor().pos.y), dumpText(state)).toBe(true);
    }
    expect(survivor().pos, dumpText(state)).toEqual({ x: 7, y: 0 });
    expect([...notes].some((n) => n.startsWith('walls changed'))).toBe(true);
  });

  it('gives up with a reason when the goal gets walled off, instead of getting stuck', () => {
    const map = [
      's.....',
      '....#.',
    ];
    const { state, survivor, id } = only(map);
    applyCommand(state, { type: 'moveSurvivor', survivorId: id, x: 5, y: 1 });
    step(state, 2);
    applyCommand(state, { type: 'placeWall', x: 5, y: 0 });
    step(state, 3 * TICKS_PER_TILE);
    expect(survivor().goal).toBeNull();
    expect(survivor().note).toBe('no path to (5, 1)');
    step(state, TICKS_PER_TILE);
    expect(survivor().move).toBeNull();
  });

  it('finishes the current step before following a new order', () => {
    const { state, survivor, id } = only(['..s..']);
    applyCommand(state, { type: 'moveSurvivor', survivorId: id, x: 4, y: 0 });
    step(state, 2);
    applyCommand(state, { type: 'moveSurvivor', survivorId: id, x: 0, y: 0 });
    for (let i = 0; i < 100 && survivor().goal; i++) step(state, 1);
    expect(survivor().pos).toEqual({ x: 0, y: 0 });
  });

  it('rejects walls on occupied tiles, including the tile being stepped onto', () => {
    const { state, id } = only(['s..']);
    expect(applyCommand(state, { type: 'placeWall', x: 0, y: 0 })).toEqual({ ok: false, reason: '(0, 0) is occupied' });
    applyCommand(state, { type: 'moveSurvivor', survivorId: id, x: 2, y: 0 });
    step(state, 1);
    expect(applyCommand(state, { type: 'placeWall', x: 1, y: 0 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'placeDoor', x: 1, y: 0 }).ok).toBe(true);
  });

  it('rejects bad move and spawn commands', () => {
    const { state, id } = only(['s.#']);
    expect(applyCommand(state, { type: 'moveSurvivor', survivorId: id, x: 2, y: 0 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'moveSurvivor', survivorId: 'nope', x: 1, y: 0 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'spawnSurvivor', x: 0, y: 0 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'spawnSurvivor', x: 2, y: 0 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'spawnSurvivor', x: 1, y: 0 }).ok).toBe(true);
    expect(state.survivors.map((s) => s.id)).toEqual(['s1', 's2']);
  });

  it('does not log a repeated order for the same goal', () => {
    const { state, id } = only(['s..']);
    applyCommand(state, { type: 'moveSurvivor', survivorId: id, x: 2, y: 0 });
    applyCommand(state, { type: 'moveSurvivor', survivorId: id, x: 2, y: 0 });
    expect(state.commandLog).toHaveLength(1);
  });
});
