// Breaching: zombies batter the doors and walls between them and survivors.
//
// Damage is kept per tile in `grid.damage` (same indexing as tiles; only walls
// and doors carry any). At STRUCTURE_HEALTH the structure breaks: the tile
// becomes floor, and a blueprint to rebuild it is left in its place. Damaged
// structures get repair jobs (build type, see jobs.ts), done from next door.

import { addBlueprint, blueprintAt, cancelBlueprint } from './designations';
import { logEvent } from './events';
import { EDGE_BARRICADE, EDGE_DOOR, EDGE_NONE, EDGE_WALL, getTile, noteEdgeChange, setDamage, setTile, tileIndex, type Tile } from './grid';
import type { SimState } from './state';

/** Damage a structure takes before it breaks. Zombies hit for 40, every 10 ticks. */
export const STRUCTURE_HEALTH: Record<'wall' | 'door' | 'barricade', number> = { door: 400, wall: 1200, barricade: 600 };
/** Damage a repairer removes per tick of work: half a zombie's rate, so repairs slow a breach but can't stop it alone. */
export const REPAIR_PER_TICK = 2;

/** What it takes to break this structure (reinforced ones take more; defences.ts). */
export function structureHealth(grid: SimState['grid'], i: number): number {
  const tile = grid.tiles[i]!;
  if (!isStructure(tile)) return 0;
  return grid.reinforced.includes(i) ? STRUCTURE_HEALTH[tile] * 2.5 : STRUCTURE_HEALTH[tile];
}

export function isStructure(tile: Tile | undefined): tile is 'wall' | 'door' | 'barricade' {
  return tile === 'wall' || tile === 'door' || tile === 'barricade';
}

export function structureDamage(state: SimState, x: number, y: number): number {
  return isStructure(getTile(state.grid, x, y)) ? state.grid.damage[tileIndex(state.grid, x, y)]! : 0;
}

/** Hits the wall or door at (x, y). Returns true if it broke. */
export function damageStructure(state: SimState, x: number, y: number, amount: number): boolean {
  const tile = getTile(state.grid, x, y);
  if (!isStructure(tile)) return false;
  const i = tileIndex(state.grid, x, y);
  setDamage(state.grid, i, state.grid.damage[i]! + amount);
  if (state.grid.damage[i]! < structureHealth(state.grid, i)) return false;
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
  setDamage(state.grid, i, Math.max(0, state.grid.damage[i]! - amount));
  return state.grid.damage[i]!;
}

// --- Edge walls and doors (grid.ts) --------------------------------------------

/** What it takes to break an edge wall or door: the same as the tile kinds. */
export function edgeHealth(grid: SimState['grid'], e: number): number {
  const kind = grid.edges[e];
  return kind === EDGE_WALL ? STRUCTURE_HEALTH.wall : kind === EDGE_DOOR ? STRUCTURE_HEALTH.door : kind === EDGE_BARRICADE ? STRUCTURE_HEALTH.barricade : 0;
}

export function edgeDamage(grid: SimState['grid'], e: number): number {
  return grid.edgeDamage[String(e)] ?? 0;
}

/** Hits an edge wall or door. Returns true if it broke (it's gone, and the way is open). */
export function damageEdge(state: SimState, e: number, amount: number): boolean {
  const { grid } = state;
  const kind = grid.edges[e];
  if (kind !== EDGE_WALL && kind !== EDGE_DOOR && kind !== EDGE_BARRICADE) return false;
  const damage = edgeDamage(grid, e) + amount;
  if (damage < edgeHealth(grid, e)) {
    grid.edgeDamage[String(e)] = damage;
    return false;
  }
  grid.edges[e] = EDGE_NONE;
  noteEdgeChange(grid, e);
  delete grid.edgeDamage[String(e)];
  const open = grid.edgeOpen.indexOf(e);
  if (open >= 0) grid.edgeOpen.splice(open, 1);
  const passing = grid.edgePassing.indexOf(e);
  if (passing >= 0) grid.edgePassing.splice(passing, 1);
  state.walkabilityVersion += 1;
  const tile = Math.floor(e / 2);
  logEvent(state, 'Zombies broke through the ' + (kind === EDGE_WALL ? 'wall' : kind === EDGE_DOOR ? 'door' : 'barricade') + ' at (' + (tile % grid.width) + ', ' + Math.floor(tile / grid.width) + ')');
  return true;
}

/** Undoes up to `amount` damage on an edge. Returns the damage left. */
export function repairEdge(state: SimState, e: number, amount: number): number {
  const left = Math.max(0, edgeDamage(state.grid, e) - amount);
  if (left === 0) delete state.grid.edgeDamage[String(e)];
  else state.grid.edgeDamage[String(e)] = left;
  return left;
}
