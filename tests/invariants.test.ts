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
import { isStructure } from '../src/sim/breach';
import { applyCommand, type Command } from '../src/sim/commands';
import { getTile } from '../src/sim/grid';
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
  '....,...~~~.....,.......',
  '..www......ff..mm..c.c..',
  '...................c..k.',
  '..####D####.............',
  '..#...t...#.............',
  '..#########.............',
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
  // Deaths can empty the colony: then the only useful command is a new survivor.
  if (state.survivors.length === 0) return { type: 'spawnSurvivor', x, y };
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
  if (roll < 70) return { type: 'designateScavenge', x, y };
  // Occasionally wound someone badly (they may die mid-job or mid-sleep) or add a survivor.
  if (roll < 72) return { type: 'setHealth', survivorId: survivor.id, health: randomInt(driver, 0, 4) };
  if (roll < 73) return { type: 'spawnSurvivor', x, y };
  if (roll < 76) return { type: 'spawnZombie', x, y };
  if (roll < 82) return { type: 'designateFarm', x, y };
  // Wound someone (hurt or critical, not lethal) so they slow down, rest, and look for bandages.
  if (roll < 85) return { type: 'setHealth', survivorId: survivor.id, health: randomInt(driver, 150, 700) };
  // Answer a waiting wanderer (or leave them to give up on their own).
  if (roll < 90 && state.wanderers.length > 0) {
    const w = state.wanderers[roll % state.wanderers.length]!;
    return { type: roll < 88 ? 'acceptWanderer' : 'dismissWanderer', wandererId: w.id };
  }
  // Weapons lying around (guards arm themselves) and the odd armory.
  if (roll >= 90 && roll < 94) return { type: 'spawnWeapon', x, y };
  if (roll >= 94 && roll < 96) return { type: 'designateRoom', x, y, purpose: 'armory' };
  // Tables and mess halls, so some meals are carried to a seat.
  if (roll >= 96 && roll < 100) return { type: 'designateBuild', x, y, structure: 'table' };
  if (roll >= 102 && roll < 105) return { type: 'designateBuild', x, y, structure: 'workbench' };
  if (roll >= 105 && roll < 108) return { type: 'setCraftTarget', kind: roll < 106 ? 'weapon' : 'bandage', count: roll % 4 };
  // Flip the storage room to a rec room and back, so idle survivors sometimes go and relax.
  if (roll >= 111 && roll < 115 && isStructure(getTile(state.grid, x, y))) return { type: 'damageStructure', x, y, amount: 40 * (1 + (roll % 5)) };
  // Scout the dark strip at the bottom right (if any of it is still dark).
  if (roll >= 115 && roll < 118) return { type: 'designateExplore', x0: 12, y0: 10, x1: 23, y1: 12 };
  if (roll >= 108 && roll < 111) return { type: 'designateRoom', x: 14, y: 3, purpose: roll < 110 ? 'recroom' : 'storage' };
  if (roll >= 100 && roll < 102) {
    // Aim at a table, if any, so whatever room it stands in can become a working mess hall.
    const tables = state.furniture.filter((f) => f.kind === 'table');
    const at = tables.length > 0 ? tables[roll % tables.length]! : { x, y };
    return { type: 'designateRoom', x: at.x, y: at.y, purpose: 'messhall' };
  }
  return null;
}

