// Spatial search helpers.

import { inBounds, isWalkable, type Grid, type Point } from './grid';

/**
 * Nearest tile to `from` (by walking distance, BFS in N, E, S, W order) that
 * `accept` likes. Searches through walkable tiles; `from` itself may be
 * unwalkable (e.g. a tile that just became a wall). Returns null if none, or if
 * nothing turned up in the first `limit` tiles searched.
 */
export function nearestTile(grid: Grid, from: Point, accept: (x: number, y: number) => boolean, limit = Infinity): Point | null {
  if (!inBounds(grid, from.x, from.y)) return null;
  const { width } = grid;
  const seen = new Uint8Array(width * grid.height);
  const queue: Point[] = [from];
  seen[from.y * width + from.x] = 1;
  for (let head = 0; head < queue.length && head < limit; head++) {
    const p = queue[head]!;
    if (accept(p.x, p.y)) return p;
    for (const [dx, dy] of [
      [0, -1],
      [1, 0],
      [0, 1],
      [-1, 0],
    ] as const) {
      const nx = p.x + dx;
      const ny = p.y + dy;
      if (!isWalkable(grid, nx, ny) || seen[ny * width + nx]) continue;
      seen[ny * width + nx] = 1;
      queue.push({ x: nx, y: ny });
    }
  }
  return null;
}

export function manhattan(a: Point, b: Point): number {
  return Math.abs(a.x - b.x) + Math.abs(a.y - b.y);
}

export function samePoint(a: Point, b: Point): boolean {
  return a.x === b.x && a.y === b.y;
}
