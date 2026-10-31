import { describe, expect, it } from 'vitest';
import { NO_JOBS } from '../src/sim/jobTypes';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { BANDAGE_HEAL, HEALTH_CRITICAL, HEALTH_HURT, HURT_STEP_TICKS } from '../src/sim/health';
import { checkInvariants } from '../src/sim/invariants';
import { HUNGRY } from '../src/sim/food';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import type { Survivor } from '../src/sim/survivors';
import { TICKS_PER_TILE } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';
import { TICKS_PER_HOUR } from '../src/sim/time';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = {}) {
  const state = loadScenario({ seed: 6, map });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

const hurt = (state: SimState, s: Survivor, health: number) => applyCommand(state, { type: 'setHealth', survivorId: s.id, health });

describe('injuries', () => {
  it('hurt survivors walk 25% slower', () => {
    const state = setup(['s.....', '......']);
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'moveSurvivor', survivorId: s.id, x: 5, y: 0 });
    tick(state);
    expect(s.move?.duration).toBe(TICKS_PER_TILE);
    hurt(state, s, 450);
    runUntil(state, () => s.move?.progress === 0 && s.move.from.x > 0);
    expect(s.move?.duration).toBe(HURT_STEP_TICKS);
  });

  it('hurt survivors work at 3/4 speed', () => {
    const timeToRemove = (health: number) => {
      const state = setup(['s#'], { build: 3 });
      hurt(state, state.survivors[0]!, health);
      applyCommand(state, { type: 'designateRemove', x: 1, y: 0 });
      return runUntil(state, (st) => st.grid.tiles[1] === 'floor');
    };
    const healthy = timeToRemove(1000);
    const injured = timeToRemove(450); // well below 60%, so slow healing never lifts them out of "hurt"
    expect(injured).toBeGreaterThan(healthy);
    expect(injured).toBeLessThanOrEqual(Math.ceil(healthy * 4 / 3) + 2);
  });

  it('a critically hurt survivor drops work, rests in bed, and gets up once recovered', () => {
    const state = setup(['s.b..', '..#..'], { build: 3 });
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'designateRemove', x: 2, y: 1 });
    runUntil(state, () => s.jobId !== null);
    hurt(state, s, HEALTH_CRITICAL - 1);
    tick(state);
    expect(s.jobId).toBeNull();
    expect(s.sleep?.bedId).toBe(state.furniture[0]!.id);
    runUntil(state, () => s.sleep?.asleep === true);
    runUntil(state, () => s.sleep === null, 1500);
    expect(s.health).toBeGreaterThanOrEqual(HEALTH_HURT);
    expect(s.note).toBe('woke up rested');
  });

  it('a critically hurt guard falls back to bed instead of fighting on', () => {
    const state = setup(['z..........s.b'], { guard: 3 });
    state.tick = 17 * TICKS_PER_HOUR + 1; // night
    const s = state.survivors[0]!;
    tick(state);
    expect(s.note).toMatch(/^took guard job/);
    hurt(state, s, HEALTH_CRITICAL - 1);
    tick(state);
    expect(s.jobId).toBeNull();
    expect(s.sleep).not.toBeNull();
  });

  it('a hurt survivor fetches a bandage and applies it', () => {
    const state = setup(['s....m']);
    const s = state.survivors[0]!;
    hurt(state, s, 400);
    tick(state);
    expect(s.note).toBe('going for a bandage at (5, 0)');
    runUntil(state, () => s.note === 'treated wounds with a bandage');
    expect(state.items).toEqual([]);
    expect(s.health).toBeGreaterThanOrEqual(400 + BANDAGE_HEAL);
  });

  it('healthy survivors leave bandages alone', () => {
    const state = setup(['s....m']);
    hurt(state, state.survivors[0]!, HEALTH_HURT);
    for (let i = 0; i < 50; i++) tick(state);
    expect(state.items).toHaveLength(1);
    expect(state.items[0]!.reservedBy).toBeNull();
  });

  it('two hurt survivors never go for the same bandage', () => {
    const state = setup(['s.m.s.....m']);
    for (const s of state.survivors) hurt(state, s, 400);
    tick(state);
    expect(checkInvariants(state)).toEqual([]);
    expect(new Set(state.survivors.map((s) => s.treatment?.itemId)).size).toBe(2);
  });

  it('a hurt sleeper still wakes to eat instead of starving in bed', () => {
    const state = setup(['s.b.f']);
    const s = state.survivors[0]!;
    hurt(state, s, HEALTH_CRITICAL - 1);
    runUntil(state, () => s.sleep?.asleep === true);
    applyCommand(state, { type: 'setSatiety', survivorId: s.id, satiety: HUNGRY - 1 });
    runUntil(state, () => s.note === 'ate a meal', 1500);
    expect(state.survivors).toEqual([s]);
  });

  it('a direct order interrupts treatment and frees the bandage', () => {
    const state = setup(['s....m', '......']);
    const s = state.survivors[0]!;
    hurt(state, s, 400);
    tick(state);
    applyCommand(state, { type: 'moveSurvivor', survivorId: s.id, x: 0, y: 1 });
    expect(s.treatment).toBeNull();
    expect(state.items[0]!.reservedBy).toBeNull();
  });
});
