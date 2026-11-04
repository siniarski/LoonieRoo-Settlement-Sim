// Climate regions and radiation, in generated worlds (hand-made maps are mild and clean).
//
//   regions    the north of the map runs cold, the south hot, the middle mild, with a
//              wandering border (a slow noise field). Cold: nights are cold from autumn,
//              and winter days too. Hot: no cold short of a blizzard, and summer days
//              dry you out twice as fast.
//   radiation  in a nuclear winter only: hot spots (the high points of another noise field,
//              well away from the start). Standing in one costs health and leaves a bad
//              thought; crates there hold twice the loot, which is why anyone goes in.

import type { SimState } from './state';
import type { Survivor } from './survivors';
import { noise } from './worldgen';
import { addThought } from './mood';
import { seasonOf } from './seasons';
import { isNight } from './time';

export type Climate = 'cold' | 'mild' | 'hot';

/** Kept clean around the start, so a new game isn't poisoned from the first step. */
export const CLEAN_RADIUS = 40;
export const RAD_EVERY = 20;
export const RAD_DAMAGE = 2;

export function climateAt(state: SimState, x: number, y: number): Climate {
  const world = state.world;
  if (!world) return 'mild';
  const t = y / world.size + 0.25 * (noise(world.seed, x, y, 64, 41) - 0.5);
  return t < 0.33 ? 'cold' : t > 0.67 ? 'hot' : 'mild';
}

/** Is the open air cold here and now, by region and season? (Blizzards are handled by the caller.) */
export function regionCold(state: SimState, x: number, y: number): boolean {
  const climate = climateAt(state, x, y);
  const season = seasonOf(state.tick);
  if (climate === 'hot') return false;
  const night = isNight(state.tick);
  if (climate === 'cold') return season === 'winter' || (night && season === 'autumn') || (night && state.settings.winter);
  return night && (season === 'winter' || state.settings.winter);
}

/** Summer days in the hot south: thirst comes twice as fast. */
export function scorching(state: SimState, x: number, y: number): boolean {
  return seasonOf(state.tick) === 'summer' && !isNight(state.tick) && climateAt(state, x, y) === 'hot';
}

export function isRadioactive(state: SimState, x: number, y: number): boolean {
  const world = state.world;
  if (!world || !state.settings.winter) return false;
  const c = world.size / 2;
  if ((x - c) ** 2 + (y - c) ** 2 < CLEAN_RADIUS * CLEAN_RADIUS) return false;
  return noise(world.seed, x, y, 30, 42) > 0.8;
}

/** Every RAD_EVERY ticks in a hot spot: health, and the thought of it. */
export function updateRadiation(state: SimState, s: Survivor): void {
  if (state.tick % RAD_EVERY !== 0 || !isRadioactive(state, s.pos.x, s.pos.y)) return;
  s.health = Math.max(0, s.health - RAD_DAMAGE);
  s.lastHurtBy = 'radiation';
  addThought(state, s, 'irradiated');
}
