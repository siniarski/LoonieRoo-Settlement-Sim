// Stuck-state invariant (CLAUDE.md). Throw random designations, cancels,
// priority changes, direct orders, and instant wall edits at a small colony,
// and after every tick check that:
//   - survivors, jobs, items, and blueprints are mutually consistent
//   - nobody holds a goal without moving towards it
//   - nobody idles > 20 ticks while a job they could do exists
//   - nobody stays on one job > 600 ticks
// Then replay the session and check the whole state matches byte for byte.

import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand, type Command } from '../src/sim/commands';
import { checkInvariants } from '../src/sim/invariants';
import { chooseJob } from '../src/sim/jobs';
import { JOB_TYPES, type Priority } from '../src/sim/jobTypes';
import { replay } from '../src/sim/replay';
import { randomInt, seedRng } from '../src/sim/rng';
import { loadScenario } from '../src/sim/scenario';
import { cloneState, type SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';

const MAP = [
  '........................',
  '.######.....######..www.',
  '.#b.,.#.....#....#..www.',
  '.#.s..D.....D..s.#..www.',
  '.#....#..,..#....#......',
  '.######.....###D##..++..',
  '..........s.........d...',
  '....,...........,.......',
  '..www......ff...........',
  '........................',
];

/** Holding a goal while standing still is fine for one tick (the claim tick). */
const MAX_TICKS_WITH_GOAL_BUT_STILL = 1;
const MAX_IDLE_WITH_DOABLE_JOB = 20;
const MAX_TICKS_ON_ONE_JOB = 600;
const TICKS = 4000;

function randomCommand(state: SimState, driver: { rng: number }): Command | null {
  const roll = randomInt(driver, 0, 1000);
  const x = randomInt(driver, 0, state.grid.width);
  const y = randomInt(driver, 0, state.grid.height);
  const survivor = state.survivors[randomInt(driver, 0, state.survivors.length)]!;
  if (roll < 15) return { type: 'designateBuild', x, y, structure: 'wall' };
  if (roll < 22) return { type: 'designateBuild', x, y, structure: 'door' };
  if (roll < 25) return { type: 'designateBuild', x, y, structure: 'bed' };
  if (roll < 31) return { type: 'designateRemove', x, y };
  if (roll < 34) return { type: 'cancelDesignation', x, y };
  if (roll < 36) return { type: 'designateRoom', x, y, purpose: roll < 35 ? 'storage' : 'housing' };
  if (roll < 38) return { type: 'moveSurvivor', survivorId: survivor.id, x, y };
  if (roll < 45) {
    const jobType = JOB_TYPES[randomInt(driver, 0, JOB_TYPES.length)]!;
    return { type: 'setPriority', survivorId: survivor.id, jobType, priority: randomInt(driver, 0, 5) as Priority };
  }
  if (roll < 50) return { type: 'placeWall', x, y };
  if (roll < 54) return { type: 'clearTile', x, y };
  // Make someone tired (or rested) at a random moment, mid-job or mid-walk.
  if (roll < 57) return { type: 'setRest', survivorId: survivor.id, rest: randomInt(driver, 0, 2001) };
  if (roll < 60) return { type: 'setSatiety', survivorId: survivor.id, satiety: randomInt(driver, 0, 3001) };
  if (roll < 62) return { type: 'spawnFood', x, y };
  return null;
}

describe('stuck-state invariant', () => {
  /** Outcomes seen across all seeds (tests in a file run in order). */
  const notesSeen = new Set<string>();

  for (const seed of [1, 2, 3, 4, 5]) {
    it(`holds under random designations, orders, and wall edits (seed ${seed})`, () => {
      const initial = loadScenario({
        seed,
        map: MAP,
        rooms: [
          { x: 3, y: 3, purpose: 'housing' },
          { x: 14, y: 3, purpose: 'storage' },
        ],
      });
      const state = cloneState(initial);
      const driver = { rng: seedRng(seed * 7919) }; // Player input, separate from sim RNG.
      const stillWithGoal = new Map<string, number>();
      const idleWithWork = new Map<string, number>();
      const onJob = new Map<string, { id: string; since: number }>();

      for (let t = 0; t < TICKS; t++) {
        const cmd = randomCommand(state, driver);
        const ordered = cmd?.type === 'moveSurvivor' && applyCommand(state, cmd).ok ? cmd.survivorId : null;
        if (cmd && cmd.type !== 'moveSurvivor') applyCommand(state, cmd);

        tick(state);

        const problems = checkInvariants(state);
        expect(problems, `tick ${state.tick}\n${dumpText(state)}`).toEqual([]);

        for (const s of state.survivors) {
          const where = `${s.name} at tick ${state.tick}: ${s.note}\n${dumpText(state)}`;
          if (s.note) notesSeen.add(s.note.split(' ')[0]!);

          const still = s.goal && !s.move && s.id !== ordered ? (stillWithGoal.get(s.id) ?? 0) + 1 : 0;
          stillWithGoal.set(s.id, still);
          expect(still, `stuck with goal ${JSON.stringify(s.goal)}: ${where}`).toBeLessThanOrEqual(MAX_TICKS_WITH_GOAL_BUT_STILL);

          const idle = !s.jobId && !s.goal && !s.move && !s.sleep && !s.meal;
          const idleTicks = idle && chooseJob(state, s) ? (idleWithWork.get(s.id) ?? 0) + 1 : 0;
          idleWithWork.set(s.id, idleTicks);
          expect(idleTicks, `idle with doable work: ${where}`).toBeLessThanOrEqual(MAX_IDLE_WITH_DOABLE_JOB);

          const current = onJob.get(s.id);
          if (!s.jobId) onJob.delete(s.id);
          else if (current?.id !== s.jobId) onJob.set(s.id, { id: s.jobId, since: state.tick });
          else expect(state.tick - current.since, `on one job too long: ${where}`).toBeLessThanOrEqual(MAX_TICKS_ON_ONE_JOB);
        }
      }

      const replayed = replay(initial, state.commandLog, state.tick);
      expect(JSON.stringify(replayed)).toBe(JSON.stringify(state));
    });
  }

  it('exercised every outcome across the seeds (guards against a vacuous run)', () => {
    // 'stopped:' is usually overwritten in the same tick by the next job, so
    // invalid-job recovery is covered by dedicated tests in jobs.test.ts.
    const expected = ['took', 'picked', 'built', 'removed', 'delivered', 'stored', 'cleaned', 'arrived', 'going', 'asleep', 'no', 'woke', 'eating', 'ate'];
    expect(expected.filter((n) => !notesSeen.has(n)), `seen: ${[...notesSeen].join(', ')}`).toEqual([]);
  });
});
