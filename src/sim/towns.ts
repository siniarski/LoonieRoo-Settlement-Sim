// Towns: where they are, their districts, and their streets. Pure functions of the seed and the
// main roads, so any chunk can ask without the others (worldgen.ts generates chunk by chunk).
//
// The map is cut into blocks (a block is a chunk, BLOCK tiles a side). Main roads run along
// block edges, so they double as town streets.
//
//   towns     at main-road crossings: about half of them, spaced apart, and always the crossing nearest the
//             start. A crossing is the corner of four blocks: the town's core.
//   size      a small town is its core (2 x 2 blocks, 64 x 64 tiles): one civic block, one of
//             shops, two of houses. A big one (on big maps only) adds a ring of blocks round the
//             core: the core all civic and shops, the ring houses with some industry.
//   districts centre (civic: police, clinic, church, school, offices), commercial (shops),
//             residential (houses), industrial (warehouses, factories), then a ring of farmland
//             (fields and farmsteads, with a ragged edge) round each town, and wild country beyond.
//   streets   two tiles wide round every town block, on the block edges, so every lot faces one.

import { hashRoll } from './hash';
import type { Point } from './grid';

/** A block is a chunk: buildings never cross chunk borders (a test checks CHUNK === BLOCK). */
export const BLOCK = 32;
export const STREET = 2;

export type District = 'centre' | 'commercial' | 'residential' | 'industrial' | 'farmland' | 'wild';

export interface Town {
  /** The block just below and right of the crossing (the crossing is its top-left corner). */
  bx: number;
  by: number;
  /** Rings of houses round the core: 0 (a small town) or 1. */
  rings: number;
}

export const TOWN_CHANCE = 0.6;
/** Big towns only on maps at least this big, and this often. */
export const BIG_TOWN_MAP = 256;
export const BIG_TOWN_CHANCE = 0.4;

/**
 * Towns at main-road crossings (roads lie on block edges), with country between them: no two
 * within TOWN_GAP crossings of each other (one more for a big town), so they never run together
 * into one sprawl. The crossing nearest the start always has one; the rest are tried in an
 * order set by the seed.
 */
export function planTowns(seed: number, size: number, roadRows: number[], roadCols: number[], start: Point): Town[] {
  const crossings: { c: number; r: number; ci: number; ri: number }[] = [];
  roadRows.forEach((r, ri) => roadCols.forEach((c, ci) => crossings.push({ c, r, ci, ri })));
  if (crossings.length === 0) return [];
  const near = (p: { c: number; r: number }) => Math.abs(p.c - start.x) + Math.abs(p.r - start.y);
  const first = crossings.reduce((a, b) => (near(b) < near(a) ? b : a));
  const rest = crossings.filter((p) => p !== first && hashRoll(seed, p.c, p.r, 0x7100) < TOWN_CHANCE);
  rest.sort((a, b) => hashRoll(seed, a.c, a.r, 0x7103) - hashRoll(seed, b.c, b.r, 0x7103));
  const chosen: { ci: number; ri: number; rings: number }[] = [];
  const towns: Town[] = [];
  for (const p of [first, ...rest]) {
    const rings = size >= BIG_TOWN_MAP && hashRoll(seed, p.c, p.r, 0x7101) < BIG_TOWN_CHANCE ? 1 : 0;
    const crowded = chosen.some((o) => Math.max(Math.abs(o.ci - p.ci), Math.abs(o.ri - p.ri)) < TOWN_GAP + Math.max(rings, o.rings));
    if (crowded) continue;
    chosen.push({ ci: p.ci, ri: p.ri, rings });
    towns.push({ bx: Math.floor(p.c / BLOCK), by: Math.floor(p.r / BLOCK), rings });
  }
  return towns;
}

/** Crossings (64 tiles apart) between towns: 2 leaves at least a block of farmland between two small ones. */
export const TOWN_GAP = 2;

const RANK: Record<District, number> = { centre: 0, commercial: 1, industrial: 2, residential: 3, farmland: 4, wild: 5 };

/** The district of block (bx, by): the most built-up any town makes it. */
export function districtOf(seed: number, towns: Town[], bx: number, by: number): District {
  let best: District = 'wild';
  for (const t of towns) {
    const d = districtFor(seed, t, bx, by);
    if (RANK[d] < RANK[best]) best = d;
  }
  return best;
}

function districtFor(seed: number, t: Town, bx: number, by: number): District {
  // Blocks from the core (t.bx - 1 .. t.bx, t.by - 1 .. t.by): 0 in it, 1 in the ring round it.
  const off = (v: number, core: number) => (v < core - 1 ? core - 1 - v : v > core ? v - core : 0);
  const ring = Math.max(off(bx, t.bx), off(by, t.by));
  if (ring === 0) {
    if (bx === t.bx && by === t.by) return 'centre';
    if (t.rings > 0) return 'commercial';
    return bx === t.bx - 1 && by === t.by - 1 ? 'commercial' : 'residential';
  }
  if (ring <= t.rings) return hashRoll(seed, bx, by, 0x7102) < 0.25 ? 'industrial' : 'residential';
  // Farmland round it with a ragged edge: most of the next ring, some of the one after.
  const farmed = hashRoll(seed, bx, by, 0x7104);
  if (ring === t.rings + 1) return farmed < 0.8 ? 'farmland' : 'wild';
  if (ring === t.rings + 2) return farmed < 0.3 ? 'farmland' : 'wild';
  return 'wild';
}

export function isTownDistrict(d: District): boolean {
  return d === 'centre' || d === 'commercial' || d === 'residential' || d === 'industrial';
}

/** A town street: the two tiles along a block edge, where the block on either side is in town. */
export function isStreet(seed: number, towns: Town[], x: number, y: number): boolean {
  const bx = Math.floor(x / BLOCK);
  const by = Math.floor(y / BLOCK);
  const town = (ax: number, ay: number) => ax >= 0 && ay >= 0 && isTownDistrict(districtOf(seed, towns, ax, ay));
  if (x - bx * BLOCK < STREET && (town(bx, by) || town(bx - 1, by))) return true;
  if (y - by * BLOCK < STREET && (town(bx, by) || town(bx, by - 1))) return true;
  return false;
}
