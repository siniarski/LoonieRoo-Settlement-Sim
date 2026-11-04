// Towns: where they are, their districts, and their streets. Pure functions of the seed and the
// main roads, so any chunk can ask without the others (worldgen.ts generates chunk by chunk).
//
// The map is cut into blocks (a block is a chunk, BLOCK tiles a side). Main roads run along
// block edges, so they double as town streets.
//
//   towns     at main-road crossings: about a third of them, and always the crossing nearest the
//             start. A crossing is the corner of four blocks: the town's core.
//   size      a small town is its core (2 x 2 blocks, 64 x 64 tiles): one civic block, one of
//             shops, two of houses. A big one (on big maps only) adds a ring of blocks round the
//             core: the core all civic and shops, the ring houses with some industry.
//   districts centre (civic: police, clinic, church, school, offices), commercial (shops),
//             residential (houses), industrial (warehouses, factories), then a ring of farmland
//             (fields and farmsteads) round each town, and wild country beyond.
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

export const TOWN_CHANCE = 0.35;
/** Big towns only on maps at least this big, and this often. */
export const BIG_TOWN_MAP = 256;
export const BIG_TOWN_CHANCE = 0.4;

/** Towns at main-road crossings (roads lie on block edges). */
export function planTowns(seed: number, size: number, roadRows: number[], roadCols: number[], start: Point): Town[] {
  const towns: Town[] = [];
  let nearest: Town | null = null;
  let nearestD = Infinity;
  for (const r of roadRows) {
    for (const c of roadCols) {
      const big = size >= BIG_TOWN_MAP && hashRoll(seed, c, r, 0x7101) < BIG_TOWN_CHANCE;
      const t: Town = { bx: Math.floor(c / BLOCK), by: Math.floor(r / BLOCK), rings: big ? 1 : 0 };
      const d = Math.abs(c - start.x) + Math.abs(r - start.y);
      if (d < nearestD) {
        nearestD = d;
        nearest = t;
      }
      if (hashRoll(seed, c, r, 0x7100) < TOWN_CHANCE) towns.push(t);
    }
  }
  if (nearest && !towns.includes(nearest)) towns.push(nearest);
  return towns;
}

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
  if (ring === t.rings + 1) return 'farmland';
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
