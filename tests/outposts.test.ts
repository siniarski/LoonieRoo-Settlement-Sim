import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { everyone } from '../src/sim/expeditions';
import { NEVER, TRUCK_FUEL, truck } from '../src/sim/outposts';
import { loadScenario } from '../src/sim/scenario';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH } from '../src/sim/time';
import { TICKS_PER_TILE } from '../src/sim/survivors';
import { createWorld } from '../src/sim/worldgen';

const MAP = ['s.$$)' + '.'.repeat(45)];

function withPoi() {
  const state = loadScenario({ seed: 8, map: MAP });
  state.pointsOfInterest.push({ id: 'poi1', x: 49, y: 0, label: 'ruin' });
  return state;
}

describe('the truck', () => {
  it('a generated world has one out past the camp', () => {
    const state = createWorld(2, 128);
    expect((state.vehicles ?? []).filter((v) => v.kind === 'truck')).toHaveLength(1);
  });

  it('an expedition takes the seen truck and its fuel: there and back in half the time', () => {
    const walk = loadScenario({ seed: 8, map: ['s' + '.'.repeat(49)] });
    walk.pointsOfInterest.push({ id: 'poi1', x: 49, y: 0, label: 'ruin' });
    applyCommand(walk, { type: 'sendExpedition', survivorIds: [walk.survivors[0]!.id], poiId: 'poi1' });
    const drive = withPoi();
    expect(truck(drive)).toBeDefined();
    applyCommand(drive, { type: 'sendExpedition', survivorIds: [drive.survivors[0]!.id], poiId: 'poi1' });
    expect(drive.expeditions[0]!.truck).toBe(true);
    expect(drive.items.filter((i) => i.kind === 'fuel')).toHaveLength(2 - TRUCK_FUEL);
    expect(walk.expeditions[0]!.returnTick - drive.expeditions[0]!.returnTick).toBe(49 * TICKS_PER_TILE);
  });
});

describe('outposts', () => {
  it('an outpost stays, sends finds home each dawn, and comes back when recalled', () => {
    const state = withPoi();
    applyCommand(state, { type: 'sendExpedition', survivorIds: [state.survivors[0]!.id], poiId: 'poi1', outpost: true });
    const x = state.expeditions[0]!;
    expect(x.returnTick).toBe(NEVER);
    const before = state.items.length;
    state.tick = DAY_LENGTH;
    tick(state);
    expect(state.items.length).toBeGreaterThan(before);
    expect(applyCommand(state, { type: 'recallOutpost', expeditionId: x.id }).ok).toBe(true);
    while (state.expeditions.length > 0 && state.tick < 3 * DAY_LENGTH) tick(state);
    expect(everyone(state)).toHaveLength(state.survivors.length);
  });
});
