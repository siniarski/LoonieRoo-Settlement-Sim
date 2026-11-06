// Tiles, walls, doors. The grid is a row-major array of tile names so it
// serialises as plain JSON. All coordinates are integer tile coordinates.
//
// Walls and doors come two ways. A wall (or door) tile fills its tile, as walls always have.
// An edge wall (or door) stands on the border between two tiles: each tile owns its north and
// west edges (edges[i * 2] and edges[i * 2 + 1]); the map's own border needs none. Edge doors
// open and close like door tiles (edgeOpen, edgePassing); crossing between two tiles goes
// through `crossable` (pathfinding.ts).

/** Water blocks everyone and is never part of a room; see worldgen.ts. */
export type Tile = 'floor' | 'wall' | 'door' | 'water' | 'barricade';

/**
 * What the ground looks like under a floor tile. Visual only: nothing in the
 * sim reads it (yet), so roads and forest paths cost the same to walk.
 */
/** Appended only: saves store the index. field: ploughed farmland; paving: yards and squares; boards, tiles, concrete: floors indoors. */
export const GROUNDS = ['grass', 'dirt', 'road', 'forest', 'bridge', 'field', 'paving', 'boards', 'tiles', 'concrete'] as const;
export type Ground = (typeof GROUNDS)[number];

export interface Point {
  x: number;
  y: number;
}

export interface Grid {
  width: number;
  height: number;
  /** Row-major: index = y * width + x. */
  tiles: Tile[];
  /** 1 where a floor tile is dirty, else 0. Same indexing as tiles. Only floor can be dirty. */
  dirt: number[];
  /** Damage taken by the wall or door on each tile (see breach.ts). 0 on floor. */
  damage: number[];
  /** Index into GROUNDS per tile (visual). */
  ground: number[];
  /** 1 where some survivor has seen the tile (fog of war, see fog.ts). New grids start fully explored. */
  explored: number[];
  /**
   * Tile indices with dirt, and with damage, in the order they appeared. Kept in step
   * with the arrays by setDirty / setDamage / setTile, so nothing needs to scan the
   * whole map each tick (the arrays stay for per-tile lookups).
   */
  dirtList: number[];
  damageList: number[];
  /** Locked doors (tile indices): survivors have keys; raiders must break them (visitors.ts). */
  locked: number[];
  /** Structures reinforced with scrap (tile indices; defences.ts). */
  reinforced: number[];
  /** Doors standing open (tile indices). Closed by default; zombies walk through open ones. */
  open: number[];
  /** Of those, the ones held open only for someone passing through: they shut behind (doors.ts). */
  passing: number[];
  /** Per tile, its north edge then its west edge: EDGE_NONE, EDGE_WALL or EDGE_DOOR. */
  edges: number[];
  /** Edge doors standing open (edge indices), and those held open only for someone passing. */
  edgeOpen: number[];
  edgePassing: number[];
  /** Damage on edge walls and doors, by edge index (breach.ts); only damaged ones are listed. */
  edgeDamage: Record<string, number>;
  /** Locked edge doors (edge indices): colonists have keys; raiders must break them. */
  edgeLocked: number[];
  /** Boarded-up windows (edge indices): no sight or climbing through until the boards are battered off. */
  edgeBoarded: number[];
}

export const EDGE_NONE = 0;
export const EDGE_WALL = 1;
export const EDGE_DOOR = 2;
/** Planks across a border: you and colonists climb over; the dead and raiders can't (like a barricade tile). */
export const EDGE_BARRICADE = 3;
/** Glass: blocks walking and bites, not sight; zombies batter it (breach.ts). Broken, it can be climbed through (slowly). */
export const EDGE_WINDOW = 4;
export const EDGE_WINDOW_BROKEN = 5;

export function isWindow(kind: number | undefined): boolean {
  return kind === EDGE_WINDOW || kind === EDGE_WINDOW_BROKEN;
}
export type Side = 'n' | 'w';

export function createGrid(width: number, height: number, fill: Tile = 'floor'): Grid {
  if (!Number.isInteger(width) || !Number.isInteger(height) || width <= 0 || height <= 0) {
    throw new Error(`createGrid: invalid size ${width}x${height}`);
  }
  return {
    width,
    height,
    tiles: new Array<Tile>(width * height).fill(fill),
    dirt: new Array<number>(width * height).fill(0),
    damage: new Array<number>(width * height).fill(0),
    ground: new Array<number>(width * height).fill(0),
    explored: new Array<number>(width * height).fill(1),
    dirtList: [],
    damageList: [],
    locked: [],
    reinforced: [],
    open: [],
    passing: [],
    edges: new Array<number>(width * height * 2).fill(EDGE_NONE),
    edgeOpen: [],
    edgePassing: [],
    edgeDamage: {},
    edgeLocked: [],
    edgeBoarded: [],
  };
}

