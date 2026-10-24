// Furniture: things built on floor tiles that don't change walkability.
// Survivors can walk over beds; items can't be dropped on them.

import type { SimState } from './state';

export type FurnitureKind = 'bed';

export interface Furniture {
  id: string;
  kind: FurnitureKind;
  x: number;
  y: number;
  /** Beds: the survivor who has claimed it (walking to it or asleep in it). */
  occupant: string | null;
}

export function furnitureAt(state: SimState, x: number, y: number): Furniture | undefined {
  return state.furniture.find((f) => f.x === x && f.y === y);
}

export function addFurniture(state: SimState, kind: FurnitureKind, x: number, y: number): Furniture {
  const f: Furniture = { id: `f${state.nextId++}`, kind, x, y, occupant: null };
  state.furniture.push(f);
  return f;
}

export function removeFurniture(state: SimState, f: Furniture): void {
  const i = state.furniture.indexOf(f);
  if (i >= 0) state.furniture.splice(i, 1);
}
