// Furniture: things on floor tiles that don't change walkability.
// Survivors can walk over them; items can't be dropped on them.
//   bed    built by survivors; slept in (see needs.ts)
//   crate  found in ruins; holds loot that survivors scavenge (see jobs.ts)
//   table  built by survivors; makes a mess hall (see mess.ts)

import type { SimState } from './state';

export type FurnitureKind = 'bed' | 'crate' | 'table';

/** Finds in a fresh crate. Each scavenge job yields one. */
export const CRATE_LOOT = 5;

export interface Furniture {
  id: string;
  kind: FurnitureKind;
  x: number;
  y: number;
  /** Beds: the survivor who has claimed it (walking to it or asleep in it). */
  occupant: string | null;
  /** Crates: finds left. 0 for beds and searched-out crates. */
  loot: number;
  /** Crates: marked by the player for scavenging. */
  scavenge: boolean;
}

export function furnitureAt(state: SimState, x: number, y: number): Furniture | undefined {
  return state.furniture.find((f) => f.x === x && f.y === y);
}

export function getFurniture(state: SimState, id: string): Furniture | undefined {
  return state.furniture.find((f) => f.id === id);
}

export function addFurniture(state: SimState, kind: FurnitureKind, x: number, y: number): Furniture {
  const f: Furniture = {
    id: `f${state.nextId++}`,
    kind,
    x,
    y,
    occupant: null,
    loot: kind === 'crate' ? CRATE_LOOT : 0,
    scavenge: false,
  };
  state.furniture.push(f);
  return f;
}

export function removeFurniture(state: SimState, f: Furniture): void {
  const i = state.furniture.indexOf(f);
  if (i >= 0) state.furniture.splice(i, 1);
}
