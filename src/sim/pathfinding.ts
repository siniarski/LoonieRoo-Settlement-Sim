// A* on the grid, 4-way movement, Manhattan heuristic.
//
// Deterministic: ties are broken by lower h (closer to the goal), then by
// insertion order, and neighbours are expanded in a fixed N, E, S, W order.
// Scratch arrays are local to the call; nothing here lives in sim state.

import { EDGE_BARRICADE, EDGE_DOOR, EDGE_NONE, EDGE_WALL, EDGE_WINDOW, EDGE_WINDOW_BROKEN, edgeBetween, edgeIsOpen, getTile, inBounds, isLocked, isOpen, isWalkable, type Grid, type Point } from './grid';

/**
 * Who is moving. Doors are open or closed (grid.open). Colonists ('survivor') open a door as
 * they step in and it shuts behind them (doors.ts), so any door is a way through. The survivor you control ('free') needs a
 * door open (you open it yourself). Zombies only get through open ones; raiders open unlocked
 * ones. 'breacher' is a zombie planning to batter its way through: every tile is passable.
 */
/** 'vehicle': a car (vehicles.ts): floor only, and no border but a bare one. Never pathfinds. */
export type Mover = 'survivor' | 'free' | 'zombie' | 'breacher' | 'raider' | 'crawler' | 'vehicle';

export function isPassable(grid: Grid, x: number, y: number, mover: Mover = 'survivor'): boolean {
  if (mover === 'breacher') return inBounds(grid, x, y) && getTile(grid, x, y) !== 'water';
  // Raiders open doors, unless they're locked.
  const tile = getTile(grid, x, y);
  const openDoor = tile === 'door' && isOpen(grid, x, y);
  if (mover === 'crawler') return tile === 'floor' || tile === 'barricade' || openDoor;
  if (mover === 'vehicle') return tile === 'floor';
  if (mover === 'raider') return tile === 'floor' || (tile === 'door' && !isLocked(grid, x, y));
  if (mover === 'survivor') return isWalkable(grid, x, y);
  if (mover === 'free') return tile === 'floor' || tile === 'barricade' || openDoor;
  return tile === 'floor' || openDoor;
}

/**
 * Can this mover cross from a tile to the one beside it: no edge wall between, or an edge door
 * it can get through (the same rules as door tiles: colonists and raiders open them, you and the
 * dead need them open). Edge walls stop everyone but a breacher, which batters them down.
 */
export function crossable(grid: Grid, ax: number, ay: number, bx: number, by: number, mover: Mover = 'survivor'): boolean {
  const e = edgeBetween(grid, ax, ay, bx, by);
  if (e < 0) return true;
  const kind = grid.edges[e]!;
  if (kind === EDGE_NONE || mover === 'breacher') return true; // a breacher batters its way through (zombies.ts)
  if (kind === EDGE_WALL || mover === 'vehicle') return false;
  if (kind === EDGE_DOOR) return mover === 'survivor' || (mover === 'raider' && !grid.edgeLocked.includes(e)) || edgeIsOpen(grid, e);
  if (kind === EDGE_BARRICADE) return mover === 'survivor' || mover === 'free' || mover === 'crawler';
  // Windows: glass stops everyone (a breacher batters it); broken and unboarded, you, the dead and
  // raiders climb through (colonists use doors).
  if (kind === EDGE_WINDOW || grid.edgeBoarded.includes(e)) return false;
  if (kind === EDGE_WINDOW_BROKEN) return mover !== 'survivor';
  return true;
}

