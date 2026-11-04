// Moodlets: thoughts (mood.ts) that do something beyond mood, while they last.
//   faster    well rested (woke in a bed, rested): work a bit faster, swing sooner
//   harder    blood up (just made a kill): hit a fifth harder
//   slower    shaken (just got hit): swing later
//   slowWalk  out in the cold: a tick slower a tile

import type { ThoughtKind } from './mood';
import type { Survivor } from './survivors';

export type Boost = 'faster' | 'harder' | 'slower' | 'slowWalk';

export const BOOSTS: Partial<Record<ThoughtKind, Boost>> = {
  wellRested: 'faster',
  bloodUp: 'harder',
  shaken: 'slower',
  cold: 'slowWalk',
};
/** Blood up: damage, in percent. */
export const HARDER_PCT = 120;
/** Ticks off (well rested) or onto (shaken) the wait between swings. */
export const SWING_SOONER = 2;
export const SWING_LATER = 3;

/** Does one of this survivor's current thoughts give this boost? (Thoughts past their time are dropped each tick.) */
export function boosted(s: Survivor, boost: Boost): boolean {
  return s.thoughts.some((t) => BOOSTS[t.kind] === boost);
}

/** A blow's damage after moodlets. */
export function boostDamage(s: Survivor, damage: number): number {
  return boosted(s, 'harder') ? Math.round((damage * HARDER_PCT) / 100) : damage;
}

/** Ticks between swings after moodlets. */
export function swingWait(s: Survivor, base: number): number {
  return base - (boosted(s, 'faster') ? SWING_SOONER : 0) + (boosted(s, 'slower') ? SWING_LATER : 0);
}
