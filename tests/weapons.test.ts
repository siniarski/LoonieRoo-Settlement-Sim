import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { killSurvivor } from '../src/sim/needs';
import { designationStatus, type RoomPurpose } from '../src/sim/rooms';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import type { Survivor } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';
import { TICKS_PER_HOUR } from '../src/sim/time';
import { WEAPON_KILLS } from '../src/sim/weapons';
import { addZombie } from '../src/sim/zombies';

/** 23:00 on day 1, just past a spawn boundary. */
const NIGHT = 17 * TICKS_PER_HOUR + 1;

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 1500): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = {}, rooms: { x: number; y: number; purpose: RoomPurpose }[] = []) {
  const state = loadScenario({ seed: 4, map, rooms });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

const weapons = (state: SimState) => state.items.filter((i) => i.kind === 'weapon');

/** Lets a guard pick up the weapon on the map by day. */
function arm(state: SimState, s: Survivor): void {
  runUntil(state, () => s.weapon !== null);
}

/** Ticks for a guard to kill a zombie standing next to them, at night. */
function timeToKill(armed: boolean): number {
  const state = setup(['s/.', '...'], { guard: 3 });
  const s = state.survivors[0]!;
  if (armed) arm(state, s);
  else state.items = [];
  state.tick = NIGHT;
  addZombie(state, s.pos.x, s.pos.y + 1);
  return runUntil(state, () => state.zombies.length === 0);
}

describe('weapons', () => {
  it('a guard picks up a free weapon between jobs and carries it', () => {
    const state = setup(['s...../'], { guard: 3 });
    const s = state.survivors[0]!;
    tick(state);
    expect(s.note).toBe('going for a weapon at (6, 0)');
    arm(state, s);
    const [w] = weapons(state);
    expect(s.weapon).toBe(w!.id);
    expect(w).toMatchObject({ pos: null, carriedBy: s.id, reservedBy: null });
    expect(s.note).toBe('armed with a weapon');
    expect(s.carrying).toBeNull();
  });

  it('survivors who never guard leave weapons alone', () => {
    const state = setup(['s...../'], { build: 3, haul: 3 });
    for (let i = 0; i < 60; i++) tick(state);
    expect(state.survivors[0]!.weapon).toBeNull();
    expect(weapons(state)[0]!.reservedBy).toBeNull();
  });

  it('two guards never go for the same weapon', () => {
    const state = setup(['s..../...s'], { guard: 3 });
    tick(state);
    expect(checkInvariants(state)).toEqual([]);
    expect(state.survivors.filter((s) => s.arming !== null)).toHaveLength(1);
    runUntil(state, () => state.survivors.some((s) => s.weapon));
    expect(state.survivors.filter((s) => s.weapon)).toHaveLength(1);
  });

  it('an armed guard kills a zombie in half the blows', () => {
    const unarmed = timeToKill(false);
    const armed = timeToKill(true);
    expect(armed).toBeLessThan(unarmed);
    // 200 health: 2 blows armed vs 4 unarmed, one every 10 ticks.
    expect(unarmed - armed).toBeGreaterThanOrEqual(19);
  });

  it(`a weapon breaks after ${WEAPON_KILLS} kills`, () => {
    const state = setup(['s/.', '...'], { guard: 3 });
    const s = state.survivors[0]!;
    arm(state, s);
    weapons(state)[0]!.wear = WEAPON_KILLS - 1;
    state.tick = NIGHT;
    addZombie(state, s.pos.x, s.pos.y + 1);
    runUntil(state, () => state.zombies.length === 0);
    expect(s.weapon).toBeNull();
    expect(weapons(state)).toEqual([]);
    expect(state.events.map((e) => e.message)).toContain(`${s.name}'s weapon broke`);
  });

  it('a weapon wears by one per armed kill', () => {
    const state = setup(['s/.', '...'], { guard: 3 });
    const s = state.survivors[0]!;
    arm(state, s);
    state.tick = NIGHT;
    addZombie(state, s.pos.x, s.pos.y + 1);
    runUntil(state, () => state.zombies.length === 0);
    expect(weapons(state)[0]!.wear).toBe(1);
  });

  it("a dead survivor's weapon falls where they died", () => {
    const state = setup(['s/...', '.....'], { guard: 3 });
    const s = state.survivors[0]!;
    arm(state, s);
    const at = { ...s.pos };
    killSurvivor(state, s, 'a test');
    expect(checkInvariants(state)).toEqual([]);
    expect(weapons(state)[0]).toMatchObject({ carriedBy: null, pos: at });
  });

  it('a direct order interrupts fetching a weapon and frees it', () => {
    const state = setup(['s...../', '.......'], { guard: 3 });
    const s = state.survivors[0]!;
    tick(state);
    expect(s.arming).not.toBeNull();
    applyCommand(state, { type: 'moveSurvivor', survivorId: s.id, x: 0, y: 1 });
    expect(s.arming).toBeNull();
    expect(weapons(state)[0]!.reservedBy).toBeNull();
  });

  it('getting hungry drops the weapon errand for a meal', () => {
    const state = setup(['f.s...../'], { guard: 3 });
    const s = state.survivors[0]!;
    tick(state);
    expect(s.arming).not.toBeNull();
    applyCommand(state, { type: 'setSatiety', survivorId: s.id, satiety: 0 });
    runUntil(state, () => s.meal !== null);
    expect(s.arming).toBeNull();
    expect(checkInvariants(state)).toEqual([]);
  });
});

describe('armory', () => {
  // Left room storage, right room armory; loose weapon and food outside.
  const MAP = [
    '#####.#####',
    '#...#.#...#',
    '#...D.D...#',
    '#...#.#...#',
    '#####.#####',
    's..../.f...',
  ];
  const STORAGE = { x: 2, y: 2, purpose: 'storage' as const };
  const ARMORY = { x: 8, y: 2, purpose: 'armory' as const };
  const roomOf = (x: number) => (x < 5 ? 'storage' : 'armory');

  it('weapons are hauled to the armory, everything else to storage', () => {
    const state = setup(MAP, { haul: 3 }, [STORAGE, ARMORY]);
    runUntil(state, () => state.jobs.length === 0 && state.items.every((i) => i.pos && i.pos.y < 4), 800);
    const where = Object.fromEntries(state.items.map((i) => [i.kind, roomOf(i.pos!.x)]));
    expect(where).toEqual({ weapon: 'armory', food: 'storage' });
  });

  it('without an armory, weapons go to storage', () => {
    const state = setup(MAP, { haul: 3 }, [STORAGE]);
    runUntil(state, () => state.jobs.length === 0 && state.items.every((i) => i.pos && i.pos.y < 4), 800);
    expect(state.items.map((i) => roomOf(i.pos!.x))).toEqual(['storage', 'storage']);
  });

  it('once an armory is complete, weapons kept in storage move to it', () => {
    const state = setup(MAP, { haul: 3 }, [STORAGE]);
    runUntil(state, () => state.jobs.length === 0 && state.items.every((i) => i.pos && i.pos.y < 4), 800);
    applyCommand(state, { type: 'designateRoom', ...ARMORY });
    runUntil(state, () => state.jobs.length === 0 && roomOf(weapons(state)[0]!.pos!.x) === 'armory', 800);
    expect(state.items.find((i) => i.kind === 'food')!.pos!.x).toBeLessThan(5);
  });

  it('an armory needs walls, a door, and 4 tiles; a cramped one is ignored', () => {
    const map = ['###.#####', '#.D.#...#', '###.D...#', '....#####', 's../.....'];
    const state = setup(map, { haul: 3 }, [{ x: 1, y: 1, purpose: 'armory' }, { x: 6, y: 1, purpose: 'storage' }]);
    const status = designationStatus(state, state.roomDesignations[0]!);
    expect(status.complete).toBe(false);
    expect(status.requirements.filter((r) => !r.met).map((r) => r.label)).toEqual(['at least 4 tiles']);
    runUntil(state, () => state.jobs.length === 0 && weapons(state)[0]!.pos!.y < 3, 800);
    expect(weapons(state)[0]!.pos!.x).toBeGreaterThan(4); // in storage, not the one-tile armory
  });
});
