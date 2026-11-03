// World generation: a seeded world of roads, ruined towns, forests, rivers and lakes.
//
// Built for a much bigger world later (see NOTES.md, "Big worlds"):
//  - Everything is a pure function of (seed, world position). Noise and
//    rolls come from a stateless hash, never from the sim RNG, so a chunk
//    comes out the same whatever order chunks are generated in.
//  - Generation runs chunk by chunk (CHUNK x CHUNK tiles). Buildings sit in
//    cells that never cross a chunk border; roads, water and forest are
//    per-tile functions, so they line up across borders by construction.
//  - Chunks are generated lazily: the start area when the world is made,
//    then any chunk within GENERATE_RADIUS chunks of a survivor, well ahead
//    of what they can see (ensureGeneratedAround, called from fog.ts).
//    (Entity ids come from state.nextId, so they do depend on generation
//    order; ids are labels, nothing else.)

import { addFurniture, CRATE_LOOT, furnitureAt } from './furniture';
import { GROUNDS, getTile, inBounds, isWalkable, setTile, tileIndex, type Ground, type Point } from './grid';
import { itemAt, spawnItem } from './items';
import { nearestTile } from './space';
import { createState, type SimState } from './state';
import { addSurvivor } from './survivors';
import { rollTraits } from './skills';

export const CHUNK = 32;
/** Chunks this far (in chunks, each way) from any survivor are always generated. */
export const GENERATE_RADIUS = 2;

/** What a generated world needs to make more of itself (null on hand-made maps). */
export interface WorldInfo {
  seed: number;
  size: number;
  /** Chunks a side. */
  chunks: number;
  /** 1 per generated chunk, row-major. */
  generated: number[];
}
/** World sizes on offer (tiles a side, multiples of CHUNK). Development uses the small ones. */
export const WORLD_SIZES = [64, 128, 256, 512] as const;
export const WORLD_SIZE = 128;
/** Building cells: CHUNK must be a multiple, so buildings never cross chunks. */
const CELL = 16;
/** Kept clear around the start, so the first day is calm. */
export const START_CLEAR_RADIUS = 16;
/** Tiles revealed around the start before anyone moves. */
export const START_REVEAL_RADIUS = 14;

// --- Stateless randomness ----------------------------------------------------

/** A well-mixed 32-bit hash of the seed and up to three integers. */
export function hash(seed: number, a: number, b: number, salt: number): number {
  let h = Math.imul(seed ^ 0x9e3779b9, 0x85ebca6b) ^ Math.imul(a, 0xc2b2ae35) ^ Math.imul(b, 0x27d4eb2f) ^ Math.imul(salt, 0x165667b1);
  h = Math.imul(h ^ (h >>> 15), 0x2c1b3c6d);
  h = Math.imul(h ^ (h >>> 12), 0x297a2d39);
  return (h ^ (h >>> 15)) >>> 0;
}

/** Uniform in [0, 1). */
function roll(seed: number, a: number, b: number, salt: number): number {
  return hash(seed, a, b, salt) / 4294967296;
}

const smooth = (t: number) => t * t * (3 - 2 * t);

/** Smooth value noise in [0, 1): hashed lattice every `scale` tiles, blended between. */
export function noise(seed: number, x: number, y: number, scale: number, salt: number): number {
  const gx = Math.floor(x / scale);
  const gy = Math.floor(y / scale);
  const fx = smooth(x / scale - gx);
  const fy = smooth(y / scale - gy);
  const c = (i: number, j: number) => roll(seed, gx + i, gy + j, salt);
  const top = c(0, 0) + (c(1, 0) - c(0, 0)) * fx;
  const bottom = c(0, 1) + (c(1, 1) - c(0, 1)) * fx;
  return top + (bottom - top) * fy;
}

// --- Per-tile features -------------------------------------------------------

export interface Layout {
  seed: number;
  size: number;
  start: Point;
  /** Rows (horizontal roads) and columns (vertical roads), two tiles wide. */
  roadRows: number[];
  roadCols: number[];
  /** The river runs along x (true) or y; its centre line wanders around `riverBase`. */
  riverAlongX: boolean;
  riverBase: number;
}

