import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { GROUNDS } from '../src/sim/grid';
import { HEALTH_MAX } from '../src/sim/health';
import { spawnItem } from '../src/sim/items';
import { stow } from '../src/sim/inventory';
import { centre } from '../src/sim/motion';
import { crossable } from '../src/sim/pathfinding';
import { loadScenario } from '../src/sim/scenario';
import { tick } from '../src/sim/tick';
import { CRASH_SPEED, FUEL_MAX, FUEL_PER_CAN, VEHICLE_HEALTH, addVehicle, vehicleOf } from '../src/sim/vehicles';
import { addZombie } from '../src/sim/zombies';
import { CHUNK, createWorld, ensureGeneratedAround } from '../src/sim/worldgen';

const ROW = '.'.repeat(40);
/** You at (2, 3) in an open field, under control, with a car facing east at (3, 3). */
function field(map = Array.from({ length: 7 }, () => ROW)) {
  const state = loadScenario({ seed: 1, map: map.map((r, y) => (y === 3 ? r.slice(0, 2) + 's' + r.slice(3) : r)) });
  const s = state.survivors[0]!;
  for (const k of Object.keys(s.priorities)) s.priorities[k as 'guard'] = 0;
  applyCommand(state, { type: 'takeControl', survivorId: s.id });
  const car = addVehicle(state, 3, 3);
  return { state, s, car };
}
const run = (state: ReturnType<typeof loadScenario>, n: number) => {
  for (let i = 0; i < n; i++) tick(state);
};

describe('vehicles', () => {
  it('you get in, put your foot down, and go; it burns fuel and it is loud', () => {
    const { state, s, car } = field();
    state.dormant = [{ id: 'd1', x: 20, y: 6 }];
    expect(applyCommand(state, { type: 'use', x: 3, y: 3 }).ok).toBe(true);
    expect(vehicleOf(state, s)).toBe(car);
    applyCommand(state, { type: 'steer', dx: 0, dy: -1 });
    run(state, 30);
    expect(car.pos.x).toBeGreaterThan(10);
    expect(s.pos).toEqual(car.pos); // you go with it
    expect(car.fuel).toBeLessThan(FUEL_MAX);
    expect(state.dormant).toEqual([]); // the engine woke it
  });

  it('turns only while moving, and gets you out beside it', () => {
    const { state, s, car } = field();
    applyCommand(state, { type: 'use', x: 3, y: 3 });
    applyCommand(state, { type: 'steer', dx: 1, dy: 0 });
    run(state, 10);
    expect(car.heading).toBe(0); // standing still: the wheel turns nothing
    applyCommand(state, { type: 'steer', dx: 1, dy: -1 });
    run(state, 10);
    expect(car.heading).toBeGreaterThan(0);
    applyCommand(state, { type: 'steer', dx: 0, dy: 1 });
    run(state, 40);
    expect(car.speed).toBeLessThanOrEqual(0);
    expect(applyCommand(state, { type: 'use', x: car.pos.x, y: car.pos.y }).ok).toBe(true);
    expect(vehicleOf(state, s)).toBeUndefined();
    expect(Math.max(Math.abs(s.pos.x - car.pos.x), Math.abs(s.pos.y - car.pos.y))).toBe(1);
  });

  it('a wall at speed is a crash; an empty tank goes nowhere', () => {
    const map = Array.from({ length: 7 }, () => ROW.slice(0, 25) + '#' + ROW.slice(26));
    const { state, car } = field(map);
    applyCommand(state, { type: 'use', x: 3, y: 3 });
    applyCommand(state, { type: 'steer', dx: 0, dy: -1 });
    let fastest = 0;
    for (let i = 0; i < 120; i++) {
      tick(state);
      fastest = Math.max(fastest, car.speed);
    }
    expect(fastest).toBeGreaterThan(CRASH_SPEED);
    expect(car.pos.x).toBeLessThan(25);
    expect(car.health).toBeLessThan(VEHICLE_HEALTH);
    expect(state.events.some((e) => e.message.includes('crashed'))).toBe(true);
    const dry = field();
    dry.car.fuel = 0;
    applyCommand(dry.state, { type: 'use', x: 3, y: 3 });
    applyCommand(dry.state, { type: 'steer', dx: 0, dy: -1 });
    run(dry.state, 20);
    expect(dry.car.pos).toEqual({ x: 3, y: 3 });
  });

  it('runs the dead down, and they can only batter the car to get at you', () => {
    const { state, s, car } = field();
    const z = addZombie(state, 16, 3, 200, 'static');
    applyCommand(state, { type: 'use', x: 3, y: 3 });
    applyCommand(state, { type: 'steer', dx: 0, dy: -1 });
    run(state, 40);
    expect(state.zombies.includes(z)).toBe(false); // run over
    // Parked, with one at the window.
    applyCommand(state, { type: 'steer', dx: 0, dy: 1 });
    run(state, 40);
    applyCommand(state, { type: 'steer', dx: 0, dy: 0 });
    run(state, 10);
    const biter = addZombie(state, car.pos.x, car.pos.y + 1, 200, 'static');
    biter.at = centre(biter.pos);
    const dent = car.health;
    run(state, 60);
    expect(s.health).toBe(HEALTH_MAX); // untouched
    expect(car.health).toBeLessThan(dent);
  });

  it('a fuel can from your bag fills the tank', () => {
    const { state, s, car } = field();
    car.fuel = 0;
    stow(state, s, spawnItem(state, 'fuel', 2, 3));
    expect(applyCommand(state, { type: 'use', x: 3, y: 3 }).ok).toBe(true);
    expect(car.fuel).toBe(FUEL_PER_CAN);
    expect(s.pack).toEqual([]);
    expect(vehicleOf(state, s)).toBeUndefined(); // filling up, not getting in
  });

  it('letting go of control puts you out of the car', () => {
    const { state, s } = field();
    applyCommand(state, { type: 'use', x: 3, y: 3 });
    applyCommand(state, { type: 'takeControl', survivorId: null });
    tick(state);
    expect(vehicleOf(state, s)).toBeUndefined();
  });

  it('no door, window or barricade lets a car through', () => {
    const state = loadScenario({ seed: 1, map: ['...'], edges: [{ x: 1, y: 0, side: 'w', kind: 'door' }] });
    state.grid.edgeOpen.push(1 * 2 + 1);
    expect(crossable(state.grid, 0, 0, 1, 0, 'vehicle')).toBe(false);
    expect(crossable(state.grid, 1, 0, 2, 0, 'vehicle')).toBe(true);
  });

  it('towns have cars parked in the street', () => {
    const state = createWorld(5, 256);
    for (let y = 0; y < 256; y += CHUNK) for (let x = 0; x < 256; x += CHUNK) ensureGeneratedAround(state, { x, y });
    const cars = state.vehicles ?? [];
    expect(cars.length).toBeGreaterThan(4);
    const onRoad = cars.filter((c) => GROUNDS[state.grid.ground[c.pos.y * 256 + c.pos.x]!] === 'road').length;
    expect(onRoad).toBeGreaterThan(cars.length / 2);
    for (const c of cars) expect(c.fuel).toBeLessThan(FUEL_MAX);
  }, 30000);
});

