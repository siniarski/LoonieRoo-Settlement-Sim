// Colony policies and the leader.
//
// Policies are switches with a price:
//   rationing   food goes a quarter further (satiety drains 3 ticks in 4), but everyone minds (-5)
//   curfew      at night nobody but guards, firefighters and doctors takes work outdoors
//   quarantine  the infected and the sick keep to their beds (so doctors come to them) and stop spreading it
//
// The leader is whoever the others think best of (an average opinion of at
// least LEADER_MIN, with three or more in the colony), settled each dawn. A
// colony with a leader is a little happier (+5); losing them hits everyone
// hard ("lost our leader", mood.ts).

import { logEvent } from './events';
import { opinion } from './relationships';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { DAY_LENGTH } from './time';

export type Policy = 'rationing' | 'curfew' | 'quarantine';
export const POLICIES: readonly Policy[] = ['rationing', 'curfew', 'quarantine'];
export const POLICY_INFO: Record<Policy, string> = {
  rationing: 'food lasts a quarter longer; everyone minds (-5 mood)',
  curfew: 'no work outdoors at night but guarding, firefighting and doctoring',
  quarantine: 'the infected and sick keep to bed (doctors come) and stop spreading it',
};
export const RATIONED_MOOD = -5;
export const LEADER_MIN = 20;
export const LEADER_MOOD = 5;

export function isPolicy(p: unknown): p is Policy {
  return typeof p === 'string' && (POLICIES as readonly string[]).includes(p);
}

export function policyOn(state: SimState, p: Policy): boolean {
  return state.policies.includes(p);
}

/** Rationing: satiety drains three ticks in four. */
export function satietyDrains(state: SimState): boolean {
  return !policyOn(state, 'rationing') || state.tick % 4 !== 0;
}

/** Quarantine: the infected and sick stay in bed. */
export function quarantined(state: SimState, s: Survivor): boolean {
  return policyOn(state, 'quarantine') && (s.infection !== null || s.sick !== null);
}

/** The best-thought-of survivor, if anyone is thought well enough of. */
export function chooseLeader(state: SimState): Survivor | null {
  if (state.survivors.length < 3) return null;
  let best: Survivor | null = null;
  let bestScore = LEADER_MIN;
  for (const s of state.survivors) {
    if (s.childUntil !== null) continue;
    const others = state.survivors.filter((o) => o !== s);
    const score = others.reduce((sum, o) => sum + opinion(o, s), 0) / others.length;
    if (score >= bestScore) {
      best = s;
      bestScore = score;
    }
  }
  return best;
}

export function updateLeader(state: SimState): void {
  if (state.tick % DAY_LENGTH !== 0) return;
  const leader = chooseLeader(state);
  if ((leader?.id ?? null) === state.leaderId) return;
  state.leaderId = leader?.id ?? null;
  if (leader) logEvent(state, `${leader.name} has become the colony's leader (+${LEADER_MOOD} mood for everyone)`);
}