export function layout(seed: number, size: number): Layout {
  const start = { x: Math.floor(size / 2), y: Math.floor(size / 2) };
  // A road every 64 tiles, nudged by up to 8, offset so none crosses the start clearing.
  const lines = (salt: number) => {
    const out: number[] = [];
    for (let k = 0; 32 + 64 * k < size; k++) out.push(32 + 64 * k + (hash(seed, k, 0, salt) % 17) - 8);
    return out;
  };
  // The river passes well to one side of the start.
  const side = hash(seed, 0, 0, 3) % 2 === 0 ? -1 : 1;
  const riverBase = start.y + side * (48 + (hash(seed, 0, 0, 4) % 24));
  return { seed, size, start, roadRows: lines(1), roadCols: lines(2), riverAlongX: hash(seed, 0, 0, 5) % 2 === 0, riverBase };
}

function isRoad(l: Layout, x: number, y: number): boolean {
  return l.roadRows.some((r) => y === r || y === r + 1) || l.roadCols.some((c) => x === c || x === c + 1);
}

function isWater(l: Layout, x: number, y: number): boolean {
  if (nearStart(l, x, y, START_CLEAR_RADIUS + 8)) return false;
  // The river: a band 3-5 wide around a wandering centre line.
  const along = l.riverAlongX ? x : y;
  const across = l.riverAlongX ? y : x;
  const centre = l.riverBase + 28 * (noise(l.seed, along, 0, 40, 6) - 0.5);
  const width = 3 + 2 * noise(l.seed, along, 0, 23, 7);
  if (Math.abs(across - centre) < width / 2) return true;
  // Lakes: the high spots of a slow noise field.
  return noise(l.seed, x, y, 24, 8) > 0.8;
}

function isForest(l: Layout, x: number, y: number): boolean {
  return noise(l.seed, x, y, 20, 9) > 0.6;
}

function nearStart(l: Layout, x: number, y: number, r: number): boolean {
  return (x - l.start.x) ** 2 + (y - l.start.y) ** 2 < r * r;
}

/** How town-like an area is: denser towns near roads and in the high spots of a slow noise field. */
function urban(l: Layout, x: number, y: number): number {
  const toRoad = Math.min(...l.roadRows.map((r) => Math.abs(y - r)), ...l.roadCols.map((c) => Math.abs(x - c)));
  return noise(l.seed, x, y, 64, 10) * 0.7 + (toRoad < 12 ? 0.35 : 0);
}

// --- Generation --------------------------------------------------------------

const FOREST = GROUNDS.indexOf('forest');

const ground = (state: SimState, x: number, y: number, g: Ground) => {
  state.grid.ground[tileIndex(state.grid, x, y)] = GROUNDS.indexOf(g);
};

/** Terrain for every tile in the chunk, then the buildings in its cells, then trees. */
export function generateChunk(state: SimState, l: Layout, cx: number, cy: number): void {
  const x0 = cx * CHUNK;
  const y0 = cy * CHUNK;
  for (let y = y0; y < y0 + CHUNK; y++) {
    for (let x = x0; x < x0 + CHUNK; x++) {
      if (!inBounds(state.grid, x, y)) continue;
      if (isRoad(l, x, y)) ground(state, x, y, isWater(l, x, y) ? 'bridge' : 'road');
      else if (isWater(l, x, y)) setTile(state.grid, x, y, 'water');
      else if (isForest(l, x, y)) ground(state, x, y, 'forest');
      else if (noise(l.seed, x, y, 6, 11) > 0.72) ground(state, x, y, 'dirt');
    }
  }
  for (let y = y0; y < y0 + CHUNK; y += CELL) for (let x = x0; x < x0 + CHUNK; x += CELL) building(state, l, x, y);
  for (let y = y0; y < y0 + CHUNK; y++) {
    for (let x = x0; x < x0 + CHUNK; x++) {
      if (!inBounds(state.grid, x, y) || !isForest(l, x, y) || isRoad(l, x, y) || nearStart(l, x, y, START_CLEAR_RADIUS)) continue;
      const i = tileIndex(state.grid, x, y);
      // Still forest ground: not inside a building (those are dirt).
      if (state.grid.tiles[i] === 'floor' && state.grid.ground[i] === FOREST && roll(l.seed, x, y, 12) < 0.3) {
        addFurniture(state, 'tree', x, y);
      }
    }
  }
}

