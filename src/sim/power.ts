// Power and spoilage.
//
// A generator burns wood (haulers keep it fuelled, up to FUEL_MAX: jobs.ts)
// and powers everything within POWER_RADIUS. It runs while there's fuel and
// something to power, burning one wood every FUEL_TICKS (furniture.loot
// counts the ticks), and it's noisy (once an hour). Lamps light up at night
// when powered: night zombies don't arrive within LAMP_DETER of a lit lamp
// (zombies.ts), and guards fight better in the light. A powered fridge keeps
// the food round it from spoiling.
//
// Spoilage (harsh games only): food and hot meals age (item.wear) and rot
// at FOOD_SHELF and MEAL_SHELF. They age twice as fast in summer, not at all
// in winter, half as fast in a complete storage room, and not near a powered
// fridge.

import { logEvent } from './events';
import type { Furniture } from './furniture';
import { removeItem } from './items';
import { makeNoise } from './noise';
import { completeRooms, labelAt } from './rooms';
import { seasonOf } from './seasons';
import { manhattan } from './space';
import type { SimState } from './state';
import { DAY_LENGTH, TICKS_PER_HOUR, isNight } from './time';

export const POWER_RADIUS = 12;
export const FUEL_MAX = 4;
export const FUEL_TICKS = 2 * TICKS_PER_HOUR;
export const LAMP_DETER = 16;
/** Lamps light this far; guards in it hit harder. */
export const LAMP_LIGHT = 4;
export const LIGHT_DAMAGE_PCT = 125;
export const FRIDGE_RADIUS = 2;

export const SPOIL_CHECK_EVERY = 100;
export const FOOD_SHELF = 4 * DAY_LENGTH;
export const MEAL_SHELF = DAY_LENGTH;

const CONSUMERS = ['lamp', 'fridge'] as const;

function generators(state: SimState): Furniture[] {
  return state.furniture.filter((f) => f.kind === 'generator');
}

/** Is a generator with fuel within reach of this point? */
export function isPowered(state: SimState, p: { x: number; y: number }): boolean {
  return generators(state).some((g) => g.stored > 0 && manhattan(g, p) <= POWER_RADIUS);
}

/** Lamps that are lit right now. */
export function litLamps(state: SimState): Furniture[] {
  if (!isNight(state.tick)) return [];
  return state.furniture.filter((f) => f.kind === 'lamp' && isPowered(state, f));
}

export function inLight(state: SimState, p: { x: number; y: number }): boolean {
  return litLamps(state).some((l) => manhattan(l, p) <= LAMP_LIGHT);
}

/** Generators burn fuel while they have something to power. Every tick. */
export function updatePower(state: SimState): void {
  for (const g of generators(state)) {
    if (g.stored === 0) continue;
    const load = state.furniture.some(
      (f) => (CONSUMERS as readonly string[]).includes(f.kind) && manhattan(g, f) <= POWER_RADIUS && (f.kind === 'fridge' || isNight(state.tick)),
    );
    if (!load) continue;
    g.loot += 1;
    if (state.tick % TICKS_PER_HOUR === 0) makeNoise(state, g, 'generator');
    if (g.loot < FUEL_TICKS) continue;
    g.loot = 0;
    g.stored -= 1;
    if (g.stored === 0) {
      g.storedKind = null;
      logEvent(state, `The generator at (${g.x}, ${g.y}) ran out of fuel`);
    }
  }
}

/** Food and meals age, and rot. Harsh games only. */
export function updateSpoilage(state: SimState): void {
  if (!state.harsh || state.tick % SPOIL_CHECK_EVERY !== 0) return;
  const season = seasonOf(state.tick);
  if (season === 'winter') return;
  const rate = season === 'summer' ? 2 : 1;
  const stores = completeRooms(state, 'storage');
  const fridges = state.furniture.filter((f) => f.kind === 'fridge' && isPowered(state, f));
  let rotted = 0;
  for (const item of [...state.items]) {
    if ((item.kind !== 'food' && item.kind !== 'meal') || !item.pos || item.reservedBy) continue;
    const p = item.pos;
    if (fridges.some((f) => manhattan(f, p) <= FRIDGE_RADIUS)) continue;
    item.wear += (SPOIL_CHECK_EVERY * rate) / (stores.has(labelAt(state, p.x, p.y)) ? 2 : 1);
    if (item.wear < (item.kind === 'meal' ? MEAL_SHELF : FOOD_SHELF)) continue;
    removeItem(state, item);
    rotted += 1;
  }
  if (rotted > 0) logEvent(state, `${rotted} food ${rotted === 1 ? 'item' : 'items'} rotted (cool storage, winter and fridges keep food)`);
}
