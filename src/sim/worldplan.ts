// The world plan: everything built on the land, laid out once per world from the seed, in order:
//   1. nature (nature.ts): water and forest, per tile
//   2. towns sited on suitable land, each growing its streets (towns.ts)
//   3. country roads routed between towns, to the start, and out to the map's edge (roads.ts)
//   4. lots cut along the streets by zone (civic, shops, houses, industry), farms along the
//      country roads near towns, the odd cabin out in the wild
//   5. a building planned inside each lot (lots.ts)
// The plan is plain data and derived (never saved): planWorld caches it by seed, size and map.
// Chunks draw their part of it (worldgen.ts), so they come out the same in any order.

import type { MapType } from './difficulty';
import type { Point } from './grid';
import { isWater, nearStart, onFenceLine, roll, START_CLEAR_RADIUS, natureFor, type Nature } from './nature';
import { costGrid, drawLine, network, reuse, route } from './roads';
import { growStreets, landGrid, siteTowns, STREET, type Street, type Town } from './towns';
import type { Facing } from './lots';

export type Zone = 'civic' | 'commercial' | 'residential' | 'industrial' | 'farm' | 'field' | 'cabin';

export interface Lot {
  id: number;
  x: number;
  y: number;
  w: number;
  h: number;
  /** Which way the lot faces: towards its street or road (a building's front goes that way). */
  facing: Facing;
  zone: Zone;
  town: number | null;
}

export interface WorldPlan {
  nature: Nature;
  towns: Town[];
  streets: Street[];
  /** Country roads' centre lines. */
  roads: Point[][];
  /** Per tile: 0 nothing, 1 a country road, 2 a town street. */
  roadMask: Uint8Array;
  lots: Lot[];
  /** Per tile: the lot's id + 1, or 0. */
  lotMask: Int32Array;
  /** Lot ids touching each chunk (chunk index = cy * chunks + cx). */
  lotsByChunk: number[][];
}

/** A chunk's size, for the per-chunk index (a literal, not worldgen's CHUNK: see CLAUDE.md rule 12; a test checks they match). */
export const PLAN_CHUNK = 32;

const cache = new Map<string, WorldPlan>();

export function planWorld(seed: number, size: number, map: MapType): WorldPlan {
  const key = `${seed}|${size}|${map}`;
  const hit = cache.get(key);
  if (hit) return hit;
  const plan = makePlan(natureFor(seed, size, map));
  if (cache.size > 6) cache.delete(cache.keys().next().value!);
  cache.set(key, plan);
  return plan;
}

function makePlan(n: Nature): WorldPlan {
  const size = n.size;
  const land = landGrid(n);
  const towns = siteTowns(n, land);
  const roadMask = new Uint8Array(size * size);
  const streets = towns.flatMap((t, i) => growStreets(n, t, i));
  for (const s of streets) {
    for (let y = s.y0; y <= s.y1 + (s.eastWest ? STREET - 1 : 0); y++) {
      for (let x = s.x0; x <= s.x1 + (s.eastWest ? 0 : STREET - 1); x++) if (x >= 0 && y >= 0 && x < size && y < size) roadMask[y * size + x] = 2;
    }
  }
  const roads = layRoads(n, towns, streets, roadMask);
  const lots: Lot[] = [];
  const lotMask = new Int32Array(size * size);
  const add = (lot: Omit<Lot, 'id'>) => {
    for (let y = lot.y; y < lot.y + lot.h; y++) {
      for (let x = lot.x; x < lot.x + lot.w; x++) {
        if (x < 1 || y < 1 || x >= size - 1 || y >= size - 1) return false;
        const i = y * size + x;
        if (roadMask[i] !== 0 || lotMask[i] !== 0 || isWater(n, x, y) || onFenceLine(n, x, y) || nearStart(n, x, y, START_CLEAR_RADIUS + 2)) return false;
      }
    }
    const id = lots.length;
    lots.push({ id, ...lot });
    for (let y = lot.y; y < lot.y + lot.h; y++) for (let x = lot.x; x < lot.x + lot.w; x++) lotMask[y * size + x] = id + 1;
    return true;
  };
  townLots(n, towns, streets, add);
  farmLots(n, towns, roads, add);
  fieldLots(n, towns, add);
  cabinLots(n, roads, add);
  const chunks = Math.ceil(size / PLAN_CHUNK);
  const lotsByChunk: number[][] = Array.from({ length: chunks * chunks }, () => []);
  for (const l of lots) {
    for (let cy = Math.floor(l.y / PLAN_CHUNK); cy <= Math.floor((l.y + l.h - 1) / PLAN_CHUNK); cy++) {
      for (let cx = Math.floor(l.x / PLAN_CHUNK); cx <= Math.floor((l.x + l.w - 1) / PLAN_CHUNK); cx++) lotsByChunk[cy * chunks + cx]!.push(l.id);
    }
  }
  return { nature: n, towns, streets, roads, roadMask, lots, lotMask, lotsByChunk };
}

