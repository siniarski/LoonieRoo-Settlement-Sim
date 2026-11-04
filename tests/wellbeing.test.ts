import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { mood } from '../src/sim/mood';
import { loadScenario } from '../src/sim/scenario';
import { tick } from '../src/sim/tick';
import { HIGH, LOW, SOCIAL_EVERY, nightScore, updateSocial, wakeComfort } from '../src/sim/wellbeing';
import { talk } from '../src/sim/talk';

describe('social', () => {
  it('rises in company and sinks alone; lonely people feel it', () => {
    const state = loadScenario({ seed: 1, map: ['s.s.........s'] });
    const [a, b, c] = state.survivors as [NonNullable<typeof state.survivors[0]>, NonNullable<typeof state.survivors[0]>, NonNullable<typeof state.survivors[0]>];
    for (let i = 0; i < 20; i++) {
      state.tick = (i + 1) * SOCIAL_EVERY;
      updateSocial(state);
    }
    expect(a.social).toBe(80);
    expect(b.social).toBe(80);
    expect(c.social).toBe(40);
    c.social = LOW - 1;
    const before = mood(state, c);
    c.social = LOW + 1;
    expect(mood(state, c)).toBeGreaterThan(before);
  });

  it('talking is company', () => {
    const state = loadScenario({ seed: 1, map: ['s.s'] });
    const [a, b] = state.survivors as [NonNullable<typeof state.survivors[0]>, NonNullable<typeof state.survivors[0]>];
    applyCommand(state, { type: 'takeControl', survivorId: a.id });
    expect(talk(state, b)).toBeNull();
    expect(a.social).toBe(80);
    expect(b.social).toBe(80);
  });
});

describe('comfort', () => {
  it('a bed in a finished bedroom beats the floor outdoors, and moves halfway each night', () => {
    const state = loadScenario({ seed: 1, map: ['s.......', '.#####..', '.#b..#..', '.#...#..', '.##D##..', '........'] });
    applyCommand(state, { type: 'designateRoom', x: 3, y: 3, purpose: 'housing' });
    const s = state.survivors[0]!;
    const bed = state.furniture.find((f) => f.kind === 'bed')!;
    const floor = nightScore(state, s, null);
    s.pos = { x: bed.x, y: bed.y };
    const good = nightScore(state, s, bed.id);
    expect(good).toBeGreaterThan(HIGH);
    expect(floor).toBeLessThan(LOW + 20);
    s.comfort = 50;
    wakeComfort(state, s, bed.id);
    expect(s.comfort).toBe(Math.round((50 + good) / 2));
  });

  it('sleeping in a bed sets it on waking', () => {
    const state = loadScenario({ seed: 1, map: ['s.b'] });
    const s = state.survivors[0]!;
    s.comfort = 0;
    applyCommand(state, { type: 'setRest', survivorId: s.id, rest: 400 });
    for (let t = 0; t < 3000 && s.comfort === 0; t++) tick(state);
    expect(s.comfort).toBeGreaterThan(0);
  });
});