export const DIRS: readonly Point[] = [
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
export function findPathToAny(
  grid: Grid,
  start: Point,
  goals: readonly Point[],
  mover: Mover = 'survivor',
): Point[] | null {
  if (!inBounds(grid, start.x, start.y)) return null;
  const passable = (x: number, y: number) => isPassable(grid, x, y, mover);
  const targets = goals.filter((g) => passable(g.x, g.y));
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
      if (!passable(nx, ny) || !crossable(grid, cx, cy, nx, ny, mover)) continue;
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

const componentCache = new WeakMap<Grid, Partial<Record<Mover, { version: number; doors: string; labels: Int32Array }>>>();

/**
 * Which doors stand open, for movers that need a door open to get through (you, the dead): doors
 * held open for someone passing swing shut without bumping walkabilityVersion (doors.ts), so the
 * labels are also keyed on this. Colonists and raiders open doors themselves: for them it's ''.
 */
function openDoors(grid: Grid, mover: Mover): string {
  if (mover !== 'zombie' && mover !== 'free' && mover !== 'crawler') return '';
  return grid.open.join(',') + '|' + grid.edgeOpen.join(',');
}

function components(grid: Grid, version: number, mover: Mover): Int32Array {
  const perMover = componentCache.get(grid) ?? {};
  const cached = perMover[mover];
  const doors = openDoors(grid, mover);
  if (cached && cached.version === version && cached.doors === doors) return cached.labels;

  const passable = (x: number, y: number) => isPassable(grid, x, y, mover);
  const { width, height } = grid;
  const labels = new Int32Array(width * height).fill(-1);
  let next = 0;
  const queue: number[] = [];
  for (let i = 0; i < labels.length; i++) {
    if (labels[i] !== -1 || !passable(i % width, Math.floor(i / width))) continue;
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
        if (passable(nx, ny) && labels[n] === -1 && crossable(grid, cx, cy, nx, ny, mover)) {
          labels[n] = next;
          queue.push(n);
        }
      }
    }
    next++;
  }
  perMover[mover] = { version, doors, labels };
  componentCache.set(grid, perMover);
  return labels;
}

/**
 * True if any passable goal is reachable from `from` for this mover.
 * `version` must be the state's walkabilityVersion. An impassable `from`
 * (e.g. inside a wall, or a zombie on a door tile) can step into any
 * passable neighbour.
 */