const dist = (ax: number, ay: number, bx: number, by: number) => Math.sqrt((ax - bx) ** 2 + (ay - by) ** 2);

/** Roads between towns (and the start), and a few out to the map's edge. */
function layRoads(n: Nature, towns: Town[], streets: Street[], mask: Uint8Array): Point[][] {
  const size = n.size;
  // Each town meets the roads at its street ends; the start at the edge of its clearing.
  const ends = (ti: number): Point[] =>
    streets.filter((s) => s.town === ti && s.main).flatMap((s) => (s.eastWest ? [{ x: s.x0, y: s.y0 }, { x: s.x1, y: s.y0 }] : [{ x: s.x0, y: s.y0 }, { x: s.x0, y: s.y1 }]));
  const nodes: Point[] = [...towns.map((t) => ({ x: t.x, y: t.y })), { x: n.start.x, y: n.start.y + 8 }];
  const nearestEnd = (node: number, toward: Point): Point => {
    if (node >= towns.length) return nodes[node]!;
    const options = ends(node);
    if (options.length === 0) return nodes[node]!;
    return options.reduce((a, b) => (dist(b.x, b.y, toward.x, toward.y) < dist(a.x, a.y, toward.x, toward.y) ? b : a));
  };
  const links: [Point, Point][] = network(n.seed, nodes).map(([a, b]) => [nearestEnd(a, nodes[b]!), nearestEnd(b, nodes[a]!)]);
  // Roads out: from some towns to the nearest edge of the map (or on a quarantine map, to a gate).
  towns.forEach((t, i) => {
    // On a quarantine map the first town always has one (to a gate in the fence).
    if (!(n.map === 'quarantine' && i === 0) && roll(n.seed, t.x, t.y, 0x7301) > (n.map === 'quarantine' ? 0.8 : 0.45)) return;
    const edges: Point[] = [{ x: 0, y: t.y }, { x: size - 1, y: t.y }, { x: t.x, y: 0 }, { x: t.x, y: size - 1 }];
    const edge = edges.reduce((a, b) => (dist(b.x, b.y, t.x, t.y) < dist(a.x, a.y, t.x, t.y) ? b : a));
    links.push([nearestEnd(i, edge), edge]);
  });
  const cost = costGrid(n, towns);
  const roads: Point[][] = [];
  for (const [a, b] of links) {
    const line = route(n, cost, a, b);
    if (!line) continue;
    roads.push(line);
    reuse(cost, size, line);
    drawLine(mask, size, line, 1);
  }
  return roads;
}

type Add = (lot: Omit<Lot, 'id'>) => boolean;

const SIZES: Record<'civic' | 'commercial' | 'residential' | 'industrial', [number, number, number, number]> = {
  // frontage min, max, depth min, max
  civic: [20, 26, 18, 24],
  commercial: [12, 15, 12, 14],
  residential: [12, 15, 13, 15],
  industrial: [22, 28, 18, 24],
};

