import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { COOK_TICKS } from '../src/sim/control';
import { addFurniture } from '../src/sim/furniture';
import { spawnItem, getItem } from '../src/sim/items';
import { stow } from '../src/sim/inventory';
import { REST_MAX } from '../src/sim/rest';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';

/** You (in control) at (1, 1) with a fitting of this look at (2, 1); the mains on or off. */
function withFitting(look: 'oven' | 'bed' | 'sofa', mains: boolean): SimState {
  const state = loadScenario({ seed: 1, map: ['.....', '.s...', '.....'] });
  state.timeline = { waterOffDay: mains ? 99 : 0, powerOffDay: mains ? 99 : 0, helicopterDay: 0, heli: null, alarms: [] };
  addFurniture(state, 'decor', 2, 1).look = look;
  const s = state.survivors[0]!;
  for (const k of Object.keys(s.priorities)) s.priorities[k as keyof typeof s.priorities] = 0;
  applyCommand(state, { type: 'takeControl', survivorId: s.id });
  return state;
}

describe("a house's fittings", () => {
  it('an oven with the power on cooks food from your bag into a hot meal', () => {
    const state = withFitting('oven', true);
    const s = state.survivors[0]!;
    const food = spawnItem(state, 'food', 1, 1);
    stow(state, s, food);
    applyCommand(state, { type: 'use', x: 2, y: 1 });
    expect(state.control!.busy?.what).toBe('cook');
    for (let k = 0; k <= COOK_TICKS; k++) tick(state);
    expect(getItem(state, food.id)?.kind).toBe('meal');
    expect(s.pack).toContain(food.id);
  });

  it('without the power the oven is dead', () => {
    const state = withFitting('oven', false);
    const s = state.survivors[0]!;
    stow(state, s, spawnItem(state, 'food', 1, 1));
    applyCommand(state, { type: 'use', x: 2, y: 1 });
    expect(state.control!.busy).toBeNull();
  });

  it("you can sleep in a house's bed, and on a sofa (more slowly)", () => {
    const rested = (look: 'bed' | 'sofa') => {
      const state = withFitting(look, false);
      const s = state.survivors[0]!;
      s.rest = 100;
      applyCommand(state, { type: 'use', x: 2, y: 1 }); // climb on
      applyCommand(state, { type: 'use', x: 2, y: 1 }); // lie down
      expect(state.control!.busy?.what, look).toBe('sleep');
      for (let k = 0; k < 200; k++) tick(state);
      return Math.min(REST_MAX, s.rest);
    };
    const bed = rested('bed');
    const sofa = rested('sofa');
    expect(sofa).toBeGreaterThan(100);
    expect(bed).toBeGreaterThan(sofa);
  });
});
