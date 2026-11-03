import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { VENISON } from '../src/sim/animals';
import { CROPS, EGG_EVERY } from '../src/sim/farming';
import { HEALTH_MAX } from '../src/sim/health';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { FUEL_MAX, isWarmed } from '../src/sim/power';
import type { RoomPurpose } from '../src/sim/rooms';
import { loadScenario } from '../src/sim/scenario';
import { SEASON_DAYS, isCold } from '../src/sim/seasons';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = {}, rooms: { x: number; y: number; purpose: RoomPurpose }[] = []) {
  const state = loadScenario({ seed: 21, map, rooms });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

const WINTER_NIGHT = 3 * SEASON_DAYS * DAY_LENGTH + 15 * TICKS_PER_HOUR + 1;

describe('heaters', () => {
  it('haulers fuel a heater; on a winter night it keeps those near it from the cold', () => {
    const state = setup(['s.X......', 'ww.......'], { haul: 3 });
    const h = state.furniture[0]!;
    runUntil(state, () => h.stored === 2, 400);
    expect(FUEL_MAX).toBeGreaterThanOrEqual(2);
    state.tick = WINTER_NIGHT;
    state.zombiesTonight = 0;
    const s = state.survivors[0]!;
    expect(isWarmed(state, s.pos)).toBe(true);
    expect(isCold(state, s)).toBe(false);
    expect(isCold(state, { ...s, pos: { x: 8, y: 1 } })).toBe(true);
  });
});

describe('crops and greenhouses', () => {
  it('wheat is slow and yields three; herbs give medicine', () => {
    const state = setup(['s....']);
    applyCommand(state, { type: 'designateFarm', x: 2, y: 0, crop: 'wheat' });
    applyCommand(state, { type: 'designateFarm', x: 3, y: 0, crop: 'herb' });
    state.survivors[0]!.priorities.farm = 3;
    runUntil(state, () => state.farm.every((p) => p.planted), 300);
    state.survivors[0]!.priorities.farm = 0;
    for (let i = 0; i < CROPS.wheat.grow + 5; i++) {
      state.survivors[0]!.satiety = 3000; // (not testing hunger or sleep)
      state.survivors[0]!.rest = 2000;
      tick(state);
    }
    state.survivors[0]!.priorities.farm = 3;
    // (Harvested and sown again straight away, so look at the yield.)
    runUntil(state, () => state.items.length >= CROPS.wheat.yield + 1, 300);
    state.survivors[0]!.priorities.farm = 0;
    expect(state.items.filter((i) => i.kind === 'food')).toHaveLength(CROPS.wheat.yield);
    expect(state.items.filter((i) => i.kind === 'medicine')).toHaveLength(1);
  });

  it('nothing grows outdoors in winter, but it does in a greenhouse', () => {
    const map = ['......', '.####.', '.#..#.', '.#..#.', '.##D#.', '......'];
    const state = setup(map, {}, [{ x: 2, y: 2, purpose: 'greenhouse' }]);
    applyCommand(state, { type: 'designateFarm', x: 2, y: 2 });
    applyCommand(state, { type: 'designateFarm', x: 0, y: 0 });
    for (const p of state.farm) p.planted = true;
    state.tick = 3 * SEASON_DAYS * DAY_LENGTH;
    for (let i = 0; i < 100; i++) tick(state);
    expect(state.farm.map((p) => p.growth)).toEqual([100, 0]);
  });
});

describe('coops and hunting', () => {
  it('a coop lays a food now and then', () => {
    const state = setup(['M..']);
    state.tick = EGG_EVERY;
    tick(state);
    expect(state.items.map((i) => i.kind)).toEqual(['food']);
  });

  it('the Hunt order marks deer; a hunter takes one down for venison', () => {
    const state = setup(['s.........', '.......u..'], { hunt: 3 });
    expect(applyCommand(state, { type: 'designateHunt', x0: 5, y0: 0, x1: 9, y1: 1 }).ok).toBe(true);
    runUntil(state, () => state.animals.length === 0, 800);
    expect(state.items.filter((i) => i.kind === 'food')).toHaveLength(VENISON);
    expect(state.survivors[0]!.health).toBe(HEALTH_MAX);
  });
});