export function canReach(
  grid: Grid,
  version: number,
  from: Point,
  goals: readonly Point[],
  mover: Mover = 'survivor',
): boolean {
  if (!inBounds(grid, from.x, from.y)) return false;
  const labels = components(grid, version, mover);
  const passable = (p: Point) => isPassable(grid, p.x, p.y, mover);
  const { width } = grid;
  const starts = passable(from)
    ? [from]
    : DIRS.map((d) => ({ x: from.x + d.x, y: from.y + d.y })).filter((p) => passable(p) && crossable(grid, from.x, from.y, p.x, p.y, mover));
  const startLabels = new Set(starts.map((p) => labels[p.y * width + p.x]));
  return goals.some((g) => passable(g) && startLabels.has(labels[g.y * width + g.x]!));
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

// --- Straightening ----------------------------------------------------------

/** How far ahead along a path straighten looks for a straight line. */
const STRAIGHTEN_LOOKAHEAD = 24;

/**
 * A four-way path straightened for walking: from each point, the furthest point further along it
 * that a straight eight-way line reaches over open floor (no wall's corner cut, no doorway
 * entered or left at an angle), then the line's tiles. Colonists walk these (survivors.ts), so
 * they cross open ground on the diagonal instead of along the grid. Same ends as the path.
 */
export function straighten(grid: Grid, start: Point, path: readonly Point[]): Point[] {
  if (path.length < 2) return path.slice();
  const pts = [start, ...path];
  const out: Point[] = [];
  let i = 0;
  while (i < pts.length - 1) {
    let best = i + 1;
    let bestLine: Point[] = [pts[i + 1]!];
    for (let j = Math.min(pts.length - 1, i + STRAIGHTEN_LOOKAHEAD); j > i + 1; j--) {
      const line = straightLine(grid, pts[i]!, pts[j]!);
      if (line) {
        best = j;
        bestLine = line;
        break;
      }
    }
    out.push(...bestLine);
    i = best;
  }
  return out;
}

/** The tiles of an eight-way line from a to b (excluding a), or null if it can't be walked straight. */
function straightLine(grid: Grid, a: Point, b: Point): Point[] | null {
  const tiles: Point[] = [];
  const dx = Math.abs(b.x - a.x);
  const dy = Math.abs(b.y - a.y);
  const sx = Math.sign(b.x - a.x);
  const sy = Math.sign(b.y - a.y);
  let x = a.x;
  let y = a.y;
  let err = dx - dy;
  const open = (px: number, py: number) => inBounds(grid, px, py) && grid.tiles[py * grid.width + px] === 'floor' && isPassable(grid, px, py, 'survivor');
  while (x !== b.x || y !== b.y) {
    const e2 = 2 * err;
    let nx = x;
    let ny = y;
    if (e2 > -dy) {
      err -= dy;
      nx += sx;
    }
    if (e2 < dx) {
      err += dx;
      ny += sy;
    }
    const diagonal = nx !== x && ny !== y;
    const door = (px: number, py: number) => grid.tiles[py * grid.width + px] === 'door';
    const bare = (ax: number, ay: number, bx: number, by: number) => grid.edges[edgeBetween(grid, ax, ay, bx, by)] === EDGE_NONE;
    if (diagonal) {
      // Both corners open floor, no edge round the corner; neither end a doorway.
      if (!open(nx, y) || !open(x, ny) || !open(nx, ny) || door(x, y)) return null;
      if (!bare(x, y, nx, y) || !bare(x, y, x, ny) || !bare(nx, y, nx, ny) || !bare(x, ny, nx, ny)) return null;
    } else if (!isPassable(grid, nx, ny, 'survivor') || !crossable(grid, x, y, nx, ny, 'survivor')) return null;
    // An edge door is crossed straight, and ends the line (like a door tile).
    if (!diagonal && (edgeKindOf(grid, x, y, nx, ny) === EDGE_DOOR || edgeKindOf(grid, x, y, nx, ny) === EDGE_BARRICADE) && (nx !== b.x || ny !== b.y)) return null;
    // Only open floor in between: a door is fine as the line's end, walked into straight.
    if (!diagonal && door(nx, ny) && (nx !== b.x || ny !== b.y)) return null;
    tiles.push({ x: nx, y: ny });
    x = nx;
    y = ny;
  }
  return tiles;
}

/**
 * Just the first straight line of `straighten`: from `start`, the furthest point of the next
 * STRAIGHTEN_LOOKAHEAD that a straight line reaches, its tiles, then the rest of the path as it was.
 * What a walker needs each step, at a fraction of the cost.
 */
export function straightenAhead(grid: Grid, start: Point, path: readonly Point[]): Point[] {
  if (path.length < 2) return path.slice();
  for (let j = Math.min(path.length - 1, STRAIGHTEN_LOOKAHEAD - 1); j >= 1; j--) {
    const line = straightLine(grid, start, path[j]!);
    if (line) return [...line, ...path.slice(j + 1)];
  }
  return path.slice();
}

function edgeKindOf(grid: Grid, ax: number, ay: number, bx: number, by: number): number {
  const e = edgeBetween(grid, ax, ay, bx, by);
  return e < 0 ? EDGE_NONE : grid.edges[e]!;
}

/**
 * Nothing solid between two tiles at most a step apart (side by side or diagonal): no edge wall
 * or shut edge door on the border (a diagonal can go either way round the corner). The same tile,
 * or tiles further apart, are open (reach decides those). For blows and bites (motion.ts inReach).
 */
export function openBetween(grid: Grid, a: Point, b: Point): boolean {
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  if ((dx === 0 && dy === 0) || Math.abs(dx) > 1 || Math.abs(dy) > 1) return true;
  const open = (px: number, py: number, qx: number, qy: number) => {
    const e = edgeBetween(grid, px, py, qx, qy);
    if (e < 0) return true;
    const kind = grid.edges[e]!;
    return kind === EDGE_NONE || kind === EDGE_BARRICADE || (kind === EDGE_DOOR && edgeIsOpen(grid, e)) || (kind === EDGE_WINDOW_BROKEN && !grid.edgeBoarded.includes(e));
  };
  if (dx === 0 || dy === 0) return open(a.x, a.y, b.x, b.y);
  return (open(a.x, a.y, b.x, a.y) && open(b.x, a.y, b.x, b.y)) || (open(a.x, a.y, a.x, b.y) && open(a.x, b.y, b.x, b.y));
}

/**
 * Does sight pass this edge? Not a wall, a shut door or boards; glass, broken windows, barricades
 * and open doors let it through. The one place that decides (lines of fire now; line of sight later).
 */
export function seesThrough(grid: Grid, e: number): boolean {
  if (e < 0) return true;
  const kind = grid.edges[e] ?? EDGE_NONE;
  if (kind === EDGE_WALL) return false;
  if (kind === EDGE_DOOR) return edgeIsOpen(grid, e);
  if (kind === EDGE_WINDOW || kind === EDGE_WINDOW_BROKEN) return !grid.edgeBoarded.includes(e);
  return true;
}

/** A step from a tile to one beside it, straight or diagonal, that this mover could walk (a diagonal either way round). */
export function walksAcross(grid: Grid, a: Point, b: Point, mover: Mover): boolean {
  if (a.x === b.x || a.y === b.y) return crossable(grid, a.x, a.y, b.x, b.y, mover);
  return (crossable(grid, a.x, a.y, b.x, a.y, mover) && crossable(grid, b.x, a.y, b.x, b.y, mover)) || (crossable(grid, a.x, a.y, a.x, b.y, mover) && crossable(grid, a.x, b.y, b.x, b.y, mover));
}