/** The index of a tile's north or west edge. */
export function edgeIndex(grid: Grid, x: number, y: number, side: Side): number {
  return (y * grid.width + x) * 2 + (side === 'n' ? 0 : 1);
}

/** The edge between two tiles side by side (sharing a border), or -1 if they aren't. */
export function edgeBetween(grid: Grid, ax: number, ay: number, bx: number, by: number): number {
  if (!inBounds(grid, ax, ay) || !inBounds(grid, bx, by)) return -1;
  if (ax === bx && by === ay - 1) return edgeIndex(grid, ax, ay, 'n');
  if (ax === bx && by === ay + 1) return edgeIndex(grid, bx, by, 'n');
  if (ay === by && bx === ax - 1) return edgeIndex(grid, ax, ay, 'w');
  if (ay === by && bx === ax + 1) return edgeIndex(grid, bx, by, 'w');
  return -1;
}

/** What stands on an edge (EDGE_NONE for -1). */
export function edgeKind(grid: Grid, e: number): number {
  return e < 0 ? EDGE_NONE : grid.edges[e] ?? EDGE_NONE;
}

/** Puts a wall, a door or nothing on a tile's north or west edge. Callers that change walkability bump walkabilityVersion. */
export function setEdge(grid: Grid, x: number, y: number, side: Side, kind: number): boolean {
  if (!inBounds(grid, x, y)) return false;
  const e = edgeIndex(grid, x, y, side);
  if (grid.edges[e] === kind) return false;
  grid.edges[e] = kind;
  noteEdgeChange(grid, e);
  drop(grid.edgeOpen, e); // a new door starts shut
  drop(grid.edgePassing, e);
  delete grid.edgeDamage[String(e)]; // and undamaged
  drop(grid.edgeLocked, e); // and unlocked
  drop(grid.edgeBoarded, e); // and unboarded
  return true;
}

// --- Changes, for rooms.ts ------------------------------------------------------
//
// Tiles whose wall, door or edge changed since rooms were last worked out, so rooms can be
// updated round them instead of flooding the whole map. Derived data outside sim state (a
// WeakMap per grid): a cloned or loaded grid has none, and rooms are worked out in full.

const changes = new WeakMap<Grid, number[]>();
/** More changed tiles than this (a new chunk, say), and rooms are worked out in full. */
const MAX_TRACKED = 2048;

/** A tile's wall, door, lock or edge changed (setTile and setEdge note their own; anything that changes those another way must call this). */
export function noteChange(grid: Grid, i: number): void {
  const list = changes.get(grid);
  if (list && list.length <= MAX_TRACKED) list.push(i);
  const log = reachLogs.get(grid);
  if (log) {
    const x = i % grid.width;
    log.chunks.push(Math.floor((i - x) / grid.width / REACH_CHUNK) * Math.ceil(grid.width / REACH_CHUNK) + Math.floor(x / REACH_CHUNK));
    // Too many to keep: forget them, and anyone behind works it all out again.
    if (log.chunks.length > MAX_REACH_LOG) {
      log.start += log.chunks.length;
      log.chunks = [];
    }
  }
}

// --- Changes, for pathfinding.ts's reachability ---------------------------------------
//
// The chunks (REACH_CHUNK tiles a side) where a wall, door, lock or edge changed, as a log with
// positions: each kind of mover's labels remember how far they've read, and re-flood only the
// chunks changed since. Derived data outside sim state, like the room changes: a cloned or loaded
// grid has no log, and its labels are worked out in full.

/** Side of a reachability chunk, in tiles. */
export const REACH_CHUNK = 32;
const MAX_REACH_LOG = 100000;
const reachLogs = new WeakMap<Grid, { start: number; chunks: number[] }>();

/** Where the log is now (starting it, if it wasn't), to read from later with reachChangesSince. */
export function reachCursor(grid: Grid): number {
  let log = reachLogs.get(grid);
  if (!log) {
    log = { start: 0, chunks: [] };
    reachLogs.set(grid, log);
  }
  return log.start + log.chunks.length;
}

/** The chunks changed since `cursor` (repeats possible), or null if the log doesn't go back that far. */
export function reachChangesSince(grid: Grid, cursor: number): number[] | null {
  const log = reachLogs.get(grid);
  if (!log || cursor < log.start) return null;
  return log.chunks.slice(cursor - log.start);
}

