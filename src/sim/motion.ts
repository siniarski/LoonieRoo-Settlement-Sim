// Motion: where movers are, finer than a tile. The world stays on the grid (walls, doors,
// furniture, rooms, paths); movers (survivors, zombies, dogs, animals, residents) also have a
// precise position, `at`, in SUB units per tile (fixed point: integers, so replays, saves and a
// future port stay exact; no floats that differ between engines).
//   at    the mover's centre now, in SUB units (a tile's centre is tile * SUB + HALF)
//   was   at as it was when this tick began, so the renderer can blend between the two
//   pos   the tile under `at` (most of the sim only asks which tile someone is on)
// Movers stepping tile to tile (a Movement in progress) get `at` from the step (syncStep).
// Free movers (moving under their own steering: control.ts, zombies.ts) own `at`, and their
// `pos` follows it; if other code moves them by tile (an eviction), `at` snaps to follow.

import { inBounds, type Grid, type Point , EDGE_WINDOW_BROKEN } from './grid';
import { crossable, isPassable, openBetween, type Mover } from './pathfinding';
import type { Movement } from './survivors';

export const SUB = 256;
export const HALF = SUB / 2;
/** A mover's body: a circle this wide (radius), for walls and for pushing apart. */
export const BODY_RADIUS = 77; // 0.3 of a tile
/** A melee swing (yours, or a guard's) reaches this far, centre to centre: 1.25 tiles. */
export const SWING_REACH = 320;
/** A blow or a bite reaches this far, centre to centre: orthogonal neighbours' centres are 1 tile apart, diagonal ones 1.41. */
export const REACH = 282;

/**
 * Within reach, centre to centre. Given the grid, not through a wall: an edge wall or shut door
 * between their tiles (grid.ts) stops a blow or a bite (pathfinding.ts openBetween).
 */
export function inReach(a: { at: Point }, b: { at: Point }, reach = REACH, grid?: Grid): boolean {
  return distance2(a.at, b.at) <= reach * reach && (!grid || openBetween(grid, tileOf(a.at), tileOf(b.at)));
}

/**
 * The nearest of `things` within `reach` of `from` and inside the arc in front (within 60 degrees
 * of `facing` either way). Integer maths: dot > 0 and 4 * dot^2 >= |facing|^2 * |d|^2 (cos 60 = 1/2).
 */
export function nearestInArc<T extends { at: Point }>(things: readonly T[], from: Point, facing: Point, reach = SWING_REACH, grid?: Grid): T | undefined {
  const f2 = facing.x * facing.x + facing.y * facing.y;
  let best: T | undefined;
  let bestD2 = Infinity;
  for (const t of things) {
    const dx = t.at.x - from.x;
    const dy = t.at.y - from.y;
    const d2 = dx * dx + dy * dy;
    if (d2 > reach * reach || d2 >= bestD2) continue;
    const dot = dx * facing.x + dy * facing.y;
    if (d2 > 0 && (dot <= 0 || 4 * dot * dot < f2 * d2)) continue;
    if (grid && !openBetween(grid, tileOf(from), tileOf(t.at))) continue; // not through a wall
    best = t;
    bestD2 = d2;
  }
  return best;
}

export interface Body {
  pos: Point;
  move: Movement | null;
  at: Point;
  was: Point;
}

export function centre(p: Point): Point {
  return { x: p.x * SUB + HALF, y: p.y * SUB + HALF };
}

export function tileOf(at: Point): Point {
  return { x: Math.floor(at.x / SUB), y: Math.floor(at.y / SUB) };
}

/** A new mover's position fields, on a tile. */
export function placedAt(tile: Point): { at: Point; was: Point } {
  return { at: centre(tile), was: centre(tile) };
}

/** Put a mover on a tile now (teleports: spawning, evictions). */
export function place(e: Body, tile: Point): void {
  e.pos = { x: tile.x, y: tile.y };
  e.move = null;
  e.at = centre(tile);
  e.was = centre(tile);
}

/** `at` from a tile step in progress (or the tile's centre when standing). Integer maths. */
export function syncStep(e: Body): void {
  const m = e.move;
  if (!m) {
    e.at = centre(e.pos);
    return;
  }
  const from = centre(m.from);
  const to = centre(m.to);
  e.at = {
    x: from.x + Math.trunc(((to.x - from.x) * m.progress) / m.duration),
    y: from.y + Math.trunc(((to.y - from.y) * m.progress) / m.duration),
  };
}

/**
 * For a free mover: if someone moved it by tile, follow. `slack` tiles of difference are
 * allowed (a zombie keeps its old tile as pos until a step lands, so it can be a tile off).
 */
