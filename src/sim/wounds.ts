// Wounds: a zombie or raider blow sometimes lands on an arm or a leg
// (WOUND_CHANCE, a hash). The wound bleeds (a health point every
// BLEED_EVERY ticks) for BLEED_TICKS or until a bandage stops it, and slows its owner for
// WOUND_TICKS: an arm wound slows work, a leg wound slows walking.
//
// A bite that infects is remembered by limb. Within AMPUTATE_WINDOW of the
// bite, the limb can come off (the amputate command): the infection goes with
// it, at a heavy cost in health and for good (lostArm or lostLeg).

import { logEvent } from './events';
import { hashRoll, idNumber } from './hash';
import { INFECTION_TURN } from './infection';
import { hasTrait } from './skills';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { DAY_LENGTH, onClock } from './time';

export type Limb = 'arm' | 'leg';

export interface Wound {
  limb: Limb;
  until: number;
}

export const WOUND_CHANCE = 0.15;
export let WOUND_TICKS = 2 * DAY_LENGTH;
onClock(() => (WOUND_TICKS = 2 * DAY_LENGTH));
export const BLEED_EVERY = 15;
/** Bleeding stops by itself after this (sooner with a bandage). Balance pass 5: it ran on until bandaged, and bandages ran out. */
export const BLEED_TICKS = 300;
/** Ticks of infection within which an amputation saves them. */
export const AMPUTATE_WINDOW = INFECTION_TURN / 4;
export const AMPUTATION_COST = 300;

/** A blow landed: maybe a wound (and the limb a bite would have gone into). */
export function maybeWound(state: SimState, s: Survivor, attackerId: string): Limb {
  const limb: Limb = hashRoll(idNumber(attackerId), state.tick, idNumber(s.id), 1) < 0.5 ? 'arm' : 'leg';
  if (!s.wound && hashRoll(idNumber(attackerId), state.tick, idNumber(s.id), 2) < WOUND_CHANCE) {
    s.wound = { limb, until: state.tick + WOUND_TICKS };
    s.bleeding = true;
    s.bleedUntil = state.tick + BLEED_TICKS;
  }
  return limb;
}

export function updateWounds(state: SimState, s: Survivor): void {
  if (s.wound && state.tick >= s.wound.until) s.wound = null;
  if (s.bleeding && state.tick >= s.bleedUntil) s.bleeding = false;
  if (s.bleeding && state.tick % BLEED_EVERY === 0) {
    s.health = Math.max(0, s.health - 1);
    s.lastHurtBy = 'bleeding';
  }
}

/** Slowed at work: a wounded or lost arm. */
export function armSlowed(s: Survivor): boolean {
  return s.wound?.limb === 'arm' || hasTrait(s, 'lostArm');
}

/** Slowed walking: a wounded or lost leg. */
export function legSlowed(s: Survivor): boolean {
  return s.wound?.limb === 'leg' || hasTrait(s, 'lostLeg');
}

export function canAmputate(s: Survivor): boolean {
  return s.infection !== null && s.infection < AMPUTATE_WINDOW && s.bitten !== null;
}

/** Takes the bitten limb off. Returns an error, or null when done. */
export function amputate(state: SimState, s: Survivor): string | null {
  if (!canAmputate(s)) return s.infection === null ? `${s.name} isn't infected` : 'too late: it has spread';
  const limb = s.bitten!;
  s.infection = null;
  s.bitten = null;
  s.bleeding = true;
  s.bleedUntil = state.tick + BLEED_TICKS;
  s.health = Math.max(1, s.health - AMPUTATION_COST);
  s.lastHurtBy = 'an amputation';
  const trait = limb === 'arm' ? 'lostArm' : 'lostLeg';
  if (!s.traits.includes(trait)) s.traits.push(trait);
  logEvent(state, `${s.name}'s bitten ${limb} came off. The infection went with it`);
  return null;
}
