// Inventory: a bag for the survivor you control, and what's in containers.
//
//   pack       item ids in your bag (survivor.pack), carried (carriedBy) like your hands and
//              slots. It holds PACK_MAX of weight (ITEM_WEIGHT, tenths of a kilo): a cart or a
//              body never goes in it.
//   use        an item in the bag: food is eaten, a remedy you need used, a weapon wielded (the
//              old one into the bag, or down if there's no room), clothing worn the same way.
//              The UI's quick slots (keys 1 to 9) use the bag's first nine.
//   containers searching one (control.ts) turns up everything left in it at once: the finds stay
//              inside (item.inside, no pos), listed in the loot list, to take one at a time or all.
//   stash      put something from the bag into a container beside you: a crate (CRATE_HOLD) or
//              a car's boot (vehicles.ts bootMax). Containers are found by id (containerOf).
//   death      the bag's contents fall where you fall.

import { SLOT_OF, isGarment } from './clothing';
import { getFurniture, type Furniture } from './furniture';
import { bootMax, vehiclesOf } from './vehicles';
import { dropItem, getItem, type Item, type ItemKind } from './items';
import type { Point } from './grid';
import type { SimState } from './state';
import type { Survivor } from './survivors';

/** Weight in tenths of a kilo. A cart or a body can't go in a bag (Infinity). */
export const ITEM_WEIGHT: Record<ItemKind, number> = {
  wood: 30, food: 5, meal: 5, bandage: 1, medicine: 1, weapon: 20, cloth: 2, coat: 15, armour: 40, boots: 10, gun: 30, ammo: 2,
  corpse: Infinity, book: 5, cart: Infinity, axe: 25, hammer: 15, medkit: 3, sample: 1, scrap: 10, fuel: 20,
};

/** What a bag holds: 12 kilos. */
export const PACK_MAX = 120;

export function packWeight(state: SimState, s: Survivor): number {
  return s.pack.reduce((sum, id) => sum + ITEM_WEIGHT[getItem(state, id)?.kind ?? 'sample'], 0);
}

export function fitsInPack(state: SimState, s: Survivor, item: Item): boolean {
  return packWeight(state, s) + ITEM_WEIGHT[item.kind] <= PACK_MAX;
}

/** What's inside a container (a crate, a car's boot), by its id. */
export function contents(state: SimState, f: Pick<Furniture, 'id'>): Item[] {
  return state.items.filter((i) => i.inside === f.id);
}

/** What a crate holds (tenths of a kilo). */
export const CRATE_HOLD = 300;

/** A container by id: a crate, or a car's boot. Where it is, and what it holds. */
export function containerOf(state: SimState, id: string): { at: Point; max: number } | null {
  const f = getFurniture(state, id);
  if (f) return f.kind === 'crate' ? { at: { x: f.x, y: f.y }, max: CRATE_HOLD } : null;
  const v = vehiclesOf(state).find((x) => x.id === id);
  return v ? { at: v.pos, max: bootMax(v) } : null;
}

/** Puts something from the bag into a container. Returns why not, or null. (Is it beside you? The caller checks.) */
export function stash(state: SimState, s: Survivor, item: Item, containerId: string): string | null {
  const box = containerOf(state, containerId);
  if (!box) return 'not a container';
  if (!s.pack.includes(item.id)) return 'not in your bag';
  const load = contents(state, { id: containerId }).reduce((sum, i) => sum + ITEM_WEIGHT[i.kind], 0);
  if (load + ITEM_WEIGHT[item.kind] > box.max) return "it won't fit";
  unpack(s, item.id);
  item.carriedBy = null;
  item.pos = null;
  item.reservedBy = null;
  item.inside = containerId;
  return null;
}

/** Puts an item into the bag: from the ground, a container, or your hands. Returns why not, or null. */
export function stow(state: SimState, s: Survivor, item: Item): string | null {
  if (s.pack.includes(item.id)) return 'already in your bag';
  if (!fitsInPack(state, s, item)) return ITEM_WEIGHT[item.kind] === Infinity ? `a ${item.kind} won't go in a bag` : 'your bag is too full';
  if (s.carrying === item.id) s.carrying = null;
  item.pos = null;
  delete item.inside;
  item.carriedBy = s.id;
  item.reservedBy = null;
  s.pack.push(item.id);
  return null;
}

/** Takes an item out of the bag (it's being used up, or moving to a slot). */
export function unpack(s: Survivor, id: string): void {
  const at = s.pack.indexOf(id);
  if (at >= 0) s.pack.splice(at, 1);
}

/** Drops an item from the bag at your feet (or the nearest free tile). */
export function dropFromPack(state: SimState, s: Survivor, id: string): string | null {
  const item = getItem(state, id);
  if (!item || !s.pack.includes(id)) return 'not in your bag';
  unpack(s, id);
  dropItem(state, item, s.pos);
  return null;
}

/**
 * Wields a weapon or wears clothing from the bag: whatever was in the slot goes into the bag in
 * its place (or down at your feet if it won't fit). Returns what happened.
 */
export function equipFromPack(state: SimState, s: Survivor, item: Item, where: Point): string {
  const slot = item.kind === 'weapon' || item.kind === 'gun' ? 'weapon' : SLOT_OF[item.kind as keyof typeof SLOT_OF];
  unpack(s, item.id);
  const old = s[slot] ? getItem(state, s[slot]!) : undefined;
  s[slot] = item.id;
  if (old) {
    s[slot] = item.id;
    if (stow(state, s, old) !== null) dropItem(state, old, where);
  }
  return isGarment(item.kind) ? `put on the ${item.kind}` : `took up the ${item.kind}`;
}

/** Everything in the bag falls where you fall (death, or leaving). */
export function dropPack(state: SimState, s: Survivor): void {
  for (const id of [...s.pack]) {
    const item = getItem(state, id);
    unpack(s, id);
    if (item) dropItem(state, item, s.pos);
  }
}