/** Maybe a ruined building in the cell at (cx0, cy0): walls with gaps, a door, crates inside. */
function building(state: SimState, l: Layout, cx0: number, cy0: number): void {
  const centre = { x: cx0 + CELL / 2, y: cy0 + CELL / 2 };
  if (urban(l, centre.x, centre.y) < 0.55 || roll(l.seed, cx0, cy0, 13) > 0.75) return;
  const w = 6 + (hash(l.seed, cx0, cy0, 14) % 8);
  const h = 6 + (hash(l.seed, cx0, cy0, 15) % 8);
  const x0 = cx0 + 1 + (hash(l.seed, cx0, cy0, 16) % (CELL - w - 1));
  const y0 = cy0 + 1 + (hash(l.seed, cx0, cy0, 17) % (CELL - h - 1));
  // The whole footprint (plus a ring) must be clear land, off roads and the start.
  for (let y = y0 - 1; y <= y0 + h; y++) {
    for (let x = x0 - 1; x <= x0 + w; x++) {
      if (!inBounds(state.grid, x, y) || isRoad(l, x, y) || isWater(l, x, y) || nearStart(l, x, y, START_CLEAR_RADIUS)) return;
    }
  }
  const x1 = x0 + w - 1;
  const y1 = y0 + h - 1;
  const doorSide = hash(l.seed, cx0, cy0, 18) % 4;
  const door = [
    { x: x0 + Math.floor(w / 2), y: y0 },
    { x: x1, y: y0 + Math.floor(h / 2) },
    { x: x0 + Math.floor(w / 2), y: y1 },
    { x: x0, y: y0 + Math.floor(h / 2) },
  ][doorSide]!;
  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) {
      ground(state, x, y, 'dirt');
      const edge = x === x0 || x === x1 || y === y0 || y === y1;
      if (!edge) continue;
      if (x === door.x && y === door.y) setTile(state.grid, x, y, 'door');
      // Ruined: about one wall tile in eight has fallen in.
      else if (roll(l.seed, x, y, 19) > 0.12) setTile(state.grid, x, y, 'wall');
    }
  }
  // Wider buildings get an inner wall with a gap, making two rooms.
  if (w >= 10) {
    const mx = x0 + Math.floor(w / 2);
    for (let y = y0 + 1; y < y1; y++) if (y !== y0 + 2) setTile(state.grid, mx, y, 'wall');
  }
  // Crates: better stocked the further from the start.
  const dist = Math.hypot(centre.x - l.start.x, centre.y - l.start.y);
  const crates = 1 + (hash(l.seed, cx0, cy0, 20) % 3);
  for (let n = 0; n < crates; n++) {
    const x = x0 + 1 + (hash(l.seed, cx0 + n, cy0, 21) % (w - 2));
    const y = y0 + 1 + (hash(l.seed, cx0, cy0 + n, 22) % (h - 2));
    if (state.grid.tiles[tileIndex(state.grid, x, y)] !== 'floor' || furnitureAt(state, x, y)) continue;
    addFurniture(state, 'crate', x, y).loot = lootFor(dist);
  }
  // Asleep inside: none near the start, more the further out.
  if (dist < START_CLEAR_RADIUS + 24) return;
  const sleepers = hash(l.seed, cx0, cy0, 23) % (2 + Math.floor(dist / 60));
  for (let n = 0; n < sleepers; n++) {
    const x = x0 + 1 + (hash(l.seed, cx0 + n, cy0, 24) % (w - 2));
    const y = y0 + 1 + (hash(l.seed, cx0, cy0 + n, 25) % (h - 2));
    state.dormant.push({ id: `d${state.nextId++}`, x, y });
  }
}

/** Finds in a crate this far from the start: CRATE_LOOT nearby, up to three times that at the far edge. */
export function lootFor(distance: number): number {
  return CRATE_LOOT - 2 + Math.min(2 * CRATE_LOOT, Math.floor(distance / 20));
}

/** The start: an intact hut with three beds, three survivors, and some wood and food. */
function startCamp(state: SimState, l: Layout): void {
  const { x: sx, y: sy } = l.start;
  const x0 = sx - 4;
  const y0 = sy - 7;
  for (let y = y0; y <= y0 + 5; y++) {
    for (let x = x0; x <= x0 + 7; x++) {
      ground(state, x, y, 'dirt');
      if (x === x0 || x === x0 + 7 || y === y0 || y === y0 + 5) setTile(state.grid, x, y, x === x0 + 3 && y === y0 + 5 ? 'door' : 'wall');
    }
  }
  for (const dx of [1, 3, 5]) addFurniture(state, 'bed', x0 + dx, y0 + 1);
  for (const dx of [-2, 0, 2]) {
    const s = addSurvivor(state, sx + dx, sy + 1);
    s.traits = rollTraits(s.id);
  }
  for (let i = 0; i < 12; i++) spawnItem(state, 'wood', sx - 6 + (i % 4), sy + 3 + Math.floor(i / 4));
  for (let i = 0; i < 10; i++) spawnItem(state, 'food', sx + 3 + (i % 4), sy + 3 + Math.floor(i / 4));
  // A rain collector with some water in it, so nobody has to find the river on day one.
  const c = nearestTile(state.grid, { x: sx + 5, y: sy + 1 }, (x, y) => getTile(state.grid, x, y) === 'floor' && !furnitureAt(state, x, y) && !itemAt(state, x, y));
  if (c) addFurniture(state, 'collector', c.x, c.y).loot = START_WATER;
}

