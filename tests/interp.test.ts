import { describe, expect, it } from 'vitest';
import { survivorWorldPos } from '../src/render/interp';
import { applyCommand } from '../src/sim/commands';
import { loadScenario } from '../src/sim/scenario';
import { TICKS_PER_TILE } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';

describe('survivor interpolation', () => {
  it('sits at the tile centre when not moving', () => {
    const state = loadScenario({ seed: 0, map: ['.s.'] });
    expect(survivorWorldPos(state.survivors[0]!, 0.7)).toEqual({ x: 1.5, y: 0.5 });
  });

  it('moves continuously across tile boundaries with no jumps', () => {
    const state = loadScenario({ seed: 0, map: ['s....'] });
    state.survivors[0]!.priorities = { build: 0, haul: 0, clean: 0 };
    applyCommand(state, { type: 'moveSurvivor', survivorId: 's1', x: 4, y: 0 });
    const samples: number[] = [];
    for (let t = 0; t < 4 * TICKS_PER_TILE + 2; t++) {
      tick(state);
      for (const alpha of [0, 0.25, 0.5, 0.75]) samples.push(survivorWorldPos(state.survivors[0]!, alpha).x);
    }
    for (let i = 1; i < samples.length; i++) {
      const delta = samples[i]! - samples[i - 1]!;
      expect(delta).toBeGreaterThanOrEqual(0);
      // One sample is a quarter tick; a quarter tick moves 1/(4*TICKS_PER_TILE) tiles.
      expect(delta).toBeLessThanOrEqual(1 / (4 * TICKS_PER_TILE) + 1e-9);
    }
    expect(samples.at(-1)).toBe(4.5);
  });
});
