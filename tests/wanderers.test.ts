import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { DEFAULT_PRIORITIES } from '../src/sim/jobTypes';
import { checkInvariants } from '../src/sim/invariants';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR, START_HOUR } from '../src/sim/time';
import { ARRIVAL_HOUR, SECOND_ARRIVAL_HOUR, WANDERER_PATIENCE } from '../src/sim/wanderers';

/** The tick at which a given day and hour begin (tick 0 is day 1, 06:00). */
const at = (day: number, hour: number) => (day - 1) * DAY_LENGTH + (hour - START_HOUR) * TICKS_PER_HOUR;

const OPEN = ['........', '...s....', '........', '........'];

function runTo(state: SimState, target: number): void {
  while (state.tick < target) {
    tick(state);
    expect(checkInvariants(state)).toEqual([]);
  }
}

function setup(map = OPEN, seed = 3): SimState {
  const state = loadScenario({ seed, map });
  for (const s of state.survivors) s.priorities = { ...DEFAULT_PRIORITIES, build: 0, clean: 0 };
  return state;
}

const lastEvent = (state: SimState) => state.events.at(-1)?.message;

describe('wanderers', () => {
  it('nobody turns up on day 1', () => {
    const state = setup();
    state.tick = at(1, ARRIVAL_HOUR) - 1;
    runTo(state, at(1, ARRIVAL_HOUR) + 5);
    expect(state.wanderers).toEqual([]);
  });

  it('from day 2, a wanderer arrives at 10:00 on an outdoor edge tile and asks to join', () => {
    const state = setup();
    state.tick = at(2, ARRIVAL_HOUR) - 1;
    runTo(state, at(2, ARRIVAL_HOUR) + 1);
    expect(state.wanderers).toHaveLength(1);
    const w = state.wanderers[0]!;
    expect(w.x === 0 || w.y === 0 || w.x === 7 || w.y === 3).toBe(true);
    expect(lastEvent(state)).toBe(`${w.name}, a wanderer, is waiting at (${w.x}, ${w.y}): take them in?`);
  });

  it('arrives on edge floor, never in a wall', () => {
    // The left of the edge is all wall; only the right columns are open floor.
    const map = ['#####...', '#..s#...', '#...#...', '#####...'];
    for (const seed of [1, 2, 3, 4, 5, 6]) {
      const state = setup(map, seed);
      state.tick = at(2, ARRIVAL_HOUR);
      tick(state);
      const w = state.wanderers[0]!;
      expect(w.x, `seed ${seed}`).toBeGreaterThanOrEqual(5);
    }
  });

  it('the same seed brings the same wanderer to the same spot', () => {
    const where = () => {
      const state = setup();
      state.tick = at(2, ARRIVAL_HOUR);
      tick(state);
      return JSON.stringify(state.wanderers);
    };
    expect(where()).toBe(where());
  });

  it('a second wanderer comes at 14:00 only if there is a spare bed', () => {
    const count = (map: string[]) => {
      const state = setup(map);
      state.tick = at(2, ARRIVAL_HOUR);
      tick(state);
      applyCommand(state, { type: 'dismissWanderer', wandererId: state.wanderers[0]!.id });
      state.tick = at(2, SECOND_ARRIVAL_HOUR);
      tick(state);
      return state.wanderers.length;
    };
    expect(count(OPEN)).toBe(0); // one survivor, no beds
    expect(count(['........', '.b.s.b..', '........', '........'])).toBe(1); // two beds, one survivor
  });

  it('taking a wanderer in makes them a survivor where they stood', () => {
    const state = setup();
    state.tick = at(2, ARRIVAL_HOUR);
    tick(state);
    const w = state.wanderers[0]!;
    expect(applyCommand(state, { type: 'acceptWanderer', wandererId: w.id }).ok).toBe(true);
    expect(state.wanderers).toEqual([]);
    const joined = state.survivors.at(-1)!;
    expect(joined.name).toBe(w.name);
    expect(joined.pos).toEqual({ x: w.x, y: w.y });
    expect(joined.priorities).toEqual(DEFAULT_PRIORITIES);
    expect(lastEvent(state)).toBe(`${w.name} joined the colony`);
    expect(state.commandLog.at(-1)?.command).toEqual({ type: 'acceptWanderer', wandererId: w.id });
  });

  it('turning a wanderer away removes them', () => {
    const state = setup();
    state.tick = at(2, ARRIVAL_HOUR);
    tick(state);
    const w = state.wanderers[0]!;
    applyCommand(state, { type: 'dismissWanderer', wandererId: w.id });
    expect(state.wanderers).toEqual([]);
    expect(state.survivors).toHaveLength(1);
    expect(lastEvent(state)).toBe(`${w.name} was turned away`);
  });

  it('commands for a wanderer who is not there are rejected', () => {
    const state = setup();
    expect(applyCommand(state, { type: 'acceptWanderer', wandererId: 'v99' }).ok).toBe(false);
    expect(state.survivors).toHaveLength(1);
  });

  it('an ignored wanderer leaves after their patience runs out', () => {
    const state = setup();
    state.tick = at(2, ARRIVAL_HOUR);
    tick(state);
    const name = state.wanderers[0]!.name;
    runTo(state, at(2, ARRIVAL_HOUR) + WANDERER_PATIENCE);
    expect(state.wanderers).toHaveLength(1);
    tick(state);
    expect(state.wanderers).toEqual([]);
    expect(lastEvent(state)).toBe(`${name} gave up waiting and left`);
  });

  it('wanderers still waiting at nightfall leave', () => {
    const state = setup(['v.......', '...s....']);
    state.wanderers[0]!.leavesTick = Number.MAX_SAFE_INTEGER;
    state.tick = at(1, 21) - 1;
    tick(state);
    expect(state.wanderers).toHaveLength(1);
    tick(state);
    expect(state.wanderers).toEqual([]);
  });

  it('a wall placed on a wanderer moves them aside', () => {
    const state = setup(['v.......', '...s....']);
    applyCommand(state, { type: 'placeWall', x: 0, y: 0 });
    tick(state);
    expect(checkInvariants(state)).toEqual([]);
    expect(state.wanderers[0]).toMatchObject({ x: 1, y: 0 });
  });

  it('names keep coming in order, so a joiner never reuses a starting name', () => {
    const state = setup(['s.s.....', '........']);
    state.tick = at(2, ARRIVAL_HOUR);
    tick(state);
    const names = [...state.survivors.map((s) => s.name), state.wanderers[0]!.name];
    expect(new Set(names).size).toBe(3);
  });
});

describe('wanderers on a partly explored map', () => {
  it('turn up at the edge of explored ground, a short walk away, not at the map edge', () => {
    const map = Array.from({ length: 80 }, (_, y) => (y === 40 ? '.'.repeat(40) + 's' + '.'.repeat(39) : '.'.repeat(80)));
    const state = loadScenario({ seed: 3, map });
    for (const s of state.survivors) s.priorities = { ...DEFAULT_PRIORITIES, build: 0, clean: 0 };
    state.grid.explored.fill(0);
    state.tick = at(2, ARRIVAL_HOUR) - 1;
    tick(state); // looks round: a circle of explored ground
    tick(state);
    const w = state.wanderers[0]!;
    const seen = (x: number, y: number) => state.grid.explored[y * 80 + x] === 1;
    expect(seen(w.x, w.y)).toBe(true);
    expect([seen(w.x + 1, w.y), seen(w.x - 1, w.y), seen(w.x, w.y + 1), seen(w.x, w.y - 1)]).toContain(false);
    expect(w.x === 0 || w.y === 0 || w.x === 79 || w.y === 79).toBe(false);
  });
});
