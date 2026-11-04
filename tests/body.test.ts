import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { COLD, PANICKING, SWING_COST, WINDED, updateBody } from '../src/sim/body';
import { loadScenario } from '../src/sim/scenario';
import { stepPace } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';
import { TICKS_PER_HOUR } from '../src/sim/time';

describe('the body', () => {
  it('swinging spends endurance; it comes back at rest', () => {
    const state = loadScenario({ seed: 1, map: ['s..'] });
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'takeControl', survivorId: s.id });
    applyCommand(state, { type: 'swing', x: 1, y: 0 });
    expect(s.endurance).toBe(100 - SWING_COST);
    for (let t = 0; t < 100; t++) tick(state);
    expect(s.endurance).toBe(100);
  });

  it('running is a tick quicker a tile, costs endurance, and stops when winded', () => {
    const state = loadScenario({ seed: 1, map: ['s.........'] });
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'takeControl', survivorId: s.id });
    applyCommand(state, { type: 'steer', dx: 1, dy: 0, run: true });
    tick(state);
    expect(s.move!.duration).toBe(stepPace(state, s, { x: 1, y: 0 }) - 1);
    expect(s.endurance).toBeLessThan(100);
    s.endurance = WINDED - 1;
    for (let t = 0; t < 4; t++) tick(state);
    expect(s.move!.duration).toBe(stepPace(state, s, s.move!.to));
  });

  it('zombies close by bring panic, faster alone; it ebbs when they go', () => {
    const state = loadScenario({ seed: 1, map: ['s......'] });
    const s = state.survivors[0]!;
    for (let i = 0; i < 4; i++) applyCommand(state, { type: 'spawnZombie', x: 3 + i, y: 0 });
    for (const z of state.zombies) z.kind = 'static';
    state.tick = 10;
    updateBody(state, s);
    expect(s.panic).toBeGreaterThan(0);
    s.panic = PANICKING + 1;
    state.zombies = [];
    state.tick = 20;
    updateBody(state, s);
    expect(s.panic).toBe(PANICKING - 3);
  });

  it('rain soaks you outdoors; a cold night drains body heat until the cold sets in', () => {
    const state = loadScenario({ seed: 1, map: ['s..'] });
    const s = state.survivors[0]!;
    state.weather = 'rain';
    for (let t = 0; t < 2 * TICKS_PER_HOUR; t++) {
      state.tick = t;
      updateBody(state, s);
    }
    expect(s.wetness).toBeGreaterThan(50);
    expect(s.thoughts.some((t) => t.kind === 'soaked')).toBe(true);
    state.weather = 'blizzard';
    for (let t = 0; t < 80; t++) {
      state.tick += 1;
      updateBody(state, s);
    }
    expect(s.bodyHeat).toBeLessThan(COLD);
    expect(s.thoughts.some((t) => t.kind === 'cold')).toBe(true);
  });
});