/** An edge changed: both its tiles. */
export function noteEdgeChange(grid: Grid, e: number): void {
  const tile = Math.floor(e / 2);
  noteChange(grid, tile);
  noteChange(grid, e % 2 === 0 ? tile - grid.width : tile - 1);
}

/** The tiles changed since the last call (and starts recording); null if unknown or too many. */
export function takeChanges(grid: Grid): number[] | null {
  const list = changes.get(grid);
  changes.set(grid, []);
  return list && list.length <= MAX_TRACKED ? list : null;
}

export function edgeIsOpen(grid: Grid, e: number): boolean {
  return grid.edgeOpen.includes(e);
}

export function inBounds(grid: Grid, x: number, y: number): boolean {
  return Number.isInteger(x) && Number.isInteger(y) && x >= 0 && y >= 0 && x < grid.width && y < grid.height;
}

export function tileIndex(grid: Grid, x: number, y: number): number {
  return y * grid.width + x;
}

/** Returns undefined when out of bounds. */
export function getTile(grid: Grid, x: number, y: number): Tile | undefined {
  return inBounds(grid, x, y) ? grid.tiles[tileIndex(grid, x, y)] : undefined;
}

/**
 * Sets a tile. Returns true if the tile changed. Callers that change
 * walkability must bump `walkabilityVersion` (commands.ts does this).
 */
export function setTile(grid: Grid, x: number, y: number, tile: Tile): boolean {
  if (!inBounds(grid, x, y)) return false;
  const i = tileIndex(grid, x, y);
  if (grid.tiles[i] === tile) return false;
  grid.tiles[i] = tile;
  noteChange(grid, i);
  if (tile !== 'floor' && grid.dirt[i] === 1) {
    grid.dirt[i] = 0;
    drop(grid.dirtList, i);
  }
  setDamage(grid, i, 0); // a new (or removed) structure starts undamaged
  drop(grid.locked, i); // and unlocked
  drop(grid.reinforced, i); // and plain
  drop(grid.open, i); // and shut
  drop(grid.passing, i);
  return true;
}

export function isDirty(grid: Grid, x: number, y: number): boolean {
  return inBounds(grid, x, y) && grid.dirt[tileIndex(grid, x, y)] === 1;
}

/** Marks a floor tile dirty or clean. Returns true if it changed. Non-floor tiles can't get dirty. */
export function setDirty(grid: Grid, x: number, y: number, dirty: boolean): boolean {
  if (getTile(grid, x, y) !== 'floor' && dirty) return false;
  if (isDirty(grid, x, y) === dirty || !inBounds(grid, x, y)) return false;
  const i = tileIndex(grid, x, y);
  grid.dirt[i] = dirty ? 1 : 0;
  if (dirty) grid.dirtList.push(i);
  else drop(grid.dirtList, i);
  return true;
}

/** Sets the damage on a tile index, keeping damageList in step. */
export function setDamage(grid: Grid, i: number, value: number): void {
  const was = grid.damage[i]!;
  if (was === value) return;
  grid.damage[i] = value;
  if (was === 0) grid.damageList.push(i);
  else if (value === 0) drop(grid.damageList, i);
}

function drop(list: number[], i: number): void {
  const at = list.indexOf(i);
  if (at >= 0) list.splice(at, 1);
}

/** Doors are always walkable (milestone 1 decision, see NOTES.md). */
export function isWalkableTile(tile: Tile): boolean {
  return tile === 'floor' || tile === 'door' || tile === 'barricade';
}

export function isLocked(grid: Grid, x: number, y: number): boolean {
  return inBounds(grid, x, y) && grid.locked.includes(tileIndex(grid, x, y));
}

/** A door standing open. */
export function isOpen(grid: Grid, x: number, y: number): boolean {
  return inBounds(grid, x, y) && grid.open.includes(tileIndex(grid, x, y));
}

export function isWalkable(grid: Grid, x: number, y: number): boolean {
  const tile = getTile(grid, x, y);
  return tile !== undefined && isWalkableTile(tile);
}

/** In-bounds 4-way neighbours in a fixed order: N, E, S, W. */
export function neighbours4(grid: Grid, x: number, y: number): Point[] {
  const candidates: Point[] = [
    { x, y: y - 1 },
    { x: x + 1, y },
    { x, y: y + 1 },
    { x: x - 1, y },
  ];
  return candidates.filter((p) => inBounds(grid, p.x, p.y));
}
