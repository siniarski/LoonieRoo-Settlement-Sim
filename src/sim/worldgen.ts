// World generation: the natural world first (nature.ts: water, forest), then what was built on
// it (worldplan.ts: towns sited on good land with streets they grew, country roads routed between
// them, lots cut along the streets and roads, a building planned in each lot), drawn chunk by chunk.
//
//  - Everything is a pure function of the seed and world position (stateless hashes, never the
//    sim RNG), and the plan is derived data, cached, never saved: so a chunk comes out the same
//    whatever order chunks are generated in.
//  - Generation runs chunk by chunk (CHUNK x CHUNK tiles): each chunk draws its tiles and its part
//    of every lot touching it (a lot may span chunks).
//  - Chunks are generated lazily: the start area when the world is made, then any chunk within
//    GENERATE_RADIUS chunks of a survivor, well ahead of what they can see (ensureGeneratedAround,
//    called from fog.ts). (Entity ids come from state.nextId, so they do depend on generation
//    order; ids are labels, nothing else.)

import { NORMAL, type MapType, type Settings } from './difficulty';
import { rollTimeline } from './timeline';
import { addFurniture, CRATE_LOOT, furnitureAt } from './furniture';
import { GROUNDS, getTile, inBounds, isWalkable, setTile, tileIndex, type Ground, type Point } from './grid';
import { itemAt, spawnItem } from './items';
import { nearestTile } from './space';
import { placeSettlements } from './settlements';
import { hashRoll } from './hash';
import { createState, type SimState } from './state';
import type { Town } from './towns';
import { drawLot, type LotContext } from './lots';
import { planWorld, type WorldPlan } from './worldplan';
import { isForest, isSea, isWater, nearStart, noise, onFenceLine, roll, START_CLEAR_RADIUS, type Nature } from './nature';
export { hash, noise, isSea, START_CLEAR_RADIUS } from './nature';
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
/** Tiles revealed around the start before anyone moves. */
export const START_REVEAL_RADIUS = 14;

// --- The plan, and per-tile features ------------------------------------------

/** A world's natural features and its plan (towns, roads, lots): what a chunk is drawn from. */
export type Layout = Nature & { plan: WorldPlan; towns: Town[] };

export function layout(seed: number, size: number, map: MapType = 'mainland'): Layout {
  const plan = planWorld(seed, size, map);
  return { ...plan.nature, plan, towns: plan.towns };
}

/** A country road or town street on this tile. */
function isRoad(l: Layout, x: number, y: number): boolean {
  return x >= 0 && y >= 0 && x < l.size && y < l.size && l.plan.roadMask[y * l.size + x] !== 0;
}

/** On a lot (its yard and building are drawn by lots.ts). */
function onLot(l: Layout, x: number, y: number): boolean {
  return l.plan.lotMask[y * l.size + x] !== 0;
}

/** A quarantine zone: a fence two tiles in from the edge, a gate where a road crosses it. */
export function fenceAt(l: Layout, x: number, y: number): 'wall' | 'door' | null {
  if (!onFenceLine(l, x, y)) return null;
  return isRoad(l, x, y) ? 'door' : 'wall';
}

// --- Generation --------------------------------------------------------------

const FOREST = GROUNDS.indexOf('forest');

const ground = (state: SimState, x: number, y: number, g: Ground) => {
  state.grid.ground[tileIndex(state.grid, x, y)] = GROUNDS.indexOf(g);
};

/** Terrain for every tile in the chunk, then its part of every lot touching it, then trees. */
export function generateChunk(state: SimState, l: Layout, cx: number, cy: number): void {
  const x0 = cx * CHUNK;
  const y0 = cy * CHUNK;
  for (let y = y0; y < y0 + CHUNK; y++) {
    for (let x = x0; x < x0 + CHUNK; x++) {
      if (!inBounds(state.grid, x, y)) continue;
      const fence = fenceAt(l, x, y);
      if (fence) setTile(state.grid, x, y, fence);
      else if (isSea(l, x, y) && !isRoad(l, x, y)) setTile(state.grid, x, y, 'water');
      else if (isRoad(l, x, y)) ground(state, x, y, isWater(l, x, y) ? 'bridge' : 'road');
      else if (isWater(l, x, y)) setTile(state.grid, x, y, 'water');
      else if (onLot(l, x, y)) continue;
      else if (isForest(l, x, y)) ground(state, x, y, 'forest');
      else if (noise(l.seed, x, y, 6, 11) > 0.72) ground(state, x, y, 'dirt');
    }
  }
  const chunks = Math.ceil(l.size / CHUNK);
  const ctx = lotContext(l);
  for (const id of l.plan.lotsByChunk[cy * chunks + cx] ?? []) drawLot(state, ctx, l.plan.lots[id]!, x0, y0, Math.min(l.size, x0 + CHUNK) - 1, Math.min(l.size, y0 + CHUNK) - 1);
  for (let y = y0; y < y0 + CHUNK; y++) {
    for (let x = x0; x < x0 + CHUNK; x++) {
      // Trees on forest ground (lots plant their own: hedges, orchards).
      if (!inBounds(state.grid, x, y) || isRoad(l, x, y) || onLot(l, x, y) || nearStart(l, x, y, START_CLEAR_RADIUS)) continue;
      const i = tileIndex(state.grid, x, y);
      if (state.grid.tiles[i] === 'floor' && state.grid.ground[i] === FOREST && roll(l.seed, x, y, 12) < 0.3) {
        addFurniture(state, 'tree', x, y);
      }
    }
  }
}

