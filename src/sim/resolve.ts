// Resolve: the will to keep going, 0..100. Slower than mood: mood is how today feels,
// resolve is what the days add up to.
//
// Every dawn it drifts with mood (a good day +, a bad day -); loneliness and a poor bed
// get in through mood (wellbeing.ts).
// Events move it directly (steel()): a request seen through, someone found, a confession
// made lift it; a friend's death, a silent radio cut it.
//   breaking (under BREAKING)  might walk away in the night; under your control, freezes
//                              in a fight now and then
//   steeled (STEELED or more)  doesn't break down
// It starts near the middle, a little different for each (a hash of their id).

import { hashRoll, idNumber } from './hash';
import { logEvent } from './events';
import { mood, MOOD_BASE } from './mood';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { DAY_LENGTH } from './time';

export const RESOLVE_MAX = 100;
export const BREAKING = 20;
export const STEELED = 80;
/** Mood points above or below MOOD_BASE per point of resolve at dawn. */
const MOOD_PER_POINT = 8;
/** The most a single dawn can move it. */
const DAWN_LIMIT = 4;
/** Chance of freezing on a swing, while breaking. */
export const FREEZE_CHANCE = 0.25;

export function startingResolve(id: string): number {
  return 40 + Math.floor(hashRoll(idNumber(id), 0x7e5) * 21);
}

export function steel(s: Survivor, by: number): void {
  s.resolve = Math.max(0, Math.min(RESOLVE_MAX, s.resolve + by));
}

export function isBreaking(s: Survivor): boolean {
  return s.resolve < BREAKING;
}

export function isSteeled(s: Survivor): boolean {
  return s.resolve >= STEELED;
}

export function describeResolve(s: Survivor): string {
  return isBreaking(s) ? 'breaking' : isSteeled(s) ? 'steeled' : s.resolve < 40 ? 'wavering' : 'steady';
}

/** Dawn: drift with mood. Returns those who walk away (the caller removes them). */
export function dawnResolve(state: SimState): Survivor[] {
  if (state.tick === 0 || state.tick % DAY_LENGTH !== 0) return [];
  const leaving: Survivor[] = [];
  for (const s of state.survivors) {
    const drift = Math.round((mood(state, s) - MOOD_BASE) / MOOD_PER_POINT);
    steel(s, Math.max(-DAWN_LIMIT, Math.min(DAWN_LIMIT, drift)));
    // Breaking: maybe gone by morning. Never the one you control, nor a child.
    if (!isBreaking(s) || state.control?.survivorId === s.id || s.childUntil !== null) continue;
    if (hashRoll(idNumber(s.id), state.tick, 0x1ea7) < (BREAKING - s.resolve) / (2 * BREAKING)) leaving.push(s);
  }
  for (const s of leaving) logEvent(state, `${s.name} walked away in the night. They couldn't do it any more.`);
  return leaving;
}

/** Breaking, and you're in control: a swing may freeze. */
export function freezes(state: SimState, s: Survivor): boolean {
  return isBreaking(s) && hashRoll(idNumber(s.id), state.tick, 0xf2) < FREEZE_CHANCE;
}
