import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { addThought } from '../src/sim/mood';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
import { BROKEN_RESOLVE, PROMISE_RESOLVE, WISH_RESOLVE, WISH_TICKS, updateWishes, type Wish } from '../src/sim/wishes';

const wish = (state: SimState, kind: Wish['kind'], targetId: string | null = null): Wish => ({ kind, targetId, since: state.tick, until: state.tick + WISH_TICKS, promised: false });
const at = (state: SimState, tick: number) => {
  state.tick = tick;
  updateWishes(state);
};

describe('wishes', () => {
  it('turn up on the hour now and then, and only ones that fit', () => {
    const state = loadScenario({ seed: 1, map: ['s.s.s.s.s.s'] });
    for (let h = 1; h <= 24; h++) at(state, h * TICKS_PER_HOUR);
    const got = state.survivors.filter((s) => s.wish);
    expect(got.length).toBeGreaterThan(0);
    // No stove, spring, no graveyard: none of those.
    for (const s of got) expect(['hotMeal', 'coat', 'grave']).not.toContain(s.wish!.kind);
  });

  it('met: a small lift', () => {
    const state = loadScenario({ seed: 1, map: ['s..'] });
    const s = state.survivors[0]!;
    s.resolve = 50;
    s.wish = wish(state, 'bed');
    at(state, 10);
    expect(s.wish).not.toBeNull();
    addThought(state, s, 'sleptInBed');
    at(state, 20);
    expect(s.wish).toBeNull();
    expect(s.resolve).toBe(50 + WISH_RESOLVE);
    expect(s.thoughts.some((t) => t.kind === 'wishMet')).toBe(true);
  });

  it('a thought from before the wish began does not count', () => {
    const state = loadScenario({ seed: 1, map: ['s..'] });
    const s = state.survivors[0]!;
    addThought(state, s, 'sleptInBed');
    state.tick = 100;
    s.wish = wish(state, 'bed');
    at(state, 110);
    expect(s.wish).not.toBeNull();
  });

  it('promised and met: more, and they think better of you', () => {
    const state = loadScenario({ seed: 1, map: ['s.s'] });
    const [you, s] = state.survivors as [NonNullable<SimState['survivors'][0]>, NonNullable<SimState['survivors'][0]>];
    applyCommand(state, { type: 'takeControl', survivorId: you.id });
    s.resolve = 50;
    s.wish = wish(state, 'kill');
    expect(applyCommand(state, { type: 'promiseWish', survivorId: s.id })).toMatchObject({ ok: true });
    expect(applyCommand(state, { type: 'promiseWish', survivorId: s.id }).ok).toBe(false);
    addThought(state, s, 'bloodUp');
    at(state, 10);
    expect(s.resolve).toBe(50 + PROMISE_RESOLVE);
    expect(s.opinions[you.id]).toBeGreaterThan(0);
  });

  it('promised and let run out: let down; unpromised and run out: nothing', () => {
    const state = loadScenario({ seed: 1, map: ['s.s'] });
    const [you, s] = state.survivors as [NonNullable<SimState['survivors'][0]>, NonNullable<SimState['survivors'][0]>];
    applyCommand(state, { type: 'takeControl', survivorId: you.id });
    s.resolve = 50;
    s.wish = wish(state, 'weapon');
    at(state, DAY_LENGTH + 10);
    expect(s.wish).toBeNull();
    expect(s.resolve).toBe(50);
    s.wish = wish(state, 'weapon');
    applyCommand(state, { type: 'promiseWish', survivorId: s.id });
    at(state, state.tick + WISH_TICKS + 10);
    expect(s.resolve).toBe(50 - BROKEN_RESOLVE);
    expect(s.opinions[you.id]).toBeLessThan(0);
    expect(state.events.some((e) => /promise broken/.test(e.message))).toBe(true);
  });

  it('a quiet day is met by the day passing quietly', () => {
    const state = loadScenario({ seed: 1, map: ['s..'] });
    const s = state.survivors[0]!;
    s.wish = wish(state, 'quiet');
    at(state, WISH_TICKS / 2);
    expect(s.wish).not.toBeNull();
    at(state, WISH_TICKS + 10);
    expect(s.wish).toBeNull();
    expect(s.thoughts.some((t) => t.kind === 'wishMet')).toBe(true);
  });

  it('nobody under your control wishes', () => {
    const state = loadScenario({ seed: 1, map: ['s..'] });
    const s = state.survivors[0]!;
    s.wish = wish(state, 'bed');
    applyCommand(state, { type: 'takeControl', survivorId: s.id });
    at(state, 10);
    expect(s.wish).toBeNull();
  });
});
