// Sight: what can be seen from where, and how far.
//
//   sight lines  from tile to tile (Bresenham): blocked by wall and door tiles in between (an
//                open door tile doesn't block) and by any border sight can't cross
//                (pathfinding.ts seesThrough: edge walls, shut edge doors, boarded windows; glass
//                lets it through). The far end is seen even if it's a wall.
//   field        the tiles within a radius with a clear sight line (fieldOfView): what a
//                survivor sees, and so what fog.ts reveals.
//   how far      SIGHT_RADIUS by day, NIGHT_SIGHT at night (plus towers and the vigilant trait);
//                from a tower you see over walls and roofs (sightOf);
//                the dead see further (ZOMBIE_SIGHT, ZOMBIE_NIGHT_SIGHT) but need the same line.
//   noticing     the dead (not the night's waves, which always know where you are) notice a
//                survivor they can see (zombieSees: by day ZOMBIE_SIGHT, at night less, half
//                that if you're sneaking) or hear moving (heardFrom: running RUN_HEARD tiles off,
//                walking WALK_HEARD, sneaking SNEAK_HEARD, standing still not at all), and keep
//                after whoever they were already chasing. Never beyond AGGRO_RADIUS (zombies.ts).
//                Loud things (noise.ts) carry much further and draw them over.
//   seen now     visibleNow: what the survivor you control can see this tick (all survivors
//                when nobody's controlled): derived, cached per state and tick, never stored.
//                The renderer dims explored tiles outside it and hides the dead you can't see.

import { inBounds, isOpen, tileIndex, type Point } from './grid';
import { mainsLit } from './timeline';
import { edgeBetween } from './grid';
import { seesThrough } from './pathfinding';
import { controlled } from './control';
import { hasTrait, VIGILANT_SIGHT } from './skills';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { isNight } from './time';
import { TOWER_SIGHT, onTower } from './towers';

export const SIGHT_RADIUS = 8;
export const NIGHT_SIGHT = 5;
export const ZOMBIE_SIGHT = 12;
export const ZOMBIE_NIGHT_SIGHT = 6;
/** How far off (Manhattan) the dead hear you move: running, walking, sneaking. */
export const RUN_HEARD = 8;
export const WALK_HEARD = 4;
export const SNEAK_HEARD = 1;

/** Does this tile stop sight passing through it (a wall, a shut door; water and barricades don't)? */
function opaque(grid: SimState['grid'], x: number, y: number): boolean {
  const t = grid.tiles[tileIndex(grid, x, y)];
  return t === 'wall' || (t === 'door' && !isOpen(grid, x, y));
}

/** A step from one tile to the next (side by side or diagonal) that sight can make: for a diagonal, either way round. */
function stepSeen(grid: SimState['grid'], ax: number, ay: number, bx: number, by: number): boolean {
  const clear = (px: number, py: number, qx: number, qy: number) => seesThrough(grid, edgeBetween(grid, px, py, qx, qy));
  if (ax === bx || ay === by) return clear(ax, ay, bx, by);
  const viaX = clear(ax, ay, bx, ay) && clear(bx, ay, bx, by) && !opaque(grid, bx, ay);
  const viaY = clear(ax, ay, ax, by) && clear(ax, by, bx, by) && !opaque(grid, ax, by);
  return viaX || viaY;
}

/** Can someone at `a` see `b`? (The far end may be a wall: you see the wall.) */
export function sightLine(grid: SimState['grid'], a: Point, b: Point): boolean {
  let x = a.x;
  let y = a.y;
  const dx = Math.abs(b.x - a.x);
  const dy = -Math.abs(b.y - a.y);
  const sx = a.x < b.x ? 1 : -1;
  const sy = a.y < b.y ? 1 : -1;
  let err = dx + dy;
  while (x !== b.x || y !== b.y) {
    const e2 = 2 * err;
    const px = x;
    const py = y;
    if (e2 >= dy) {
      err += dy;
      x += sx;
    }
    if (e2 <= dx) {
      err += dx;
      y += sy;
    }
    if (!stepSeen(grid, px, py, x, y)) return false;
    if ((x !== b.x || y !== b.y) && opaque(grid, x, y)) return false;
  }
  return true;
}

