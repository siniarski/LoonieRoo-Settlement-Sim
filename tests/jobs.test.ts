import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { getTile, isDirty } from '../src/sim/grid';
import { checkInvariants } from '../src/sim/invariants';
import { explainIdle } from '../src/sim/jobs';
import type { Priorities } from '../src/sim/jobTypes';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';

/** Ticks until `done` holds, checking invariants every tick. Fails after `max`. */
function runUntil(state: SimState, done: (s: SimState) => boolean, max = 600): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${JSON.stringify(state.survivors.map((s) => s.note))}`);
}

function setup(map: string[], priorities?: Partial<Priorities>) {
  const state = loadScenario({ seed: 1, map });
  if (priorities) for (const s of state.survivors) Object.assign(s.priorities, priorities);
  return state;
}

describe('build jobs', () => {
  it('hauls wood to a wall blueprint, then builds it', () => {
    const state = setup(['s.w.....'], { clean: 0 });
    applyCommand(state, { type: 'designateBuild', x: 6, y: 0, structure: 'wall' });
    runUntil(state, (s) => s.blueprints.length === 0);
    expect(getTile(state.grid, 6, 0)).toBe('wall');
    expect(state.items).toEqual([]);
    expect(state.survivors[0]!.jobId).toBeNull();
  });

  it('builds doors the same way', () => {
    const state = setup(['s.w...', '......'], { clean: 0 });
    applyCommand(state, { type: 'designateBuild', x: 5, y: 1, structure: 'door' });
    runUntil(state, (s) => getTile(s.grid, 5, 1) === 'door');
  });

  it('removing a wall gives the wood back', () => {
    const state = setup(['s..#..'], { clean: 0 });
    applyCommand(state, { type: 'designateRemove', x: 3, y: 0 });
    runUntil(state, (s) => getTile(s.grid, 3, 0) === 'floor');
    expect(state.items.map((i) => [i.kind, i.pos])).toEqual([['wood', { x: 3, y: 0 }]]);
  });

  it('evicts a survivor standing on the tile when the wall completes', () => {
    const state = setup(['s.w.s.'], { clean: 0 });
    state.survivors[1]!.priorities = { build: 0, haul: 0, clean: 0 }; // just stands there
    applyCommand(state, { type: 'designateBuild', x: 4, y: 0, structure: 'wall' });
    runUntil(state, (s) => getTile(s.grid, 4, 0) === 'wall');
    expect(state.survivors[1]!.pos).not.toEqual({ x: 4, y: 0 });
  });
});

describe('clean jobs', () => {
  it('cleans dirty tiles', () => {
    const state = setup(['s..,..'], { build: 0, haul: 0 });
    runUntil(state, (s) => !isDirty(s.grid, 3, 0));
    expect(state.survivors[0]!.note).toBe('cleaned (3, 0)');
  });
});

describe('job selection', () => {
  it('two survivors never reserve the same job', () => {
    const state = setup(['s....,....s']);
    tick(state);
    const holders = state.survivors.filter((s) => s.jobId);
    expect(holders).toHaveLength(1);
    expect(state.jobs[0]!.reservedBy).toBe(holders[0]!.id);
  });

  it('picks the highest-priority job type first', () => {
    const state = setup(['s,.w...'], { build: 3, haul: 3, clean: 1 });
    applyCommand(state, { type: 'designateBuild', x: 6, y: 0, structure: 'wall' });
    tick(state);
    expect(state.survivors[0]!.note).toMatch(/^took clean job/);
  });

  it('breaks priority ties by job type order: build, haul, clean', () => {
    const state = setup(['s,.w...']);
    applyCommand(state, { type: 'designateBuild', x: 6, y: 0, structure: 'wall' });
    tick(state);
    // No build job yet (material missing), so haul beats clean.
    expect(state.survivors[0]!.note).toMatch(/^took haul job/);
  });

  it('never takes a job type set to off', () => {
    const state = setup(['s.,.'], { clean: 0 });
    for (let i = 0; i < 50; i++) {
      tick(state);
      expect(state.survivors[0]!.jobId).toBeNull();
    }
    expect(isDirty(state.grid, 2, 0)).toBe(true);
    expect(explainIdle(state, state.survivors[0]!)).toBe('no open jobs of enabled types');
  });

  it('takes the nearest job of the chosen type', () => {
    const state = setup(['.....,..s.,']);
    tick(state);
    expect(state.survivors[0]!.note).toBe('took clean job at (10, 0): priority 3, nearest of 2 open');
  });

  it('explains why haul jobs are waiting', () => {
    const state = setup(['s.....'], { clean: 0 });
    applyCommand(state, { type: 'designateBuild', x: 4, y: 0, structure: 'wall' });
    tick(state);
    expect(state.survivors[0]!.jobId).toBeNull();
    expect(explainIdle(state, state.survivors[0]!)).toBe('1 haul job(s) waiting for wood');
  });

  it('skips unreachable wood until a path opens', () => {
    const map = [
      's.....###',
      '......#w#',
      '......###',
      '.........',
    ];
    const state = setup(map, { clean: 0 });
    applyCommand(state, { type: 'designateBuild', x: 3, y: 2, structure: 'wall' });
    for (let i = 0; i < 30; i++) tick(state);
    expect(state.survivors[0]!.jobId).toBeNull();
    expect(explainIdle(state, state.survivors[0]!)).toBe('1 open job(s), none reachable right now');
    applyCommand(state, { type: 'clearTile', x: 7, y: 2 });
    runUntil(state, (s) => getTile(s.grid, 3, 2) === 'wall');
  });
});

describe('invalid-job recovery', () => {
  it('drops the wood and goes idle when a designation is cancelled mid-haul', () => {
    const state = setup(['s.w........'], { clean: 0 });
    applyCommand(state, { type: 'designateBuild', x: 10, y: 0, structure: 'wall' });
    runUntil(state, (s) => s.survivors[0]!.carrying !== null);
    applyCommand(state, { type: 'cancelDesignation', x: 10, y: 0 });
    tick(state);
    const s = state.survivors[0]!;
    expect(s.carrying).toBeNull();
    expect(s.jobId).toBeNull();
    expect(state.items).toHaveLength(1);
    expect(state.items[0]!.pos).not.toBeNull();
    expect(state.items[0]!.reservedBy).toBeNull();
    expect(checkInvariants(state)).toEqual([]);
  });

  it('refunds delivered wood when an instant wall replaces a blueprint', () => {
    const state = setup(['s.w.....'], { build: 0, clean: 0 });
    applyCommand(state, { type: 'designateBuild', x: 6, y: 0, structure: 'wall' });
    runUntil(state, (s) => s.blueprints[0]?.delivered === true);
    expect(state.items).toEqual([]);
    applyCommand(state, { type: 'placeWall', x: 6, y: 0 });
    tick(state);
    expect(state.blueprints).toEqual([]);
    expect(state.items).toHaveLength(1);
  });

  it('a direct order overrides work and the survivor drops what they carry', () => {
    const state = setup(['s.w........', '...........'], { clean: 0 });
    applyCommand(state, { type: 'designateBuild', x: 10, y: 0, structure: 'wall' });
    runUntil(state, (s) => s.survivors[0]!.carrying !== null);
    applyCommand(state, { type: 'moveSurvivor', survivorId: 's1', x: 0, y: 1 });
    const s = state.survivors[0]!;
    expect(s.carrying).toBeNull();
    expect(s.jobId).toBeNull();
    expect(s.goal).toEqual({ x: 0, y: 1 });
    expect(state.jobs[0]!.reservedBy).toBeNull();
  });

  it('gives up a build when it gets walled off, and resumes when reopened', () => {
    const map = [
      's...#...',
      '....#...',
      '....#...',
    ];
    const state = setup(map, { haul: 0, clean: 0 });
    applyCommand(state, { type: 'designateRemove', x: 4, y: 1 });
    runUntil(state, (s) => s.survivors[0]!.jobId !== null);
    // Wall the survivor into the corner.
    applyCommand(state, { type: 'placeWall', x: 1, y: 0 });
    applyCommand(state, { type: 'placeWall', x: 0, y: 1 });
    applyCommand(state, { type: 'placeWall', x: 1, y: 1 });
    runUntil(state, (s) => s.survivors[0]!.jobId === null, 50);
    applyCommand(state, { type: 'clearTile', x: 1, y: 0 });
    runUntil(state, (s) => getTile(s.grid, 4, 1) === 'floor');
  });
});

describe('designation commands', () => {
  it('only designates builds on empty floor', () => {
    const state = setup(['.#']);
    expect(applyCommand(state, { type: 'designateBuild', x: 1, y: 0, structure: 'wall' }).ok).toBe(false);
    expect(applyCommand(state, { type: 'designateBuild', x: 0, y: 0, structure: 'wall' })).toEqual({ ok: true, changed: true });
    expect(applyCommand(state, { type: 'designateBuild', x: 0, y: 0, structure: 'wall' })).toEqual({ ok: true, changed: false });
    expect(applyCommand(state, { type: 'designateBuild', x: 0, y: 0, structure: 'door' })).toEqual({ ok: true, changed: true });
    expect(state.blueprints.map((b) => b.target)).toEqual(['door']);
  });

  it('remove marks walls, cancels planned builds, and ignores bare floor', () => {
    const state = setup(['.#.']);
    applyCommand(state, { type: 'designateBuild', x: 0, y: 0, structure: 'wall' });
    expect(applyCommand(state, { type: 'designateRemove', x: 0, y: 0 })).toEqual({ ok: true, changed: true });
    expect(applyCommand(state, { type: 'designateRemove', x: 1, y: 0 })).toEqual({ ok: true, changed: true });
    expect(applyCommand(state, { type: 'designateRemove', x: 1, y: 0 })).toEqual({ ok: true, changed: false });
    expect(applyCommand(state, { type: 'designateRemove', x: 2, y: 0 })).toEqual({ ok: true, changed: false });
    expect(dumpText(state)).toBe('.x.');
  });

  it('validates priorities and logs only real changes', () => {
    const state = setup(['s']);
    expect(applyCommand(state, { type: 'setPriority', survivorId: 's1', jobType: 'clean', priority: 9 as never }).ok).toBe(false);
    expect(applyCommand(state, { type: 'setPriority', survivorId: 's1', jobType: 'nope' as never, priority: 1 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'setPriority', survivorId: 's9', jobType: 'clean', priority: 1 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'setPriority', survivorId: 's1', jobType: 'clean', priority: 3 })).toEqual({ ok: true, changed: false });
    expect(applyCommand(state, { type: 'setPriority', survivorId: 's1', jobType: 'clean', priority: 1 })).toEqual({ ok: true, changed: true });
    expect(state.commandLog).toHaveLength(1);
  });

  it('scenario maps can hold wood, dirt, and blueprints', () => {
    const state = setup(['sw,+d']);
    expect(dumpText(state)).toBe('sw,+d');
    expect(state.items).toHaveLength(1);
    expect(state.blueprints.map((b) => b.target)).toEqual(['wall', 'door']);
  });
});
