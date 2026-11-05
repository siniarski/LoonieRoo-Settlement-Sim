// Designations (blueprints): what the player wants built or removed.
// Each blueprint gets one job at a time from jobs.ts: a haul job while it
// still needs material, then a build job.

import { clearPlot } from './farming';
import { EDGE_BARRICADE, EDGE_DOOR, EDGE_NONE, EDGE_WALL, edgeIndex, setEdge, type Side } from './grid';
import { addFurniture, furnitureAt, removeFurniture } from './furniture';
import { getTile, isWalkable, setTile, type Point } from './grid';
import { dropItem, itemAt, spawnItem } from './items';
import { nearestTile, samePoint } from './space';
import type { SimState } from './state';
import { evictZombies } from './zombies';
import { isStructure } from './breach';

/**
 * What the tile ends up as. 'floor' means "remove what's here": furniture if
 * there is some, otherwise the wall or door.
 */
export type BuildTarget = 'wall' | 'door' | 'barricade' | 'bed' | 'table' | 'workbench' | 'stove' | 'trap' | 'tower' | 'well' | 'collector' | 'generator' | 'lamp' | 'fridge' | 'heater' | 'coop' | 'radio' | 'lab' | 'beacon' | 'turret' | 'lure' | 'pit' | 'reinforce' | 'floor';

/** Build targets that are furniture rather than a tile. */
export const FURNITURE_TARGETS = ['bed', 'table', 'workbench', 'stove', 'trap', 'tower', 'well', 'collector', 'generator', 'lamp', 'fridge', 'heater', 'coop', 'radio', 'lab', 'beacon', 'turret', 'lure', 'pit'] as const;
export type FurnitureTarget = (typeof FURNITURE_TARGETS)[number];

export function isFurnitureTarget(target: BuildTarget): target is FurnitureTarget {
  return (FURNITURE_TARGETS as readonly string[]).includes(target);
}

export interface Blueprint {
  id: string;
  x: number;
  y: number;
  target: BuildTarget;
  /** Walls, doors, beds, and tables need one wood delivered before work can start. */
  needsMaterial: boolean;
  delivered: boolean;
  workDone: number;
  workRequired: number;
  /** An edge wall or door (grid.ts): on this tile's north or west side, not on the tile. */
  side?: Side;
}

/** Ticks of work (10 ticks = 1s at 1x). */
export const BUILD_WORK: Record<BuildTarget, number> = { wall: 30, door: 20, barricade: 15, bed: 25, table: 25, workbench: 40, stove: 40, trap: 20, tower: 60, well: 120, collector: 40, generator: 80, lamp: 20, fridge: 60, heater: 30, coop: 60, radio: 60, lab: 100, beacon: 200, turret: 80, lure: 30, pit: 50, reinforce: 40, floor: 20 };

/** What a blueprint needs delivered: scrap to reinforce, wood for everything else. */
export function materialFor(target: BuildTarget): 'wood' | 'scrap' {
  return target === 'reinforce' ? 'scrap' : 'wood';
}

export function getBlueprint(state: SimState, id: string): Blueprint | undefined {
  return state.blueprints.find((b) => b.id === id);
}

export function blueprintAt(state: SimState, x: number, y: number): Blueprint | undefined {
  return state.blueprints.find((b) => b.x === x && b.y === y);
}

/** Can something be removed here? */
export function hasRemovable(state: SimState, x: number, y: number): boolean {
  const tile = getTile(state.grid, x, y);
  return tile === 'wall' || tile === 'door' || tile === 'barricade' || furnitureAt(state, x, y) !== undefined;
}

/** Building needs empty floor (no furniture); removing needs something to remove. */
export function preconditionHolds(state: SimState, bp: Blueprint): boolean {
  if (bp.side) return state.grid.edges[edgeIndex(state.grid, bp.x, bp.y, bp.side)] === EDGE_NONE;
  if (bp.target === 'floor') return hasRemovable(state, bp.x, bp.y);
  if (bp.target === 'reinforce') return isStructure(getTile(state.grid, bp.x, bp.y)) && !state.grid.reinforced.includes(bp.y * state.grid.width + bp.x);
  return getTile(state.grid, bp.x, bp.y) === 'floor' && !furnitureAt(state, bp.x, bp.y);
}

export function addBlueprint(state: SimState, x: number, y: number, target: BuildTarget, side?: Side): Blueprint {
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
  if (side) bp.side = side;
  state.blueprints.push(bp);
  return bp;
}

/** Cancels a blueprint. Delivered material comes back as an item. Its job is dropped by reconcileJobs. */
export function cancelBlueprint(state: SimState, bp: Blueprint): void {
  const i = state.blueprints.indexOf(bp);
  if (i >= 0) state.blueprints.splice(i, 1);
  if (bp.delivered) spawnItem(state, materialFor(bp.target), bp.x, bp.y);
}

/** Finishes construction: changes the tile or furniture and clears anything in the way. */
export function completeBlueprint(state: SimState, bp: Blueprint): void {
  const i = state.blueprints.indexOf(bp);
  if (i >= 0) state.blueprints.splice(i, 1);
  if (bp.side && (bp.target === 'wall' || bp.target === 'door' || bp.target === 'barricade')) {
    if (setEdge(state.grid, bp.x, bp.y, bp.side, bp.target === 'wall' ? EDGE_WALL : bp.target === 'door' ? EDGE_DOOR : EDGE_BARRICADE)) state.walkabilityVersion += 1;
    return;
  }
  switch (bp.target) {
    case 'wall':
    case 'door':
    case 'barricade':
      if (setTile(state.grid, bp.x, bp.y, bp.target)) state.walkabilityVersion += 1;
      if (bp.target === 'wall') evictFrom(state, bp);
      evictZombies(state, bp); // zombies can stand on none of these
      clearPlot(state, bp);
      return;
    case 'bed':
    case 'table':
    case 'workbench':
    case 'stove':
    case 'trap':
    case 'tower':
    case 'well':
    case 'collector':
    case 'generator':
    case 'lamp':
    case 'fridge':
    case 'heater':
    case 'coop':
    case 'radio':
    case 'lab':
    case 'beacon':
    case 'turret':
    case 'lure':
    case 'pit': {
      addFurniture(state, bp.target, bp.x, bp.y);
      const item = itemAt(state, bp.x, bp.y);
      if (item) dropItem(state, item, bp);
      return;
    }
    case 'reinforce': {
      const i = bp.y * state.grid.width + bp.x;
      if (!state.grid.reinforced.includes(i)) state.grid.reinforced.push(i);
      return;
    }
    case 'floor': {
      // Removing anything gives its wood back. Furniture goes before the tile.
      const f = furnitureAt(state, bp.x, bp.y);
      if (f) removeFurniture(state, f);
      else if (setTile(state.grid, bp.x, bp.y, 'floor')) state.walkabilityVersion += 1;
      spawnItem(state, 'wood', bp.x, bp.y);
      return;
    }
  }
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
