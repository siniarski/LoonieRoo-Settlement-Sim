import { describe, expect, it } from 'vitest';
import { NO_JOBS } from '../src/sim/jobTypes';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { checkInvariants } from '../src/sim/invariants';
import { findPath, findPathToAny } from '../src/sim/pathfinding';
import { TIRED } from '../src/sim/rest';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR, clockText, isNight, nightNumber } from '../src/sim/time';
import { MAX_ZOMBIES, ZOMBIE_DAMAGE, isSpawnTick, zombiesForNight } from '../src/sim/zombies';

/** 22:00 on day 1: night, and on a spawn boundary. */
const NIGHT = 16 * TICKS_PER_HOUR;

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 1500): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

/** A scenario at night, survivors doing nothing unless asked. */
function night(map: string[], jobs: Partial<typeof NO_JOBS> = {}) {
  const state = loadScenario({ seed: 3, map });
  state.tick = NIGHT + 1; // just past a spawn boundary, so no surprise spawns
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

describe('zombies', () => {
  it('doors stop zombies but not survivors', () => {
    const state = loadScenario({ seed: 0, map: ['###', '.D.', '###'] });
    expect(findPath(state.grid, { x: 0, y: 1 }, { x: 2, y: 1 })).toHaveLength(2);
    expect(findPathToAny(state.grid, { x: 0, y: 1 }, [{ x: 2, y: 1 }], 'zombie')).toBeNull();
  });

  it('spawn only at night, on outdoor map edges, away from survivors, up to a cap', () => {
    const day = loadScenario({ seed: 3, map: Array.from({ length: 12 }, () => '.'.repeat(30)) });
    for (let i = 0; i < 800; i++) tick(day);
    expect(day.zombies).toEqual([]);

    const state = night(Array.from({ length: 12 }, (_, y) => (y === 6 ? `${'.'.repeat(14)}s${'.'.repeat(15)}` : '.'.repeat(30))));
    state.tick = NIGHT;
    tick(state);
    expect(state.zombies).toHaveLength(1);
    const z = state.zombies[0]!;
    const onEdge = z.pos.x === 0 || z.pos.y === 0 || z.pos.x === 29 || z.pos.y === 11;
    expect(onEdge).toBe(true);
    expect(state.events.at(-1)?.message).toMatch(/^A zombie appeared at/);
    // Run a whole night: never more than the cap.
    for (let i = 0; i < 6 * TICKS_PER_HOUR; i++) {
      tick(state);
      expect(state.zombies.length).toBeLessThanOrEqual(MAX_ZOMBIES);
    }
  });

  it('each night brings one more zombie, spread through the night', () => {
    const spawnTicks = (night: number) => {
      const ticks: number[] = [];
      for (let t = (night - 1) * DAY_LENGTH; t < night * DAY_LENGTH + DAY_LENGTH; t++) {
        if (isSpawnTick(t) && nightNumber(t) === night) ticks.push(t);
      }
      return ticks;
    };
    expect([1, 2, 3, 10].map(zombiesForNight)).toEqual([4, 5, 6, 13]);
    expect(spawnTicks(1).map((t) => clockText(t))).toEqual(['22:00', '00:00', '02:00', '04:00']);
    expect(spawnTicks(2)).toHaveLength(5);
    expect(spawnTicks(3)).toHaveLength(6);
    expect(spawnTicks(3).every((t) => isNight(t))).toBe(true);
  });

  it('a whole run: 4, then 5, then 6 zombies, with a nightfall notice each night', () => {
    // A survivor safe in a sealed box, so nobody fights and every spawn is counted.
    const map = Array.from({ length: 20 }, (_, y) =>
      y === 9 ? `${'.'.repeat(17)}###${'.'.repeat(20)}` : y === 10 ? `${'.'.repeat(17)}#s#${'.'.repeat(20)}` : y === 11 ? `${'.'.repeat(17)}###${'.'.repeat(20)}` : '.'.repeat(40),
    );
    const state = loadScenario({ seed: 5, map });
    state.survivors[0]!.priorities = { ...NO_JOBS };
    for (let i = 0; i < 3 * DAY_LENGTH; i++) tick(state);
    const perNight = [1, 2, 3].map((n) => state.events.filter((e) => e.message.startsWith('A zombie appeared') && nightNumber(e.tick) === n).length);
    expect(perNight).toEqual([4, 5, 6]);
    expect(state.events.filter((e) => e.message.endsWith('zombies expected')).map((e) => e.message)).toEqual([
      'Night 1 falls: 4 zombies expected',
      'Night 2 falls: 5 zombies expected',
      'Night 3 falls: 6 zombies expected',
    ]);
  });

  it('a zombie walks up to a survivor in the open and attacks', () => {
    const state = night(['z.........s']);
    const s = state.survivors[0]!;
    runUntil(state, () => s.health < 1000);
    expect(s.health).toBe(1000 - ZOMBIE_DAMAGE);
    expect(s.note).toMatch(/^attacked by a zombie/);
    expect(state.zombies[0]!.pos.x).toBe(9);
  });

  it('enough hits kill, and the death names the zombie', () => {
    const state = night(['z.s']);
    const name = state.survivors[0]!.name;
    runUntil(state, (st) => st.survivors.length === 0, 400);
    expect(state.events.at(-1)?.message).toBe(`${name} died of a zombie attack`);
  });

  it('a closed building with a door keeps a survivor safe', () => {
    const map = [
      'z..........',
      '...#####...',
      '...#.s.#...',
      '...##D##...',
      '...........',
    ];
    const state = night(map);
    for (let i = 0; i < 600; i++) tick(state);
    expect(state.survivors[0]!.health).toBe(1000);
    expect(state.zombies[0]!.targetId).toBeNull(); // nobody reachable: it wanders
  });

  it('a guard hunts the zombie down and kills it', () => {
    const state = night(['z...........s'], { guard: 3 });
    const s = state.survivors[0]!;
    tick(state);
    expect(s.note).toMatch(/^took guard job/);
    runUntil(state, (st) => st.zombies.length === 0);
    expect(state.events.at(-1)?.message).toBe(`${s.name} killed a zombie`);
    expect(s.note).toMatch(/^killed a zombie/);
    expect(state.jobs).toEqual([]);
  });

  it('balance: one healthy guard beats a basic zombie and keeps at least 80% health', () => {
    for (const gap of [3, 10, 20]) {
      const state = night([`z${'.'.repeat(gap)}s`, '.'.repeat(gap + 2)], { guard: 3 });
      const s = state.survivors[0]!;
      runUntil(state, (st) => st.zombies.length === 0, 600);
      expect(state.survivors, `gap ${gap}`).toEqual([s]);
      expect(s.health, `gap ${gap}`).toBeGreaterThanOrEqual(800);
    }
  });

  it('a tired or hungry guard finishes the fight before bed or food', () => {
    const state = night(['z.............b.f..s'], { guard: 3 });
    const s = state.survivors[0]!;
    tick(state);
    expect(s.note).toMatch(/^took guard job/);
    applyCommand(state, { type: 'setRest', survivorId: s.id, rest: TIRED - 1 });
    applyCommand(state, { type: 'setSatiety', survivorId: s.id, satiety: 100 });
    runUntil(state, (st) => st.zombies.length === 0);
    expect(state.events.at(-1)?.message).toBe(`${s.name} killed a zombie`);
  });

  it('two guards never take the same zombie', () => {
    const state = night(['s.....z.....s'], { guard: 3 });
    for (let i = 0; i < 5; i++) {
      tick(state);
      expect(state.survivors.filter((s) => s.jobId).length).toBeLessThanOrEqual(1);
    }
  });

  it('at dawn zombies shamble off and guard jobs are released', () => {
    const state = night(['z...........s'], { guard: 3 });
    tick(state);
    expect(state.survivors[0]!.jobId).not.toBeNull();
    state.tick = DAY_LENGTH - TICKS_PER_HOUR; // 05:00 on day 2
    tick(state);
    expect(state.zombies).toEqual([]);
    expect(state.survivors[0]!.jobId).toBeNull();
    expect(state.events.at(-1)?.message).toBe('1 zombie shambled off at dawn');
    expect(checkInvariants(state)).toEqual([]);
  });

  it('a hit wakes a sleeper', () => {
    const state = night(['....z', 'sb...']);
    const s = state.survivors[0]!;
    // Asleep in bed before the zombie arrives (it starts 4 tiles away).
    s.pos = { x: 1, y: 1 };
    applyCommand(state, { type: 'setRest', survivorId: s.id, rest: 100 });
    state.zombies = [];
    runUntil(state, () => s.sleep?.asleep === true);
    applyCommand(state, { type: 'spawnZombie', x: 4, y: 0 });
    runUntil(state, () => s.hurtTick >= 0);
    // Woken the same tick they were hit, and (zombie close by) not going back to bed.
    expect(s.sleep).toBeNull();
    expect(state.furniture[0]!.occupant).toBeNull();
  });

  it('nobody goes to bed with a zombie nearby', () => {
    const state = night(['s.b....z']);
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'setRest', survivorId: s.id, rest: TIRED - 1 });
    tick(state);
    expect(s.sleep).toBeNull();
  });

  it('building a wall or door on a zombie moves it off', () => {
    const state = night(['....', '.z..', '....']);
    applyCommand(state, { type: 'placeDoor', x: 1, y: 1 });
    expect(state.zombies[0]!.pos).not.toEqual({ x: 1, y: 1 });
    expect(checkInvariants(state)).toEqual([]);
  });
});
