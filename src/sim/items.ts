// Items: one per tile, one carried at a time, no stacks (hauling v1).
// An item is either on the ground (`pos`) or carried (`carriedBy`), never both.

import { isWalkable, type Point } from './grid';
import { nearestTile } from './space';
import type { SimState } from './state';

export type ItemKind = 'wood';

export interface Item {
  id: string;
  kind: ItemKind;
  pos: Point | null;
  carriedBy: string | null;
  /** Survivor whose haul job has claimed this item. */
  reservedBy: string | null;
}

export function getItem(state: SimState, id: string): Item | undefined {
  return state.items.find((i) => i.id === id);
}

export function itemAt(state: SimState, x: number, y: number): Item | undefined {
  return state.items.find((i) => i.pos !== null && i.pos.x === x && i.pos.y === y);
}

/** A tile an item can be dropped on: walkable, empty, and not about to become a wall. */
export function canHoldItem(state: SimState, x: number, y: number): boolean {
  return (
    isWalkable(state.grid, x, y) &&
    !itemAt(state, x, y) &&
    !state.blueprints.some((b) => b.x === x && b.y === y && b.target === 'wall')
  );
}

/** Creates an item on the nearest free tile to (x, y). */
export function spawnItem(state: SimState, kind: ItemKind, x: number, y: number): Item {
  const item: Item = { id: `i${state.nextId++}`, kind, pos: null, carriedBy: null, reservedBy: null };
  state.items.push(item);
  placeNear(state, item, { x, y });
  return item;
}

/** Puts an item on the ground at the nearest free tile and clears carry/reservation. */
export function dropItem(state: SimState, item: Item, near: Point): void {
  item.carriedBy = null;
  item.reservedBy = null;
  placeNear(state, item, near);
}

export function removeItem(state: SimState, item: Item): void {
  const i = state.items.indexOf(item);
  if (i >= 0) state.items.splice(i, 1);
}

function placeNear(state: SimState, item: Item, near: Point): void {
  item.pos = null; // so it doesn't count as occupying its own old tile
  // If the whole map is full, leave it where it was asked to go.
  item.pos = nearestTile(state.grid, near, (x, y) => canHoldItem(state, x, y)) ?? { x: near.x, y: near.y };
}
