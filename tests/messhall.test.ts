import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { HUNGRY } from '../src/sim/food';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { MISERABLE, MOOD_BASE, THOUGHTS, addThought, mood, moodFactors } from '../src/sim/mood';
import { killSurvivor } from '../src/sim/needs';
import { REST_MAX, TIRED } from '../src/sim/rest';
import { designationStatus, type RoomPurpose } from '../src/sim/rooms';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import type { Survivor } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 1500): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], rooms: { x: number; y: number; purpose: RoomPurpose }[] = [], jobs: Partial<typeof NO_JOBS> = {}) {
  const state = loadScenario({ seed: 5, map, rooms });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

const hungry = (state: SimState, s: Survivor) => applyCommand(state, { type: 'setSatiety', survivorId: s.id, satiety: HUNGRY - 1 });
const thoughtsOf = (s: Survivor) => s.thoughts.map((t) => t.kind);

// A 3x3 hall with a table in the middle, its door on the bottom wall; food and survivor outside.
const HALL = ['#####', '#...#', '#.t.#', '#...#', '##D##', '.....', 'f.s..'];
const MESS = { x: 1, y: 1, purpose: 'messhall' as const };

describe('mess hall', () => {
  it('needs walls, a door, and a table', () => {
    const state = setup(['#####', '#...#', '#...#', '##D##', '.s...'], [{ x: 1, y: 1, purpose: 'messhall' }]);
    const status = designationStatus(state, state.roomDesignations[0]!);
    expect(status.requirements.filter((r) => !r.met).map((r) => r.label)).toEqual(['at least 1 table']);
    applyCommand(state, { type: 'placeTable', x: 2, y: 1 });
    expect(designationStatus(state, state.roomDesignations[0]!).complete).toBe(true);
  });

  it('a hungry survivor carries food to a seat by the table and eats there', () => {
    const state = setup(HALL, [MESS]);
    const s = state.survivors[0]!;
    hungry(state, s);
    tick(state);
    expect(s.meal?.seat).toEqual({ x: 1, y: 2 }); // nearest to the food; ties go to the top row
    runUntil(state, () => s.carrying !== null);
    expect(s.note).toBe('taking food to the mess hall at (1, 2)');
    runUntil(state, () => s.meal === null);
    expect(s.pos).toEqual({ x: 1, y: 2 });
    expect(s.note).toBe('ate a meal at a table');
    expect(state.items).toEqual([]);
    expect(thoughtsOf(s)).toEqual(['ateAtTable']);
  });

  it('without a mess hall, food is eaten where it lies, and that is a little grim', () => {
    const state = setup(HALL);
    const s = state.survivors[0]!;
    hungry(state, s);
    runUntil(state, () => s.note === 'ate a meal');
    expect(s.pos).toEqual({ x: 0, y: 6 });
    expect(thoughtsOf(s)).toEqual(['ateWithoutTable']);
  });

  it('two hungry survivors never take the same seat', () => {
    // A one-seat hall: the table's only free neighbour is (2, 1).
    const state = setup(['#####', '#t..#', '###D#', 'f...f', 's...s'], [{ x: 2, y: 1, purpose: 'messhall' }]);
    for (const s of state.survivors) hungry(state, s);
    tick(state);
    expect(state.survivors.map((s) => s.meal?.seat ?? null).filter((p) => p !== null)).toEqual([{ x: 2, y: 1 }]);
    runUntil(state, () => state.survivors.every((s) => s.meal === null && s.satiety > HUNGRY));
    expect(state.survivors.map((s) => s.note).sort()).toEqual(['ate a meal', 'ate a meal at a table']);
  });

  it('an order on the way to the hall drops the food, still reserved by nobody', () => {
    const state = setup(HALL, [MESS]);
    const s = state.survivors[0]!;
    hungry(state, s);
    runUntil(state, () => s.carrying !== null);
    applyCommand(state, { type: 'moveSurvivor', survivorId: s.id, x: 4, y: 6 });
    expect(checkInvariants(state)).toEqual([]);
    expect(s.carrying).toBeNull();
    expect(state.items[0]).toMatchObject({ carriedBy: null, reservedBy: null, pos: s.pos });
  });

  it('if the table goes while they walk, they eat where they stop, without the table', () => {
    const state = setup(HALL, [MESS]);
    const s = state.survivors[0]!;
    hungry(state, s);
    runUntil(state, () => s.carrying !== null);
    applyCommand(state, { type: 'clearTile', x: 2, y: 2 });
    runUntil(state, () => s.meal === null);
    expect(s.note).toBe('ate a meal');
    expect(thoughtsOf(s)).toEqual(['ateWithoutTable']);
  });

  it('survivors build tables from wood like beds', () => {
    const state = setup(['T..', 's.w'], [], { build: 3, haul: 3 });
    runUntil(state, () => state.furniture.some((f) => f.kind === 'table'));
    expect(state.blueprints).toEqual([]);
  });
});

describe('mood', () => {
  it('starts at the baseline and moves with thoughts', () => {
    const state = setup(['s..']);
    const s = state.survivors[0]!;
    expect(mood(state, s)).toBe(MOOD_BASE);
    addThought(state, s, 'ateAtTable');
    addThought(state, s, 'sleptOnFloor');
    expect(mood(state, s)).toBe(MOOD_BASE + 10 - 10);
    expect(moodFactors(state, s)).toEqual([
      ['ate at a table', 10],
      ['slept on the floor', -10],
    ]);
  });

  it('a repeated thought refreshes instead of stacking, and thoughts fade', () => {
    const state = setup(['s..']);
    const s = state.survivors[0]!;
    addThought(state, s, 'ateWithoutTable');
    state.tick += 100;
    addThought(state, s, 'ateWithoutTable');
    expect(s.thoughts).toEqual([{ kind: 'ateWithoutTable', until: state.tick + THOUGHTS.ateWithoutTable.duration }]);
    state.tick += THOUGHTS.ateWithoutTable.duration;
    tick(state);
    expect(s.thoughts).toEqual([]);
    expect(mood(state, s)).toBe(MOOD_BASE);
  });

  it('being hurt weighs on mood while it lasts', () => {
    const state = setup(['s..']);
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'setHealth', survivorId: s.id, health: 450 });
    expect(mood(state, s)).toBe(MOOD_BASE - 10);
  });

  it('everyone left remembers a death', () => {
    const state = setup(['s.s.s']);
    killSurvivor(state, state.survivors[0]!, 'a test');
    expect(state.survivors.map(thoughtsOf)).toEqual([['sawDeath'], ['sawDeath']]);
  });

  it('a full night in a bed cheers; on the floor it grates', () => {
    const sleep = (map: string[]) => {
      const state = setup(map);
      const s = state.survivors[0]!;
      applyCommand(state, { type: 'setRest', survivorId: s.id, rest: TIRED - 1 });
      runUntil(state, () => s.sleep?.asleep === true);
      runUntil(state, () => s.note === 'woke up rested', 3000);
      expect(s.rest).toBe(REST_MAX);
      return thoughtsOf(s);
    };
    expect(sleep(['s.b'])).toEqual(['sleptInBed', 'wellRested']);
    expect(sleep(['s..'])).toEqual(['sleptOnFloor']);
  });

  it(`miserable survivors (mood under ${MISERABLE}) work at 3/4 speed`, () => {
    const timeToRemove = (miserable: boolean) => {
      const state = setup(['s#'], [], { build: 3 });
      const s = state.survivors[0]!;
      if (miserable) {
        addThought(state, s, 'sawDeath');
        addThought(state, s, 'sleptOnFloor');
      }
      applyCommand(state, { type: 'designateRemove', x: 1, y: 0 });
      return runUntil(state, (st) => st.grid.tiles[1] === 'floor');
    };
    const content = timeToRemove(false);
    const miserable = timeToRemove(true);
    expect(miserable).toBeGreaterThan(content);
    expect(miserable).toBeLessThanOrEqual(Math.ceil((content * 4) / 3) + 2);
  });
});
