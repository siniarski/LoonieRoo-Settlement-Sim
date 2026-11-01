// Furniture: things on floor tiles that don't change walkability.
// Survivors can walk over them; items can't be dropped on them.
//   bed    built by survivors; slept in (see needs.ts)
//   crate  found in ruins; holds loot that survivors scavenge (see jobs.ts)
//   table  built by survivors; makes a mess hall (see mess.ts)
//   workbench  built by survivors; crafting happens here (see crafting.ts)
//   tree   marked like crates; chopping yields wood, and it grows back (TREE_WOOD)

import { spawnItem } from './items';
import type { SimState } from './state';

export type FurnitureKind = 'bed' | 'crate' | 'table' | 'workbench' | 'tree';

/** Finds in a fresh crate. Each scavenge job yields one. */
export const CRATE_LOOT = 5;
/** Wood on a grown tree. A chopped tree grows one back every TREE_REGROW_TICKS (two days), up to this. */
export const TREE_WOOD = 3;
export const TREE_REGROW_TICKS = 2 * 4800; // two in-game days (one a day gave a big surplus: see NOTES.md)

export interface Furniture {
  id: string;
  kind: FurnitureKind;
  x: number;
  y: number;
  /** Beds: the survivor who has claimed it (walking to it or asleep in it). */
  occupant: string | null;
  /** Beds: whose bed it is (claimed on first night, or assigned). Nobody else sleeps in it. Never set on infirmary beds. */
  owner: string | null;
  /** Crates: finds left. Trees: wood left. 0 for everything else. */
  loot: number;
  /** Crates and trees: marked by the player for scavenging (or chopping). Trees stay marked as they regrow. */
  scavenge: boolean;
  /** Workbenches: wood brought in for the next craft. Given back if the bench is removed. */
  stored: number;
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
    owner: null,
    loot: kind === 'crate' ? CRATE_LOOT : kind === 'tree' ? TREE_WOOD : 0,
    scavenge: false,
    stored: 0,
  };
  state.furniture.push(f);
  return f;
}

/** Trees grow back a piece of wood each TREE_REGROW_TICKS (up to TREE_WOOD). */
export function regrowTrees(state: SimState): void {
  if (state.tick % TREE_REGROW_TICKS !== 0) return;
  for (const f of state.furniture) if (f.kind === 'tree' && f.loot < TREE_WOOD) f.loot += 1;
}

export function removeFurniture(state: SimState, f: Furniture): void {
  const i = state.furniture.indexOf(f);
  if (i >= 0) state.furniture.splice(i, 1);
  for (let n = 0; n < f.stored; n++) spawnItem(state, 'wood', f.x, f.y);
}
