import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { killSurvivor } from '../src/sim/needs';
import { planFurnish } from '../src/sim/planner';
import type { RoomPurpose } from '../src/sim/rooms';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import type { Survivor } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';
import { TICKS_PER_HOUR } from '../src/sim/time';
import { addZombie } from '../src/sim/zombies';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 1500): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], rooms: { x: number; y: number; purpose: RoomPurpose }[] = [], jobs: Partial<typeof NO_JOBS> = {}) {
  const state = loadScenario({ seed: 6, map, rooms });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

const tire = (state: SimState, s: Survivor) => applyCommand(state, { type: 'setRest', survivorId: s.id, rest: 0 });
const wakeUp = (state: SimState, s: Survivor) => {
  applyCommand(state, { type: 'setRest', survivorId: s.id, rest: 2000 });
  applyCommand(state, { type: 'moveSurvivor', survivorId: s.id, x: s.pos.x, y: s.pos.y + 1 });
};

describe('bed ownership', () => {
  it("your first bed becomes yours, and you go back to it, not a nearer one", () => {
    const state = setup(['b.....b', '......s', '.......']);
    const s = state.survivors[0]!;
    const [far, near] = state.furniture;
    tire(state, s);
    runUntil(state, () => s.sleep?.asleep === true);
    expect(near!.owner).toBe(s.id);
    wakeUp(state, s);
    // Walk over by the other bed, then get tired again: still goes home.
    applyCommand(state, { type: 'moveSurvivor', survivorId: s.id, x: 0, y: 1 });
    runUntil(state, () => s.pos.x === 0 && s.pos.y === 1 && !s.goal);
    tire(state, s);
    runUntil(state, () => s.sleep?.asleep === true);
    expect(s.sleep!.bedId).toBe(near!.id);
    expect(far!.owner).toBeNull();
  });

  it("nobody sleeps in someone else's bed; with no other bed it's the floor", () => {
    const state = setup(['b..', 's.s']);
    const [ash, bea] = state.survivors as [Survivor, Survivor];
    applyCommand(state, { type: 'assignBed', bedId: state.furniture[0]!.id, survivorId: ash.id });
    tire(state, bea);
    runUntil(state, () => bea.sleep?.asleep === true);
    expect(bea.sleep!.bedId).toBeNull();
  });

  it('a death frees the bed', () => {
    const state = setup(['b..', 's.s']);
    const [ash, bea] = state.survivors as [Survivor, Survivor];
    applyCommand(state, { type: 'assignBed', bedId: state.furniture[0]!.id, survivorId: ash.id });
    killSurvivor(state, ash, 'a test');
    expect(state.furniture[0]!.owner).toBeNull();
    tire(state, bea);
    runUntil(state, () => bea.sleep?.asleep === true);
    expect(bea.sleep!.bedId).toBe(state.furniture[0]!.id);
  });

  it("giving a bed away moves its sleeper out, and you only ever own one", () => {
    const state = setup(['b.b', 's.s']);
    const [ash, bea] = state.survivors as [Survivor, Survivor];
    const [first, second] = state.furniture;
    applyCommand(state, { type: 'assignBed', bedId: first!.id, survivorId: ash.id });
    applyCommand(state, { type: 'assignBed', bedId: second!.id, survivorId: ash.id });
    expect([first!.owner, second!.owner]).toEqual([null, ash.id]);
    tire(state, bea);
    runUntil(state, () => bea.sleep?.asleep === true);
    expect(bea.sleep!.bedId).toBe(first!.id);
    applyCommand(state, { type: 'assignBed', bedId: first!.id, survivorId: ash.id });
    expect(bea.sleep).toBeNull();
    expect(checkInvariants(state)).toEqual([]);
  });

  it('infirmary beds stay shared', () => {
    const state = setup(['####.', '#b.#.', '#..#.', '##D#.', '...s.'], [{ x: 1, y: 2, purpose: 'infirmary' }]);
    const s = state.survivors[0]!;
    tire(state, s);
    runUntil(state, () => s.sleep?.asleep === true);
    expect(s.sleep!.bedId).toBe(state.furniture[0]!.id);
    expect(state.furniture[0]!.owner).toBeNull();
  });
});

describe('barracks', () => {
  // Left: a barracks with one bed. Right: a bed in the open.
  const MAP = ['####........', '#b.#........', '#..#.....b..', '##D#........', '...s........', '............'];
  const BARRACKS = { x: 1, y: 2, purpose: 'barracks' as const };

  it('guards take barracks beds; others leave them be', () => {
    const guard = setup(MAP, [BARRACKS], { guard: 3 });
    tire(guard, guard.survivors[0]!);
    runUntil(guard, () => guard.survivors[0]!.sleep !== null);
    expect(guard.survivors[0]!.sleep!.bedId).toBe(guard.furniture[0]!.id);

    const civilian = setup(MAP, [BARRACKS]);
    tire(civilian, civilian.survivors[0]!);
    runUntil(civilian, () => civilian.survivors[0]!.sleep !== null);
    expect(civilian.survivors[0]!.sleep!.bedId).toBe(civilian.furniture[1]!.id);
  });

  it('a guard asleep in the barracks wakes when a zombie comes near; elsewhere they sleep on', () => {
    const run = (rooms: { x: number; y: number; purpose: RoomPurpose }[]) => {
      const state = setup(MAP, rooms, { guard: 3 });
      const s = state.survivors[0]!;
      tire(state, s);
      runUntil(state, () => s.sleep?.asleep === true);
      state.tick = 17 * TICKS_PER_HOUR + 1; // night
      addZombie(state, 6, 5);
      tick(state);
      return s;
    };
    const inBarracks = run([BARRACKS]);
    expect(inBarracks.sleep).toBeNull();
    expect(inBarracks.note).toMatch(/^(woken for guard duty|took guard job)/);
    // Same bed, no barracks: sleeps until hit.
    expect(run([]).sleep?.asleep).toBe(true);
  });

  it('the planner gives every guard a bed, up to capacity', () => {
    const state = setup(['#####', '#...#', '#...#', '##D##', 's.s.s'], [{ x: 1, y: 1, purpose: 'barracks' }]);
    state.survivors[0]!.priorities.guard = 1;
    state.survivors[1]!.priorities.guard = 2;
    expect(planFurnish(state, 1, 1).commands).toHaveLength(1); // 6 tiles: capacity 1
    expect(planFurnish(state, 1, 1).notes[0]).toBe('Planned 1 bed for the guards (room holds 1, 2 guarding).');
  });
});
