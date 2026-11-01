import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { BANDAGE_HEAL, HEALTH_CRITICAL } from '../src/sim/health';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { planFurnish } from '../src/sim/planner';
import type { RoomPurpose } from '../src/sim/rooms';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import type { Survivor } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 1500): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], rooms: { x: number; y: number; purpose: RoomPurpose }[] = []) {
  const state = loadScenario({ seed: 4, map, rooms });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS };
  return state;
}

const wound = (state: SimState, s: Survivor, health: number) => applyCommand(state, { type: 'setHealth', survivorId: s.id, health });

describe('doctor', () => {
  it('brings a bandage to a bedridden patient and treats them', () => {
    // Ash (critical) goes to bed; Bea is the doctor; the bandage is across the room.
    const state = setup(['s.b...', '......', 's.....']);
    const [ash, bea] = state.survivors as [Survivor, Survivor];
    bea.priorities = { ...NO_JOBS, doctor: 3 };
    applyCommand(state, { type: 'setRest', survivorId: bea.id, rest: 2000 });
    wound(state, ash, HEALTH_CRITICAL - 50);
    runUntil(state, () => ash.sleep?.asleep === true);
    // A bandage turns up only now (awake, Ash would have used it himself).
    state.items.push({ id: 'i900', kind: 'bandage', pos: { x: 5, y: 2 }, carriedBy: null, reservedBy: null, wear: 0 });
    const before = ash.health;
    runUntil(state, () => bea.note === `treated ${ash.name}'s wounds`);
    expect(ash.health).toBeGreaterThanOrEqual(before + BANDAGE_HEAL);
    expect(state.items).toEqual([]);
    expect(ash.sleep?.asleep).toBe(true); // treated in their sleep
  });

  it('no bandages, no doctor job claimed (and nobody stuck)', () => {
    const state = setup(['s.b...', 's.....']);
    const [ash, bea] = state.survivors as [Survivor, Survivor];
    bea.priorities = { ...NO_JOBS, doctor: 3 };
    wound(state, ash, HEALTH_CRITICAL - 50);
    runUntil(state, () => ash.sleep?.asleep === true);
    for (let i = 0; i < 50; i++) tick(state);
    expect(state.jobs).toMatchObject([{ type: 'doctor', patientId: ash.id, reservedBy: null }]);
  });

  it('the job goes once the patient is up', () => {
    const state = setup(['s.b...', 's.....']);
    const [ash] = state.survivors as [Survivor];
    wound(state, ash, HEALTH_CRITICAL - 50);
    runUntil(state, () => state.jobs.some((j) => j.type === 'doctor'));
    applyCommand(state, { type: 'moveSurvivor', survivorId: ash.id, x: 5, y: 1 }); // an order wakes them
    tick(state);
    expect(state.jobs).toEqual([]);
  });
});

describe('infirmary', () => {
  // Left: an infirmary with one bed. Right: an ordinary bed in the open.
  const MAP = ['####....', '#b.#....', '#..#..b.', '##D#....', '...s....'];
  const INF = { x: 1, y: 2, purpose: 'infirmary' as const };

  it('the hurt choose its bed; the healthy leave it for them', () => {
    const hurt = setup(MAP, [INF]);
    wound(hurt, hurt.survivors[0]!, HEALTH_CRITICAL - 50);
    runUntil(hurt, () => hurt.survivors[0]!.sleep !== null);
    expect(hurt.survivors[0]!.sleep!.bedId).toBe(hurt.furniture[0]!.id);

    const tired = setup(MAP, [INF]);
    applyCommand(tired, { type: 'setRest', survivorId: tired.survivors[0]!.id, rest: 0 });
    runUntil(tired, () => tired.survivors[0]!.sleep !== null);
    expect(tired.survivors[0]!.sleep!.bedId).toBe(tired.furniture[1]!.id);
  });

  it('heals twice as fast as an ordinary bed', () => {
    const healed = (rooms: { x: number; y: number; purpose: RoomPurpose }[]) => {
      const state = setup(['####', '#b.#', '#..#', '##D#', '..s.'], rooms);
      const s = state.survivors[0]!;
      wound(state, s, HEALTH_CRITICAL - 50);
      runUntil(state, () => s.sleep?.asleep === true);
      const start = s.health;
      for (let i = 0; i < 100; i++) tick(state);
      return s.health - start;
    };
    const plain = healed([]);
    expect(healed([{ x: 1, y: 2, purpose: 'infirmary' }])).toBe(2 * plain);
  });

  it('needs a bed; the planner furnishes one per 4 survivors', () => {
    const state = setup(['#####', '#...#', '#...#', '##D##', '.s.s.'], [{ x: 1, y: 1, purpose: 'infirmary' }]);
    const plan = planFurnish(state, 1, 1);
    expect(plan.commands).toHaveLength(1);
    expect(plan.notes[0]).toBe('Planned 1 infirmary bed (one per 4 survivors).');
  });
});
