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
export const CONTAINER_LOOKS = ['counter', 'icebox', 'shelf', 'wardrobe', 'dresser', 'cabinet', 'toolbox', 'locker', 'desk', 'pallet', 'sack', 'crate', 'sink', 'bin', 'nightstand', 'bookcase'] as const;
export const DECOR_LOOKS = ['sofa', 'bed', 'toilet', 'bath', 'table', 'oven', 'tv', 'car', 'pew', 'altar', 'hay', 'tractor', 'pump', 'bench', 'cot', 'register', 'machine', 'silo', 'shower', 'armchair', 'washer'] as const;
export type Look = (typeof CONTAINER_LOOKS)[number] | (typeof DECOR_LOOKS)[number];

/** What each look is called (an icebox is a house's fridge; the fridge you build is furniture kind 'fridge'). */
export const LOOK_NAMES: Record<Look, string> = {
  counter: 'counter', icebox: 'fridge', shelf: 'shelf', wardrobe: 'wardrobe', dresser: 'dresser', cabinet: 'cabinet', toolbox: 'toolbox',
  locker: 'locker', desk: 'desk', pallet: 'pallet', sack: 'sacks', crate: 'crate', sink: 'sink', bin: 'bin', nightstand: 'nightstand',
  bookcase: 'bookcase', sofa: 'sofa', bed: 'bed', toilet: 'toilet', bath: 'bath', table: 'table', oven: 'oven', tv: 'television', car: 'car',
  pew: 'pew', altar: 'altar', hay: 'hay bales', tractor: 'tractor', pump: 'fuel pump', bench: 'bench', cot: 'cot', register: 'till',
  machine: 'machine', silo: 'silo', shower: 'shower', armchair: 'armchair', washer: 'washing machine',
};

/** Small containers hold fewer finds (lots.ts caps a new one's loot at this). */
export const LOOK_CAPACITY: Partial<Record<Look, number>> = { bin: 2, nightstand: 2, sink: 2, cabinet: 3, toolbox: 3 };

/** A container's name from its look and what it holds: a medicine cabinet, a filing cabinet, a kitchen sink. */
export function containerName(f: Pick<Furniture, 'look' | 'table'>): string {
  const look = f.look ?? 'crate';
  if (look === 'cabinet' && (f.table === 'bathroom' || f.table === 'clinic' || f.table === 'pharmacy')) return 'medicine cabinet';
  if (look === 'cabinet' && (f.table === 'office' || f.table === 'police' || f.table === 'school')) return 'filing cabinet';
  if (look === 'sink') return f.table === 'kitchen' ? 'kitchen sink' : 'bathroom sink';
  if (look === 'bin') return 'rubbish bin';
  return LOOK_NAMES[look];
}

/**
 * Furniture by tile ("x,y") and by id, derived and cached outside sim state. Furniture never
 * moves, and only addFurniture/removeFurniture change the list, so they drop the cache.
 * (A generated world has thousands of trees: scanning the list per tile was the cost.)
 */
const indexes = new WeakMap<Furniture[], { byTile: Map<string, Furniture>; byId: Map<string, Furniture>; byKind: Map<FurnitureKind, Furniture[]>; marked: Furniture[] | null }>();

function index(state: SimState) {
  let found = indexes.get(state.furniture);
  if (!found) {
    found = { byTile: new Map(), byId: new Map(), byKind: new Map(), marked: null };
    for (const f of state.furniture) {
      if (!found.byTile.has(`${f.x},${f.y}`)) found.byTile.set(`${f.x},${f.y}`, f);
      found.byId.set(f.id, f);
      const same = found.byKind.get(f.kind);
      if (same) same.push(f);
      else found.byKind.set(f.kind, [f]);
    }
    indexes.set(state.furniture, found);
  }
  return found;
}

export function furnitureAt(state: SimState, x: number, y: number): Furniture | undefined {
  return index(state).byTile.get(`${x},${y}`);
}

/**
 * Every piece of one kind, in list order (so results come out as a scan of the list would).
 * Shared with the index, and kept up to date in place: read it, never change it, and don't add or
 * remove furniture of that kind while looping over it. (A world has thousands of trees and fittings;
 * scanning them all each tick for the one generator was a steady cost.)
 */
export function ofKind(state: SimState, kind: FurnitureKind): readonly Furniture[] {
  return index(state).byKind.get(kind) ?? NONE;
}
const NONE: readonly Furniture[] = [];

/**
 * Everything marked for scavenging, in list order: worked out once and kept until a mark changes
 * (setScavenge) or furniture comes or goes. (A 1024 world has 88,000 pieces; the job sweep
 * scanned them all every tick for the few that are marked.) Read it, never change it.
 */
export function markedForScavenge(state: SimState): readonly Furniture[] {
  const ix = index(state);
  ix.marked ??= state.furniture.filter((f) => f.scavenge);
  return ix.marked;
}

/** Marks (or unmarks) a piece for scavenging. Always this way, never f.scavenge = ..., or the marked list goes stale. */
export function setScavenge(state: SimState, f: Furniture, on: boolean): void {
  if (f.scavenge === on) return;
  f.scavenge = on;
  const ix = indexes.get(state.furniture);
  if (ix) ix.marked = null;
}

/** Pieces of several kinds, in list order (furniture is only ever appended, so that's id order). */
export function ofKinds(state: SimState, kinds: readonly FurnitureKind[]): Furniture[] {
  return kinds.flatMap((k) => ofKind(state, k)).sort((a, b) => idOrder(a) - idOrder(b));
}
const idOrder = (f: Furniture) => Number(f.id.slice(1));

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
  // Keep the index, if there is one, up to date in place (rebuilding it over a big world's
  // 88,000 pieces for every piece added was a spike). New pieces go last, as in the list.
  const ix = indexes.get(state.furniture);
  if (ix) {
    const key = `${x},${y}`;
    if (!ix.byTile.has(key)) ix.byTile.set(key, f);
    ix.byId.set(f.id, f);
    const same = ix.byKind.get(kind);
    if (same) same.push(f);
    else ix.byKind.set(kind, [f]);
  }
  return f;
}

/** Trees grow back a piece of wood each TREE_REGROW_TICKS (up to TREE_WOOD). */
export function regrowTrees(state: SimState): void {
  if (state.tick % TREE_REGROW_TICKS !== 0) return;
  for (const f of state.furniture) if (f.kind === 'tree' && f.loot < TREE_WOOD) f.loot += 1;
}

export function removeFurniture(state: SimState, f: Furniture): void {
  const i = state.furniture.indexOf(f);
  if (i < 0) return;
  state.furniture.splice(i, 1);
  // The index, in place: the tile falls to the next piece there in list order, if any.
  const ix = indexes.get(state.furniture);
  if (ix) {
    const key = `${f.x},${f.y}`;
    if (ix.byTile.get(key) === f) {
      const next = state.furniture.find((g) => g.x === f.x && g.y === f.y);
      if (next) ix.byTile.set(key, next);
      else ix.byTile.delete(key);
    }
    ix.byId.delete(f.id);
    const same = ix.byKind.get(f.kind);
    if (same) {
      const k = same.indexOf(f);
      if (k >= 0) same.splice(k, 1);
    }
    if (ix.marked) ix.marked = ix.marked.filter((g) => g !== f);
  }
  for (let n = 0; n < f.stored; n++) spawnItem(state, f.storedKind ?? 'wood', f.x, f.y);
}
