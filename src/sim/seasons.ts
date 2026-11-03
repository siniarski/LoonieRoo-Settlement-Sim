// Seasons and weather.
//
// Seasons run SEASON_DAYS each, from spring. Crops grow at full speed in
// spring and summer, half speed in autumn, not at all in winter. Each dawn
// rolls the day's weather (sim RNG): rain (not in winter) makes crops grow
// half as fast again and soaks anyone outdoors; snow (in winter) just looks
// cold. Winter nights are cold outdoors: a slow loss of health, and the mood
// to match. Indoors (any enclosed room) is warm.

import { addThought } from './mood';
import { random } from './rng';
import { isIndoors } from './rooms';
import type { SimState } from './state';
import { DAY_LENGTH, TICKS_PER_HOUR, dayOf, isNight } from './time';
import { wearOut, wearing } from './clothing';
import type { Survivor } from './survivors';

export const SEASON_DAYS = 5;
export const SEASONS = ['spring', 'summer', 'autumn', 'winter'] as const;
export type Season = (typeof SEASONS)[number];
export type Weather = 'clear' | 'rain' | 'snow';

export const RAIN_CHANCE = 0.3;
export const SNOW_CHANCE = 0.4;
/** Out on a winter night, a survivor loses 1 health this often, and doesn't heal (about 160 over a night). */
export const COLD_DAMAGE_EVERY = 10;

export function seasonOf(tick: number): Season {
  return SEASONS[Math.floor((dayOf(tick) - 1) / SEASON_DAYS) % SEASONS.length]!;
}

/** Does a planted crop grow this tick? (Called once per plot per tick.) */
export function cropGrows(state: SimState): number {
  const season = seasonOf(state.tick);
  if (season === 'winter') return 0;
  let growth = season === 'autumn' ? (state.tick % 2 === 0 ? 1 : 0) : 1;
  if (state.weather === 'rain' && state.tick % 2 === 0) growth += 1;
  return growth;
}

/** Out of doors on a winter night without a coat. */
export function isCold(state: SimState, s: Survivor): boolean {
  return seasonOf(state.tick) === 'winter' && isNight(state.tick) && !isIndoors(state, s.pos.x, s.pos.y) && !wearing(state, s, 'coat');
}

/** Dawn's weather roll, and the weather's effect on people outdoors. Runs every tick. */
export function updateWeather(state: SimState): void {
  // Day 1 starts clear; from day 2 each dawn rolls.
  if (state.tick > 0 && state.tick % DAY_LENGTH === 0) {
    const winter = seasonOf(state.tick) === 'winter';
    state.weather = random(state) < (winter ? SNOW_CHANCE : RAIN_CHANCE) ? (winter ? 'snow' : 'rain') : 'clear';
  }
  const coldNight = seasonOf(state.tick) === 'winter' && isNight(state.tick);
  if (!coldNight && state.weather !== 'rain') return;
  for (const s of [...state.survivors]) {
    if (isIndoors(state, s.pos.x, s.pos.y)) continue;
    // A coat keeps the weather off, and wears a little for each hour it does.
    const coat = wearing(state, s, 'coat');
    if (coat) {
      if (state.tick % TICKS_PER_HOUR === 0) wearOut(state, s, coat);
      continue;
    }
    if (state.weather === 'rain' && !coldNight) addThought(state, s, 'soaked');
    if (coldNight) {
      addThought(state, s, 'cold');
      if (state.tick % COLD_DAMAGE_EVERY === 0) {
        s.health = Math.max(0, s.health - 1);
        s.lastHurtBy = 'the cold';
      }
    }
  }
}
