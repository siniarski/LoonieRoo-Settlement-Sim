// The storyteller: paces the threats by how the colony is doing, instead of a
// fixed schedule.
//
// threatScale multiplies the night's zombie count and the chance of a raid:
// up THREAT_PER_WEALTH for every WEALTH_STEP things the colony owns (a rich
// colony draws more attention), down THREAT_PER_DEATH for each death in the
// last day and a half (a beaten colony gets a breather), within
// [MIN_THREAT, MAX_THREAT]. And when things are dire (two or more dead lately,
// food for under a day) it may leave a cache of supplies, at most once every
// RELIEF_GAP ticks.

import { foodDays } from './alerts';
import { logEvent } from './events';
import { spawnItem } from './items';
import type { SimState } from './state';
import { DAY_LENGTH } from './time';
import { frontierSpot } from './wanderers';

export const WEALTH_STEP = 100;
export const THREAT_PER_WEALTH = 0.1;
export const THREAT_PER_DEATH = 0.15;
export const MIN_THREAT = 0.5;
export const MAX_THREAT = 1.6;
export const RECENT = (3 * DAY_LENGTH) / 2;
export const RELIEF_GAP = 3 * DAY_LENGTH;

export function recentDeaths(state: SimState): number {
  return state.events.filter((e) => e.tick >= state.tick - RECENT && e.message.includes(' died of ')).length;
}

export function threatScale(state: SimState): number {
  const wealth = state.items.filter((i) => i.kind !== 'corpse').length;
  const scale = 1 + THREAT_PER_WEALTH * Math.floor(wealth / WEALTH_STEP) - THREAT_PER_DEATH * recentDeaths(state);
  return Math.max(MIN_THREAT, Math.min(MAX_THREAT, scale));
}

/** At dawn: maybe a cache, when the colony is on its knees. */
export function updateDirector(state: SimState): void {
  if (state.tick % DAY_LENGTH !== 0 || state.survivors.length === 0) return;
  if (state.tick - state.lastRelief < RELIEF_GAP || recentDeaths(state) < 2 || foodDays(state) >= 1) return;
  const spot = frontierSpot(state, new Set()) ?? state.survivors[0]!.pos;
  for (let n = 0; n < 6; n++) spawnItem(state, 'food', spot.x, spot.y);
  for (let n = 0; n < 2; n++) spawnItem(state, 'bandage', spot.x, spot.y);
  state.lastRelief = state.tick;
  logEvent(state, `Someone left a cache of supplies at (${spot.x}, ${spot.y})`);
}
