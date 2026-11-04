import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { GARMENT_LIFE, TRAP_STEP_DAMAGE, wearing } from '../src/sim/clothing';
import { addFurniture } from '../src/sim/furniture';
import { HEALTH_MAX } from '../src/sim/health';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { killSurvivor } from '../src/sim/needs';
import { loadScenario } from '../src/sim/scenario';
import { SEASON_DAYS } from '../src/sim/seasons';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
import { ZOMBIE_DAMAGE, addZombie } from '../src/sim/zombies';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = {}) {
  const state = loadScenario({ seed: 7, map });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

/** Just after nightfall (21:00) on the first night of winter, past the raid roll. */
const WINTER_NIGHT = 3 * SEASON_DAYS * DAY_LENGTH + 15 * TICKS_PER_HOUR + 1;

describe('clothing', () => {
  it('a survivor puts on a free coat and boots between jobs, and drops them on death', () => {
    const state = setup(['s..C..U']);
    const s = state.survivors[0]!;
    runUntil(state, () => s.body !== null && s.feet !== null);
    expect(wearing(state, s, 'coat')).toBeDefined();
    expect(wearing(state, s, 'boots')).toBeDefined();
    expect(state.items.every((i) => i.pos === null)).toBe(true);
    killSurvivor(state, s, 'testing');
    expect(state.items.filter((i) => i.pos !== null)).toHaveLength(2);
  });

  it('guards would rather have armour than a coat', () => {
    const state = setup(['C.s...A'], { guard: 3 });
    const s = state.survivors[0]!;
    runUntil(state, () => s.body !== null);
    expect(wearing(state, s, 'armour')).toBeDefined();
  });

  it('a coat keeps a winter night off (no cold damage), and wears with each hour of it', () => {
    const cold = setup(['s......']);
    const coated = setup(['sC.....']);
    for (const st of [cold, coated]) runUntil(st, () => st.tick > 20);
    expect(coated.survivors[0]!.body).not.toBeNull();
    for (const st of [cold, coated]) {
      st.tick = WINTER_NIGHT;
      st.zombiesTonight = 0;
      st.survivors[0]!.rest = 99999; // stay up
      for (let i = 0; i < 2 * TICKS_PER_HOUR; i++) tick(st);
    }
    expect(cold.survivors[0]!.health).toBeLessThan(HEALTH_MAX);
    expect(cold.survivors[0]!.thoughts.some((t) => t.kind === 'cold')).toBe(true);
    expect(coated.survivors[0]!.health).toBe(HEALTH_MAX);
    expect(coated.survivors[0]!.thoughts.some((t) => t.kind === 'cold')).toBe(false);
    expect(coated.items[0]!.wear).toBe(2);
  });

  it('armour takes some of a zombie blow, wears, and falls apart at its life', () => {
    const state = setup(['sA.', '...']);
    const s = state.survivors[0]!;
    runUntil(state, () => s.body !== null);
    const armour = state.items[0]!;
    armour.wear = GARMENT_LIFE.armour - 1;
    s.health = HEALTH_MAX;
    const z = addZombie(state, s.pos.x, s.pos.y + 1, 200, 'static');
    z.windup = 1; // the bite lands this tick
    tick(state);
    expect(HEALTH_MAX - s.health).toBe(Math.round((ZOMBIE_DAMAGE * 70) / 100));
    expect(s.body).toBeNull();
    expect(state.items).toHaveLength(0);
  });

  it('a spike trap hurts the barefoot but not someone in boots', () => {
    const barefoot = setup(['s.....']);
    const booted = setup(['sU....']);
    for (const st of [barefoot, booted]) {
      runUntil(st, () => st.tick > 30);
      addFurniture(st, 'trap', 3, 0);
      applyCommand(st, { type: 'moveSurvivor', survivorId: st.survivors[0]!.id, x: 5, y: 0 });
      runUntil(st, () => st.survivors[0]!.pos.x === 5);
    }
    expect(booted.survivors[0]!.feet).not.toBeNull();
    // (Minus the trap, plus a little healing on the way.)
    expect(barefoot.survivors[0]!.health).toBeLessThanOrEqual(HEALTH_MAX - TRAP_STEP_DAMAGE + 5);
    expect(booted.survivors[0]!.health).toBe(HEALTH_MAX);
    expect(booted.items[0]!.wear).toBe(1);
  });

  it('a half-worn garment gets mended with a piece of cloth', () => {
    const state = setup(['sC...L']);
    const s = state.survivors[0]!;
    runUntil(state, () => s.body !== null);
    const coat = state.items.find((i) => i.kind === 'coat')!;
    coat.wear = GARMENT_LIFE.coat / 2;
    s.nextDressTick = 0;
    runUntil(state, () => coat.wear === 0);
    expect(state.items.some((i) => i.kind === 'cloth')).toBe(false);
    expect(s.note).toBe('mended their coat');
  });

  it('a workbench makes coats from cloth to the stock target', () => {
    const state = setup(['s.k..LL'], { craft: 3 });
    applyCommand(state, { type: 'setCraftTarget', kind: 'coat', count: 1 });
    runUntil(state, () => state.items.some((i) => i.kind === 'coat'), 3000);
    expect(state.items.some((i) => i.kind === 'cloth')).toBe(false);
    expect(state.furniture[0]!.stored).toBe(0);
  });

  it('a workbench holding wood for weapons gives it back when cloth goods are all that are wanted', () => {
    const state = setup(['s.k..ww.L'], { craft: 3 });
    const bench = state.furniture[0]!;
    applyCommand(state, { type: 'setCraftTarget', kind: 'weapon', count: 1 });
    runUntil(state, () => bench.stored === 1);
    applyCommand(state, { type: 'setCraftTarget', kind: 'weapon', count: 0 });
    applyCommand(state, { type: 'setCraftTarget', kind: 'boots', count: 1 });
    runUntil(state, () => state.items.some((i) => i.kind === 'boots'), 3000);
    expect(state.items.filter((i) => i.kind === 'wood')).toHaveLength(2);
  });
});
