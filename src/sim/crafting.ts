// Crafting and cooking: survivors turn one thing into another at a station.
//
// A recipe names its station (workbench or stove), its input and how many,
// and how long it takes. While anything a station makes is wanted, the
// station offers one job (craft at a workbench, cook at a stove): carry the
// input to it one piece at a time (the station keeps it in `stored`), then
// work, and the product drops beside it. Which recipe is made is decided
// when the input is in: the first wanted one, in CRAFTABLES order.
//
// Workbench products follow the player's stock targets. Hot meals follow the
// colony: MEALS_PER_SURVIVOR each, while there's raw food to cook.

import type { Furniture, FurnitureKind } from './furniture';
import type { Knowledge } from './books';
import type { ItemKind } from './items';
import { completeRooms, labelAt } from './rooms';
import type { SimState } from './state';

export type CraftKind = 'weapon' | 'bandage' | 'medicine' | 'meal' | 'coat' | 'armour' | 'boots' | 'ammo' | 'gun' | 'cart' | 'axe' | 'hammer' | 'medkit';
export type Station = Extract<FurnitureKind, 'workbench' | 'stove'>;
export const CRAFTABLES: readonly CraftKind[] = ['weapon', 'bandage', 'medicine', 'ammo', 'gun', 'coat', 'armour', 'boots', 'cart', 'axe', 'hammer', 'medkit', 'meal'];
/** What the player sets targets for (the Workbench orders panel). */
export const ORDERABLE: readonly CraftKind[] = ['weapon', 'bandage', 'medicine', 'ammo', 'gun', 'coat', 'armour', 'boots', 'cart', 'axe', 'hammer', 'medkit'];

export interface Recipe {
  station: Station;
  input: ItemKind;
  count: number;
  label: string;
  /** Only a workbench in a complete workshop can make it. */
  workshopOnly?: true;
  /** Something the colony has to have read up on first (books.ts). */
  needs?: Knowledge;
}

export const RECIPES: Record<CraftKind, Recipe> = {
  weapon: { station: 'workbench', input: 'wood', count: 2, label: 'Weapons' },
  bandage: { station: 'workbench', input: 'wood', count: 1, label: 'Bandages' },
  medicine: { station: 'workbench', input: 'wood', count: 2, label: 'Medicine' },
  ammo: { station: 'workbench', input: 'wood', count: 2, label: 'Ammo (hand-loaded)', workshopOnly: true, needs: 'gunsmithing' },
  gun: { station: 'workbench', input: 'wood', count: 4, label: 'Guns', workshopOnly: true, needs: 'gunsmithing' },
  coat: { station: 'workbench', input: 'cloth', count: 2, label: 'Coats' },
  armour: { station: 'workbench', input: 'cloth', count: 3, label: 'Padded armour', needs: 'tailoring' },
  boots: { station: 'workbench', input: 'cloth', count: 1, label: 'Boots' },
  cart: { station: 'workbench', input: 'wood', count: 3, label: 'Carts' },
  axe: { station: 'workbench', input: 'wood', count: 2, label: 'Axes (scavenging, chopping)' },
  hammer: { station: 'workbench', input: 'wood', count: 2, label: 'Hammers (building)' },
  medkit: { station: 'workbench', input: 'cloth', count: 2, label: 'Medic kits (doctoring)' },
  meal: { station: 'stove', input: 'food', count: 1, label: 'Hot meals' },
};

/** Ticks of work once the input is in. */
export const CRAFT_WORK = 60;
/** At a workbench in a complete workshop. */
export const WORKSHOP_CRAFT_WORK = 40;
/** Highest stock target the player can set. */
export const MAX_CRAFT_TARGET = 20;
/** Hot meals the cooks keep ready, per survivor. */
export const MEALS_PER_SURVIVOR = 2;

export function isCraftKind(k: unknown): k is CraftKind {
  return typeof k === 'string' && (ORDERABLE as readonly string[]).includes(k);
}

export function isStation(kind: FurnitureKind): kind is Station {
  return kind === 'workbench' || kind === 'stove';
}

/** Items of this kind the colony has spare: on the ground or being hauled, not wielded or worn. */
export function craftStock(state: SimState, kind: ItemKind): number {
  // What people are using doesn't count: their ids gathered once, not every item checked against everyone.
  const used = new Set<string>();
  for (const s of state.survivors) for (const id of [s.weapon, s.body, s.feet, s.cart, s.tool]) if (id) used.add(id);
  let n = 0;
  for (const i of state.items) if (i.kind === kind && !used.has(i.id)) n++;
  return n;
}

function target(state: SimState, kind: CraftKind): number {
  return kind === 'meal' ? MEALS_PER_SURVIVOR * state.survivors.length : state.craftTargets[kind];
}

/** Craftables for this station below their target, in CRAFTABLES order. */
export function wantedCrafts(state: SimState, station: Station = 'workbench', inWorkshop = false): CraftKind[] {
  return CRAFTABLES.filter(
    (k) =>
      RECIPES[k].station === station &&
      (inWorkshop || !RECIPES[k].workshopOnly) &&
      (!RECIPES[k].needs || state.knowledge.includes(RECIPES[k].needs!)) &&
      craftStock(state, k) < target(state, k),
  );
}

/** The first craftable for this station below its target, or null when stocks are full. */
export function wantedCraft(state: SimState, station: Station = 'workbench', inWorkshop = false): CraftKind | null {
  return wantedCrafts(state, station, inWorkshop)[0] ?? null;
}

/**
 * What this station should make next: something that uses what's already in it if possible,
 * else the first wanted thing. Null when stocks are full.
 */
export function stationCraft(state: SimState, f: Furniture): CraftKind | null {
  if (!isStation(f.kind)) return null;
  const wanted = wantedCrafts(state, f.kind, inWorkshop(state, f));
  // Something it can finish with what's in it (or can get more of); else something whose input exists
  // (a bench waiting on cloth that never comes mustn't hold up medicine); else the first wanted.
  const holds = (k: CraftKind) => f.stored > 0 && RECIPES[k].input === f.storedKind;
  const has = (k: CraftKind) => craftStock(state, RECIPES[k].input) > 0;
  return wanted.find((k) => holds(k) && (f.stored >= RECIPES[k].count || has(k))) ?? wanted.find(has) ?? wanted[0] ?? null;
}

/** Is this station in a complete workshop? */
export function inWorkshop(state: SimState, f: { x: number; y: number }): boolean {
  return completeRooms(state, 'workshop').has(labelAt(state, f.x, f.y));
}
