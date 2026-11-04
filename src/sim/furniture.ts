// Furniture: things on floor tiles that don't change walkability.
// Survivors can walk over them; items can't be dropped on them.
//   bed    built by survivors; slept in (see needs.ts)
//   crate  found in ruins; holds loot that survivors scavenge (see jobs.ts)
//   table  built by survivors; makes a mess hall (see mess.ts)
//   workbench  built by survivors; crafting happens here (see crafting.ts)
//   stove  built by survivors; cooking happens here (see crafting.ts)
//   trap   spikes: hurts a zombie that steps on it, TRAP_CHARGES times (see zombies.ts)
//   tower  a watchtower: see further and shoot further from it (see towers.ts)
//   well, collector  clean water (see water.ts); a collector's loot is the water in it
//   generator, lamp, fridge  power (see power.ts); a generator's stored is its fuel, its loot the burn timer
//   heater a brazier: warmth outdoors on cold nights, fuelled like a generator (power.ts)
//   coop   chickens: lays food (farming.ts)
//   turret, lure, pit  defences (defences.ts); a turret's stored is ammo boxes, its loot the rounds left in one
//   truck  found out in a generated world; expeditions drive it (outposts.ts)
//   lab, beacon  the endgame: research a cure, or call for evacuation (endgame.ts)
//   radio  powered: hears distress calls and neighbours' asks (radio.ts)
//   tree   marked like crates; chopping yields wood, and it grows back (TREE_WOOD)
//   decor  generated fittings (a sofa, a toilet, a pew, a wrecked car): `look` says which. Only for show.
// Generated containers are crates with a `look` (a kitchen counter, a wardrobe, a police locker)
// and a loot `table` (loot.ts): what's in them depends on the room they're in.

import { spawnItem, type ItemKind } from './items';
import type { LootTable } from './loot';
import type { SimState } from './state';

export type FurnitureKind = 'decor' | 'bed' | 'crate' | 'table' | 'workbench' | 'stove' | 'tree' | 'trap' | 'tower' | 'well' | 'collector' | 'generator' | 'lamp' | 'fridge' | 'heater' | 'coop' | 'radio' | 'lab' | 'beacon' | 'turret' | 'lure' | 'pit' | 'truck';

/** Finds in a fresh crate. Each scavenge job yields one. */
export const CRATE_LOOT = 5;
/** Spikes left on a new trap. */
export const TRAP_CHARGES = 3;
/** Wood on a grown tree. A chopped tree grows one back every TREE_REGROW_TICKS (three days), up to this. Was 3 every 2 days: generated forests flooded the map with wood. */
export const TREE_WOOD = 2;
export const TREE_REGROW_TICKS = 3 * 4800; // three in-game days

export interface Furniture {
  id: string;
  kind: FurnitureKind;
  x: number;
  y: number;
  /** Beds: the survivor who has claimed it (walking to it or asleep in it). */
  occupant: string | null;
  /** Beds: whose bed it is (claimed on first night, or assigned). Nobody else sleeps in it. Never set on infirmary beds. */
  owner: string | null;
  /** Crates: finds left. Trees: wood left. Traps: charges left. Collectors: water. Generators: ticks into the current wood. 0 for everything else. */
  loot: number;
  /** Crates and trees: marked by the player for scavenging (or chopping). Trees stay marked as they regrow. */
  scavenge: boolean;
  /** Stations (workbench, stove): input brought in for the next job. Given back if the station is removed. */
  stored: number;
  /** What `stored` is (wood, cloth, food); null when empty. */
  storedKind: ItemKind | null;
  /** Generated crates and decor: what it looks like (a wardrobe, a sofa); null for a plain one. */
  look: Look | null;
  /** Generated crates: what's found in it (loot.ts); null for the general table. */
  table: LootTable | null;
}

/** Containers (crates) and decor in generated buildings (buildings.ts). */
export const CONTAINER_LOOKS = ['counter', 'icebox', 'shelf', 'wardrobe', 'dresser', 'cabinet', 'toolbox', 'locker', 'desk', 'pallet', 'sack', 'crate'] as const;
export const DECOR_LOOKS = ['sofa', 'bed', 'toilet', 'bath', 'table', 'oven', 'tv', 'car', 'pew', 'altar', 'hay', 'tractor', 'pump', 'bench', 'cot', 'register', 'machine'] as const;
export type Look = (typeof CONTAINER_LOOKS)[number] | (typeof DECOR_LOOKS)[number];

/**
 * Furniture by tile ("x,y") and by id, derived and cached outside sim state. Furniture never
 * moves, and only addFurniture/removeFurniture change the list, so they drop the cache.
 * (A generated world has thousands of trees: scanning the list per tile was the cost.)
 */
const indexes = new WeakMap<Furniture[], { byTile: Map<string, Furniture>; byId: Map<string, Furniture> }>();

function index(state: SimState) {
  let found = indexes.get(state.furniture);
  if (!found) {
    found = { byTile: new Map(), byId: new Map() };
    for (const f of state.furniture) {
      if (!found.byTile.has(`${f.x},${f.y}`)) found.byTile.set(`${f.x},${f.y}`, f);
      found.byId.set(f.id, f);
    }
    indexes.set(state.furniture, found);
  }
  return found;
}

export function furnitureAt(state: SimState, x: number, y: number): Furniture | undefined {
  return index(state).byTile.get(`${x},${y}`);
}

export function getFurniture(state: SimState, id: string): Furniture | undefined {
  return index(state).byId.get(id);
}

export function addFurniture(state: SimState, kind: FurnitureKind, x: number, y: number): Furniture {
  const f: Furniture = {
    id: `f${state.nextId++}`,
    kind,
    x,
    y,
    occupant: null,
    owner: null,
    loot: kind === 'crate' ? CRATE_LOOT : kind === 'tree' ? TREE_WOOD : kind === 'trap' ? TRAP_CHARGES : 0,
    scavenge: false,
    stored: 0,
    storedKind: null,
    look: null,
    table: null,
  };
  state.furniture.push(f);
  indexes.delete(state.furniture);
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
  indexes.delete(state.furniture);
  for (let n = 0; n < f.stored; n++) spawnItem(state, f.storedKind ?? 'wood', f.x, f.y);
}
