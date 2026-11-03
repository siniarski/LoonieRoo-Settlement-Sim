import { describe, expect, it } from 'vitest';
import { compound, unfold, updateColonies } from '../src/sim/colonies';
import { applyCommand } from '../src/sim/commands';
import { checkInvariants } from '../src/sim/invariants';
import { spawnItem } from '../src/sim/items';
import { HOSTILE_SIGHT, residentsOf } from '../src/sim/residents';
import { FRIENDLY, HOSTILE, newSettlement } from '../src/sim/settlements';
import { createState, type SimState } from '../src/sim/state';
import { addSurvivor, type Survivor } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';
import { neighbours4 } from '../src/sim/grid';
import { DAY_LENGTH } from '../src/sim/time';

function town(people = 6): { state: SimState; you: Survivor; n: ReturnType<typeof newSettlement> } {
  const state = createState({ seed: 1, width: 100, height: 100 });
  const n = newSettlement(state, 'Saint Agnes', 50, 50, people, 40, 1);
  unfold(state, n);
  const box = compound(n);
  const you = addSurvivor(state, n.x, box.y1 + 2);
  applyCommand(state, { type: 'takeControl', survivorId: you.id });
  return { state, you, n };
}

/** A free floor tile next to a point. */
const beside = (state: SimState, p: { x: number; y: number }) => neighbours4(state.grid, p.x, p.y).find((q) => !state.residents.some((r) => r.pos.x === q.x && r.pos.y === q.y))!;

const run = (state: SimState, ticks: number) => {
  for (let t = 0; t < ticks; t++) {
    tick(state);
    expect(checkInvariants(state)).toEqual([]);
  }
};

describe('residents', () => {
  it('one a head, pottering about inside their walls', () => {
    const { state, n } = town();
    expect(residentsOf(state, n)).toHaveLength(6);
    const box = compound(n);
    run(state, 400);
    for (const r of residentsOf(state, n)) {
      expect(r.pos.x).toBeGreaterThan(box.x0);
      expect(r.pos.x).toBeLessThan(box.x1);
      expect(r.pos.y).toBeGreaterThan(box.y0);
      expect(r.pos.y).toBeLessThan(box.y1);
    }
    expect(new Set(residentsOf(state, n).map((r) => `${r.pos.x},${r.pos.y}`)).size).toBeGreaterThan(1);
  });

  it('follow the numbers at dawn: fewer people, fewer residents', () => {
    const { state, n } = town();
    n.people = 4;
    state.tick = DAY_LENGTH;
    updateColonies(state);
    expect(residentsOf(state, n).length).toBeLessThanOrEqual(n.people);
  });

  it('trade in person: what you carry, for food from their stores', () => {
    const { state, you, n } = town();
    const r = residentsOf(state, n)[0]!;
    you.pos = beside(state, r.pos);
    r.nextThinkTick = 1e9; // stand still
    const med = spawnItem(state, 'medicine', you.pos.x, you.pos.y);
    med.pos = null;
    med.carriedBy = you.id;
    you.carrying = med.id;
    expect(applyCommand(state, { type: 'talk' })).toMatchObject({ ok: true });
    expect(you.carrying).toBeNull();
    expect(state.items.filter((i) => i.kind === 'food')).toHaveLength(3);
    expect(n.food).toBe(37);
  });

  it('recruit in person once their settlement thinks well of you', () => {
    const { state, you, n } = town();
    n.reputation = FRIENDLY;
    const r = residentsOf(state, n)[0]!;
    you.pos = beside(state, r.pos);
    expect(applyCommand(state, { type: 'talk' })).toMatchObject({ ok: true });
    expect(state.survivors.find((s) => s.name === r.name)?.order).toEqual({ kind: 'follow' });
    expect(n.people).toBe(5);
  });

  it('hitting one makes their settlement an enemy, and its people come for you', () => {
    const { state, you, n } = town();
    const r = residentsOf(state, n)[0]!;
    you.pos = beside(state, r.pos);
    r.nextThinkTick = 1e9;
    expect(applyCommand(state, { type: 'swing', x: r.pos.x, y: r.pos.y })).toMatchObject({ ok: true });
    expect(n.reputation).toBeLessThanOrEqual(HOSTILE);
    const health = you.health;
    run(state, 60);
    expect(you.health).toBeLessThan(health);
    expect(HOSTILE_SIGHT).toBeGreaterThan(0);
  });
});
