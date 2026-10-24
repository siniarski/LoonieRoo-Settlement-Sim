// Placement tools. A tool turns a tile into a sim command; it never touches
// state directly (architecture rule 5).

import type { Command } from '../sim/commands';
import type { Point } from '../sim/grid';

export type ToolId = 'wall' | 'door' | 'clear';

export interface Tool {
  id: ToolId;
  label: string;
  /** KeyboardEvent.key that selects this tool. */
  key: string;
  command(x: number, y: number): Command;
}

export const TOOLS: readonly Tool[] = [
  { id: 'wall', label: 'Wall', key: '1', command: (x, y) => ({ type: 'placeWall', x, y }) },
  { id: 'door', label: 'Door', key: '2', command: (x, y) => ({ type: 'placeDoor', x, y }) },
  { id: 'clear', label: 'Clear', key: '3', command: (x, y) => ({ type: 'clearTile', x, y }) },
];

export function getTool(id: ToolId): Tool {
  const tool = TOOLS.find((t) => t.id === id);
  if (!tool) throw new Error(`unknown tool ${id}`);
  return tool;
}

/**
 * Tiles on the line from a to b, inclusive (Bresenham). Used so a fast drag
 * paints every tile it crosses instead of skipping some between frames.
 */
export function tilesOnLine(a: Point, b: Point): Point[] {
  const points: Point[] = [];
  const dx = Math.abs(b.x - a.x);
  const dy = -Math.abs(b.y - a.y);
  const sx = a.x < b.x ? 1 : -1;
  const sy = a.y < b.y ? 1 : -1;
  let err = dx + dy;
  let { x, y } = a;
  for (;;) {
    points.push({ x, y });
    if (x === b.x && y === b.y) return points;
    const e2 = 2 * err;
    if (e2 >= dy) {
      err += dy;
      x += sx;
    }
    if (e2 <= dx) {
      err += dx;
      y += sy;
    }
  }
}
