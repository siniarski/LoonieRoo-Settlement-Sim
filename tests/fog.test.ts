import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { SCOUT_HOME_HOUR, SIGHT_RADIUS, isExplored, scoutingOver } from '../src/sim/fog';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
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

/** A long open strip, dark except where the survivor first stands. */
function dark(map: string[], jobs: Partial<typeof NO_JOBS> = {}) {
  const state = loadScenario({ seed: 2, map });
  state.grid.explored.fill(0);
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  tick(state); // first look round
  return state;
}

const STRIP = ['s' + '.'.repeat(59), '.'.repeat(60), '.'.repeat(60)];

describe('fog of war', () => {
  it('survivors see what is within sight, and walking reveals more', () => {
    const state = dark(STRIP);
    expect(isExplored(state, SIGHT_RADIUS, 0)).toBe(true);
    expect(isExplored(state, SIGHT_RADIUS + 1, 0)).toBe(false);
    applyCommand(state, { type: 'moveSurvivor', survivorId: state.survivors[0]!.id, x: 30, y: 1 });
    runUntil(state, () => state.survivors[0]!.pos.x === 30 && state.survivors[0]!.pos.y === 1);
    expect(isExplored(state, 30 + SIGHT_RADIUS, 1)).toBe(true);
    expect(isExplored(state, 59, 0)).toBe(false);
  });

  it("nothing can be planned on ground nobody has seen", () => {
    const state = dark(STRIP);
    expect(applyCommand(state, { type: 'designateBuild', x: 40, y: 1, structure: 'wall' })).toEqual({ ok: false, reason: '(40, 1) is unexplored' });
    expect(applyCommand(state, { type: 'designateFarm', x: 40, y: 1 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'designateBuild', x: 3, y: 1, structure: 'wall' }).ok).toBe(true);
  });
});

describe('exploring', () => {
  it('a scout sweeps a dark box until nothing in it is left unseen, then the order is done', () => {
    const state = dark(STRIP, { scout: 3 });
    expect(applyCommand(state, { type: 'designateExplore', x0: 59, y0: 2, x1: 40, y1: 0 }).ok).toBe(true);
    expect(state.exploreAreas).toMatchObject([{ x0: 40, y0: 0, x1: 59, y1: 2 }]);
    runUntil(state, () => state.survivors[0]!.note?.startsWith('took scout job') === true, 10);
    runUntil(state, () => state.exploreAreas.length === 0);
    for (let x = 40; x < 60; x++) expect(isExplored(state, x, 1)).toBe(true);
    tick(state);
    expect(state.jobs).toEqual([]);
  });

  it('a box that is all explored already is refused', () => {
    const state = dark(STRIP);
    expect(applyCommand(state, { type: 'designateExplore', x0: 0, y0: 0, x1: 3, y1: 2 })).toEqual({ ok: false, reason: 'already explored' });
  });

  it('dark ground nobody can reach is left alone, and nobody gets stuck on it', () => {
    // The right side is walled off.
    const state = dark(['s.........#' + '.'.repeat(20), '..........#' + '.'.repeat(20)], { scout: 3 });
    applyCommand(state, { type: 'designateExplore', x0: 20, y0: 0, x1: 30, y1: 1 });
    for (let i = 0; i < 100; i++) tick(state);
    expect(state.jobs).toMatchObject([{ type: 'scout', reservedBy: null }]);
    expect(checkInvariants(state)).toEqual([]);
  });
});

describe('scouting through the day', () => {
  it(`scouts stop at ${SCOUT_HOME_HOUR}:00 and head home, and go out again in the morning`, () => {
    const state = dark(STRIP, { scout: 3 });
    state.tick = (SCOUT_HOME_HOUR - 6) * TICKS_PER_HOUR - 30; // half an hour before
    applyCommand(state, { type: 'designateExplore', x0: 40, y0: 0, x1: 59, y1: 2 });
    const s = state.survivors[0]!;
    runUntil(state, () => s.jobId !== null, 20);
    runUntil(state, () => s.note === 'heading home before dark', 100);
    expect(s.jobId).toBeNull();
    for (let i = 0; i < 100; i++) tick(state);
    expect(s.jobId).toBeNull(); // nobody takes the scout job in the evening
    state.tick = DAY_LENGTH; // skip the night (and its zombies): 06:00 next day
    runUntil(state, () => s.jobId !== null, 50); // out scouting again
    expect(scoutingOver(state.tick)).toBe(false);
  });

  it('a newly seen crate marks its ruin, once, and the mark goes when the crates are empty', () => {
    const state = dark(['s' + '.'.repeat(19) + 'c.c' + '.'.repeat(7)], { scavenge: 3 });
    expect(state.pointsOfInterest).toEqual([]);
    applyCommand(state, { type: 'moveSurvivor', survivorId: state.survivors[0]!.id, x: 16, y: 0 });
    runUntil(state, () => state.pointsOfInterest.length > 0);
    expect(state.pointsOfInterest).toMatchObject([{ x: 20, y: 0, label: 'ruin with crates' }]);
    expect(state.events.at(-1)!.message).toBe('Found a ruin with crates at (20, 0)');
    runUntil(state, () => isExplored(state, 22, 0));
    expect(state.pointsOfInterest).toHaveLength(1);
    for (const f of state.furniture) f.loot = 0;
    for (let i = 0; i < 60; i++) tick(state);
    expect(state.pointsOfInterest).toEqual([]);
  });
});
