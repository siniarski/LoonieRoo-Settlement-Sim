import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { NORMAL } from '../src/sim/difficulty';
import { loadScenario } from '../src/sim/scenario';
import { STEP_UP, heirs } from '../src/sim/succession';
import { tick } from '../src/sim/tick';
import { createWorld } from '../src/sim/worldgen';

function dies(state: ReturnType<typeof loadScenario>) {
  const you = state.survivors.find((s) => s.id === state.control!.survivorId)!;
  you.health = 0;
  tick(state);
  return you;
}

describe('the death rule', () => {
  it('your death asks who carries on; someone resolved enough can', () => {
    const state = loadScenario({ seed: 1, map: ['s.s.s'] });
    const [you, steady, shaky] = state.survivors as [NonNullable<typeof state.survivors[0]>, NonNullable<typeof state.survivors[0]>, NonNullable<typeof state.survivors[0]>];
    steady.resolve = 70;
    shaky.resolve = STEP_UP - 1;
    applyCommand(state, { type: 'takeControl', survivorId: you.id });
    dies(state);
    expect(state.succession?.name).toBe(you.name);
    expect(state.control).toBeNull();
    expect(heirs(state).map((s) => s.id)).toEqual([steady.id]);
    expect(applyCommand(state, { type: 'succeed', choice: shaky.id }).ok).toBe(false);
    expect(applyCommand(state, { type: 'succeed', choice: steady.id })).toMatchObject({ ok: true });
    expect(state.control!.survivorId).toBe(steady.id);
    expect(state.succession).toBeNull();
  });

  it('or start anew, far off, alone and kitted out', () => {
    const state = createWorld(7, 256, { ...NORMAL, start: 'solo' });
    const you = state.survivors[0]!;
    const at = { ...you.pos };
    dies(state);
    expect(state.succession).not.toBeNull();
    expect(applyCommand(state, { type: 'succeed', choice: 'anew' })).toMatchObject({ ok: true });
    const fresh = state.survivors.find((s) => s.id === state.control!.survivorId)!;
    expect(Math.max(Math.abs(fresh.pos.x - at.x), Math.abs(fresh.pos.y - at.y))).toBeGreaterThan(30);
    expect(fresh.weapon).not.toBeNull();
  });

  it('or just watch the colony', () => {
    const state = loadScenario({ seed: 1, map: ['s.s'] });
    applyCommand(state, { type: 'takeControl', survivorId: state.survivors[0]!.id });
    dies(state);
    expect(applyCommand(state, { type: 'succeed', choice: 'none' })).toMatchObject({ ok: true });
    expect(state.control).toBeNull();
    expect(state.succession).toBeNull();
  });

  it('losing someone you were not controlling asks nothing', () => {
    const state = loadScenario({ seed: 1, map: ['s.s'] });
    applyCommand(state, { type: 'takeControl', survivorId: state.survivors[0]!.id });
    state.survivors[1]!.health = 0;
    tick(state);
    expect(state.succession).toBeNull();
  });
});
