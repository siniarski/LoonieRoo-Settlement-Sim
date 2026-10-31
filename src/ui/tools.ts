// Build tools, grouped into the categories of the build bar (B). A tool
// turns a tile into a sim command; it never touches state directly
// (architecture rule 5). Outside build mode there is no tool: left-click
// selects, right-click moves the selected survivor.

import type { Command } from '../sim/commands';
import type { Point } from '../sim/grid';

export type ToolId = 'wall' | 'door' | 'bed' | 'housing' | 'storage' | 'armory' | 'farm' | 'furnish' | 'scavenge' | 'remove' | 'cancel';

export type CategoryId = 'structure' | 'furniture' | 'rooms' | 'orders';

export interface Category {
  id: CategoryId;
  label: string;
}

export const CATEGORIES: readonly Category[] = [
  { id: 'structure', label: 'Structure' },
  { id: 'furniture', label: 'Furniture' },
  { id: 'rooms', label: 'Rooms' },
  { id: 'orders', label: 'Orders' },
];

export interface ToolContext {
  /** Shift held: build or remove instantly instead of designating a job. */
  instant: boolean;
}

export interface Tool {
  id: ToolId;
  label: string;
  category: CategoryId;
  /** Painting tools apply on every tile a drag crosses; others only on click. */
  paints: boolean;
  /** Area tools drag out a box instead, applied to every tile in it on release. */
  area?: true;
  /** The auto-planner: a click asks the planner to furnish the room (several commands at once). */
  furnish?: true;
  /** Whether Shift (instant) does anything for this tool. */
  hasInstant: boolean;
  /** Null for tools that don't map to a single command (Furnish: see `furnish`). */
  command(x: number, y: number, ctx: ToolContext): Command | null;
}

export const TOOLS: readonly Tool[] = [
  {
    id: 'wall',
    label: 'Wall',
    category: 'structure',
    paints: true,
    hasInstant: true,
    command: (x, y, { instant }) => (instant ? { type: 'placeWall', x, y } : { type: 'designateBuild', x, y, structure: 'wall' }),
  },
  {
    id: 'door',
    label: 'Door',
    category: 'structure',
    paints: true,
    hasInstant: true,
    command: (x, y, { instant }) => (instant ? { type: 'placeDoor', x, y } : { type: 'designateBuild', x, y, structure: 'door' }),
  },
  {
    id: 'bed',
    label: 'Bed',
    category: 'furniture',
    paints: true,
    hasInstant: true,
    command: (x, y, { instant }) => (instant ? { type: 'placeBed', x, y } : { type: 'designateBuild', x, y, structure: 'bed' }),
  },
  {
    id: 'housing',
    label: 'Housing',
    category: 'rooms',
    paints: false,
    hasInstant: false,
    command: (x, y) => ({ type: 'designateRoom', x, y, purpose: 'housing' }),
  },
  {
    id: 'storage',
    label: 'Storage',
    category: 'rooms',
    paints: false,
    hasInstant: false,
    command: (x, y) => ({ type: 'designateRoom', x, y, purpose: 'storage' }),
  },
  {
    id: 'armory',
    label: 'Armory',
    category: 'rooms',
    paints: false,
    hasInstant: false,
    command: (x, y) => ({ type: 'designateRoom', x, y, purpose: 'armory' }),
  },
  {
    id: 'farm',
    label: 'Farm',
    category: 'rooms',
    paints: false,
    area: true,
    hasInstant: false,
    command: (x, y) => ({ type: 'designateFarm', x, y }),
  },
  {
    id: 'furnish',
    label: 'Furnish',
    category: 'rooms',
    paints: false,
    furnish: true,
    hasInstant: false,
    command: () => null,
  },
  {
    id: 'scavenge',
    label: 'Scavenge',
    category: 'orders',
    paints: false,
    area: true,
    hasInstant: false,
    command: (x, y) => ({ type: 'designateScavenge', x, y }),
  },
  {
    id: 'remove',
    label: 'Remove',
    category: 'orders',
    paints: true,
    hasInstant: true,
    command: (x, y, { instant }) => (instant ? { type: 'clearTile', x, y } : { type: 'designateRemove', x, y }),
  },
  {
    id: 'cancel',
    label: 'Cancel',
    category: 'orders',
    paints: true,
    hasInstant: false,
    command: (x, y) => ({ type: 'cancelDesignation', x, y }),
  },
];

export function getTool(id: ToolId): Tool {
  const tool = TOOLS.find((t) => t.id === id);
  if (!tool) throw new Error(`unknown tool ${id}`);
  return tool;
}

export function isToolId(id: unknown): id is ToolId {
  return TOOLS.some((t) => t.id === id);
}

export function isCategoryId(id: unknown): id is CategoryId {
  return CATEGORIES.some((c) => c.id === id);
}

export function toolsIn(category: CategoryId): Tool[] {
  return TOOLS.filter((t) => t.category === category);
}

/** Every tile in the box spanned by two corners, row by row (the order commands are applied). */
export function tilesInBox(a: Point, b: Point): Point[] {
  const tiles: Point[] = [];
  for (let y = Math.min(a.y, b.y); y <= Math.max(a.y, b.y); y++) {
    for (let x = Math.min(a.x, b.x); x <= Math.max(a.x, b.x); x++) tiles.push({ x, y });
  }
  return tiles;
}

/** Number keys pick tools within the open category: '1' is the first. */
export function toolForKey(category: CategoryId, key: string): Tool | undefined {
  const n = Number(key);
  return Number.isInteger(n) && n >= 1 ? toolsIn(category)[n - 1] : undefined;
}

/** Next (or previous) category, wrapping around. */
export function stepCategory(category: CategoryId, direction: 1 | -1): CategoryId {
  const i = CATEGORIES.findIndex((c) => c.id === category);
  return CATEGORIES[(i + direction + CATEGORIES.length) % CATEGORIES.length]!.id;
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
