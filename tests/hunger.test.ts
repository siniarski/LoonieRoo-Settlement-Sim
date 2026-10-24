import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { EAT_TICKS, HUNGRY, MEAL_VALUE, SATIETY_MAX, initialSatiety } from '../src/sim/food';
import { checkInvariants } from '../src/sim/invariants';
import { isStoreJob } from '../src/sim/jobs';
import { TIRED } from '../src/sim/rest';
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

function setup(map: string[]) {
  const state = loadScenario({ seed: 1, map });
  for (const s of state.survivors) s.priorities = { build: 0, haul: 0, clean: 0 };
  return state;
}

const starve = (state: SimState, s: Survivor, satiety = HUNGRY - 1) =>
  applyCommand(state, { type: 'setSatiety', survivorId: s.id, satiety });

describe('hunger and food', () => {
  it('starts staggered and drains by 1 per tick', () => {
    expect([0, 1, 2, 3].map(initialSatiety)).toEqual([3000, 2400, 1800, 2700]);
    const state = setup(['s...']);
    for (let i = 0; i < 10; i++) tick(state);
    expect(state.survivors[0]!.satiety).toBe(SATIETY_MAX - 10);
  });

  it('a hungry survivor walks to the nearest food, eats it, and is fed', () => {
    const state = setup(['s....f..f']);
    const s = state.survivors[0]!;
    starve(state, s);
    tick(state);
    expect(s.note).toBe('going to eat at (5, 0)');
    expect(state.items[0]!.reservedBy).toBe(s.id);
    runUntil(state, () => s.meal === null);
    expect(s.note).toBe('ate a meal');
    expect(state.items.map((i) => i.pos)).toEqual([{ x: 8, y: 0 }]);
    // Fed by one meal, minus what drained on the way and while eating.
    expect(s.satiety).toBeGreaterThan(HUNGRY - 1 + MEAL_VALUE - 5 * 4 - EAT_TICKS - 5);
  });

  it('two hungry survivors never go for the same food', () => {
    // The middle food is nearest for both; the second one must settle for the far one.
    const state = setup(['s.f.s.....f']);
    for (const s of state.survivors) starve(state, s);
    tick(state);
    expect(checkInvariants(state)).toEqual([]);
    const targets = state.survivors.map((s) => s.meal?.itemId);
    expect(new Set(targets).size).toBe(2);
    runUntil(state, (st) => st.items.length === 0);
  });

  it('with no food, a hungry survivor keeps working', () => {
    const state = loadScenario({ seed: 1, map: ['s.w.....'] });
    const s = state.survivors[0]!;
    s.priorities.clean = 0;
    starve(state, s);
    applyCommand(state, { type: 'designateBuild', x: 6, y: 0, structure: 'wall' });
    runUntil(state, (st) => st.grid.tiles[6] === 'wall');
    expect(s.meal).toBeNull();
  });

  it('getting hungry mid-job drops the job (and the wood) when there is food to go to', () => {
    const state = loadScenario({ seed: 1, map: ['s.w........f'] });
    const s = state.survivors[0]!;
    s.priorities.clean = 0;
    applyCommand(state, { type: 'designateBuild', x: 9, y: 0, structure: 'wall' });
    runUntil(state, () => s.carrying !== null);
    starve(state, s);
    tick(state);
    expect(s.jobId).toBeNull();
    expect(s.carrying).toBeNull();
    expect(s.meal).not.toBeNull();
    runUntil(state, () => s.note === 'ate a meal');
    // ...and then goes back to finish the wall.
    runUntil(state, (st) => st.grid.tiles[9] === 'wall');
  });

  it('eats before sleeping when both hungry and tired', () => {
    const state = setup(['s.f.b']);
    const s = state.survivors[0]!;
    starve(state, s);
    applyCommand(state, { type: 'setRest', survivorId: s.id, rest: TIRED - 1 });
    tick(state);
    expect(s.meal).not.toBeNull();
    expect(s.sleep).toBeNull();
    runUntil(state, () => s.note === 'ate a meal');
    runUntil(state, () => s.sleep?.asleep === true);
  });

  it('sleepers are not woken to eat', () => {
    const state = setup(['s.b.f']);
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'setRest', survivorId: s.id, rest: TIRED - 1 });
    runUntil(state, () => s.sleep?.asleep === true);
    starve(state, s);
    for (let i = 0; i < 20; i++) tick(state);
    expect(s.sleep?.asleep).toBe(true);
    expect(s.meal).toBeNull();
  });

  it('food walled off mid-walk is given up, and the survivor finds other food', () => {
    const state = setup(['f.....s........f', '................']);
    const s = state.survivors[0]!;
    starve(state, s);
    tick(state);
    const first = s.meal!.itemId;
    // Wall off whichever food was chosen.
    const target = state.items.find((i) => i.id === first)!.pos!;
    const dx = target.x === 0 ? 1 : -1;
    applyCommand(state, { type: 'placeWall', x: target.x + dx, y: 0 });
    applyCommand(state, { type: 'placeWall', x: target.x, y: 1 });
    applyCommand(state, { type: 'placeWall', x: target.x + dx, y: 1 });
    runUntil(state, () => s.note === 'ate a meal');
    expect(state.items.map((i) => i.id)).toEqual([first]);
  });

  it('a direct order interrupts a meal and frees the food', () => {
    const state = setup(['s.....f', '.......']);
    const s = state.survivors[0]!;
    starve(state, s);
    tick(state);
    applyCommand(state, { type: 'moveSurvivor', survivorId: s.id, x: 0, y: 1 });
    expect(s.meal).toBeNull();
    expect(state.items[0]!.reservedBy).toBeNull();
  });

  it('food is stored like any item, but never used for building', () => {
    const map = [
      '..........',
      '.####.....',
      '.#..#..f..',
      '.#..#.....',
      '.##D#.....',
      '..s.......',
    ];
    const state = loadScenario({ seed: 1, map, rooms: [{ x: 2, y: 2, purpose: 'storage' }] });
    const s = state.survivors[0]!;
    s.priorities.clean = 0;
    applyCommand(state, { type: 'designateBuild', x: 8, y: 5, structure: 'wall' });
    tick(state);
    expect(state.jobs.some((j) => isStoreJob(j) && j.itemId === state.items[0]!.id)).toBe(true);
    runUntil(state, (st) => st.items[0]!.pos?.x === 2 || st.items[0]!.pos?.x === 3);
    expect(state.blueprints[0]!.delivered).toBe(false);
  });

  it('setSatiety and spawnFood validate their input', () => {
    const state = setup(['s.#']);
    expect(applyCommand(state, { type: 'setSatiety', survivorId: 's1', satiety: SATIETY_MAX + 1 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'setSatiety', survivorId: 's1', satiety: 0 }).ok).toBe(true);
    expect(applyCommand(state, { type: 'spawnFood', x: 2, y: 0 }).ok).toBe(false);
    expect(applyCommand(state, { type: 'spawnFood', x: 1, y: 0 }).ok).toBe(true);
    expect(dumpText(state)).toBe('sf#');
  });
});
