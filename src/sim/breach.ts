// Breaching: zombies batter the doors and walls between them and survivors.
//
// Damage is kept per tile in `grid.damage` (same indexing as tiles; only walls
// and doors carry any). At STRUCTURE_HEALTH the structure breaks: the tile
// becomes floor, and a blueprint to rebuild it is left in its place. Damaged
// structures get repair jobs (build type, see jobs.ts), done from next door.

import { addBlueprint, blueprintAt, cancelBlueprint } from './designations';
import { logEvent } from './events';
import { getTile, setTile, tileIndex, type Tile } from './grid';
import type { SimState } from './state';

/** Damage a structure takes before it breaks. Zombies hit for 40, every 10 ticks. */
export const STRUCTURE_HEALTH: Record<'wall' | 'door', number> = { door: 400, wall: 1200 };
/** Damage a repairer removes per tick of work: half a zombie's rate, so repairs slow a breach but can't stop it alone. */
export const REPAIR_PER_TICK = 2;

export function isStructure(tile: Tile | undefined): tile is 'wall' | 'door' {
  return tile === 'wall' || tile === 'door';
}

export function structureDamage(state: SimState, x: number, y: number): number {
  return isStructure(getTile(state.grid, x, y)) ? state.grid.damage[tileIndex(state.grid, x, y)]! : 0;
}

/** Hits the wall or door at (x, y). Returns true if it broke. */
export function damageStructure(state: SimState, x: number, y: number, amount: number): boolean {
  const tile = getTile(state.grid, x, y);
  if (!isStructure(tile)) return false;
  const i = tileIndex(state.grid, x, y);
  state.grid.damage[i] = state.grid.damage[i]! + amount;
  if (state.grid.damage[i]! < STRUCTURE_HEALTH[tile]) return false;
  setTile(state.grid, x, y, 'floor'); // also clears the damage
  state.walkabilityVersion += 1;
  const planned = blueprintAt(state, x, y);
  if (planned) cancelBlueprint(state, planned);
  addBlueprint(state, x, y, tile);
  logEvent(state, `Zombies broke through the ${tile} at (${x}, ${y})`);
  return true;
}

/** Undoes up to `amount` damage. Returns the damage left. */
export function repairStructure(state: SimState, x: number, y: number, amount: number): number {
  if (!isStructure(getTile(state.grid, x, y))) return 0;
  const i = tileIndex(state.grid, x, y);
  state.grid.damage[i] = Math.max(0, state.grid.damage[i]! - amount);
  return state.grid.damage[i]!;
}
