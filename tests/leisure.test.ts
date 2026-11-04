import { describe, expect, it } from 'vitest';
import { STEELED } from '../src/sim/resolve';
import { LONELY_MOOD, LOW } from '../src/sim/wellbeing';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { HUNGRY } from '../src/sim/food';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { BREAKDOWN_CHECK_EVERY, BREAKDOWN_TICKS, RELAX_TICKS } from '../src/sim/leisure';
import { addThought, mood, MOOD_BASE } from '../src/sim/mood';
import type { RoomPurpose } from '../src/sim/rooms';
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

function setup(map: string[], rooms: { x: number; y: number; purpose: RoomPurpose }[] = [], jobs: Partial<typeof NO_JOBS> = {}, seed = 2) {
  const state = loadScenario({ seed, map, rooms });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

const thoughtsOf = (s: Survivor) => s.thoughts.map((t) => t.kind);
/** Pushes mood under the miserable line. */
const miserable = (state: SimState, s: Survivor) => {
  addThought(state, s, 'sawDeath');
  addThought(state, s, 'sleptOnFloor');
};

// A rec room (3x2) with its door at the bottom; the survivor outside.
const REC = ['#####', '#...#', '#...#', '##D##', '.s...'];
const RECROOM = { x: 1, y: 1, purpose: 'recroom' as const };

describe('mood: new thoughts', () => {
  it('hunger weighs on mood while it lasts', () => {
    const state = setup(['s..']);
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'setSatiety', survivorId: s.id, satiety: HUNGRY - 1 });
    expect(mood(state, s)).toBe(MOOD_BASE - 10);
  });

  it('a zombie attack leaves its mark', () => {
    const state = setup(['zs..']);
    state.tick = 17 * 200 + 1; // night
    runUntil(state, () => state.survivors[0]!.health < 1000);
    expect(thoughtsOf(state.survivors[0]!)).toContain('attacked');
  });
});

describe('rec room', () => {
  it('an idle, so-so survivor goes to relax there and comes back happier', () => {
    const state = setup(REC, [RECROOM]);
    const s = state.survivors[0]!;
    addThought(state, s, 'ateWithoutTable'); // 45: below the relax line
    runUntil(state, () => s.leisure !== null);
    expect(s.note).toMatch(/^going to relax at/);
    runUntil(state, () => s.note === 'relaxed in the rec room', RELAX_TICKS + 200);
    expect(thoughtsOf(s)).toContain('relaxed');
    // Relaxed today: no second visit.
    for (let i = 0; i < 100; i++) tick(state);
    expect(s.leisure).toBeNull();
  });

  it('a content survivor (mood 70+) has no need of it', () => {
    const state = setup(REC, [RECROOM]);
    addThought(state, state.survivors[0]!, 'ateAtTable');
    addThought(state, state.survivors[0]!, 'relaxed'); // 70
    for (let i = 0; i < 100; i++) tick(state);
    expect(state.survivors[0]!.leisure).toBeNull();
  });

  it('a job turning up ends the visit: work comes first', () => {
    const state = setup(['#####', '#...#', '#...#', '##D##', '.s..#'], [RECROOM], { build: 3 });
    const s = state.survivors[0]!;
    addThought(state, s, 'ateWithoutTable');
    runUntil(state, () => s.leisure?.progress === 5);
    applyCommand(state, { type: 'designateRemove', x: 4, y: 4 });
    const t = runUntil(state, () => s.jobId !== null);
    expect(t).toBeLessThanOrEqual(10);
    expect(s.leisure).toBeNull();
  });

  it('two survivors never share a spot', () => {
    const state = setup(['####', '#..#', '#..#', '##D#', 's..s'], [{ x: 1, y: 1, purpose: 'recroom' }]);
    for (const s of state.survivors) addThought(state, s, 'ateWithoutTable');
    runUntil(state, () => state.survivors.every((s) => s.leisure?.progress));
    const spots = state.survivors.map((s) => `${s.leisure!.x},${s.leisure!.y}`);
    expect(new Set(spots).size).toBe(2);
  });
});

describe('breakdowns', () => {
  /** Ticks until a miserable survivor breaks down (sim RNG; checked every BREAKDOWN_CHECK_EVERY). */
  const breakdownState = (seed: number) => {
    const state = setup(['s...', '#...'], [], { build: 3 }, seed);
    miserable(state, state.survivors[0]!);
    return state;
  };

  it('a miserable survivor eventually breaks down: drops work, then pulls together', () => {
    // A dry run finds when it happens (sim RNG), so the real run can be mid-job at that moment.
    const dry = breakdownState(2);
    // Not about to walk away (resolve.ts): this is about breakdowns.
    dry.survivors[0]!.social = 60;
    dry.survivors[0]!.resolve = STEELED - 1;
    runUntil(dry, () => dry.survivors[0]!.breakdown !== null, 20 * BREAKDOWN_CHECK_EVERY);
    const state = breakdownState(2);
    const s = state.survivors[0]!;
    s.social = 60;
    s.resolve = STEELED - 1;
    runUntil(state, () => state.tick === dry.tick - 15, 20 * BREAKDOWN_CHECK_EVERY);
    applyCommand(state, { type: 'designateRemove', x: 0, y: 1 });
    runUntil(state, () => s.breakdown !== null, 20);
    expect(s.jobId).toBeNull();
    expect(state.events.at(-1)!.message).toBe(`${s.name} is having a breakdown`);
    expect(state.jobs[0]!.reservedBy).toBeNull();
    // No work while it lasts...
    for (let i = 0; i < BREAKDOWN_TICKS - 10; i++) tick(state);
    expect(s.jobId).toBeNull();
    // ...then it passes, with a lift that keeps it from coming straight back.
    runUntil(state, () => s.breakdown === null, 20);
    expect(thoughtsOf(s)).toContain('vented');
    // (Alone all this while, they are lonely too by now: wellbeing.ts.)
    expect(mood(state, s)).toBeGreaterThanOrEqual(30 + (s.social < LOW ? LONELY_MOOD : 0));
    runUntil(state, () => s.jobId !== null, 3000); // back to work (after a night's sleep, if it's late)
  });

  it('only the miserable break down', () => {
    const state = setup(['s...'], [], {}, 2);
    for (let i = 0; i < 40 * BREAKDOWN_CHECK_EVERY && i < 3000; i++) tick(state);
    expect(state.survivors[0]!.breakdown).toBeNull();
    expect(state.events.some((e) => e.message.includes('breakdown'))).toBe(false);
  });

  it('breakdowns are rolled with the sim RNG: same seed, same moment', () => {
    const when = (seed: number) => {
      const state = breakdownState(seed);
      return runUntil(state, () => state.survivors[0]!.breakdown !== null, 40 * BREAKDOWN_CHECK_EVERY);
    };
    expect(when(4)).toBe(when(4));
  });
});