describe('stuck-state invariant', () => {
  /** Outcomes seen across all seeds (tests in a file run in order). */
  const notesSeen = new Set<string>();
  let deaths = 0;
  /** Wanderer outcomes seen across all seeds. */
  const wandererEvents = new Set<string>();
  let mealsAtTables = 0;

  for (const seed of [1, 2, 3, 4, 5]) {
    it(`holds under random designations, orders, and wall edits (seed ${seed})`, () => {
      const initial = loadScenario({
        seed,
        map: MAP,
        rooms: [
          { x: 3, y: 3, purpose: 'housing' },
          { x: 14, y: 3, purpose: 'storage' },
          { x: 3, y: 11, purpose: 'messhall' },
        ],
      });
      // A standing order at the workbench, so crafting always gets exercised.
      initial.craftTargets = { weapon: 1, bandage: 2 };
      // The bottom-right strip starts unexplored, so fog and scouting are exercised.
      for (let y = 10; y <= 12; y++) for (let x = 12; x <= 23; x++) initial.grid.explored[y * initial.grid.width + x] = 0;
      // Start at 18:00 so the run covers a whole night of zombies and the dawn retreat.
      initial.tick = 12 * 200;
      const state = cloneState(initial);
      const driver = { rng: seedRng(seed * 7919) }; // Player input, separate from sim RNG.
      const stillWithGoal = new Map<string, number>();
      /** Last tick's position and needs state, to spot legitimate one-tick hold-ups. */
      const before = new Map<string, { pos: string; moving: boolean; needs: string; jobId: string | null }>();
      const idleWithWork = new Map<string, number>();
      const onJob = new Map<string, { id: string; since: number }>();

      for (let t = 0; t < TICKS; t++) {
        const cmd = randomCommand(state, driver);
        const ordered = cmd?.type === 'moveSurvivor' && applyCommand(state, cmd).ok ? cmd.survivorId : null;
        if (cmd && cmd.type !== 'moveSurvivor') applyCommand(state, cmd);

        const versionBefore = state.walkabilityVersion;
        tick(state);
        // A wall or door finished this tick can cut a path: re-planning costs one standing tick.
        const pathCut = state.walkabilityVersion !== versionBefore;

        const problems = checkInvariants(state);
        expect(problems, `tick ${state.tick}\n${dumpText(state)}`).toEqual([]);

        for (const s of state.survivors) {
          const where = `${s.name} at tick ${state.tick}: ${s.note}\n${dumpText(state)}`;
          if (s.note) notesSeen.add(s.note.split(' ')[0]!);
          if (s.note === 'ate a meal at a table') mealsAtTables += 1;

          // Legitimate extra standing ticks: being moved aside (a wall finished on your tile clears
          // your path) and a need kicking in (dropping work for bed, food, a bandage, or a weapon). Anything else repeated is a bug.
          const prev = before.get(s.id);
          const needs = `${s.sleep ? 'sleep' : ''}${s.meal ? 'meal' : ''}${s.treatment ? 'treat' : ''}${s.arming ? 'arm' : ''}`;
          const movedAside = prev !== undefined && !prev.moving && prev.pos !== `${s.pos.x},${s.pos.y}`;
          const needKickedIn = prev !== undefined && prev.needs !== needs && needs !== '';
          // A job cancelled under you, and straight on to the next: one more claim tick.
          const jobSwitched = prev !== undefined && prev.jobId !== null && s.jobId !== null && prev.jobId !== s.jobId;
          before.set(s.id, { pos: `${s.pos.x},${s.pos.y}`, moving: s.move !== null, needs, jobId: s.jobId });
          const excused = s.id === ordered || movedAside || needKickedIn || pathCut || jobSwitched;
          const still = s.goal && !s.move && !excused ? (stillWithGoal.get(s.id) ?? 0) + 1 : 0;
          stillWithGoal.set(s.id, still);
          expect(still, `stuck with goal ${JSON.stringify(s.goal)}: ${where}`).toBeLessThanOrEqual(MAX_TICKS_WITH_GOAL_BUT_STILL);

          // Mid-breakdown is deliberately idle. Relaxing is not excused: a job turning up must end it.
          const idle = !s.jobId && !s.goal && !s.move && !s.sleep && !s.meal && !s.arming && !s.breakdown;
          const idleTicks = idle && chooseJob(state, s) ? (idleWithWork.get(s.id) ?? 0) + 1 : 0;
          idleWithWork.set(s.id, idleTicks);
          expect(idleTicks, `idle with doable work: ${where}`).toBeLessThanOrEqual(MAX_IDLE_WITH_DOABLE_JOB);

          const current = onJob.get(s.id);
          if (!s.jobId) onJob.delete(s.id);
          else if (current?.id !== s.jobId) onJob.set(s.id, { id: s.jobId, since: state.tick });
          else expect(state.tick - current.since, `on one job too long: ${where}`).toBeLessThanOrEqual(MAX_TICKS_ON_ONE_JOB);
        }
      }

      for (const e of state.events) {
        for (const outcome of ['a wanderer', 'joined', 'turned away', 'gave up', 'walled out']) {
          if (e.message.includes(outcome)) wandererEvents.add(outcome);
        }
      }
      deaths += state.events.filter((e) => e.message.includes(' died of ')).length;

      const replayed = replay(initial, state.commandLog, state.tick);
      expect(JSON.stringify(replayed)).toBe(JSON.stringify(state));
    });
  }

  it('saw survivors die along the way (death handling is exercised)', () => {
    expect(deaths).toBeGreaterThan(0);
  });

  it('saw meals carried to a mess hall table', () => {
    expect(mealsAtTables).toBeGreaterThan(0);
  });

  it('saw wanderers arrive, join, and get turned away', () => {
    expect([...wandererEvents].sort()).toEqual(expect.arrayContaining(['a wanderer', 'joined', 'turned away']));
  });

  it('exercised every outcome across the seeds (guards against a vacuous run)', () => {
    // 'stopped:' is usually overwritten in the same tick by the next job, so
    // invalid-job recovery is covered by dedicated tests in jobs.test.ts.
    const expected = ['took', 'picked', 'built', 'removed', 'delivered', 'stored', 'cleaned', 'arrived', 'going', 'asleep', 'no', 'woke', 'eating', 'ate', 'scavenged', 'attacked', 'killed', 'sowed', 'harvested', 'treated', 'armed', 'repaired', 'crafted', 'brought', 'relaxing', 'having'];
    expect(expected.filter((n) => !notesSeen.has(n)), `seen: ${[...notesSeen].join(', ')}`).toEqual([]);
  });
});
