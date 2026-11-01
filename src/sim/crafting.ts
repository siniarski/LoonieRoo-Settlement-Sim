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

import type { FurnitureKind } from './furniture';
import type { ItemKind } from './items';
import type { SimState } from './state';

export type CraftKind = 'weapon' | 'bandage' | 'meal';
export type Station = Extract<FurnitureKind, 'workbench' | 'stove'>;
export const CRAFTABLES: readonly CraftKind[] = ['weapon', 'bandage', 'meal'];
/** What the player sets targets for (the Workbench orders panel). */
export const ORDERABLE: readonly CraftKind[] = ['weapon', 'bandage'];

export interface Recipe {
  station: Station;
  input: ItemKind;
  count: number;
  label: string;
}

export const RECIPES: Record<CraftKind, Recipe> = {
  weapon: { station: 'workbench', input: 'wood', count: 2, label: 'Weapons' },
  bandage: { station: 'workbench', input: 'wood', count: 1, label: 'Bandages' },
  meal: { station: 'stove', input: 'food', count: 1, label: 'Hot meals' },
};

/** What each station takes in (so it can be given back if the station goes). */
export const STATION_INPUT: Record<Station, ItemKind> = { workbench: 'wood', stove: 'food' };

/** Ticks of work once the input is in. */
export const CRAFT_WORK = 60;
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

/** Items of this kind the colony has spare: on the ground or being hauled, not wielded. */
export function craftStock(state: SimState, kind: ItemKind): number {
  return state.items.filter((i) => i.kind === kind && !state.survivors.some((s) => s.weapon === i.id)).length;
}

function target(state: SimState, kind: CraftKind): number {
  return kind === 'meal' ? MEALS_PER_SURVIVOR * state.survivors.length : state.craftTargets[kind];
}

/** The first craftable for this station below its target, or null when stocks are full. */
export function wantedCraft(state: SimState, station: Station = 'workbench'): CraftKind | null {
  return CRAFTABLES.find((k) => RECIPES[k].station === station && craftStock(state, k) < target(state, k)) ?? null;
}
