// Doors held open by whoever is passing through.
//
// Colonists (and raiders) open a closed door as they step into the doorway, and it swings shut
// behind them once nobody is in it or stepping into it: grid.passing lists the doors open only
// for someone passing. A door you opened yourself (toggleDoor) isn't in it, so it stays open.
// While it's held open, a zombie can slip through.
// This doesn't bump walkabilityVersion: survivors path through doors either way, and zombies
// check each next tile as they go (zombies.ts), so nothing cached needs throwing away.

import { EDGE_DOOR, edgeBetween, edgeIsOpen, getTile, isLocked, isOpen, tileIndex, type Point } from './grid';
import { BODY_RADIUS, SUB } from './motion';
import type { SimState } from './state';

/**
 * Someone is stepping into this tile: if it's a closed door, open it until they're through. Given
 * where they're coming from, an edge door between the two opens the same way.
 */
export function passThrough(state: SimState, x: number, y: number, hasKey: boolean, from?: Point): void {
  if (from) {
    const e = edgeBetween(state.grid, from.x, from.y, x, y);
    if (e >= 0 && state.grid.edges[e] === EDGE_DOOR && !edgeIsOpen(state.grid, e)) {
      state.grid.edgeOpen.push(e);
      state.grid.edgePassing.push(e);
    }
  }
  if (getTile(state.grid, x, y) !== 'door' || isOpen(state.grid, x, y)) return;
  if (isLocked(state.grid, x, y) && !hasKey) return;
  const i = tileIndex(state.grid, x, y);
  state.grid.open.push(i);
  state.grid.passing.push(i);
}

/** Doors held open for someone passing close once the doorway is clear. Runs every tick. */
export function closeBehind(state: SimState): void {
  closeEdgesBehind(state);
  const { grid } = state;
  if (grid.passing.length === 0) return;
  const occupied = new Set<number>();
  const mark = (x: number, y: number) => occupied.add(y * grid.width + x);
  for (const s of state.survivors) {
    mark(s.pos.x, s.pos.y);
    if (s.move) mark(s.move.to.x, s.move.to.y);
  }
  for (const z of state.zombies) {
    mark(z.pos.x, z.pos.y);
    if (z.path[0]) mark(z.path[0].x, z.path[0].y);
  }
  for (const r of state.residents) mark(r.pos.x, r.pos.y);
  for (const i of [...grid.passing]) {
    if (occupied.has(i)) continue;
    grid.passing.splice(grid.passing.indexOf(i), 1);
    const at = grid.open.indexOf(i);
    if (at >= 0) grid.open.splice(at, 1);
  }
}

/**
 * Edge doors held open for someone passing shut once nobody is crossing: nobody mid-step across
 * it (a colonist's step, a zombie's next tile) and nobody's body touching the doorway.
 */
function closeEdgesBehind(state: SimState): void {
  const { grid } = state;
  if (grid.edgePassing.length === 0) return;
  const crossing = new Set<number>();
  const across = (a: Point, b: Point | undefined) => {
    if (!b) return;
    const e = edgeBetween(grid, a.x, a.y, b.x, b.y);
    if (e >= 0) crossing.add(e);
  };
  for (const s of state.survivors) if (s.move) across(s.move.from, s.move.to);
  for (const z of state.zombies) across(z.pos, z.path[0]);
  const bodies = [...state.survivors, ...state.zombies, ...state.residents].map((b) => b.at);
  for (const e of [...grid.edgePassing]) {
    if (crossing.has(e)) continue;
    // The doorway: the edge's line, from the tile's corner along its north or west side.
    const tile = Math.floor(e / 2);
    const x = (tile % grid.width) * SUB;
    const y = Math.floor(tile / grid.width) * SUB;
    const [x1, y1] = e % 2 === 0 ? [x + SUB, y] : [x, y + SUB];
    const r = BODY_RADIUS + SUB / 8;
    const inDoorway = bodies.some((c) => {
      const px = Math.max(x, Math.min(x1, c.x));
      const py = Math.max(y, Math.min(y1, c.y));
      return (c.x - px) ** 2 + (c.y - py) ** 2 < r * r;
    });
    if (inDoorway) continue;
    grid.edgePassing.splice(grid.edgePassing.indexOf(e), 1);
    const at = grid.edgeOpen.indexOf(e);
    if (at >= 0) grid.edgeOpen.splice(at, 1);
  }
}

/** Opens or shuts the edge door between two tiles side by side. Returns what happened, or why not (null: no door there). */
export function toggleEdgeDoor(state: SimState, a: Point, b: Point): string | null {
  const { grid } = state;
  const e = edgeBetween(grid, a.x, a.y, b.x, b.y);
  if (e < 0 || grid.edges[e] !== EDGE_DOOR) return null;
  const passing = grid.edgePassing.indexOf(e);
  if (passing >= 0) {
    grid.edgePassing.splice(passing, 1); // held open for someone: now it's yours, and stays open
    return 'opened the door';
  }
  const at = grid.edgeOpen.indexOf(e);
  if (at >= 0) {
    grid.edgeOpen.splice(at, 1);
    return 'closed the door';
  }
  grid.edgeOpen.push(e);
  return 'opened the door';
}