/**
 * Tile indices within `r` of `p` with a clear sight line from it (from up high, `overWalls`: all
 * of them). Tiles `skip` says no to aren't looked at (fog.ts: those already explored).
 */
export function fieldOfView(grid: SimState['grid'], p: Point, r: number, overWalls = false, skip?: (i: number) => boolean): number[] {
  const out: number[] = [];
  for (let y = Math.max(0, p.y - r); y <= Math.min(grid.height - 1, p.y + r); y++) {
    for (let x = Math.max(0, p.x - r); x <= Math.min(grid.width - 1, p.x + r); x++) {
      if ((x - p.x) ** 2 + (y - p.y) ** 2 > r * r || skip?.(y * grid.width + x)) continue;
      if (overWalls || sightLine(grid, p, { x, y })) out.push(y * grid.width + x);
    }
  }
  return out;
}

/** How far a survivor sees: less at night (unless the lights are on indoors: timeline.ts); more from a tower, or with the vigilant trait. */
export function sightRadius(state: SimState, s: Survivor): number {
  const base = onTower(state, s) ? TOWER_SIGHT : isNight(state.tick) && !mainsLit(state, s.pos) ? NIGHT_SIGHT : SIGHT_RADIUS;
  return base + (hasTrait(s, 'vigilant') ? VIGILANT_SIGHT : 0);
}

/** What a survivor sees now (tile indices): from a tower, everything in range. */
export function sightOf(state: SimState, s: Survivor, skip?: (i: number) => boolean): number[] {
  return fieldOfView(state.grid, s.pos, sightRadius(state, s), onTower(state, s), skip);
}

/** Is this survivor the one you control, sneaking? */
export function isSneaking(state: SimState, s: Survivor): boolean {
  return state.control?.survivorId === s.id && state.control.sneaking === true;
}

/** How far off (Manhattan) a survivor's movement can be heard, 0 standing still (traits: graceful, clumsy). */
export function heardFrom(state: SimState, s: Survivor): number {
  const c = state.control?.survivorId === s.id ? state.control : null;
  const base = !c ? (s.move ? WALK_HEARD : 0) : c.dx === 0 && c.dy === 0 ? 0 : c.sneaking ? SNEAK_HEARD : c.running ? RUN_HEARD : WALK_HEARD;
  // Graceful: half as far; clumsy: half as far again.
  return hasTrait(s, 'graceful') ? Math.floor(base / 2) : hasTrait(s, 'clumsy') ? Math.round((base * 3) / 2) : base;
}

/** Can a zombie at `z` see a survivor at `s` (`sneaking`: half as far)? */
export function zombieSees(state: SimState, z: Point, s: Point, sneaking = false): boolean {
  const r = (isNight(state.tick) ? ZOMBIE_NIGHT_SIGHT : ZOMBIE_SIGHT) / (sneaking ? 2 : 1);
  if ((z.x - s.x) ** 2 + (z.y - s.y) ** 2 > r * r) return false;
  return inBounds(state.grid, s.x, s.y) && sightLine(state.grid, z, s);
}

const seen = new WeakMap<SimState, { tick: number; who: string; mask: Uint8Array; set: number[] }>();

/**
 * What can be seen now: by the survivor you control, or by any survivor when nobody is
 * controlled. Per tile 1 or 0. Derived, cached per state and tick (for the renderer).
 */
export function visibleNow(state: SimState): Uint8Array {
  const you = controlled(state);
  const lookers = you ? [you] : state.survivors;
  const who = lookers.map((s) => `${s.id}@${s.pos.x},${s.pos.y}`).join('|');
  const hit = seen.get(state);
  if (hit && hit.tick === state.tick && hit.who === who) return hit.mask;
  // The same mask each time (a new one was a megabyte a tick on a 1024 map): clear what was set
  // last time, then set what's seen now. Callers read it, and don't keep it past the tick.
  const size = state.grid.width * state.grid.height;
  const mask = hit && hit.mask.length === size ? hit.mask : new Uint8Array(size);
  if (hit && hit.mask === mask) for (const i of hit.set) mask[i] = 0;
  const set: number[] = [];
  for (const s of lookers) for (const i of sightOf(state, s)) if (mask[i] === 0) {
    mask[i] = 1;
    set.push(i);
  }
  seen.set(state, { tick: state.tick, who, mask, set });
  return mask;
}
