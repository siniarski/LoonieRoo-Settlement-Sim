// A* on the grid, 4-way movement, Manhattan heuristic.
//
// Deterministic: ties are broken by lower h (closer to the goal), then by
// insertion order, and neighbours are expanded in a fixed N, E, S, W order.
// Scratch arrays are local to the call; nothing here lives in sim state.

import { inBounds, isWalkable, type Grid, type Point } from './grid';

const DIRS: readonly Point[] = [
  { x: 0, y: -1 },
  { x: 1, y: 0 },
  { x: 0, y: 1 },
  { x: -1, y: 0 },
];

/**
 * Path from start to goal, excluding start and including goal.
 * Returns [] when start equals goal, and null when the goal is out of
 * bounds, not walkable, or unreachable. The start tile itself need not be
 * walkable (you can always walk out of the tile you are on).
 */
export function findPath(grid: Grid, start: Point, goal: Point): Point[] | null {
  return findPathToAny(grid, start, [goal]);
}

/**
 * Shortest path to whichever goal is nearest (e.g. any tile next to a
 * blueprint). Unwalkable goals are ignored. Same conventions as findPath.
 */
export function findPathToAny(grid: Grid, start: Point, goals: readonly Point[]): Point[] | null {
  if (!inBounds(grid, start.x, start.y)) return null;
  const targets = goals.filter((g) => isWalkable(grid, g.x, g.y));
  if (targets.length === 0) return null;
  if (targets.some((g) => g.x === start.x && g.y === start.y)) return [];

  const { width } = grid;
  const size = width * grid.height;
  const startIdx = start.y * width + start.x;
  const goalSet = new Set(targets.map((g) => g.y * width + g.x));

  const gScore = new Float64Array(size).fill(Infinity);
  const cameFrom = new Int32Array(size).fill(-1);
  const closed = new Uint8Array(size);
  const open = new MinHeap();

  const h = (idx: number) => {
    const x = idx % width;
    const y = Math.floor(idx / width);
    let best = Infinity;
    for (const g of targets) best = Math.min(best, Math.abs(x - g.x) + Math.abs(y - g.y));
    return best;
  };

  gScore[startIdx] = 0;
  open.push(startIdx, h(startIdx), h(startIdx));

  while (open.size > 0) {
    const current = open.pop();
    if (goalSet.has(current)) return reconstruct(cameFrom, current, width);
    if (closed[current]) continue;
    closed[current] = 1;

    const cx = current % width;
    const cy = Math.floor(current / width);
    const g = gScore[current]! + 1;
    for (const dir of DIRS) {
      const nx = cx + dir.x;
      const ny = cy + dir.y;
      if (!isWalkable(grid, nx, ny)) continue;
      const next = ny * width + nx;
      if (closed[next] || g >= gScore[next]!) continue;
      gScore[next] = g;
      cameFrom[next] = current;
      const hn = h(next);
      open.push(next, g + hn, hn);
    }
  }
  return null;
}

// --- Reachability -----------------------------------------------------------
//
// Connected-component labels for walkable tiles, so "can I reach any of
// these?" is O(1) instead of a failed A* that floods the whole map. Memoised
// per Grid object and walkabilityVersion. This is a derived cache outside sim
// state: it never affects results, only speed, and a cloned state just
// recomputes it.

const componentCache = new WeakMap<Grid, { version: number; labels: Int32Array }>();

function components(grid: Grid, version: number): Int32Array {
  const cached = componentCache.get(grid);
  if (cached && cached.version === version) return cached.labels;

  const { width, height } = grid;
  const labels = new Int32Array(width * height).fill(-1);
  let next = 0;
  const queue: number[] = [];
  for (let i = 0; i < labels.length; i++) {
    if (labels[i] !== -1 || !isWalkable(grid, i % width, Math.floor(i / width))) continue;
    labels[i] = next;
    queue.push(i);
    while (queue.length > 0) {
      const cur = queue.pop()!;
      const cx = cur % width;
      const cy = Math.floor(cur / width);
      for (const dir of DIRS) {
        const nx = cx + dir.x;
        const ny = cy + dir.y;
        const n = ny * width + nx;
        if (isWalkable(grid, nx, ny) && labels[n] === -1) {
          labels[n] = next;
          queue.push(n);
        }
      }
    }
    next++;
  }
  componentCache.set(grid, { version, labels });
  return labels;
}

/**
 * True if any walkable goal is reachable from `from`. `version` must be the
 * state's walkabilityVersion. An unwalkable `from` (e.g. inside a wall) can
 * step into any walkable neighbour.
 */
export function canReach(grid: Grid, version: number, from: Point, goals: readonly Point[]): boolean {
  if (!inBounds(grid, from.x, from.y)) return false;
  const labels = components(grid, version);
  const { width } = grid;
  const starts = isWalkable(grid, from.x, from.y)
    ? [from]
    : DIRS.map((d) => ({ x: from.x + d.x, y: from.y + d.y })).filter((p) => isWalkable(grid, p.x, p.y));
  const startLabels = new Set(starts.map((p) => labels[p.y * width + p.x]));
  return goals.some((g) => isWalkable(grid, g.x, g.y) && startLabels.has(labels[g.y * width + g.x]!));
}

function reconstruct(cameFrom: Int32Array, end: number, width: number): Point[] {
  const path: Point[] = [];
  for (let idx = end; cameFrom[idx] !== -1; idx = cameFrom[idx]!) {
    path.push({ x: idx % width, y: Math.floor(idx / width) });
  }
  return path.reverse();
}

/** Binary min-heap ordered by (f, h, insertion order). Allows duplicates; stale entries are skipped via `closed`. */
class MinHeap {
  private items: { idx: number; f: number; h: number; seq: number }[] = [];
  private seq = 0;

  get size(): number {
    return this.items.length;
  }

  push(idx: number, f: number, h: number): void {
    this.items.push({ idx, f, h, seq: this.seq++ });
    let i = this.items.length - 1;
    while (i > 0) {
      const parent = (i - 1) >> 1;
      if (!this.less(i, parent)) break;
      this.swap(i, parent);
      i = parent;
    }
  }

  pop(): number {
    const top = this.items[0]!;
    const last = this.items.pop()!;
    if (this.items.length > 0) {
      this.items[0] = last;
      let i = 0;
      for (;;) {
        const l = i * 2 + 1;
        const r = l + 1;
        let smallest = i;
        if (l < this.items.length && this.less(l, smallest)) smallest = l;
        if (r < this.items.length && this.less(r, smallest)) smallest = r;
        if (smallest === i) break;
        this.swap(i, smallest);
        i = smallest;
      }
    }
    return top.idx;
  }

  private less(a: number, b: number): boolean {
    const x = this.items[a]!;
    const y = this.items[b]!;
    return x.f !== y.f ? x.f < y.f : x.h !== y.h ? x.h < y.h : x.seq < y.seq;
  }

  private swap(a: number, b: number): void {
    const tmp = this.items[a]!;
    this.items[a] = this.items[b]!;
    this.items[b] = tmp;
  }
}
