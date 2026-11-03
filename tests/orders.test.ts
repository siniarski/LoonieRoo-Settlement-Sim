import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand, type Command } from '../src/sim/commands';
import { checkInvariants } from '../src/sim/invariants';
import { FOLLOW_GAP } from '../src/sim/orders';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';

function run(state: SimState, ticks: number): void {
  for (let t = 0; t < ticks; t++) {
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
}

const cmd = (state: SimState, c: Command) => expect(applyCommand(state, c), JSON.stringify(c)).toMatchObject({ ok: true });
const gap = (a: { x: number; y: number }, b: { x: number; y: number }) => Math.max(Math.abs(a.x - b.x), Math.abs(a.y - b.y));

describe('group orders', () => {
  it('followers keep up with you, a few tiles behind, and leave colony work alone', () => {
    const state = loadScenario({ seed: 1, map: ['ss........................', 'w.+.......................'] });
    const [you, them] = state.survivors as [NonNullable<SimState['survivors'][0]>, NonNullable<SimState['survivors'][0]>];
    cmd(state, { type: 'takeControl', survivorId: you.id });
    cmd(state, { type: 'shout', order: 'follow' });
    expect(them.order).toEqual({ kind: 'follow' });
    cmd(state, { type: 'steer', dx: 1, dy: 0 });
    run(state, 80);
    cmd(state, { type: 'steer', dx: 0, dy: 0 });
    run(state, 40);
    expect(you.pos.x).toBeGreaterThan(15);
    expect(gap(you.pos, them.pos)).toBeLessThanOrEqual(FOLLOW_GAP);
    expect(state.blueprints).toHaveLength(1); // nobody built the wall
  });

  it('hold keeps them at a spot; settle sends them back to work', () => {
    const state = loadScenario({ seed: 1, map: ['s.........', 'w.+.......'] });
    const s = state.survivors[0]!;
    cmd(state, { type: 'setOrder', survivorId: s.id, order: 'hold', x: 8, y: 0 });
    run(state, 60);
    expect(s.pos).toEqual({ x: 8, y: 0 });
    expect(state.blueprints).toHaveLength(1);
    cmd(state, { type: 'setOrder', survivorId: s.id, order: 'settle' });
    run(state, 200);
    expect(state.blueprints).toHaveLength(0);
  });

  it('under an order they still fight a zombie that comes close', () => {
    const state = loadScenario({ seed: 1, map: ['s...'] });
    const s = state.survivors[0]!;
    cmd(state, { type: 'setOrder', survivorId: s.id, order: 'hold' });
    applyCommand(state, { type: 'spawnZombie', x: 3, y: 0 });
    state.zombies[0]!.kind = 'static';
    run(state, 150);
    expect(state.zombies).toEqual([]);
  });

  it('people taken in while you are out come with you', () => {
    const state = loadScenario({ seed: 1, map: ['s..v'] });
    cmd(state, { type: 'takeControl', survivorId: state.survivors[0]!.id });
    cmd(state, { type: 'acceptWanderer', wandererId: state.wanderers[0]!.id });
    expect(state.survivors[1]!.order).toEqual({ kind: 'follow' });
  });
});
