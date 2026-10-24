import { describe, expect, it } from 'vitest';
import { NO_JOBS } from '../src/sim/jobTypes';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { HEALTH_MAX, STARVE_DAMAGE } from '../src/sim/health';
import { checkInvariants } from '../src/sim/invariants';
import { NIGHT_TIRED, TIRED } from '../src/sim/rest';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR, isNight } from '../src/sim/time';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 3000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}`);
}

function setup(map: string[]) {
  const state = loadScenario({ seed: 1, map });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS };
  return state;
}

describe('health and death', () => {
  it('starving costs health every tick', () => {
    const state = setup(['s...']);
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'setSatiety', survivorId: s.id, satiety: 0 });
    for (let i = 0; i < 10; i++) tick(state);
    expect(s.health).toBe(HEALTH_MAX - 10 * STARVE_DAMAGE);
  });

  it('a starving survivor dies, is removed cleanly, and the death is logged', () => {
    const state = loadScenario({ seed: 1, map: ['s.w.....b', '.........'] });
    const s = state.survivors[0]!;
    s.priorities = { ...NO_JOBS, haul: 3 };
    applyCommand(state, { type: 'designateBuild', x: 6, y: 1, structure: 'wall' });
    runUntil(state, () => s.carrying !== null);
    applyCommand(state, { type: 'setSatiety', survivorId: s.id, satiety: 0 });
    applyCommand(state, { type: 'setHealth', survivorId: s.id, health: 5 });
    runUntil(state, (st) => st.survivors.length === 0, 20);
    expect(state.events.map((e) => e.message)).toEqual(['Ash died of starvation']);
    // Nothing is left pointing at the dead.
    expect(state.items.every((i) => i.carriedBy === null && i.reservedBy === null && i.pos !== null)).toBe(true);
    expect(state.jobs.every((j) => j.reservedBy === null)).toBe(true);
    expect(checkInvariants(state)).toEqual([]);
  });

  it('a death frees a bed and lets the next survivor in the list still update that tick', () => {
    const state = setup(['s.b...s']);
    const [a, b] = state.survivors;
    applyCommand(state, { type: 'setRest', survivorId: a!.id, rest: TIRED - 1 });
    runUntil(state, () => a!.sleep?.asleep === true);
    applyCommand(state, { type: 'setHealth', survivorId: a!.id, health: 1 });
    applyCommand(state, { type: 'setSatiety', survivorId: a!.id, satiety: 0 });
    const restBefore = b!.rest;
    tick(state);
    expect(state.survivors).toEqual([b]);
    expect(b!.rest).toBe(restBefore - 1); // b's update still ran
    expect(state.furniture[0]!.occupant).toBeNull();
    expect(state.events.at(-1)?.message).toBe('Ash died of starvation');
  });

  it('health 0 from anything else counts as injuries', () => {
    const state = setup(['s']);
    applyCommand(state, { type: 'setHealth', survivorId: 's1', health: 0 });
    tick(state);
    expect(state.survivors).toEqual([]);
    expect(state.events.at(-1)?.message).toBe('Ash died of injuries');
  });

  it('heals while asleep, and slowly while awake and fed', () => {
    const state = setup(['s.b']);
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'setHealth', survivorId: s.id, health: 500 });
    for (let i = 0; i < 50; i++) tick(state);
    expect(s.health).toBe(510); // awake and fed: 1 per 5 ticks
    applyCommand(state, { type: 'setRest', survivorId: s.id, rest: TIRED - 1 });
    runUntil(state, () => s.sleep?.asleep === true);
    const asleepAt = s.health;
    for (let i = 0; i < 20; i++) tick(state);
    expect(s.health).toBe(asleepAt + 20);
  });
});

describe('sleep follows the clock', () => {
  it('at night survivors go to bed below 75% rest instead of 25%', () => {
    expect(NIGHT_TIRED).toBeGreaterThan(TIRED);
    const state = setup(['s.b']);
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'setRest', survivorId: s.id, rest: 1200 });
    tick(state);
    expect(s.sleep).toBeNull(); // daytime: 60% isn't tired
    // Jump the clock to night (rest is reset so only the threshold changes).
    state.tick = DAY_LENGTH - 2 * TICKS_PER_HOUR; // 04:00 on day 2
    expect(isNight(state.tick)).toBe(true);
    applyCommand(state, { type: 'setRest', survivorId: s.id, rest: 1200 });
    tick(state);
    expect(s.sleep).not.toBeNull();
  });
});
