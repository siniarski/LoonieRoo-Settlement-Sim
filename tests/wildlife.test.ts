import { describe, expect, it } from 'vitest';
import { BEAR_DAMAGE, HEALTH, MEAT, WILD_RADIUS, WOLF_CLOSE, WOLF_DAMAGE, WOLF_HUNGRY, WOLF_STARVING, addAnimal, updateAnimals } from '../src/sim/animals';
import { hitAnimal } from '../src/sim/handiwork';
import { checkInvariants } from '../src/sim/invariants';
import { loadScenario } from '../src/sim/scenario';
import { manhattan } from '../src/sim/space';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';

/** Open ground, size x size, one survivor in the middle. */
function wild(size = 90): SimState {
  const rows = Array.from({ length: size }, (_, y) => (y === size >> 1 ? '.'.repeat(size >> 1) + 's' + '.'.repeat(size - (size >> 1) - 1) : '.'.repeat(size)));
  const state = loadScenario({ seed: 5, map: rows });
  state.settings = { ...state.settings, zombies: 0 };
  state.tick = 8 * TICKS_PER_HOUR + 1; // morning: no night spawns
  return state;
}

/** Runs only the animals, n ticks (the rest of the sim left out: nobody walks off). */
function animals(state: SimState, n: number): void {
  for (let k = 0; k < n; k++) {
    state.tick += 1;
    updateAnimals(state);
  }
}

describe('wildlife', () => {
  it('rabbits turn up out in the open round your people, and not on a small map', () => {
    const big = wild();
    animals(big, DAY_LENGTH);
    const rabbits = big.animals.filter((a) => a.kind === 'rabbit');
    expect(rabbits.length).toBeGreaterThan(0);
    const small = wild(40);
    animals(small, DAY_LENGTH);
    expect(small.animals.filter((a) => a.kind !== 'deer')).toHaveLength(0);
  });

  it('a rabbit bolts from someone close', () => {
    const state = wild();
    const s = state.survivors[0]!;
    const r = addAnimal(state, 'rabbit', s.pos.x + 2, s.pos.y);
    animals(state, 20);
    expect(manhattan(r.pos, s.pos)).toBeGreaterThan(3);
  });

  it('a hungry wolf hunts down a rabbit and is fed', () => {
    const state = wild();
    const s = state.survivors[0]!;
    const w = addAnimal(state, 'wolf', s.pos.x - 30, s.pos.y);
    w.hunger = WOLF_HUNGRY;
    const r = addAnimal(state, 'rabbit', s.pos.x - 24, s.pos.y + 4);
    // The rabbit runs, but wolves are quicker over time; give it a while.
    for (let k = 0; k < 600 && state.animals.includes(r); k++) animals(state, 1);
    expect(state.animals.includes(r)).toBe(false);
    expect(w.hunger).toBeLessThan(WOLF_HUNGRY);
  });

  it('a fed wolf leaves people alone; one too close bites, and is marked for hunting', () => {
    const state = wild();
    const s = state.survivors[0]!;
    const far = addAnimal(state, 'wolf', s.pos.x + 10, s.pos.y);
    animals(state, 40);
    expect(s.health).toBe(100 * 10); // untouched
    state.animals.splice(state.animals.indexOf(far), 1);
    const close = addAnimal(state, 'wolf', s.pos.x + 1, s.pos.y);
    animals(state, 30);
    expect(s.health).toBeLessThan(1000);
    expect(close.hunted).toBe(true);
    // A warning: one bite, then it keeps clear.
    expect(1000 - s.health).toBe(WOLF_DAMAGE);
    expect(manhattan(close.pos, s.pos)).toBeGreaterThan(WOLF_CLOSE);
  });

  it('a starving wolf comes for people', () => {
    const state = wild();
    const s = state.survivors[0]!;
    const w = addAnimal(state, 'wolf', s.pos.x + 10, s.pos.y);
    w.hunger = WOLF_STARVING;
    animals(state, 120);
    expect(s.health).toBeLessThan(1000);
  });

  it('a bear mauls anyone who comes too close', () => {
    const state = wild();
    const s = state.survivors[0]!;
    addAnimal(state, 'bear', s.pos.x + 3, s.pos.y);
    animals(state, 40);
    expect(1000 - s.health).toBeGreaterThanOrEqual(BEAR_DAMAGE);
  });

  it('hitting a wolf sets its pack on you; brought down it leaves meat', () => {
    const state = wild();
    const s = state.survivors[0]!;
    const lead = addAnimal(state, 'wolf', s.pos.x + 8, s.pos.y);
    const mate = addAnimal(state, 'wolf', s.pos.x + 9, s.pos.y, lead.id);
    expect(hitAnimal(state, s, lead.id, 10)).toBe('hit the wolf');
    expect(lead.angryAt).toBe(s.id);
    expect(mate.angryAt).toBe(s.id);
    const food = state.items.filter((i) => i.kind === 'food').length;
    expect(hitAnimal(state, s, lead.id, HEALTH.wolf)).toBe('brought it down');
    expect(state.items.filter((i) => i.kind === 'food').length - food).toBe(MEAT.wolf);
  });

  it('animals far from everyone are gone at the hour', () => {
    const state = wild(200);
    const s = state.survivors[0]!;
    const r = addAnimal(state, 'rabbit', Math.min(199, s.pos.x + WILD_RADIUS + 5), s.pos.y);
    state.tick = 10 * TICKS_PER_HOUR - 1;
    animals(state, 1);
    expect(state.animals.includes(r)).toBe(false);
  });

  it('the whole sim stays sound with wildlife about for a few days', () => {
    const state = wild();
    for (let k = 0; k < 3 * DAY_LENGTH; k++) {
      tick(state);
      if (k % 200 === 0) expect(checkInvariants(state)).toEqual([]);
    }
  });
});
