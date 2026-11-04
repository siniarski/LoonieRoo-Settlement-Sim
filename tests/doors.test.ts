import { describe, expect, it } from 'vitest';
import { createArena, CROWD_SIZE } from '../src/sim/arena';
import { applyCommand } from '../src/sim/commands';
import { isOpen } from '../src/sim/grid';
import { centre } from '../src/sim/motion';
import { isPassable } from '../src/sim/pathfinding';
import { loadScenario } from '../src/sim/scenario';
import { tick } from '../src/sim/tick';
import { NO_JOBS } from '../src/sim/jobTypes';

describe('doors that open and close', () => {
  it('closed: nobody you steer and no zombie gets through; open: both do', () => {
    const state = loadScenario({ seed: 1, map: ['s.D..'] });
    expect(isPassable(state.grid, 2, 0, 'free')).toBe(false);
    expect(isPassable(state.grid, 2, 0, 'zombie')).toBe(false);
    expect(isPassable(state.grid, 2, 0, 'survivor')).toBe(true); // colonists open and close as they pass
    applyCommand(state, { type: 'toggleDoor', x: 2, y: 0 });
    expect(isOpen(state.grid, 2, 0)).toBe(true);
    expect(isPassable(state.grid, 2, 0, 'free')).toBe(true);
    expect(isPassable(state.grid, 2, 0, 'zombie')).toBe(true);
  });

  it('you open a door by using it, walk through, and it stays as you left it', () => {
    const state = loadScenario({ seed: 1, map: ['.sD..'] });
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'takeControl', survivorId: s.id });
    applyCommand(state, { type: 'steer', dx: 1, dy: 0 });
    for (let t = 0; t < 12; t++) tick(state);
    expect(s.pos.x).toBe(1); // the closed door stops you
    applyCommand(state, { type: 'steer', dx: 0, dy: 0 });
    expect(applyCommand(state, { type: 'use', x: 2, y: 0 })).toMatchObject({ ok: true });
    expect(isOpen(state.grid, 2, 0)).toBe(true);
    applyCommand(state, { type: 'steer', dx: 1, dy: 0 });
    for (let t = 0; t < 12; t++) tick(state);
    expect(s.pos.x).toBeGreaterThanOrEqual(3);
    expect(isOpen(state.grid, 2, 0)).toBe(true);
  });

  it('a locked door will not open; nobody closes a door on someone standing in it', () => {
    const state = loadScenario({ seed: 1, map: ['s.D..'] });
    applyCommand(state, { type: 'toggleLock', x: 2, y: 0 });
    expect(applyCommand(state, { type: 'toggleDoor', x: 2, y: 0 }).ok).toBe(false);
    applyCommand(state, { type: 'toggleLock', x: 2, y: 0 });
    applyCommand(state, { type: 'toggleDoor', x: 2, y: 0 });
    const s = state.survivors[0]!;
    s.pos = { x: 2, y: 0 };
    s.at = centre(s.pos);
    expect(applyCommand(state, { type: 'toggleDoor', x: 2, y: 0 }).ok).toBe(false);
  });

  it('a zombie walks in through a door left open, and not through a shut one', () => {
    const run = (open: boolean) => {
      const state = loadScenario({ seed: 1, map: ['#####', '#s..#', '##D##', '.....', '..z..'] });
      state.zombies[0]!.kind = 'static';
      state.survivors[0]!.priorities = { ...NO_JOBS }; // no guard going out to meet it
      if (open) applyCommand(state, { type: 'toggleDoor', x: 2, y: 2 });
      for (let t = 0; t < 60; t++) tick(state);
      return state.survivors[0]!.health;
    };
    expect(run(true)).toBeLessThan(1000);
    expect(run(false)).toBe(1000);
  });
});

describe('debug arenas', () => {
  it('one zombie, or a crowd, near you from the start, staying past dawn', () => {
    const one = createArena(5, 'one');
    expect(one.zombies).toHaveLength(1);
    expect(one.control).not.toBeNull();
    const crowd = createArena(5, 'crowd');
    expect(crowd.zombies.length).toBeGreaterThan(CROWD_SIZE / 2);
    for (const z of crowd.zombies) expect(z.kind).toBe('static');
    expect(crowd.settlements).toEqual([]);
  });
});
