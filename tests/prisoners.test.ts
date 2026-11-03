import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { idNumber, hashRoll } from '../src/sim/hash';
import { NO_JOBS } from '../src/sim/jobTypes';
import { isTired, killSurvivor } from '../src/sim/needs';
import { CAPTURE_CHANCE, RECRUIT_DAYS } from '../src/sim/prisoners';
import { NIGHT_TIRED, TIRED } from '../src/sim/rest';
import type { RoomPurpose } from '../src/sim/rooms';
import { loadScenario } from '../src/sim/scenario';
import { HARDENED_KILLS, hasTrait } from '../src/sim/skills';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
import { addZombie, damageZombie } from '../src/sim/zombies';

const CELLS = ['s.......', '###D####', '#......#', '#......#', '########'];

function setup(map: string[], rooms: { x: number; y: number; purpose: RoomPurpose }[] = []) {
  const state = loadScenario({ seed: 2, map, rooms });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS };
  return state;
}

/** A raider whose hash says "taken alive". */
function catchableRaider(state: ReturnType<typeof setup>) {
  while (hashRoll(state.nextId, 0xca9) >= CAPTURE_CHANCE) state.nextId += 1;
  return addZombie(state, 5, 0, 10, 'raider');
}

describe('prisoners', () => {
  it('a raider brought down may be taken alive, into a free cell', () => {
    const state = setup(CELLS, [{ x: 1, y: 2, purpose: 'cell' }]);
    const z = catchableRaider(state);
    expect(idNumber(z.id)).toBeGreaterThan(0);
    damageZombie(state, z, 100, state.survivors[0]!);
    expect(state.zombies).toEqual([]);
    expect(state.prisoners).toHaveLength(1);
    expect(state.prisoners[0]!.y).toBeGreaterThanOrEqual(2);
    expect(state.items.filter((i) => i.kind === 'corpse')).toEqual([]);
  });

  it('no cells, no prisoners', () => {
    const state = setup(CELLS);
    damageZombie(state, catchableRaider(state), 100, state.survivors[0]!);
    expect(state.prisoners).toEqual([]);
  });

  it('they eat a food each dawn, get away with none, and can be recruited after a while', () => {
    const state = setup(['s.ff....', '###D####', '#......#', '#......#', '########'], [{ x: 1, y: 2, purpose: 'cell' }]);
    state.prisoners.push({ id: 'q1', name: 'Rook', x: 2, y: 2, from: null, since: 0 });
    state.prisoners.push({ id: 'q2', name: 'Vex', x: 3, y: 2, from: null, since: 0 });
    expect(applyCommand(state, { type: 'recruitPrisoner', prisonerId: 'q1' }).ok).toBe(false);
    state.tick = DAY_LENGTH;
    tick(state);
    expect(state.items.filter((i) => i.kind === 'food')).toHaveLength(0);
    expect(state.prisoners).toHaveLength(2);
    state.tick = RECRUIT_DAYS * DAY_LENGTH + 1;
    expect(applyCommand(state, { type: 'recruitPrisoner', prisonerId: 'q1' }).ok).toBe(true);
    expect(state.survivors.map((s) => s.name)).toContain('Rook');
    state.tick = 3 * DAY_LENGTH;
    tick(state);
    expect(state.prisoners).toEqual([]); // Vex: nothing to eat, got away
  });
});

describe('shifts', () => {
  it('the night shift turns in by day instead of at night', () => {
    const state = setup(['ss']);
    const [day, night] = state.survivors;
    night!.shift = 'night';
    for (const s of state.survivors) s.rest = (TIRED + NIGHT_TIRED) / 2;
    state.tick = 6 * TICKS_PER_HOUR; // noon
    expect([isTired(state, day!), isTired(state, night!)]).toEqual([false, true]);
    state.tick = 17 * TICKS_PER_HOUR; // 23:00
    expect([isTired(state, day!), isTired(state, night!)]).toEqual([true, false]);
    expect(applyCommand(state, { type: 'setShift', survivorId: day!.id, shift: 'night' }).ok).toBe(true);
  });
});

describe('earned traits', () => {
  it('enough kills harden a guard; losing a friend haunts someone', () => {
    const state = setup(['ss.']);
    const [a, b] = state.survivors;
    for (let i = 0; i < HARDENED_KILLS; i++) damageZombie(state, addZombie(state, 2, 0, 1, 'static'), 10, a!);
    expect(hasTrait(a!, 'hardened')).toBe(true);
    b!.opinions[a!.id] = 50;
    killSurvivor(state, a!, 'testing');
    expect(hasTrait(b!, 'haunted')).toBe(true);
  });
});
