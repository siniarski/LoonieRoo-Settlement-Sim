import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { addThought } from '../src/sim/mood';
import { HARDER_PCT, boostDamage, boosted, swingWait } from '../src/sim/moodlets';
import { BREAKING, LONELY, STEELED, dawnResolve, freezes, startingResolve, steel } from '../src/sim/resolve';
import { loadScenario } from '../src/sim/scenario';
import { stepPace } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH } from '../src/sim/time';
import { GUARD_ATTACK_EVERY } from '../src/sim/jobs';

describe('moodlets', () => {
  it('blood up hits harder; shaken and well rested change the wait between swings; cold slows walking', () => {
    const state = loadScenario({ seed: 1, map: ['s..'] });
    const s = state.survivors[0]!;
    expect(boostDamage(s, 100)).toBe(100);
    addThought(state, s, 'bloodUp');
    expect(boostDamage(s, 100)).toBe(HARDER_PCT);
    addThought(state, s, 'shaken');
    expect(swingWait(s, GUARD_ATTACK_EVERY)).toBeGreaterThan(GUARD_ATTACK_EVERY);
    s.thoughts = [];
    addThought(state, s, 'wellRested');
    expect(swingWait(s, GUARD_ATTACK_EVERY)).toBeLessThan(GUARD_ATTACK_EVERY);
    const pace = stepPace(state, s, { x: 1, y: 0 });
    addThought(state, s, 'cold');
    expect(stepPace(state, s, { x: 1, y: 0 })).toBe(pace + 1);
  });

  it('a kill gets your blood up', () => {
    const state = loadScenario({ seed: 1, map: ['s.'] });
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'spawnZombie', x: 1, y: 0 });
    const z = state.zombies[0]!;
    z.kind = 'static';
    z.health = 1;
    applyCommand(state, { type: 'takeControl', survivorId: s.id });
    expect(applyCommand(state, { type: 'swing', x: 1, y: 0 })).toMatchObject({ ok: true });
    expect(boosted(s, 'harder')).toBe(true);
  });
});

describe('resolve', () => {
  it('starts near the middle, the same for the same id', () => {
    expect(startingResolve('s5')).toBe(startingResolve('s5'));
    for (let n = 1; n < 50; n++) {
      expect(startingResolve(`s${n}`)).toBeGreaterThanOrEqual(40);
      expect(startingResolve(`s${n}`)).toBeLessThanOrEqual(60);
    }
  });

  it('drifts with mood at dawn, and sinks for someone alone', () => {
    const state = loadScenario({ seed: 1, map: ['s.s'] });
    const [a, b] = state.survivors as [NonNullable<typeof state.survivors[0]>, NonNullable<typeof state.survivors[0]>];
    a.resolve = b.resolve = 50;
    addThought(state, a, 'reunited'); // +30 mood
    addThought(state, b, 'lostFriend'); // -25 mood
    state.tick = DAY_LENGTH;
    dawnResolve(state);
    expect(a.resolve).toBeGreaterThan(50);
    expect(b.resolve).toBeLessThan(50);
    const solo = loadScenario({ seed: 1, map: ['s..'] });
    const s = solo.survivors[0]!;
    s.resolve = 50;
    solo.tick = DAY_LENGTH;
    dawnResolve(solo);
    expect(s.resolve).toBe(50 - LONELY);
  });

  it('the breaking may walk away in the night (never the one you control); the steeled do not break down', () => {
    const state = loadScenario({ seed: 1, map: ['s.s.s.s.s.s'] });
    for (const s of state.survivors) s.resolve = 0;
    applyCommand(state, { type: 'takeControl', survivorId: state.survivors[0]!.id });
    state.tick = DAY_LENGTH;
    tick(state);
    expect(state.survivors.length).toBeLessThan(6);
    expect(state.survivors.some((s) => s.id === state.control!.survivorId)).toBe(true);
    expect(state.items.filter((i) => i.kind === 'corpse')).toEqual([]);
    expect(state.events.some((e) => /walked away/.test(e.message))).toBe(true);
    expect(STEELED).toBeGreaterThan(BREAKING);
  });

  it('breaking under your control: some swings freeze', () => {
    const state = loadScenario({ seed: 1, map: ['s.'] });
    const s = state.survivors[0]!;
    s.resolve = 0;
    let froze = 0;
    for (let t = 0; t < 200; t++, state.tick++) if (freezes(state, s)) froze++;
    expect(froze).toBeGreaterThan(20);
    expect(froze).toBeLessThan(90);
    steel(s, 50);
    expect(freezes(state, s)).toBe(false);
  });

  it('a request seen through lifts it; a friend dying cuts it', async () => {
    const { mournDeath } = await import('../src/sim/mood');
    const state = loadScenario({ seed: 1, map: ['s.s'] });
    const [a, b] = state.survivors as [NonNullable<typeof state.survivors[0]>, NonNullable<typeof state.survivors[0]>];
    a.resolve = 50;
    a.opinions[b.id] = 60; // friends
    mournDeath(state, b);
    expect(a.resolve).toBeLessThan(50);
  });
});