export function followTile(e: Body, slack = 0): void {
  const t = tileOf(e.at);
  if (Math.max(Math.abs(t.x - e.pos.x), Math.abs(t.y - e.pos.y)) > slack) e.at = centre(e.pos);
}

export function distance2(a: Point, b: Point): number {
  const dx = a.x - b.x;
  const dy = a.y - b.y;
  return dx * dx + dy * dy;
}

/** Can a body of BODY_RADIUS stand with its centre here (every tile it overlaps passable)? */
export function fits(grid: Grid, at: Point, mover: Mover, r = BODY_RADIUS): boolean {
  const x0 = Math.floor((at.x - r) / SUB);
  const x1 = Math.floor((at.x + r) / SUB);
  const y0 = Math.floor((at.y - r) / SUB);
  const y1 = Math.floor((at.y + r) / SUB);
  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) {
      if (!inBounds(grid, x, y) || !isPassable(grid, x, y, mover)) return false;
    }
  }
  // Edge walls (and edge doors this mover can't get through): the circle mustn't reach the line.
  for (let y = y0; y <= y1 + 1; y++) {
    for (let x = x0; x <= x1 + 1; x++) {
      if (!inBounds(grid, x, y)) continue;
      // North edge: from (x, y) to (x + 1, y) in tiles; west edge: from (x, y) to (x, y + 1).
      if (y > 0 && !crossable(grid, x, y, x, y - 1, mover) && touches(at, r, x * SUB, y * SUB, (x + 1) * SUB, y * SUB)) return false;
      if (x > 0 && !crossable(grid, x, y, x - 1, y, mover) && touches(at, r, x * SUB, y * SUB, x * SUB, (y + 1) * SUB)) return false;
    }
  }
  return true;
}

/** Does a circle at `c` of radius r reach an axis-aligned segment (x0, y0)-(x1, y1)? Integers. */
function touches(c: Point, r: number, x0: number, y0: number, x1: number, y1: number): boolean {
  const px = Math.max(x0, Math.min(x1, c.x));
  const py = Math.max(y0, Math.min(y1, c.y));
  return (c.x - px) ** 2 + (c.y - py) ** 2 < r * r;
}

/**
 * Move a body by (dx, dy) SUB units, stopping at walls: each axis on its own, so it slides
 * along a wall rather than sticking to it. Big moves go in steps no longer than a radius,
 * so nothing tunnels through a wall. Returns the new centre.
 */
export function slide(grid: Grid, at: Point, dx: number, dy: number, mover: Mover): Point {
  let x = at.x;
  let y = at.y;
  const steps = Math.max(1, Math.ceil(Math.max(Math.abs(dx), Math.abs(dy)) / BODY_RADIUS));
  for (let i = 1; i <= steps; i++) {
    const sx = Math.trunc((dx * i) / steps) - Math.trunc((dx * (i - 1)) / steps);
    const sy = Math.trunc((dy * i) / steps) - Math.trunc((dy * (i - 1)) / steps);
    if (sx !== 0 && fits(grid, { x: x + sx, y }, mover)) x += sx;
    if (sy !== 0 && fits(grid, { x, y: y + sy }, mover)) y += sy;
  }
  return { x, y };
}

/** A vector of length `len` (SUB units) towards (dx, dy), in integers. (0, 0) stays (0, 0). */
export function toward(dx: number, dy: number, len: number): Point {
  const d = Math.sqrt(dx * dx + dy * dy); // sqrt is exact everywhere; hypot need not be
  if (d === 0) return { x: 0, y: 0 };
  return { x: Math.round((dx * len) / d), y: Math.round((dy * len) / d) };
}

/** Is a body in a broken window's frame (its circle on the line)? Climbing through is slow (control.ts). */
export function inWindowFrame(grid: Grid, at: Point, r = BODY_RADIUS): boolean {
  const x0 = Math.floor((at.x - r) / SUB);
  const x1 = Math.floor((at.x + r) / SUB);
  const y0 = Math.floor((at.y - r) / SUB);
  const y1 = Math.floor((at.y + r) / SUB);
  for (let y = y0; y <= y1 + 1; y++) {
    for (let x = x0; x <= x1 + 1; x++) {
      if (!inBounds(grid, x, y)) continue;
      const i = (y * grid.width + x) * 2;
      if (grid.edges[i] === EDGE_WINDOW_BROKEN && touches(at, r, x * SUB, y * SUB, (x + 1) * SUB, y * SUB)) return true;
      if (grid.edges[i + 1] === EDGE_WINDOW_BROKEN && touches(at, r, x * SUB, y * SUB, x * SUB, (y + 1) * SUB)) return true;
    }
  }
  return false;
}
