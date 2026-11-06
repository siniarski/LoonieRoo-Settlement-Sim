// Spatial search helpers.

import { inBounds, isWalkable, type Grid, type Point } from './grid';

/**
 * Nearest tile to `from` (by walking distance, BFS in N, E, S, W order) that
 * `accept` likes. Searches through walkable tiles; `from` itself may be
 * unwalkable (e.g. a tile that just became a wall). Returns null if none, or if
 * nothing turned up in the first `limit` tiles searched.
 *
 * Its working arrays are kept between searches (one set per nesting depth, in case `accept`
 * searches too) and stamped per search, so a search costs what it visits, not the size of the
 * map: it used to allocate a byte per tile every call (a megabyte on a 1024 map), and it's called
 * all the time (the stress test, 2026-11-06).
 */
export function nearestTile(grid: Grid, from: Point, accept: (x: number, y: number) => boolean, limit = Infinity): Point | null {
  if (!inBounds(grid, from.x, from.y)) return null;
  const { width } = grid;
  const size = width * grid.height;
  const sc = scratch(depth, size);
  depth++;
  try {
    const gen = ++sc.gen;
    const { seen, queue } = sc;
    let tail = 0;
    const start = from.y * width + from.x;
    queue[tail++] = start;
    seen[start] = gen;
    for (let head = 0; head < tail && head < limit; head++) {
      const i = queue[head]!;
      const x = i % width;
      const y = (i - x) / width;
      if (accept(x, y)) return head === 0 ? from : { x, y };
      // North, east, south, west.
      for (let d = 0; d < 4; d++) {
        const nx = d === 1 ? x + 1 : d === 3 ? x - 1 : x;
        const ny = d === 0 ? y - 1 : d === 2 ? y + 1 : y;
        if (!isWalkable(grid, nx, ny)) continue;
        const n = ny * width + nx;
        if (seen[n] === gen) continue;
        seen[n] = gen;
        queue[tail++] = n;
      }
    }
    return null;
  } finally {
    depth--;
  }
}

/** nearestTile's working arrays, by nesting depth, for the map size last used. */
const pool: { size: number; gen: number; seen: Uint32Array; queue: Int32Array }[] = [];
let depth = 0;
function scratch(d: number, size: number) {
  let sc = pool[d];
  if (!sc || sc.size !== size || sc.gen >= 0xfffffff0) {
    sc = { size, gen: 0, seen: new Uint32Array(size), queue: new Int32Array(size) };
    pool[d] = sc;
  }
  return sc;
}

export function manhattan(a: Point, b: Point): number {
  return Math.abs(a.x - b.x) + Math.abs(a.y - b.y);
}

export function samePoint(a: Point, b: Point): boolean {
  return a.x === b.x && a.y === b.y;
}