/** Lots along both sides of every street, zoned by how far they are from the town's centre. */
function townLots(n: Nature, towns: Town[], streets: Street[], add: Add): void {
  const civic = towns.map(() => 0);
  const CIVIC_MAX = { village: 1, town: 2, city: 4 } as const;
  // Main streets first: the centre's lots go to them.
  const ordered = [...streets].sort((a, b) => Number(b.main) - Number(a.main));
  ordered.forEach((s, si) => {
    const t = towns[s.town]!;
    for (const side of [-1, 1] as const) {
      let p = s.eastWest ? s.x0 : s.y0;
      const end = s.eastWest ? s.x1 + 1 : s.y1 + 1;
      let k = 0;
      while (p < end - 8) {
        const r = (j: number) => roll(n.seed, si * 31 + side, p, 0x7400 + j + k * 7);
        // Zone by how far the lot's middle is from the centre: civic and shops in the middle
        // (civic on the main streets), houses further out, some industry at the edge.
        const mid = p + 9;
        const [mx, my] = s.eastWest ? [mid, s.y0 + side * 9] : [s.x0 + side * 9, mid];
        const share = dist(mx, my, t.x, t.y) / t.radius;
        let zone: keyof typeof SIZES = 'residential';
        if (share < 0.45 && s.main && civic[s.town]! < CIVIC_MAX[t.kind] && r(4) < 0.6) zone = 'civic';
        else if (share < (t.kind === 'village' ? 0.4 : 0.6)) zone = 'commercial';
        else if (t.kind !== 'village' && share > 0.6 && r(0) < 0.2) zone = 'industrial';
        const [f0, f1, d0, d1] = SIZES[zone];
        const front = f0 + Math.floor(r(1) * (f1 - f0 + 1));
        const deep = d0 + Math.floor(r(2) * (d1 - d0 + 1));
        const front2 = Math.min(front, end - p);
        if (front2 < f0) break;
        // Beside the street, a tile's verge between. If it doesn't fit, a narrower, shallower one
        // (a house's size) before moving along.
        const lotAt = (w: number, d: number) =>
          s.eastWest
            ? { x: p, y: side < 0 ? s.y0 - 1 - d : s.y0 + STREET + 1, w, h: d, facing: (side < 0 ? 'south' : 'north') as Facing }
            : { x: side < 0 ? s.x0 - 1 - d : s.x0 + STREET + 1, y: p, w: d, h: w, facing: (side < 0 ? 'east' : 'west') as Facing };
        const placed = add({ ...lotAt(front2, deep), zone, town: s.town }) ? front2 : zone !== 'civic' && zone !== 'industrial' && add({ ...lotAt(12, 12), zone, town: s.town }) ? 12 : 0;
        if (placed > 0) {
          if (zone === 'civic' && placed === front2) civic[s.town]!++;
          p += placed + (r(3) < 0.5 ? 1 : 0);
        } else p += 2;
        k++;
      }
    }
  });
}

/**
 * Along the country roads round each town: houses where the town spills out along its roads,
 * then farms (a farmstead and its fields, or fields alone) further out.
 */
