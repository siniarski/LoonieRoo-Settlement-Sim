// Items: one per tile, one carried at a time, no stacks (hauling v1).
// An item is either on the ground (`pos`) or carried (`carriedBy`), never both.

import { isWalkable, type Point } from './grid';
import { isFurnitureTarget } from './designations';
import { furnitureAt } from './furniture';
import { nearestTile } from './space';
import type { SimState } from './state';
import { bookSubject } from './books';

export type ItemKind = 'wood' | 'food' | 'meal' | 'bandage' | 'medicine' | 'weapon' | 'cloth' | 'coat' | 'armour' | 'boots' | 'gun' | 'ammo' | 'corpse' | 'book' | 'cart' | 'axe' | 'hammer' | 'medkit' | 'sample' | 'scrap' | 'fuel';

export interface Item {
  id: string;
  kind: ItemKind;
  pos: Point | null;
  carriedBy: string | null;
  /** Survivor who has claimed this item: for a haul job, to eat it (food), or to use it (bandage). */
  reservedBy: string | null;
  /** Weapons: kills made with it so far (see weapons.ts). Clothing: wear (clothing.ts). Guns: rounds loaded. Corpses: tick of death. 0 for everything else. */
  wear: number;
  /** Corpses: the survivor's name (null for a zombie). Books: the subject (books.ts). Null for everything else. */
  tag: string | null;
}

export function getItem(state: SimState, id: string): Item | undefined {
  return state.items.find((i) => i.id === id);
}

export function itemAt(state: SimState, x: number, y: number): Item | undefined {
  return state.items.find((i) => i.pos !== null && i.pos.x === x && i.pos.y === y);
}

/** A tile an item can be dropped on: walkable, empty, no furniture, and not about to become a wall or furniture. */
export function canHoldItem(state: SimState, x: number, y: number): boolean {
  return (
    isWalkable(state.grid, x, y) &&
    !itemAt(state, x, y) &&
    !furnitureAt(state, x, y) &&
    !state.blueprints.some((b) => b.x === x && b.y === y && (b.target === 'wall' || isFurnitureTarget(b.target)))
  );
}

/** Creates an item on the nearest free tile to (x, y). */
export function spawnItem(state: SimState, kind: ItemKind, x: number, y: number): Item {
  const item: Item = { id: `i${state.nextId++}`, kind, pos: null, carriedBy: null, reservedBy: null, wear: 0, tag: null };
  if (kind === 'book') item.tag = bookSubject(item.id);
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
  // Tiles already holding an item, gathered once (not a scan of every item per tile tried).
  const held = new Set(state.items.flatMap((i) => (i.pos ? [`${i.pos.x},${i.pos.y}`] : [])));
  const free = (x: number, y: number) =>
    isWalkable(state.grid, x, y) &&
    !held.has(`${x},${y}`) &&
    !furnitureAt(state, x, y) &&
    !state.blueprints.some((b) => b.x === x && b.y === y && (b.target === 'wall' || isFurnitureTarget(b.target)));
  // If the whole map is full, leave it where it was asked to go.
  item.pos = nearestTile(state.grid, near, free) ?? { x: near.x, y: near.y };
}