/** Water in the start camp's rain collector. */
export const START_WATER = 12;

/**
 * Generates any missing chunk within GENERATE_RADIUS of a point. New terrain can change
 * walkability, so this bumps the version and moves anything left standing in a wall.
 */
export function ensureGeneratedAround(state: SimState, p: Point): void {
  const world = state.world;
  if (!world) return;
  const pcx = Math.floor(p.x / CHUNK);
  const pcy = Math.floor(p.y / CHUNK);
  let l: Layout | null = null;
  for (let cy = Math.max(0, pcy - GENERATE_RADIUS); cy <= Math.min(world.chunks - 1, pcy + GENERATE_RADIUS); cy++) {
    for (let cx = Math.max(0, pcx - GENERATE_RADIUS); cx <= Math.min(world.chunks - 1, pcx + GENERATE_RADIUS); cx++) {
      const i = cy * world.chunks + cx;
      if (world.generated[i] === 1) continue;
      world.generated[i] = 1;
      l ??= layout(world.seed, world.size);
      generateChunk(state, l, cx, cy);
      settle(state, cx, cy);
      state.walkabilityVersion += 1;
    }
  }
}

/** Anything in a freshly generated chunk that ended up on a wall or water steps to the nearest open tile. */
function settle(state: SimState, cx: number, cy: number): void {
  const inChunk = (q: Point) => Math.floor(q.x / CHUNK) === cx && Math.floor(q.y / CHUNK) === cy;
  const open = (x: number, y: number) => isWalkable(state.grid, x, y);
  const floorOnly = (x: number, y: number) => state.grid.tiles[tileIndex(state.grid, x, y)] === 'floor';
  for (const z of state.zombies) {
    if (!inChunk(z.pos) || floorOnly(z.pos.x, z.pos.y)) continue;
    z.pos = nearestAcross(state, z.pos, floorOnly) ?? z.pos;
    z.move = null;
    z.path = [];
  }
  for (const s of state.survivors) {
    if (!inChunk(s.pos) || open(s.pos.x, s.pos.y)) continue;
    s.pos = nearestAcross(state, s.pos, open) ?? s.pos;
    s.move = null;
    s.path = [];
    s.pathVersion = -1;
  }
}

/** The nearest accepted tile in growing square rings, across anything (nearestTile only spreads over walkable ground). */
function nearestAcross(state: SimState, p: Point, accept: (x: number, y: number) => boolean): Point | null {
  for (let r = 1; r <= CHUNK; r++) {
    for (let y = p.y - r; y <= p.y + r; y++) {
      for (let x = p.x - r; x <= p.x + r; x++) {
        if (Math.max(Math.abs(x - p.x), Math.abs(y - p.y)) === r && inBounds(state.grid, x, y) && accept(x, y)) return { x, y };
      }
    }
  }
  return null;
}

/** A fresh world: the start area's chunks, the start camp, and fog over everything not near it. */
export function createWorld(seed: number, size = WORLD_SIZE): SimState {
  const state = createState({ seed, width: size, height: size });
  const l = layout(seed, size);
  const chunks = Math.ceil(size / CHUNK);
  state.world = { seed, size, chunks, generated: new Array<number>(chunks * chunks).fill(0) };
  ensureGeneratedAround(state, l.start);
  startCamp(state, l);
  state.harsh = true;
  state.grid.explored.fill(0);
  for (let y = l.start.y - START_REVEAL_RADIUS; y <= l.start.y + START_REVEAL_RADIUS; y++) {
    for (let x = l.start.x - START_REVEAL_RADIUS; x <= l.start.x + START_REVEAL_RADIUS; x++) {
      if (inBounds(state.grid, x, y) && nearStart(l, x, y, START_REVEAL_RADIUS + 1)) state.grid.explored[tileIndex(state.grid, x, y)] = 1;
    }
  }
  return state;
}
