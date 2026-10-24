import { describe, expect, it } from 'vitest';
import type { Point } from '../src/sim/grid';
import { findPath } from '../src/sim/pathfinding';
import { loadScenario } from '../src/sim/scenario';

/** Builds a grid from rows; 'A' marks start and 'B' marks goal (both floor). */
function setup(rows: string[]) {
  let start: Point = { x: 0, y: 0 };
  let goal: Point = { x: 0, y: 0 };
  const map = rows.map((row, y) =>
    [...row]
      .map((c, x) => {
        if (c === 'A') start = { x, y };
        if (c === 'B') goal = { x, y };
        return c === 'A' || c === 'B' ? '.' : c;
      })
      .join(''),
  );
  return { grid: loadScenario({ seed: 0, map }).grid, start, goal, rows };
}

/** Draws the path with '*' so failures show the route in the terminal. */
function show(rows: string[], path: Point[] | null): string {
  const out = rows.map((r) => [...r]);
  for (const p of path ?? []) {
    const row = out[p.y]!;
    if (row[p.x] !== 'B') row[p.x] = '*';
  }
  return out.map((r) => r.join('')).join('\n');
}

function isConnected(start: Point, path: Point[]): boolean {
  let prev = start;
  for (const p of path) {
    if (Math.abs(p.x - prev.x) + Math.abs(p.y - prev.y) !== 1) return false;
    prev = p;
  }
  return true;
}

describe('findPath', () => {
  it('walks a straight line, excluding start and including goal', () => {
    const { grid, start, goal } = setup(['A...B']);
    expect(findPath(grid, start, goal)).toEqual([
      { x: 1, y: 0 },
      { x: 2, y: 0 },
      { x: 3, y: 0 },
      { x: 4, y: 0 },
    ]);
  });

  it('returns [] when already at the goal', () => {
    const { grid } = setup(['...']);
    expect(findPath(grid, { x: 1, y: 0 }, { x: 1, y: 0 })).toEqual([]);
  });

  it('goes around walls with an optimal, connected path', () => {
    const rows = [
      '.......',
      '.A.#.B.',
      '...#...',
      '.###...',
      '.......',
    ];
    const { grid, start, goal } = setup(rows);
    const path = findPath(grid, start, goal);
    expect(path, show(rows, path)).not.toBeNull();
    expect(isConnected(start, path!), show(rows, path)).toBe(true);
    expect(path!.at(-1)).toEqual(goal);
    // Over the top: up 1, right 4, down 1.
    expect(path!.length, show(rows, path)).toBe(6);
  });

  it('walks through doors', () => {
    const rows = [
      '#####',
      'A.D.B',
      '#####',
    ];
    const { grid, start, goal } = setup(rows);
    expect(findPath(grid, start, goal)).toHaveLength(4);
  });

  it('returns null when the goal is walled off', () => {
    const rows = [
      'A..###',
      '...#B#',
      '...###',
    ];
    const { grid, start, goal } = setup(rows);
    expect(findPath(grid, start, goal)).toBeNull();
  });

  it('returns null for unwalkable or out-of-bounds goals', () => {
    const { grid } = setup(['A.#']);
    expect(findPath(grid, { x: 0, y: 0 }, { x: 2, y: 0 })).toBeNull();
    expect(findPath(grid, { x: 0, y: 0 }, { x: 9, y: 0 })).toBeNull();
  });

  it('can leave an unwalkable start tile', () => {
    const { grid } = setup(['#..']);
    expect(findPath(grid, { x: 0, y: 0 }, { x: 2, y: 0 })).toHaveLength(2);
  });

  it('is deterministic and Manhattan-optimal in open space', () => {
    const rows = Array.from({ length: 20 }, () => '.'.repeat(30));
    const { grid } = setup(rows);
    const a = findPath(grid, { x: 2, y: 3 }, { x: 27, y: 17 });
    const b = findPath(grid, { x: 2, y: 3 }, { x: 27, y: 17 });
    expect(a).toEqual(b);
    expect(a).toHaveLength(25 + 14);
  });

  it('solves a maze', () => {
    const rows = [
      'A.#.......',
      '#.#.####.#',
      '#.#.#..#.#',
      '#...#.##.#',
      '#####.#..#',
      '#.....#.##',
      '#.#####..B',
    ];
    const { grid, start, goal } = setup(rows);
    const path = findPath(grid, start, goal);
    expect(path, show(rows, path)).not.toBeNull();
    expect(isConnected(start, path!)).toBe(true);
    expect(path!.at(-1)).toEqual(goal);
  });
});
