import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { DISTRESS_LABEL, RADIO_HOUR, REQUEST_REP, fulfilRequest, updateRadio } from '../src/sim/radio';
import { loadScenario } from '../src/sim/scenario';
import { workStep } from '../src/sim/skills';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
import { hashRoll } from '../src/sim/hash';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}`);
}

describe('tools', () => {
  it('a builder picks up a hammer, and builds faster with it', () => {
    const state = loadScenario({ seed: 3, map: ['s..S'] });
    const s = state.survivors[0]!;
    s.priorities = { ...NO_JOBS, build: 2 };
    runUntil(state, () => s.tool !== null, 200);
    const sum = (type: 'build' | 'farm') => {
      let n = 0;
      for (let t = 0; t < 30; t++) {
        state.tick = 1000 + t;
        n += workStep(state, s, type);
      }
      return n;
    };
    expect(sum('build')).toBeGreaterThan(sum('farm'));
  });
});

describe('the auto-wall planner', () => {
  it('plans walls round the box with a door in each side', () => {
    const state = loadScenario({ seed: 3, map: ['.......', '.......', '.......', '.......', '.......'] });
    expect(applyCommand(state, { type: 'planPerimeter', x0: 0, y0: 0, x1: 6, y1: 4 }).ok).toBe(true);
    expect(state.blueprints.filter((b) => b.target === 'wall')).toHaveLength(20 - 4); // a 7x5 box's edge, less the doors
    expect(state.blueprints.filter((b) => b.target === 'door').map((b) => [b.x, b.y])).toEqual([
      [3, 0],
      [0, 2],
      [6, 2],
      [3, 4],
    ]);
  });
});

describe('the radio', () => {
  it('a powered radio hears a call; requests can be met for goodwill', () => {
    const state = loadScenario({ seed: 3, map: ['s.I.E', 'fffff'] });
    state.furniture.find((f) => f.kind === 'generator')!.stored = 2;
    state.settlements.push({ id: 'n1', name: 'Testville', x: 0, y: 0, reputation: 0, people: 8, food: 32, defence: 1, ties: {}, unfolded: true, lastTalk: -1, lastRaid: -1 });
    let day = 1;
    while (state.requests.length === 0 && !state.pointsOfInterest.some((p) => p.label === DISTRESS_LABEL) && day < 40) {
      state.tick = day * DAY_LENGTH + (RADIO_HOUR - 6) * TICKS_PER_HOUR;
      updateRadio(state);
      day += 1;
    }
    expect(day).toBeLessThan(40);
    state.requests = [{ id: 'r1', settlementId: 'n1', kind: 'food', count: 5, until: state.tick + DAY_LENGTH }];
    expect(fulfilRequest(state, 'r1')).toBeNull();
    expect(state.settlements[0]!.reputation).toBe(REQUEST_REP);
    expect(hashRoll(1)).toBeLessThan(1);
  });
});
