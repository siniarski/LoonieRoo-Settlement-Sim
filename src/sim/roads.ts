// Country roads: routed between towns across the land (worldplan.ts), once per world.
//
//   network  towns (and the start) joined by a minimum spanning tree, plus a few extra links
//            between near neighbours, and some roads out to the map's edge (the world goes on).
//   routing  A* over a coarse grid (ROAD_CELL tiles a cell, eight ways): open land is cheap, woods
//            dearer, water dear (a bridge: only where it must), the sea impassable, other towns
//            avoided, and roads already laid cheap, so later roads join earlier ones.
//   drawing  the cell path is smoothed (corner cutting) into a curve, drawn two to three tiles wide.

import { isForest, isSea, isWater, roll, type Nature } from './nature';
import type { Point } from './grid';

/** A point on a road's centre line, in tiles: fractional where the curve is smoothed (corner cutting). A Point's shape. */
export type RoadPoint = Point;
import type { Town } from './towns';

export const ROAD_CELL = 4;

export interface RoadNode {
  /** Where the road starts or ends: a street end in a town, the start clearing, or the map's edge. */
  at: Point;
  town: number | null;
}

/** Which nodes to join: a minimum spanning tree over the nodes, plus a few short extra links. */
export function network(seed: number, nodes: Point[]): [number, number][] {
  const edges: [number, number][] = [];
  if (nodes.length < 2) return edges;
  const d = (a: number, b: number) => Math.sqrt((nodes[a]!.x - nodes[b]!.x) ** 2 + (nodes[a]!.y - nodes[b]!.y) ** 2);
  const inTree = [0];
  const rest = nodes.map((_, i) => i).slice(1);
  while (rest.length > 0) {
    let best: [number, number, number] | null = null;
    for (const a of inTree) for (const b of rest) if (!best || d(a, b) < best[2]) best = [a, b, d(a, b)];
    edges.push([best![0], best![1]]);
    inTree.push(best![1]);
    rest.splice(rest.indexOf(best![1]), 1);
  }
  // A loop or two: each node's nearest not already linked, if it's not much further than its tree links.
  nodes.forEach((_, a) => {
    const linked = edges.filter(([p, q]) => p === a || q === a).map(([p, q]) => (p === a ? q : p));
    const longest = Math.max(...linked.map((b) => d(a, b)));
    const near = nodes.map((_, b) => b).filter((b) => b !== a && !linked.includes(b)).sort((p, q) => d(a, p) - d(a, q))[0];
    if (near !== undefined && d(a, near) < longest * 1.4 && roll(seed, a, near, 0x7300) < 0.5 && !edges.some(([p, q]) => p === near && q === a)) edges.push([a, near]);
  });
  return edges;
}