/** What drawing a lot needs to know about the world. */
function lotContext(l: Layout): LotContext {
  return {
    seed: l.seed,
    lootAt: (x, y) => lootFor(Math.sqrt((x - l.start.x) ** 2 + (y - l.start.y) ** 2)),
    distance: (x, y) => Math.sqrt((x - l.start.x) ** 2 + (y - l.start.y) ** 2),
    calm: START_CLEAR_RADIUS + 24,
  };
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
  // Alone: one bed, one person (under your control from the start), and less to go on.
  const solo = state.settings.start === 'solo';
  for (const dx of solo ? [3] : [1, 3, 5]) addFurniture(state, 'bed', x0 + dx, y0 + 1);
  for (const dx of solo ? [0] : [-2, 0, 2]) {
    const s = addSurvivor(state, sx + dx, sy + 1);
    s.traits = rollTraits(s.id);
  }
  if (solo) {
    const you = state.survivors[0]!;
    state.control = { survivorId: you.id, dx: 0, dy: 0, facing: { x: 0, y: 1 }, nextSwing: 0, busy: null };
    // What you set out with: something to swing, and a bandage.
    const weapon = spawnItem(state, 'weapon', you.pos.x, you.pos.y);
    weapon.pos = null;
    weapon.carriedBy = you.id;
    you.weapon = weapon.id;
    spawnItem(state, 'bandage', you.pos.x, you.pos.y);
  }
  for (let i = 0; i < (solo ? 4 : 12); i++) spawnItem(state, 'wood', sx - 6 + (i % 4), sy + 3 + Math.floor(i / 4));
  for (let i = 0; i < (solo ? 4 : 10); i++) spawnItem(state, 'food', sx + 3 + (i % 4), sy + 3 + Math.floor(i / 4));
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
      l ??= layout(world.seed, world.size, state.settings.map);
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
export function createWorld(seed: number, size = WORLD_SIZE, settings: Settings = NORMAL): SimState {
  const state = createState({ seed, width: size, height: size });
  state.settings = { ...settings };
  const l = layout(seed, size, settings.map);
  const chunks = Math.ceil(size / CHUNK);
  state.world = { seed, size, chunks, generated: new Array<number>(chunks * chunks).fill(0) };
  ensureGeneratedAround(state, l.start);
  startCamp(state, l);
  state.harsh = true;
  rollTimeline(state, seed);
  placeSettlements(state, l.start, seed);
  // A truck somewhere out past the camp (within the chunks generated already).
  const angle = hashRoll(seed, 0x7a0c) * Math.PI * 2;
  const near = { x: Math.round(l.start.x + Math.cos(angle) * 35), y: Math.round(l.start.y + Math.sin(angle) * 35) };
  const clamped = { x: Math.max(1, Math.min(size - 2, near.x)), y: Math.max(1, Math.min(size - 2, near.y)) };
  const spot = nearestTile(state.grid, clamped, (x, y) => getTile(state.grid, x, y) === 'floor' && !furnitureAt(state, x, y) && !itemAt(state, x, y));
  if (spot) addFurniture(state, 'truck', spot.x, spot.y);
  state.grid.explored.fill(0);
  for (let y = l.start.y - START_REVEAL_RADIUS; y <= l.start.y + START_REVEAL_RADIUS; y++) {
    for (let x = l.start.x - START_REVEAL_RADIUS; x <= l.start.x + START_REVEAL_RADIUS; x++) {
      if (inBounds(state.grid, x, y) && nearStart(l, x, y, START_REVEAL_RADIUS + 1)) state.grid.explored[tileIndex(state.grid, x, y)] = 1;
    }
  }
  return state;
}
