// Tiles, walls, doors. The grid is a row-major array of tile names so it
// serialises as plain JSON. All coordinates are integer tile coordinates.

/** Water blocks everyone and is never part of a room; see worldgen.ts. */
export type Tile = 'floor' | 'wall' | 'door' | 'water' | 'barricade';

/**
 * What the ground looks like under a floor tile. Visual only: nothing in the
 * sim reads it (yet), so roads and forest paths cost the same to walk.
 */
export const GROUNDS = ['grass', 'dirt', 'road', 'forest', 'bridge'] as const;
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
}

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
  };
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
