import { describe, expect, it } from 'vitest';
import { createGrid, getTile, inBounds, isWalkable, neighbours4, setTile } from '../src/sim/grid';

describe('grid', () => {
  it('creates a grid filled with floor', () => {
    const grid = createGrid(3, 2);
    expect(grid.tiles).toEqual(['floor', 'floor', 'floor', 'floor', 'floor', 'floor']);
  });

  it('rejects invalid sizes', () => {
    expect(() => createGrid(0, 5)).toThrow();
    expect(() => createGrid(2.5, 5)).toThrow();
  });

  it('checks bounds, including non-integers', () => {
    const grid = createGrid(4, 3);
    expect(inBounds(grid, 0, 0)).toBe(true);
    expect(inBounds(grid, 3, 2)).toBe(true);
    expect(inBounds(grid, 4, 0)).toBe(false);
    expect(inBounds(grid, -1, 0)).toBe(false);
    expect(inBounds(grid, 1.5, 0)).toBe(false);
  });

  it('gets and sets tiles row-major', () => {
    const grid = createGrid(4, 3);
    expect(setTile(grid, 2, 1, 'wall')).toBe(true);
    expect(getTile(grid, 2, 1)).toBe('wall');
    expect(grid.tiles[1 * 4 + 2]).toBe('wall');
    expect(getTile(grid, 9, 9)).toBeUndefined();
  });

  it('reports whether setTile changed anything', () => {
    const grid = createGrid(2, 2);
    expect(setTile(grid, 0, 0, 'floor')).toBe(false);
    expect(setTile(grid, 0, 0, 'door')).toBe(true);
    expect(setTile(grid, 0, 0, 'door')).toBe(false);
    expect(setTile(grid, 5, 5, 'wall')).toBe(false);
  });

  it('treats floor and doors as walkable, walls and out-of-bounds as not', () => {
    const grid = createGrid(3, 1);
    setTile(grid, 1, 0, 'wall');
    setTile(grid, 2, 0, 'door');
    expect(isWalkable(grid, 0, 0)).toBe(true);
    expect(isWalkable(grid, 1, 0)).toBe(false);
    expect(isWalkable(grid, 2, 0)).toBe(true);
    expect(isWalkable(grid, 3, 0)).toBe(false);
  });

  it('lists in-bounds neighbours in N, E, S, W order', () => {
    const grid = createGrid(3, 3);
    expect(neighbours4(grid, 1, 1)).toEqual([
      { x: 1, y: 0 },
      { x: 2, y: 1 },
      { x: 1, y: 2 },
      { x: 0, y: 1 },
    ]);
    expect(neighbours4(grid, 0, 0)).toEqual([
      { x: 1, y: 0 },
      { x: 0, y: 1 },
    ]);
  });
});