describe('vehicles: roads, trees and the boot', () => {
  it('goes slower off the road, and a tree stops it like a wall', async () => {
    const { GROUNDS: G } = await import('../src/sim/grid');
    const { OFFROAD_MAX, MAX_SPEED } = await import('../src/sim/vehicles');
    const { addFurniture } = await import('../src/sim/furniture');
    const top = (road: boolean) => {
      const { state, car } = field();
      if (road) state.grid.ground.fill(G.indexOf('road'));
      applyCommand(state, { type: 'use', x: 3, y: 3 });
      applyCommand(state, { type: 'steer', dx: 0, dy: -1 });
      let fastest = 0;
      for (let i = 0; i < 25; i++) {
        tick(state);
        fastest = Math.max(fastest, car.speed);
      }
      return fastest;
    };
    expect(top(false)).toBe(OFFROAD_MAX);
    expect(top(true)).toBe(MAX_SPEED);
    const { state, car } = field();
    addFurniture(state, 'tree', 12, 3);
    applyCommand(state, { type: 'use', x: 3, y: 3 });
    applyCommand(state, { type: 'steer', dx: 0, dy: -1 });
    run(state, 80);
    expect(car.pos.x).toBeLessThan(12);
  });

  it('things go in the boot from your bag, and come out again', async () => {
    const { BOOT_MAX } = await import('../src/sim/vehicles');
    const { ITEM_WEIGHT, contents } = await import('../src/sim/inventory');
    const { state, s, car } = field();
    const can = spawnItem(state, 'wood', 2, 3);
    stow(state, s, can);
    expect(applyCommand(state, { type: 'stash', itemId: can.id, containerId: car.id }).ok).toBe(true);
    expect(s.pack).toEqual([]);
    expect(contents(state, car).map((i) => i.id)).toEqual([can.id]);
    expect(applyCommand(state, { type: 'takeAll', containerId: car.id }).ok).toBe(true);
    expect(s.pack).toEqual([can.id]);
    // It only holds so much.
    for (let i = 0; i * ITEM_WEIGHT.wood <= BOOT_MAX; i++) {
      const w = spawnItem(state, 'wood', 2, 3);
      w.pos = null;
      w.inside = car.id;
    }
    expect(applyCommand(state, { type: 'stash', itemId: can.id, containerId: car.id }).ok).toBe(false);
  });

  it("the expedition truck is a vehicle, and a trip takes fuel from its tank", async () => {
    const { truck, takeTruck, TRIP_FUEL } = await import('../src/sim/outposts');
    const state = createWorld(2, 128);
    const t = (state.vehicles ?? []).find((v) => v.kind === 'truck')!;
    expect(t).toBeDefined();
    state.grid.explored.fill(1);
    expect(truck(state)).toEqual(t.pos);
    const fuel = t.fuel;
    expect(takeTruck(state)).toBe(true);
    expect(t.fuel).toBe(fuel - TRIP_FUEL);
  }, 20000);
});
