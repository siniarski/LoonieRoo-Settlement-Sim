import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { DEAD_LABEL, RUN_FIND, RUN_PARTY, runsOf, updateRuns, type ScavRun } from '../src/sim/runs';
import { loadScenario } from '../src/sim/scenario';
import { newSettlement } from '../src/sim/settlements';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
import { addVehicle, vehiclesOf } from '../src/sim/vehicles';
import { revealAround } from '../src/sim/fog';

function world(): SimState {
  const state = loadScenario({ seed: 3, map: Array.from({ length: 60 }, (_, y) => (y === 10 ? 's' : '.') + '.'.repeat(79)) });
  return state;
}

function run(state: SimState, over: Partial<ScavRun> = {}): ScavRun {
  const n = state.settlements[0]!;
  const r: ScavRun = { id: 'sr1', from: n.id, x: n.x, y: n.y, home: { x: n.x, y: n.y }, ruin: { x: n.x + 10, y: n.y }, people: 3, phase: 'out', untilTick: 0, nextStepTick: state.tick + 1, food: 0, met: false, ...over };
  state.runs = [r];
  return r;
}

/** Only the runs, n ticks. */
function runs(state: SimState, n: number): void {
  for (let k = 0; k < n; k++) {
    state.tick += 1;
    updateRuns(state);
  }
}

describe("neighbours' scavenging runs", () => {
  it('a hungry settlement sends people out at dawn now and then', () => {
    const state = world();
    const n = newSettlement(state, 'Hollow', 40, 30, 10, 0, 1);
    state.settlements = [n];
    let sent = false;
    for (let d = 1; d <= 20 && !sent; d++) {
      state.tick = d * DAY_LENGTH - 1;
      runs(state, 1);
      sent = runsOf(state).length > 0;
    }
    expect(sent).toBe(true);
    expect(runsOf(state)[0]!.people).toBe(RUN_PARTY);
    expect(n.people).toBe(10 - RUN_PARTY);
  });

  it('out to the ruin, a search, and home with food', () => {
    const state = world();
    const n = newSettlement(state, 'Hollow', 40, 30, 7, 0, 1);
    state.settlements = [n];
    state.settings = { ...state.settings, zombies: 0 }; // nothing catches them
    n.people -= 3;
    const r = run(state);
    runs(state, 20 * TICKS_PER_HOUR);
    expect(runsOf(state)).toHaveLength(0);
    expect(n.people).toBe(7);
    expect(n.food).toBe(3 * RUN_FIND);
    expect(r.phase).toBe('back');
  });

  it('caught by the dead to the last: bodies, their food, and a mark on your map', () => {
    const state = world();
    const n = newSettlement(state, 'Hollow', 40, 30, 7, 0, 1);
    state.settlements = [n];
    state.settings = { ...state.settings, zombies: 1e9 };
    state.harsh = true;
    run(state, { phase: 'search', untilTick: 1e9, x: 50, y: 30, food: 4 });
    state.tick = TICKS_PER_HOUR - 1;
    for (let h = 0; h < 4 && runsOf(state).length > 0; h++) runs(state, TICKS_PER_HOUR);
    expect(runsOf(state)).toHaveLength(0);
    expect(state.pointsOfInterest.some((p) => p.label === DEAD_LABEL)).toBe(true);
    expect(state.items.filter((i) => i.kind === 'corpse')).toHaveLength(3);
    expect(state.items.filter((i) => i.kind === 'food')).toHaveLength(4);
  });

  it('meeting one of yours: they think better of you and say where they are going', () => {
    const state = world();
    const s = state.survivors[0]!;
    const n = newSettlement(state, 'Hollow', s.pos.x + 2, s.pos.y, 7, 0, 1);
    state.settlements = [n];
    state.settings = { ...state.settings, zombies: 0 };
    const rep = n.reputation;
    const r = run(state);
    runs(state, 2);
    expect(r.met).toBe(true);
    expect(n.reputation).toBe(rep + 3);
    expect(state.pointsOfInterest.some((p) => p.x === r.ruin.x && p.y === r.ruin.y)).toBe(true);
  });
});

describe('the truck on the road', () => {
  function withTruck() {
    const state = loadScenario({ seed: 8, map: ['s' + '.'.repeat(59), '.'.repeat(60)] });
    state.settings = { ...state.settings, zombies: 0 };
    state.pointsOfInterest.push({ id: 'poi1', x: 59, y: 0, label: 'ruin' });
    const v = addVehicle(state, 2, 1, 0, undefined, undefined, 'truck');
    revealAround(state, { x: 2, y: 1 }, 3);
    return { state, v };
  }

  it('drives out, waits, and comes back; nobody else takes it meanwhile', () => {
    const { state, v } = withTruck();
    const s = state.survivors[0]!;
    expect(applyCommand(state, { type: 'sendExpedition', survivorIds: [s.id], poiId: 'poi1' }).ok).toBe(true);
    const x = state.expeditions[0]!;
    expect(x.truckId).toBe(v.id);
    expect(v.driverId).toBe(`x:${x.id}`);
    let furthest = 0;
    while (state.expeditions.length > 0 && state.tick < DAY_LENGTH * 2) {
      tick(state);
      furthest = Math.max(furthest, v.pos.x);
    }
    expect(furthest).toBeGreaterThanOrEqual(55);
    expect(v.driverId).toBeNull();
    expect(Math.abs(v.pos.x - x.home.x) + Math.abs(v.pos.y - x.home.y)).toBeLessThanOrEqual(3);
    expect(vehiclesOf(state)).toHaveLength(1);
  });

  it('caught on the road with nobody left, the truck stands where it stopped', () => {
    const { state, v } = withTruck();
    state.settings = { ...state.settings, zombies: 1e9 };
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'sendExpedition', survivorIds: [s.id], poiId: 'poi1' });
    while (state.expeditions.length > 0 && state.tick < DAY_LENGTH) tick(state);
    expect(state.expeditions).toHaveLength(0);
    expect(v.driverId).toBeNull();
    expect(state.events.some((e) => e.message.includes('truck stands empty'))).toBe(true);
  });
});
