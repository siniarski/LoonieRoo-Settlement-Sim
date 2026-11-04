import { describe, expect, it } from 'vitest';
import { barsOf, conditionsOf } from '../src/ui/card';
import { addThought } from '../src/sim/mood';
import { loadScenario } from '../src/sim/scenario';

describe('the character card', () => {
  it('shows no conditions for someone fine, and the bad ones first when not', () => {
    const state = loadScenario({ seed: 1, map: ['s..'] });
    const s = state.survivors[0]!;
    s.social = 50;
    s.comfort = 50;
    expect(conditionsOf(state, s).map((c) => c.id)).toEqual([]);
    s.wetness = 80;
    s.bleeding = true;
    addThought(state, s, 'bloodUp');
    const list = conditionsOf(state, s);
    expect(list.map((c) => c.id)).toEqual(['bleeding', 'soaked', 'bloodUp']);
    expect(list.map((c) => c.tone)).toEqual(['bad', 'mild', 'good']);
  });

  it('bars run 0..100, with water only in a harsh game', () => {
    const state = loadScenario({ seed: 1, map: ['s..'] });
    const s = state.survivors[0]!;
    expect(barsOf(state, s).map((b) => b.label)).toEqual(['Health', 'Food', 'Rest', 'Endurance', 'Warmth']);
    expect(barsOf(state, s)[0]!.pct).toBe(100);
    state.harsh = true;
    expect(barsOf(state, s).map((b) => b.label)).toContain('Water');
    for (const b of barsOf(state, s)) expect(b.pct).toBeGreaterThanOrEqual(0);
  });
});
