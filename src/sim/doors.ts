// Doors held open by whoever is passing through.
//
// Colonists (and raiders) open a closed door as they step into the doorway, and it swings shut
// behind them once nobody is in it or stepping into it: grid.passing lists the doors open only
// for someone passing. A door you opened yourself (toggleDoor) isn't in it, so it stays open.
// While it's held open, a zombie can slip through.
// This doesn't bump walkabilityVersion: survivors path through doors either way, and zombies
// check each next tile as they go (zombies.ts), so nothing cached needs throwing away.

import { getTile, isLocked, isOpen, tileIndex } from './grid';
import type { SimState } from './state';

/** Someone is stepping into this tile: if it's a closed door, open it until they're through. */
export function passThrough(state: SimState, x: number, y: number, hasKey: boolean): void {
  if (getTile(state.grid, x, y) !== 'door' || isOpen(state.grid, x, y)) return;
  if (isLocked(state.grid, x, y) && !hasKey) return;
  const i = tileIndex(state.grid, x, y);
  state.grid.open.push(i);
  state.grid.passing.push(i);
}

/** Doors held open for someone passing close once the doorway is clear. Runs every tick. */
export function closeBehind(state: SimState): void {
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
