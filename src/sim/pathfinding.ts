// A* on the grid, 4-way movement, Manhattan heuristic.
//
// Deterministic: ties are broken by lower h (closer to the goal), then by
// insertion order, and neighbours are expanded in a fixed N, E, S, W order.
// Scratch arrays are local to the call; nothing here lives in sim state.

import { EDGE_BARRICADE, EDGE_DOOR, EDGE_NONE, EDGE_WALL, EDGE_WINDOW, EDGE_WINDOW_BROKEN, edgeBetween, edgeIsOpen, getTile, inBounds, isLocked, isOpen, isWalkable, type Grid, type Point, REACH_CHUNK, reachChangesSince, reachCursor } from './grid';

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

  // Scratch reused across searches (scratchFor): a cell's g and cameFrom count only if it was
  // touched in this search (seen === gen), so nothing map-sized is allocated or cleared per search.
  const sc = scratchFor(size);
  const gen = sc.gen;
  const { seen, g: gScoreRaw, from: cameFrom, closed } = sc;
  const gOf = (i: number) => (seen[i] === gen ? gScoreRaw[i]! : Infinity);
  const open = new MinHeap();

  const h = (idx: number) => {
    const x = idx % width;
    const y = Math.floor(idx / width);
    let best = Infinity;
    for (const g of targets) best = Math.min(best, Math.abs(x - g.x) + Math.abs(y - g.y));
    return best;
  };

  seen[startIdx] = gen;
  gScoreRaw[startIdx] = 0;
  cameFrom[startIdx] = -1;
  open.push(startIdx, h(startIdx), h(startIdx));

  while (open.size > 0) {
    const current = open.pop();
    if (goalSet.has(current)) return reconstruct(cameFrom, current, width);
    if (closed[current] === gen) continue;
    closed[current] = gen;

    const cx = current % width;
    const cy = Math.floor(current / width);
    const g = gOf(current) + 1;
    for (const dir of DIRS) {
      const nx = cx + dir.x;
      const ny = cy + dir.y;
      if (!passable(nx, ny) || !crossable(grid, cx, cy, nx, ny, mover)) continue;
      const next = ny * width + nx;
      if (closed[next] === gen || g >= gOf(next)) continue;
      seen[next] = gen;
      gScoreRaw[next] = g;
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

/**
 * Reachability labels, per grid and mover.
 *
 * Chunked: each REACH_CHUNK square is flooded on its own into local pieces, and pieces that touch
 * across a chunk border are joined (a union-find over every piece on the map). When walls, doors,
 * locks or edges change, only the chunks they're in are flooded again (grid.ts reachChangesSince:
 * a log of changed chunks), then the borders are joined afresh. A new map chunk generated, or a wall
 * built, re-floods a chunk or two, not the whole map (the stress test, 2026-11-06: on a 1024 map
 * each kind of mover re-flooded a million tiles per change, the 30-60 ms spikes). With no log to go
 * on (the first time, a loaded grid, or more changes than the log keeps) it floods every chunk.
 *
 * Doors: for movers that need a door open to get through it (you, the dead, crawlers: DOOR_MOVERS)
 * the flood is done with every door shut, and the doors open now join the pieces either side
 * (another union-find over the pieces' roots). Doors swing open and shut all the time without
 * bumping the version (held open for someone passing: doors.ts), so only that join is redone.
 *
 * The answer is always the same as one flood of the whole map with the doors as they are
 * (tests/reach.test.ts checks, with walls and doors built, broken and locked between questions).
 */
interface Reach {
  version: number;
  /** How far into grid.ts's change log these labels have read. */
  cursor: number;
  /** Chunks across and down. */
  cw: number;
  ch: number;
  /** Each tile's piece within its chunk (-1: not passable, with doors shut for door movers). */
  local: Int32Array;
  /** Pieces in each chunk, and where each chunk's pieces start among all of them. */
  counts: Int32Array;
  offsets: Int32Array;
  /** Each chunk's links to its east and south neighbours (borderLinks). */
  east: number[][];
  south: number[][];
  /** Every piece's root once the chunks are joined at their borders; how many pieces in all. */
  baseRoot: Int32Array;
  count: number;
  /** Which doors were open when the door joins were made; each root's root after them; open door tiles' own labels (count and up). */
  doors: string;
  root: Int32Array;
  doorIds: Map<number, number>;
}
const componentCache = new WeakMap<Grid, Partial<Record<Mover, Reach>>>();
const DOOR_MOVERS: readonly Mover[] = ['zombie', 'free', 'crawler'];

/** Which doors stand open (the joins are keyed on it). Colonists and raiders open doors themselves: for them it's ''. */
function openDoors(grid: Grid, mover: Mover): string {
  if (!DOOR_MOVERS.includes(mover)) return '';
  return grid.open.join(',') + '|' + grid.edgeOpen.join(',');
}

function chunkOf(r: Reach, width: number, i: number): number {
  const x = i % width;
  return Math.floor((i - x) / width / REACH_CHUNK) * r.cw + Math.floor(x / REACH_CHUNK);
}

/** A tile's piece after the chunks are joined (doors shut for door movers), or -1. */
function baseLabel(r: Reach, width: number, i: number): number {
  const l = r.local[i]!;
  return l < 0 ? -1 : r.baseRoot[r.offsets[chunkOf(r, width, i)]! + l]!;
}

/** Passable for the flood (door movers: with every door shut), and across an edge (index e) the same way. */
function rules(grid: Grid, mover: Mover) {
  const shut = DOOR_MOVERS.includes(mover);
  const pass = (x: number, y: number) => isPassable(grid, x, y, mover) && !(shut && grid.tiles[y * grid.width + x] === 'door');
  const across = (e: number, ax: number, ay: number, bx: number, by: number) => {
    const kind = grid.edges[e];
    if (!kind) return true;
    return !(shut && kind === EDGE_DOOR) && crossable(grid, ax, ay, bx, by, mover);
  };
  return { pass, across };
}

/** Floods one chunk into its own pieces. */
function floodChunk(grid: Grid, r: Reach, mover: Mover, c: number, rule: ReturnType<typeof rules>, stack: Int32Array): void {
  const { width, height } = grid;
  const x0 = (c % r.cw) * REACH_CHUNK;
  const y0 = Math.floor(c / r.cw) * REACH_CHUNK;
  const x1 = Math.min(width, x0 + REACH_CHUNK);
  const y1 = Math.min(height, y0 + REACH_CHUNK);
  for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) r.local[y * width + x] = rule.pass(x, y) ? -2 : -1;
  let next = 0;
  for (let y = y0; y < y1; y++) {
    for (let x = x0; x < x1; x++) {
      const start = y * width + x;
      if (r.local[start] !== -2) continue;
      r.local[start] = next;
      let top = 0;
      stack[top++] = start;
      while (top > 0) {
        const cur = stack[--top]!;
        const cx = cur % width;
        const cy = (cur - cx) / width;
        // North (this tile's north edge), south (the next row's north edge), west (this tile's west edge), east (the next tile's west edge): within the chunk.
        if (cy > y0) {
          const n = cur - width;
          if (r.local[n] === -2 && rule.across(cur * 2, cx, cy, cx, cy - 1)) {
            r.local[n] = next;
            stack[top++] = n;
          }
        }
        if (cy < y1 - 1) {
          const n = cur + width;
          if (r.local[n] === -2 && rule.across(n * 2, cx, cy, cx, cy + 1)) {
            r.local[n] = next;
            stack[top++] = n;
          }
        }
        if (cx > x0) {
          const n = cur - 1;
          if (r.local[n] === -2 && rule.across(cur * 2 + 1, cx, cy, cx - 1, cy)) {
            r.local[n] = next;
            stack[top++] = n;
          }
        }
        if (cx < x1 - 1) {
          const n = cur + 1;
          if (r.local[n] === -2 && rule.across(n * 2 + 1, cx, cy, cx + 1, cy)) {
            r.local[n] = next;
            stack[top++] = n;
          }
        }
      }
      next++;
    }
  }
  r.counts[c] = next;
}

/**
 * A chunk's links to its east and south neighbours: pairs of (its piece, their piece) that touch
 * across the border, each pair once. Worked out again only when either chunk changed.
 */
function borderLinks(grid: Grid, r: Reach, c: number, rule: ReturnType<typeof rules>): void {
  const { width, height } = grid;
  const x0 = (c % r.cw) * REACH_CHUNK;
  const y0 = Math.floor(c / r.cw) * REACH_CHUNK;
  const x1 = Math.min(width, x0 + REACH_CHUNK);
  const y1 = Math.min(height, y0 + REACH_CHUNK);
  const east: number[] = [];
  const south: number[] = [];
  const seen = new Set<number>();
  // East: (x1 - 1, y) | (x1, y), across x1's west edge.
  if (x1 < width) {
    for (let y = y0; y < y1; y++) {
      const b = y * width + x1;
      const la = r.local[b - 1]!;
      const lb = r.local[b]!;
      if (la < 0 || lb < 0 || seen.has(la * 2048 + lb) || !rule.across(b * 2 + 1, x1, y, x1 - 1, y)) continue;
      seen.add(la * 2048 + lb);
      east.push(la, lb);
    }
  }
  seen.clear();
  // South: (x, y1 - 1) above (x, y1), across y1's north edge.
  if (y1 < height) {
    for (let x = x0; x < x1; x++) {
      const b = y1 * width + x;
      const la = r.local[b - width]!;
      const lb = r.local[b]!;
      if (la < 0 || lb < 0 || seen.has(la * 2048 + lb) || !rule.across(b * 2, x, y1, x, y1 - 1)) continue;
      seen.add(la * 2048 + lb);
      south.push(la, lb);
    }
  }
  r.east[c] = east;
  r.south[c] = south;
}

/** Joins pieces that touch across chunk borders (from each chunk's cached links). */
function joinChunks(r: Reach): void {
  const chunks = r.cw * r.ch;
  let total = 0;
  for (let c = 0; c < chunks; c++) {
    r.offsets[c] = total;
    total += r.counts[c]!;
  }
  r.count = total;
  const parent = new Int32Array(total);
  for (let k = 0; k < total; k++) parent[k] = k;
  const find = (k: number): number => {
    while (parent[k] !== k) {
      parent[k] = parent[parent[k]!]!;
      k = parent[k]!;
    }
    return k;
  };
  const unite = (ga: number, gb: number) => {
    const ra = find(ga);
    const rb = find(gb);
    if (ra !== rb) parent[Math.max(ra, rb)] = Math.min(ra, rb);
  };
  for (let c = 0; c < chunks; c++) {
    const east = r.east[c]!;
    const south = r.south[c]!;
    const o = r.offsets[c]!;
    for (let k = 0; k < east.length; k += 2) unite(o + east[k]!, r.offsets[c + 1]! + east[k + 1]!);
    for (let k = 0; k < south.length; k += 2) unite(o + south[k]!, r.offsets[c + r.cw]! + south[k + 1]!);
  }
  for (let k = 0; k < total; k++) parent[k] = find(k);
  r.baseRoot = parent;
}

/** Joins the pieces through the doors open now. */
function join(grid: Grid, r: Reach, mover: Mover): void {
  const { width } = grid;
  const doorTiles = grid.open.filter((i) => grid.tiles[i] === 'door' && isPassable(grid, i % width, Math.floor(i / width), mover));
  r.doorIds = new Map(doorTiles.map((i, k) => [i, r.count + k]));
  const root = new Int32Array(r.count + doorTiles.length);
  for (let k = 0; k < root.length; k++) root[k] = k;
  const find = (k: number): number => {
    while (root[k] !== k) {
      root[k] = root[root[k]!]!;
      k = root[k]!;
    }
    return k;
  };
  const labelAt = (i: number) => (r.local[i]! >= 0 ? baseLabel(r, width, i) : r.doorIds.get(i) ?? -1);
  const unite = (a: number, b: number) => {
    if (a < 0 || b < 0) return;
    const ra = find(a);
    const rb = find(b);
    if (ra !== rb) root[Math.max(ra, rb)] = Math.min(ra, rb);
  };
  // An open door tile joins whatever it opens onto.
  for (const i of doorTiles) {
    const x = i % width;
    const y = Math.floor(i / width);
    for (const d of DIRS) {
      const nx = x + d.x;
      const ny = y + d.y;
      if (!inBounds(grid, nx, ny) || !isPassable(grid, nx, ny, mover) || !crossable(grid, x, y, nx, ny, mover)) continue;
      unite(r.doorIds.get(i)!, labelAt(ny * width + nx));
    }
  }
  // An open edge door joins the tiles either side (a tile's north edge is even, its west edge odd).
  for (const e of grid.edgeOpen) {
    if (grid.edges[e] !== EDGE_DOOR) continue;
    const i = Math.floor(e / 2);
    const x = i % width;
    const y = Math.floor(i / width);
    const ox = e % 2 === 0 ? x : x - 1;
    const oy = e % 2 === 0 ? y - 1 : y;
    if (!inBounds(grid, ox, oy) || !isPassable(grid, x, y, mover) || !isPassable(grid, ox, oy, mover)) continue;
    unite(labelAt(i), labelAt(oy * width + ox));
  }
  for (let k = 0; k < root.length; k++) root[k] = find(k);
  r.root = root;
}

/** A label for each tile (by index) for this mover: two tiles share one when each can reach the other. */
function components(grid: Grid, version: number, mover: Mover): (i: number) => number {
  const perMover = componentCache.get(grid) ?? {};
  componentCache.set(grid, perMover);
  const doorMover = DOOR_MOVERS.includes(mover);
  const { width, height } = grid;
  let r = perMover[mover];
  if (!r || r.version !== version) {
    const changed = r ? reachChangesSince(grid, r.cursor) : null;
    const cursor = reachCursor(grid);
    const rule = rules(grid, mover);
    const stack = new Int32Array(REACH_CHUNK * REACH_CHUNK);
    if (!r || !changed) {
      const cw = Math.ceil(width / REACH_CHUNK);
      const ch = Math.ceil(height / REACH_CHUNK);
      r = { version, cursor, cw, ch, local: new Int32Array(width * height), counts: new Int32Array(cw * ch), offsets: new Int32Array(cw * ch), east: [], south: [], baseRoot: new Int32Array(0), count: 0, doors: '', root: new Int32Array(0), doorIds: new Map() };
      perMover[mover] = r;
      for (let c = 0; c < cw * ch; c++) floodChunk(grid, r, mover, c, rule, stack);
      for (let c = 0; c < cw * ch; c++) borderLinks(grid, r, c, rule);
    } else {
      const dirty = new Set(changed);
      for (const c of dirty) floodChunk(grid, r, mover, c, rule, stack);
      // Links change for a changed chunk, and for the chunks west and north of it (their east and south links reach into it).
      const relink = new Set<number>();
      for (const c of dirty) {
        relink.add(c);
        if (c % r.cw > 0) relink.add(c - 1);
        if (c >= r.cw) relink.add(c - r.cw);
      }
      for (const c of relink) borderLinks(grid, r, c, rule);
      r.version = version;
      r.cursor = cursor;
    }
    joinChunks(r);
    if (doorMover) join(grid, r, mover);
    r.doors = openDoors(grid, mover);
  } else if (doorMover) {
    const doors = openDoors(grid, mover);
    if (r.doors !== doors) {
      join(grid, r, mover);
      r.doors = doors;
    }
  }
  const reach = r;
  if (!doorMover) return (i) => baseLabel(reach, width, i);
  return (i) => {
    const l = reach.local[i]! >= 0 ? baseLabel(reach, width, i) : reach.doorIds.get(i) ?? -1;
    return l < 0 ? -1 : reach.root[l]!;
  };
}

/** Every kind of mover that asks about reachability. */
const MOVERS: readonly Mover[] = ['survivor', 'free', 'zombie', 'breacher', 'raider', 'crawler'];

/**
 * Builds every mover's reachability now, so the first question during play doesn't pay for a
 * whole-map build (20-30 ms on a 1024 map, the first time raiders or crawlers turn up). Derived
 * data only: it changes no answer, only when the work is done. The app calls it on a new or loaded
 * game, while it's paused.
 */
export function warmReach(grid: Grid, version: number): void {
  for (const m of MOVERS) components(grid, version, m);
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
  const label = components(grid, version, mover);
  const passable = (p: Point) => isPassable(grid, p.x, p.y, mover);
  const { width } = grid;
  const starts = passable(from)
    ? [from]
    : DIRS.map((d) => ({ x: from.x + d.x, y: from.y + d.y })).filter((p) => passable(p) && crossable(grid, from.x, from.y, p.x, p.y, mover));
  const startLabels = new Set(starts.map((p) => label(p.y * width + p.x)));
  return goals.some((g) => passable(g) && startLabels.has(label(g.y * width + g.x)));
}

/** A*'s working arrays, kept between searches for the map size last used; each search takes a new generation. */
let scratch: { size: number; gen: number; seen: Uint32Array; g: Float64Array; from: Int32Array; closed: Uint32Array } | null = null;
function scratchFor(size: number) {
  if (!scratch || scratch.size !== size || scratch.gen >= 0xfffffff0) {
    scratch = { size, gen: 0, seen: new Uint32Array(size), g: new Float64Array(size), from: new Int32Array(size), closed: new Uint32Array(size) };
  }
  scratch.gen += 1;
  return scratch;
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
