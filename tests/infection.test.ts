import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { INFECTION_CHANCE, INFECTION_TURN, maybeInfect } from '../src/sim/infection';
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

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = {}, rooms: { x: number; y: number; purpose: 'workshop' | 'infirmary' }[] = []) {
  const state = loadScenario({ seed: 3, map, rooms });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

describe('infection', () => {
  it(`a bite infects about ${INFECTION_CHANCE * 100}% of the time, decided by a hash (the same bite, the same answer)`, () => {
    const state = setup(['s']);
    const s = state.survivors[0]!;
    let infected = 0;
    for (let t = 0; t < 1000; t++) {
      s.infection = null;
      state.tick = t;
      maybeInfect(state, s, 'z7');
      if (s.infection !== null) infected += 1;
    }
    expect(infected).toBeGreaterThan(100);
    expect(infected).toBeLessThan(200);
  });

  it('untreated, it kills in a day and the survivor gets up as a zombie', () => {
    const state = setup(['s....', '.....']);
    const s = state.survivors[0]!;
    s.infection = INFECTION_TURN - 3;
    for (let i = 0; i < 5; i++) tick(state);
    expect(state.survivors).toEqual([]);
    expect(state.events.map((e) => e.message)).toContain(`${s.name} died of the infection`);
    expect(state.zombies).toMatchObject([{ pos: s.pos, kind: 'static' }]);
  });

  it('the infected fetch medicine and are cured', () => {
    const state = setup(['s...!']);
    const s = state.survivors[0]!;
    s.infection = 100;
    runUntil(state, () => s.note === 'took medicine for the infection');
    expect(s.infection).toBeNull();
    expect(state.items).toEqual([]);
  });

  it('resting in an infirmary bed slows it by half', () => {
    const spread = (rooms: { x: number; y: number; purpose: 'infirmary' }[]) => {
      const state = setup(['####', '#b.#', '#..#', '##D#', '.s..'], {}, rooms);
      const s = state.survivors[0]!;
      applyCommand(state, { type: 'setRest', survivorId: s.id, rest: 0 });
      s.infection = 0;
      runUntil(state, () => s.sleep?.asleep === true);
      const at = s.infection!;
      for (let i = 0; i < 100; i++) tick(state);
      return s.infection! - at;
    };
    expect(spread([])).toBe(100);
    expect(spread([{ x: 1, y: 2, purpose: 'infirmary' }])).toBe(50);
  });

  it('a doctor brings medicine to an infected patient in bed', () => {
    const state = setup(['s.b...', '......', 's.....']);
    const [ash, bea] = state.survivors as [NonNullable<SimState['survivors'][0]>, NonNullable<SimState['survivors'][0]>];
    bea.priorities = { ...NO_JOBS, doctor: 3 };
    applyCommand(state, { type: 'setRest', survivorId: bea.id, rest: 2000 });
    applyCommand(state, { type: 'setRest', survivorId: ash.id, rest: 0 });
    runUntil(state, () => ash.sleep?.asleep === true);
    ash.infection = 50;
    state.items.push({ id: 'i900', kind: 'medicine', pos: { x: 5, y: 2 }, carriedBy: null, reservedBy: null, wear: 0, tag: null });
    runUntil(state, () => bea.note === `cured ${ash.name}'s infection`);
    expect(ash.infection).toBeNull();
  });
});

describe('workshop and medicine', () => {
  const MAP = ['#####', '#.k.#', '#...#', '##D##', 's.www', '..ww.'];
  it('only a workbench in a complete workshop makes medicine', () => {
    const plain = setup(MAP, { craft: 3 });
    applyCommand(plain, { type: 'setCraftTarget', kind: 'medicine', count: 1 });
    for (let i = 0; i < 200; i++) tick(plain);
    expect(plain.jobs.filter((j) => j.type === 'craft')).toEqual([]);

    const shop = setup(MAP, { craft: 3 }, [{ x: 1, y: 1, purpose: 'workshop' }]);
    applyCommand(shop, { type: 'setCraftTarget', kind: 'medicine', count: 1 });
    runUntil(shop, () => shop.items.some((i) => i.kind === 'medicine'), 1500);
  });

  it('a workshop bench works faster', () => {
    const time = (rooms: { x: number; y: number; purpose: 'workshop' }[]) => {
      const state = setup(MAP, { craft: 3 }, rooms);
      applyCommand(state, { type: 'setCraftTarget', kind: 'bandage', count: 1 });
      const bench = state.furniture[0]!;
      runUntil(state, () => bench.stored === 1, 1500);
      return runUntil(state, () => state.items.some((i) => i.kind === 'bandage'), 1500);
    };
    expect(time([{ x: 1, y: 1, purpose: 'workshop' }])).toBeLessThan(time([]));
  });
});