/** Routes a road from `from` to `to`; returns its smoothed centre line in tiles, or null if there's no way. */
export function route(n: Nature, cost: Float32Array, from: Point, to: Point): RoadPoint[] | null {
  const N = Math.ceil(n.size / ROAD_CELL);
  const cell = (p: Point) => Math.min(N - 1, Math.max(0, Math.floor(p.y / ROAD_CELL))) * N + Math.min(N - 1, Math.max(0, Math.floor(p.x / ROAD_CELL)));
  const start = cell(from);
  const goal = cell(to);
  const g = new Float32Array(N * N).fill(Infinity);
  const came = new Int32Array(N * N).fill(-1);
  const gx = goal % N;
  const gy = Math.floor(goal / N);
  // A binary heap of [f, cell].
  const heap: [number, number][] = [];
  const push = (f: number, c: number) => {
    heap.push([f, c]);
    let i = heap.length - 1;
    while (i > 0) {
      const p = (i - 1) >> 1;
      if (heap[p]![0] <= heap[i]![0]) break;
      [heap[p], heap[i]] = [heap[i]!, heap[p]!];
      i = p;
    }
  };
  const pop = () => {
    const top = heap[0]!;
    const last = heap.pop()!;
    if (heap.length > 0) {
      heap[0] = last;
      let i = 0;
      for (;;) {
        const l = 2 * i + 1;
        const r = l + 1;
        let m = i;
        if (l < heap.length && heap[l]![0] < heap[m]![0]) m = l;
        if (r < heap.length && heap[r]![0] < heap[m]![0]) m = r;
        if (m === i) break;
        [heap[m], heap[i]] = [heap[i]!, heap[m]!];
        i = m;
      }
    }
    return top;
  };
  g[start] = 0;
  push(0, start);
  const MIN = 0.35;
  while (heap.length > 0) {
    const [f, c] = pop();
    if (c === goal) break;
    const cx = c % N;
    const cy = Math.floor(c / N);
    if (f > g[c]! + MIN * Math.sqrt((gx - cx) ** 2 + (gy - cy) ** 2) + 1e-6) continue; // stale
    for (let dy = -1; dy <= 1; dy++) {
      for (let dx = -1; dx <= 1; dx++) {
        if (dx === 0 && dy === 0) continue;
        const nx = cx + dx;
        const ny = cy + dy;
        if (nx < 0 || ny < 0 || nx >= N || ny >= N) continue;
        const nc = ny * N + nx;
        const step = ((cost[c]! + cost[nc]!) / 2) * (dx !== 0 && dy !== 0 ? Math.SQRT2 : 1);
        if (!Number.isFinite(step)) continue;
        const ng = g[c]! + step;
        if (ng < g[nc]!) {
          g[nc] = ng;
          came[nc] = c;
          push(ng + MIN * Math.sqrt((gx - nx) ** 2 + (gy - ny) ** 2), nc);
        }
      }
    }
  }
  if (!Number.isFinite(g[goal]!)) return null;
  const cells: Point[] = [];
  for (let c = goal; c !== -1; c = came[c]!) cells.push({ x: (c % N) * ROAD_CELL + ROAD_CELL / 2, y: Math.floor(c / N) * ROAD_CELL + ROAD_CELL / 2 });
  cells.reverse();
  // The real ends, then two rounds of corner cutting (Chaikin) for a curve.
  let line: RoadPoint[] = [from, ...cells.slice(1, -1), to];
  for (let pass = 0; pass < 2; pass++) {
    const next: RoadPoint[] = [line[0]!];
    for (let i = 0; i < line.length - 1; i++) {
      const a = line[i]!;
      const b = line[i + 1]!;
      next.push({ x: a.x * 0.75 + b.x * 0.25, y: a.y * 0.75 + b.y * 0.25 }, { x: a.x * 0.25 + b.x * 0.75, y: a.y * 0.25 + b.y * 0.75 });
    }
    next.push(line.at(-1)!);
    line = next;
  }
  return line;
}

/** The cost of crossing each coarse cell: land 1, woods 3, water 14 (a bridge), sea impassable. */
export function costGrid(n: Nature, towns: Town[]): Float32Array {
  const N = Math.ceil(n.size / ROAD_CELL);
  const c = new Float32Array(N * N);
  for (let j = 0; j < N; j++) {
    for (let i = 0; i < N; i++) {
      const x = i * ROAD_CELL + ROAD_CELL / 2;
      const y = j * ROAD_CELL + ROAD_CELL / 2;
      let v = isSea(n, x, y) ? Infinity : isWater(n, x, y) ? 14 : isForest(n, x, y) ? 3 : 1;
      // Keep out of towns except at their streets (cheap: those are marked as roads later).
      if (towns.some((t) => Math.sqrt((x - t.x) ** 2 + (y - t.y) ** 2) < t.radius * 0.9)) v += 4;
      c[j * N + i] = v;
    }
  }
  return c;
}

/** Marks a laid road's cells cheap, so later roads share it. */
export function reuse(cost: Float32Array, size: number, line: RoadPoint[]): void {
  const N = Math.ceil(size / ROAD_CELL);
  for (const p of line) {
    const i = Math.min(N - 1, Math.max(0, Math.floor(p.y / ROAD_CELL))) * N + Math.min(N - 1, Math.max(0, Math.floor(p.x / ROAD_CELL)));
    if (Number.isFinite(cost[i]!)) cost[i] = 0.35;
  }
}

/** Draws a road's centre line into the mask, about two and a half tiles wide. */
export function drawLine(mask: Uint8Array, size: number, line: RoadPoint[], value: number): void {
  for (let i = 0; i < line.length - 1; i++) {
    const a = line[i]!;
    const b = line[i + 1]!;
    const steps = Math.max(1, Math.ceil(Math.sqrt((b.x - a.x) ** 2 + (b.y - a.y) ** 2) / 0.4));
    for (let s = 0; s <= steps; s++) {
      const px = a.x + ((b.x - a.x) * s) / steps;
      const py = a.y + ((b.y - a.y) * s) / steps;
      for (let y = Math.floor(py - 1); y <= Math.ceil(py + 1); y++) {
        for (let x = Math.floor(px - 1); x <= Math.ceil(px + 1); x++) {
          if (x < 0 || y < 0 || x >= size || y >= size) continue;
          if ((x + 0.5 - px) ** 2 + (y + 0.5 - py) ** 2 <= 1.1 && mask[y * size + x] === 0) mask[y * size + x] = value;
        }
      }
    }
  }
}
