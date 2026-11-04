import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { CLUTCHED_MIN, CLUTCH_PCT } from '../src/sim/control';
import { SUB, centre } from '../src/sim/motion';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { stepPace } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';
import { STAGGER_TICKS, WINDUP_TICKS } from '../src/sim/zombies';

/** You, controlled, at (1, 0); a static zombie at (zx, zy). */
function fight(map: string[]): SimState {
  const state = loadScenario({ seed: 1, map: map.map((row) => row.replace('z', '.')) });
  const y = map.findIndex((row) => row.includes('z'));
  applyCommand(state, { type: 'spawnZombie', x: map[y]!.indexOf('z'), y });
  state.zombies[0]!.kind = 'static';
  applyCommand(state, { type: 'takeControl', survivorId: state.survivors[0]!.id });
  return state;
}

describe('fights you can read', () => {
  it('a zombie winds up before it bites, and the bite lands WINDUP_TICKS later', () => {
    const state = fight(['.sz..']);
    const s = state.survivors[0]!;
    tick(state);
    expect(state.zombies[0]!.windup).toBe(WINDUP_TICKS);
    for (let t = 1; t < WINDUP_TICKS; t++) tick(state);
    expect(s.health).toBe(1000);
    tick(state);
    expect(s.health).toBeLessThan(1000);
  });

  it('step out of reach mid-windup and the bite closes on air', () => {
    const state = fight(['..sz....']);
    const s = state.survivors[0]!;
    tick(state);
    tick(state);
    s.pos = { x: 0, y: 0 };
    s.at = centre(s.pos);
    tick(state);
    expect(state.zombies[0]!.windup).toBe(0);
    expect(s.health).toBe(1000);
  });

  it('your blow staggers it and shoves it back: no bite while it reels', () => {
    const state = fight(['.sz...']);
    const s = state.survivors[0]!;
    const z = state.zombies[0]!;
    z.health = 1000;
    z.maxHealth = 1000;
    const x = z.at.x;
    const before = z.health;
    expect(applyCommand(state, { type: 'swing', x: 2, y: 0 }).ok).toBe(true);
    expect(z.health).toBeLessThan(before);
    expect(z.at.x).toBeGreaterThan(x);
    expect(z.staggerUntil).toBe(state.tick + STAGGER_TICKS);
    for (let t = 0; t < STAGGER_TICKS; t++) tick(state);
    expect(s.health).toBe(1000);
    expect(state.control!.swungTick).toBe(0);
  });

  it('the dead within reach drag at you', () => {
    const free = fight(['z.....', '.s....']);
    free.zombies = [];
    const held = fight(['.z....', '.s....']);
    const moved = (state: SimState) => {
      const s = state.survivors[0]!;
      const x = s.at.x;
      applyCommand(state, { type: 'steer', dx: 1, dy: 0 });
      tick(state);
      return s.at.x - x;
    };
    const walk = Math.round(SUB / stepPace(free, free.survivors[0]!, free.survivors[0]!.pos));
    expect(moved(free)).toBe(walk);
    expect(moved(held)).toBe(Math.round((walk * Math.max(CLUTCHED_MIN, 100 - CLUTCH_PCT)) / 100));
  });
});
