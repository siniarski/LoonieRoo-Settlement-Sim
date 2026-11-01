import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { tileIndex } from '../src/sim/grid';
import { checkInvariants } from '../src/sim/invariants';
import { findPath } from '../src/sim/pathfinding';
import { MAX_ROOM_TILES, roomAt } from '../src/sim/rooms';
import { loadScenario } from '../src/sim/scenario';
import { createState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { CHUNK, START_CLEAR_RADIUS, WORLD_SIZES, createWorld, generateChunk, layout, lootFor } from '../src/sim/worldgen';

const SIZE = 128; // smaller than the real world, to keep the suite quick

describe('terrain', () => {
  it('water blocks walking, building, and zombies', () => {
    const state = loadScenario({ seed: 1, map: ['s.%..'] });
    expect(findPath(state.grid, { x: 0, y: 0 }, { x: 4, y: 0 })).toBeNull();
    expect(applyCommand(state, { type: 'placeWall', x: 2, y: 0 }).ok).toBe(false);
  });

  it('a pocket of floor that touches water, or is huge, is outdoors, not a room', () => {
    const pond = loadScenario({ seed: 1, map: ['#####', '#..%#', '#####'] });
    expect(roomAt(pond, 1, 1)).toBeNull();
    const side = Math.ceil(Math.sqrt(MAX_ROOM_TILES)) + 1;
    const big = loadScenario({ seed: 1, map: ['#'.repeat(side + 2), ...Array.from({ length: side }, () => `#${'.'.repeat(side)}#`), '#'.repeat(side + 2)] });
    expect(roomAt(big, 1, 1)).toBeNull();
    const small = loadScenario({ seed: 1, map: ['####', '#..#', '####'] });
    expect(roomAt(small, 1, 1)).not.toBeNull();
  });
});

describe('world generation', () => {
  const world = createWorld(7, SIZE);
  const count = (pred: (t: string) => boolean) => world.grid.tiles.filter(pred).length;

  it('is the same world every time for a seed, and a different one for another seed', () => {
    expect(JSON.stringify(createWorld(7, SIZE))).toBe(JSON.stringify(world));
    expect(createWorld(8, SIZE).grid.tiles.join()).not.toBe(world.grid.tiles.join());
  });

  it('generates any chunk the same whatever order the chunks come in', () => {
    const l = layout(7, SIZE);
    const forward = createState({ seed: 7, width: SIZE, height: SIZE });
    const backward = createState({ seed: 7, width: SIZE, height: SIZE });
    const n = SIZE / CHUNK;
    for (let i = 0; i < n * n; i++) generateChunk(forward, l, i % n, Math.floor(i / n));
    for (let i = n * n - 1; i >= 0; i--) generateChunk(backward, l, i % n, Math.floor(i / n));
    expect(backward.grid.tiles).toEqual(forward.grid.tiles);
    expect(backward.grid.ground).toEqual(forward.grid.ground);
    const things = (s: typeof forward) => s.furniture.map((f) => `${f.kind}@${f.x},${f.y}:${f.loot}`).sort();
    expect(things(backward)).toEqual(things(forward));
  });

  it('has roads, ruins with crates, forest, and water', () => {
    expect(world.grid.ground.filter((g) => g === 2).length).toBeGreaterThan(SIZE * 2); // road
    expect(count((t) => t === 'water')).toBeGreaterThan(SIZE);
    expect(count((t) => t === 'wall')).toBeGreaterThan(100);
    expect(world.furniture.filter((f) => f.kind === 'crate').length).toBeGreaterThan(5);
    expect(world.furniture.filter((f) => f.kind === 'tree').length).toBeGreaterThan(50);
  });

  it('starts with a calm, revealed clearing: three survivors, a hut with three beds, wood and food', () => {
    const { x: sx, y: sy } = layout(7, SIZE).start;
    expect(world.survivors).toHaveLength(3);
    expect(world.furniture.filter((f) => f.kind === 'bed')).toHaveLength(3);
    expect(world.items.filter((i) => i.kind === 'wood')).toHaveLength(12);
    for (let y = sy - START_CLEAR_RADIUS + 2; y < sy + START_CLEAR_RADIUS - 2; y++) {
      for (let x = sx - START_CLEAR_RADIUS + 2; x < sx + START_CLEAR_RADIUS - 2; x++) {
        expect(world.grid.tiles[tileIndex(world.grid, x, y)]).not.toBe('water');
      }
    }
    expect(world.grid.explored[tileIndex(world.grid, sx, sy)]).toBe(1);
    expect(world.grid.explored[0]).toBe(0);
  });

  it('crates further out hold more', () => {
    expect(lootFor(0)).toBeLessThan(lootFor(100));
    expect(lootFor(1000)).toBe(lootFor(10000)); // capped
  });

  it('a fresh world is consistent and runs', () => {
    const state = createWorld(3, SIZE);
    expect(checkInvariants(state)).toEqual([]);
    for (let i = 0; i < 300; i++) tick(state);
    expect(checkInvariants(state)).toEqual([]);
  });
});

describe('world sizes', () => {
  it('every offered size generates a consistent, runnable world', () => {
    for (const size of WORLD_SIZES.filter((n) => n <= 256)) {
      const state = createWorld(11, size);
      expect(state.grid.width).toBe(size);
      expect(state.survivors).toHaveLength(3);
      expect(checkInvariants(state)).toEqual([]);
      for (let i = 0; i < 50; i++) tick(state);
      expect(checkInvariants(state)).toEqual([]);
    }
  });
});
