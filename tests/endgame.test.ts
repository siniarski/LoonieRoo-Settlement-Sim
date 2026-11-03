import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { CURE_SAMPLES, EVAC_DAYS, EVAC_SIEGE, SIEGE_EVERY, SIEGE_FACTOR, daysToSiege, siegeFactor } from '../src/sim/endgame';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, dayOf } from '../src/sim/time';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}`);
}

describe('the cure', () => {
  it('a researcher brings samples to a powered lab bench and studies them; enough and you win', () => {
    const state = loadScenario({ seed: 4, map: ['s.1.3', 'nn..E'] });
    state.furniture.find((f) => f.kind === 'generator')!.stored = 4;
    state.survivors[0]!.priorities = { ...NO_JOBS, research: 1 };
    runUntil(state, () => state.endgame.cure === 2, 1500);
    expect(state.items.filter((i) => i.kind === 'sample')).toEqual([]);
    state.endgame.cure = CURE_SAMPLES - 1;
    state.items.push({ id: 'smp', kind: 'sample', pos: { x: 2, y: 1 }, carriedBy: null, reservedBy: null, wear: 0, tag: null });
    runUntil(state, () => state.endgame.won !== null, 1500);
    expect(state.endgame.won).toBe('cure');
  });

  it('no power, no research', () => {
    const state = loadScenario({ seed: 4, map: ['s.1..', 'nn...'] });
    state.survivors[0]!.priorities = { ...NO_JOBS, research: 1 };
    tick(state);
    expect(state.jobs.filter((j) => j.type === 'research')).toEqual([]);
  });
});

describe('evacuation and sieges', () => {
  it('a powered beacon and radio get through; the helicopter comes days later, after a siege night', () => {
    const state = loadScenario({ seed: 4, map: ['s.3I.E'] });
    state.furniture.find((f) => f.kind === 'generator')!.stored = 4;
    state.survivors[0]!.priorities = { ...NO_JOBS };
    tick(state);
    const evac = state.endgame.evacuation!;
    expect(dayOf(evac)).toBe(1 + EVAC_DAYS);
    expect(siegeFactor(state, dayOf(evac) - 1)).toBe(EVAC_SIEGE);
    state.tick = evac;
    state.zombies = [];
    tick(state);
    expect(state.endgame.won).toBe('evacuation');
  });

  it('every few nights is a siege', () => {
    const state = loadScenario({ seed: 4, map: ['s'] });
    expect(siegeFactor(state, SIEGE_EVERY)).toBe(SIEGE_FACTOR);
    expect(siegeFactor(state, SIEGE_EVERY + 1)).toBe(1);
    expect(daysToSiege((SIEGE_EVERY - 1) * DAY_LENGTH)).toBe(0);
  });
});
