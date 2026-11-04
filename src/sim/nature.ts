// The natural world, generated first: per-tile functions of the seed for water (sea, island
// channels, a river, lakes) and forest. Towns, roads and lots (worldplan.ts) are laid out over it.
// Everything here is a pure function of (seed, position), from a stateless hash, never the sim RNG.

import type { MapType } from './difficulty';
import type { Point } from './grid';

/** Kept clear around the start, so the first day is calm. */
export const START_CLEAR_RADIUS = 16;

/** A well-mixed 32-bit hash of the seed and up to three integers. */
export function hash(seed: number, a: number, b: number, salt: number): number {
  let h = Math.imul(seed ^ 0x9e3779b9, 0x85ebca6b) ^ Math.imul(a, 0xc2b2ae35) ^ Math.imul(b, 0x27d4eb2f) ^ Math.imul(salt, 0x165667b1);
  h = Math.imul(h ^ (h >>> 15), 0x2c1b3c6d);
  h = Math.imul(h ^ (h >>> 12), 0x297a2d39);
  return (h ^ (h >>> 15)) >>> 0;
}

/** Uniform in [0, 1). */
export function roll(seed: number, a: number, b: number, salt: number): number {
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

export interface Nature {
  seed: number;
  size: number;
  map: MapType;
  start: Point;
  /** The river runs along x (true) or y; its centre line wanders around `riverBase`. */
  riverAlongX: boolean;
  riverBase: number;
}

export function natureFor(seed: number, size: number, map: MapType): Nature {
  const start = { x: Math.floor(size / 2), y: Math.floor(size / 2) };
  // The river passes well to one side of the start.
  const side = hash(seed, 0, 0, 3) % 2 === 0 ? -1 : 1;
  const riverBase = start.y + side * (48 + (hash(seed, 0, 0, 4) % 24));
  return { seed, size, map, start, riverAlongX: hash(seed, 0, 0, 5) % 2 === 0, riverBase };
}

export function nearStart(n: Nature, x: number, y: number, r: number): boolean {
  return (x - n.start.x) ** 2 + (y - n.start.y) ** 2 < r * r;
}

/**
 * Open sea round an island or a chain (no bridges reach out into it): beyond a coast that
 * wanders around 40% of the map's width from the middle.
 */
export function isSea(n: Nature, x: number, y: number): boolean {
  if (n.map !== 'island' && n.map !== 'islands') return false;
  if (nearStart(n, x, y, START_CLEAR_RADIUS + 8)) return false;
  const r = Math.sqrt((x - n.size / 2) ** 2 + (y - n.size / 2) ** 2) / (n.size / 2);
  return r > 0.78 + 0.18 * (noise(n.seed, x, y, 18, 30) - 0.5);
}

/** An island chain: channels between the islands, which roads cross on bridges. */
export function isChannel(n: Nature, x: number, y: number): boolean {
  return n.map === 'islands' && Math.abs(noise(n.seed, x, y, 40, 31) - 0.5) < 0.045;
}

/** The river's distance from (x, y), across it, in tiles (negative never). */
export function riverDistance(n: Nature, x: number, y: number): number {
  const along = n.riverAlongX ? x : y;
  const across = n.riverAlongX ? y : x;
  const centre = n.riverBase + 28 * (noise(n.seed, along, 0, 40, 6) - 0.5);
  const width = 3 + 2 * noise(n.seed, along, 0, 23, 7);
  return Math.max(0, Math.abs(across - centre) - width / 2);
}

export function isWater(n: Nature, x: number, y: number): boolean {
  if (nearStart(n, x, y, START_CLEAR_RADIUS + 8)) return false;
  if (isSea(n, x, y) || isChannel(n, x, y)) return true;
  if (riverDistance(n, x, y) === 0) return true; // the river: a band 3-5 wide round a wandering line
  return noise(n.seed, x, y, 24, 8) > 0.8; // lakes: the high spots of a slow noise field
}

/** How wooded the land is here, 0..1: forest where it's over FOREST_AT. */
export function woodedness(n: Nature, x: number, y: number): number {
  return noise(n.seed, x, y, 20, 9);
}

export const FOREST_AT = 0.6;

export function isForest(n: Nature, x: number, y: number): boolean {
  return woodedness(n, x, y) > FOREST_AT;
}

/** A quarantine zone's fence ring, two tiles in from the edge. */
export function onFenceLine(n: Nature, x: number, y: number): boolean {
  if (n.map !== 'quarantine') return false;
  const s = n.size;
  return (x === 2 || y === 2 || x === s - 3 || y === s - 3) && x >= 2 && y >= 2 && x <= s - 3 && y <= s - 3;
}
