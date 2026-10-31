// Crafting: survivors turn wood into weapons and bandages at a workbench.
//
// The player sets a stock target per craftable ("keep 2 bandages"). While
// any item is below its target, every workbench offers one craft job. The
// crafter carries wood to the bench one piece at a time (the bench keeps it
// in `stored`), then works CRAFT_WORK ticks and the item drops beside it.
// Which item is made is decided when the wood is in: the first one, in
// CRAFTABLES order, still below target.

import type { ItemKind } from './items';
import type { SimState } from './state';

export type CraftKind = 'weapon' | 'bandage';
export const CRAFTABLES: readonly CraftKind[] = ['weapon', 'bandage'];

export const RECIPES: Record<CraftKind, { wood: number; label: string }> = {
  weapon: { wood: 2, label: 'Weapons' },
  bandage: { wood: 1, label: 'Bandages' },
};

/** Ticks of work at the bench once the wood is in. */
export const CRAFT_WORK = 60;
/** Highest stock target the player can set. */
export const MAX_CRAFT_TARGET = 20;

export function isCraftKind(k: unknown): k is CraftKind {
  return typeof k === 'string' && (CRAFTABLES as readonly string[]).includes(k);
}

/** Items of this kind the colony has spare: on the ground or being hauled, not wielded. */
export function craftStock(state: SimState, kind: ItemKind): number {
  return state.items.filter((i) => i.kind === kind && !state.survivors.some((s) => s.weapon === i.id)).length;
}

/** The first craftable below its target, or null when stocks are full. */
export function wantedCraft(state: SimState): CraftKind | null {
  return CRAFTABLES.find((k) => craftStock(state, k) < state.craftTargets[k]) ?? null;
}
