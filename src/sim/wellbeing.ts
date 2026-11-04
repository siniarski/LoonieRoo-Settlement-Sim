// Higher needs, above survival: Social and Comfort, 0..100 each. Neither kills anyone;
// both move mood (mood.ts), and so, day by day, resolve (resolve.ts).
//
//   Social   rises while awake near someone else (within COMPANY tiles) and with every
//            talk; sinks while alone. Under LOW: "lonely". Over HIGH: "good company".
//   Comfort  set each time they wake, from where they slept: a bed, indoors, a finished
//            bedroom (housing or barracks), warm, clean. It moves halfway towards each
//            night's score, so one bad night doesn't undo a week of good ones.

import { furnitureAt } from './furniture';
import { isDirty } from './grid';
import { isWarmed } from './power';
import { designationOf, evaluateRoom, roomAt } from './rooms';
import { seasonOf } from './seasons';
import type { SimState } from './state';
import type { Survivor } from './survivors';

export const NEED_MAX = 100;
export const LOW = 25;
export const HIGH = 75;
/** Social is updated this often, by one point up (in company) or down (alone). */
export const SOCIAL_EVERY = 40;
/** Within this many tiles (Chebyshev) of someone awake counts as company. */
export const COMPANY = 4;
export const TALK_SOCIAL = 20;
/** Mood while lonely / in good company / uncomfortable / comfortable. */
export const LONELY_MOOD = -10;
export const COMPANY_MOOD = 5;
export const UNCOMFORTABLE_MOOD = -8;
export const COMFORTABLE_MOOD = 8;

const clamp = (n: number) => Math.max(0, Math.min(NEED_MAX, n));

export function updateSocial(state: SimState): void {
  if (state.tick % SOCIAL_EVERY !== 0) return;
  const awake = state.survivors.filter((s) => !s.sleep?.asleep);
  for (const s of state.survivors) {
    if (s.sleep?.asleep) continue;
    const company = awake.some((o) => o !== s && Math.max(Math.abs(o.pos.x - s.pos.x), Math.abs(o.pos.y - s.pos.y)) <= COMPANY);
    s.social = clamp(s.social + (company ? 1 : -1));
  }
}

export function socialise(s: Survivor, by: number): void {
  s.social = clamp(s.social + by);
}

/** How good a night's sleep was, where they lie now (bedId: the bed, or null for the floor). */
export function nightScore(state: SimState, s: Survivor, bedId: string | null): number {
  const bed = bedId ? furnitureAt(state, s.pos.x, s.pos.y) : undefined;
  const inBed = bed?.kind === 'bed' && bed.id === bedId;
  const room = roomAt(state, s.pos.x, s.pos.y);
  const purpose = room ? designationOf(state, room)?.purpose : undefined;
  const bedroom = room && (purpose === 'housing' || purpose === 'barracks') && evaluateRoom(state, room, purpose).complete;
  const warm = isWarmed(state, s.pos) || (seasonOf(state.tick) !== 'winter' && !state.settings.winter);
  const dirty = room ? room.tiles.filter((i) => isDirty(state.grid, i % state.grid.width, Math.floor(i / state.grid.width))).length : 0;
  return clamp(15 + (inBed ? 30 : 0) + (room ? 20 : 0) + (bedroom ? 15 : 0) + (warm ? 15 : 0) - Math.min(15, dirty * 5));
}

/** On waking: comfort moves halfway to the night's score. */
export function wakeComfort(state: SimState, s: Survivor, bedId: string | null): void {
  s.comfort = Math.round((s.comfort + nightScore(state, s, bedId)) / 2);
}

/** Mood factors from the higher needs, as [label, effect] pairs. */
export function wellbeingFactors(s: Survivor): [string, number][] {
  const out: [string, number][] = [];
  if (s.social < LOW) out.push(['lonely', LONELY_MOOD]);
  else if (s.social > HIGH) out.push(['good company', COMPANY_MOOD]);
  if (s.comfort < LOW) out.push(['uncomfortable', UNCOMFORTABLE_MOOD]);
  else if (s.comfort > HIGH) out.push(['comfortable', COMFORTABLE_MOOD]);
  return out;
}
