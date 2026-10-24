// Tiles, walls, doors. The grid is a row-major array of tile names so it
// serialises as plain JSON. All coordinates are integer tile coordinates.

export type Tile = 'floor' | 'wall' | 'door';

export interface Point {
  x: number;
  y: number;
}

export interface Grid {
  width: number;
  height: number;
  /** Row-major: index = y * width + x. */
  tiles: Tile[];
}

export function createGrid(width: number, height: number, fill: Tile = 'floor'): Grid {
  if (!Number.isInteger(width) || !Number.isInteger(height) || width <= 0 || height <= 0) {
    throw new Error(`createGrid: invalid size ${width}x${height}`);
  }
  return { width, height, tiles: new Array<Tile>(width * height).fill(fill) };
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
  return true;
}

/** Doors are always walkable (milestone 1 decision, see NOTES.md). */
export function isWalkableTile(tile: Tile): boolean {
  return tile === 'floor' || tile === 'door';
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
