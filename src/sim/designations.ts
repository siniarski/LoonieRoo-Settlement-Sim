// Designations (blueprints): what the player wants built or removed.
// Each blueprint gets one job at a time from jobs.ts: a haul job while it
// still needs material, then a build job.

import { getTile, isWalkable, setTile, type Point, type Tile } from './grid';
import { dropItem, itemAt, spawnItem } from './items';
import { nearestTile, samePoint } from './space';
import type { SimState } from './state';

/** What the tile becomes. 'floor' means "remove the wall or door here". */
export type BuildTarget = Tile;

export interface Blueprint {
  id: string;
  x: number;
  y: number;
  target: BuildTarget;
  /** Walls and doors need one wood delivered before work can start. */
  needsMaterial: boolean;
  delivered: boolean;
  workDone: number;
  workRequired: number;
}

/** Ticks of work (10 ticks = 1s at 1x). */
export const BUILD_WORK: Record<BuildTarget, number> = { wall: 30, door: 20, floor: 20 };

export function getBlueprint(state: SimState, id: string): Blueprint | undefined {
  return state.blueprints.find((b) => b.id === id);
}

export function blueprintAt(state: SimState, x: number, y: number): Blueprint | undefined {
  return state.blueprints.find((b) => b.x === x && b.y === y);
}

/** Building needs empty floor; removing needs a wall or door. */
export function preconditionHolds(state: SimState, bp: Blueprint): boolean {
  const tile = getTile(state.grid, bp.x, bp.y);
  return bp.target === 'floor' ? tile === 'wall' || tile === 'door' : tile === 'floor';
}

export function addBlueprint(state: SimState, x: number, y: number, target: BuildTarget): Blueprint {
  const bp: Blueprint = {
    id: `b${state.nextId++}`,
    x,
    y,
    target,
    needsMaterial: target !== 'floor',
    delivered: false,
    workDone: 0,
    workRequired: BUILD_WORK[target],
  };
  state.blueprints.push(bp);
  return bp;
}

/** Cancels a blueprint. Delivered material comes back as an item. Its job is dropped by reconcileJobs. */
export function cancelBlueprint(state: SimState, bp: Blueprint): void {
  const i = state.blueprints.indexOf(bp);
  if (i >= 0) state.blueprints.splice(i, 1);
  if (bp.delivered) spawnItem(state, 'wood', bp.x, bp.y);
}

/** Finishes construction: changes the tile and clears anything now inside a wall. */
export function completeBlueprint(state: SimState, bp: Blueprint): void {
  const i = state.blueprints.indexOf(bp);
  if (i >= 0) state.blueprints.splice(i, 1);
  if (setTile(state.grid, bp.x, bp.y, bp.target)) state.walkabilityVersion += 1;
  if (bp.target === 'wall') evictFrom(state, bp);
  // Removing a wall or door gives its wood back.
  if (bp.target === 'floor') spawnItem(state, 'wood', bp.x, bp.y);
}

/** Moves survivors and items off a tile that just became solid. */
export function evictFrom(state: SimState, tile: Point): void {
  for (const s of state.survivors) {
    const onIt = samePoint(s.pos, tile) || (s.move !== null && samePoint(s.move.to, tile));
    if (!onIt) continue;
    const to = nearestTile(state.grid, tile, (x, y) => isWalkable(state.grid, x, y));
    if (!to) continue;
    s.pos = to;
    s.move = null;
    s.path = [];
    s.pathVersion = -1;
  }
  const item = itemAt(state, tile.x, tile.y);
  if (item) dropItem(state, item, tile);
}