function farmLots(n: Nature, towns: Town[], roads: Point[][], add: Add): void {
  roads.forEach((line, ri) => {
    let since = 99;
    for (let i = 1; i < line.length; i++) {
      const a = line[i - 1]!;
      const b = line[i]!;
      since += dist(a.x, a.y, b.x, b.y);
      const edge = towns.find((t) => {
        const d = dist(b.x, b.y, t.x, t.y);
        return d > t.radius * 0.8 && d < t.radius * 1.35 + 6;
      });
      if (edge && since >= 15) {
        const r = (j: number) => roll(n.seed, ri, i, 0x7480 + j);
        const along = Math.abs(b.x - a.x) >= Math.abs(b.y - a.y);
        const cx = Math.round(b.x);
        const cy = Math.round(b.y);
        for (const side of [-1, 1]) {
          if (r(side + 2) > 0.7) continue;
          const front = 12 + Math.floor(r(5 + side) * 3);
          const deep = 13 + Math.floor(r(8 + side) * 3);
          const lot = along
            ? { x: cx - Math.floor(front / 2), y: side < 0 ? cy - 3 - deep : cy + 3, w: front, h: deep, facing: (side < 0 ? 'south' : 'north') as Facing }
            : { x: side < 0 ? cx - 3 - deep : cx + 3, y: cy - Math.floor(front / 2), w: deep, h: front, facing: (side < 0 ? 'east' : 'west') as Facing };
          if (add({ ...lot, zone: 'residential', town: towns.indexOf(edge) })) since = 0;
        }
        continue;
      }
      if (since < 34) continue;
      const near = towns.find((t) => {
        const d = dist(b.x, b.y, t.x, t.y);
        return d > t.radius * 1.2 && d < t.radius * 2.6 + 24;
      });
      if (!near) continue;
      const r = (j: number) => roll(n.seed, ri, i, 0x7500 + j);
      if (r(0) > 0.85) continue;
      const along = Math.abs(b.x - a.x) >= Math.abs(b.y - a.y); // the road runs east-west here
      const side = r(1) < 0.5 ? -1 : 1;
      const front = 26 + Math.floor(r(2) * 14);
      const deep = 22 + Math.floor(r(3) * 10);
      const zone: Zone = r(4) < 0.6 ? 'farm' : 'field';
      const cx = Math.round(b.x);
      const cy = Math.round(b.y);
      const lot = along
        ? { x: cx - Math.floor(front / 2), y: side < 0 ? cy - 3 - deep : cy + 3, w: front, h: deep, facing: (side < 0 ? 'south' : 'north') as Facing }
        : { x: side < 0 ? cx - 3 - deep : cx + 3, y: cy - Math.floor(front / 2), w: deep, h: front, facing: (side < 0 ? 'east' : 'west') as Facing };
      if (add({ ...lot, zone, town: null })) since = 0;
    }
  });
}

/** Now and then a cabin: beside a country road far from any town, or off in the wild. */
/** Fields ringing each town beyond its edge, facing it (farm tracks run in from the town). */
function fieldLots(n: Nature, towns: Town[], add: Add): void {
  towns.forEach((t, ti) => {
    const tries = Math.round(t.radius / 2);
    for (let i = 0; i < tries; i++) {
      const r = (j: number) => roll(n.seed, ti, i, 0x7700 + j);
      const a = r(0) * Math.PI * 2;
      const d = t.radius * (1.25 + r(1) * 0.9);
      const cx = Math.round(t.x + Math.cos(a) * d);
      const cy = Math.round(t.y + Math.sin(a) * d);
      const w = 18 + Math.floor(r(2) * 16);
      const h = 16 + Math.floor(r(3) * 14);
      // Facing the town: its front (the yard, if a farmstead) towards the streets.
      const dx = t.x - cx;
      const dy = t.y - cy;
      const facing: Facing = Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? 'east' : 'west') : dy > 0 ? 'south' : 'north';
      add({ x: cx - Math.floor(w / 2), y: cy - Math.floor(h / 2), w, h, facing, zone: r(4) < 0.25 && w >= 27 ? 'farm' : 'field', town: null });
    }
  });
}

function cabinLots(n: Nature, roads: Point[][], add: Add): void {
  const size = n.size;
  for (let gy = 0; gy < size; gy += 48) {
    for (let gx = 0; gx < size; gx += 48) {
      const r = (j: number) => roll(n.seed, gx, gy, 0x7600 + j);
      if (r(0) > 0.35) continue;
      const x = gx + 4 + Math.floor(r(1) * 30);
      const y = gy + 4 + Math.floor(r(2) * 30);
      const facings: Facing[] = ['north', 'south', 'east', 'west'];
      add({ x, y, w: 13, h: 13, facing: facings[Math.floor(r(3) * 4)]!, zone: 'cabin', town: null });
    }
  }
  void roads;
}
