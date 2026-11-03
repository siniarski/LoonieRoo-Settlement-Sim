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
import { isWarmed } from './power';
import { hashRoll } from './hash';
import { ignite } from './fire';
import { logEvent } from './events';
import type { Survivor } from './survivors';

export const SEASON_DAYS = 5;
export const SEASONS = ['spring', 'summer', 'autumn', 'winter'] as const;
export type Season = (typeof SEASONS)[number];
export type Weather = 'clear' | 'rain' | 'snow' | 'blizzard' | 'heatwave' | 'storm';

export const RAIN_CHANCE = 0.3;
export const SNOW_CHANCE = 0.4;
/** The top of the dawn roll is a weather event: a blizzard in winter, a heatwave in summer, a storm otherwise. */
export const EVENT_CHANCE = 0.15;
/** In a storm, the chance each hour that lightning strikes near the colony. */
export const LIGHTNING_CHANCE = 0.08;

/** Rain, snow, storms and blizzards wet things (crops, collectors, fires). */
export function isWet(w: Weather): boolean {
  return w === 'rain' || w === 'snow' || w === 'storm' || w === 'blizzard';
}
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
  if ((state.weather === 'rain' || state.weather === 'storm') && state.tick % 2 === 0) growth += 1;
  return growth;
}

/** Lightning strikes something that burns near a survivor (a hash picks what). */
function lightning(state: SimState): void {
  const s = state.survivors[Math.floor(hashRoll(state.tick, 1) * state.survivors.length)];
  if (!s) return;
  const targets = state.furniture.filter((f) => Math.abs(f.x - s.pos.x) + Math.abs(f.y - s.pos.y) <= 20 && !isIndoors(state, f.x, f.y));
  const t = targets[Math.floor(hashRoll(state.tick, 2) * targets.length)];
  if (t && ignite(state, t.x, t.y)) logEvent(state, `Lightning struck a ${t.kind} at (${t.x}, ${t.y})!`);
}

/** Out of doors on a winter night (or in a blizzard) without a coat or a heater. */
export function isCold(state: SimState, s: Survivor): boolean {
  return (
    ((seasonOf(state.tick) === 'winter' && isNight(state.tick)) || state.weather === 'blizzard') &&
    !isIndoors(state, s.pos.x, s.pos.y) &&
    !wearing(state, s, 'coat') &&
    !isWarmed(state, s.pos)
  );
}

/** Dawn's weather roll, and the weather's effect on people outdoors. Runs every tick. */
export function updateWeather(state: SimState): void {
  // Day 1 starts clear; from day 2 each dawn rolls.
  if (state.tick > 0 && state.tick % DAY_LENGTH === 0) {
    const season = seasonOf(state.tick);
    const winter = season === 'winter';
    // One roll (so the sim RNG stream is as it was): low for rain or snow, the top for an event.
    const roll = random(state);
    state.weather = roll < (winter ? SNOW_CHANCE : RAIN_CHANCE) ? (winter ? 'snow' : 'rain') : roll >= 1 - EVENT_CHANCE ? (winter ? 'blizzard' : season === 'summer' ? 'heatwave' : 'storm') : 'clear';
    if (state.weather === 'blizzard') logEvent(state, 'A blizzard: anyone outdoors without a coat or a heater will freeze, day or night');
    if (state.weather === 'heatwave') logEvent(state, 'A heatwave: thirst comes twice as fast, and food spoils quicker');
    if (state.weather === 'storm') logEvent(state, 'A storm: lightning may set things alight');
  }
  if (state.weather === 'storm' && state.tick % TICKS_PER_HOUR === 0 && hashRoll(state.tick, 0x57) < LIGHTNING_CHANCE) lightning(state);
  const coldNight = (seasonOf(state.tick) === 'winter' && isNight(state.tick)) || state.weather === 'blizzard';
  const soaking = state.weather === 'rain' || state.weather === 'storm';
  if (!coldNight && !soaking) return;
  for (const s of [...state.survivors]) {
    if (isIndoors(state, s.pos.x, s.pos.y)) continue;
    // A coat keeps the weather off, and wears a little for each hour it does.
    const coat = wearing(state, s, 'coat');
    if (coat) {
      if (state.tick % TICKS_PER_HOUR === 0) wearOut(state, s, coat);
      continue;
    }
    if (soaking && !coldNight) addThought(state, s, 'soaked');
    if (coldNight && !isWarmed(state, s.pos)) {
      addThought(state, s, 'cold');
      if (state.tick % COLD_DAMAGE_EVERY === 0) {
        s.health = Math.max(0, s.health - 1);
        s.lastHurtBy = 'the cold';
      }
    }
  }
}
