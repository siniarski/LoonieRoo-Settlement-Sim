// Infection: a zombie's bite can carry it.
//
// A hit infects with INFECTION_CHANCE (a hash of the zombie and the tick, not
// the sim RNG, so combat doesn't shift everything else's randomness). The
// infection runs for INFECTION_TURN ticks (a day), half as fast for someone
// resting in an infirmary bed; then the survivor dies and gets up as a
// zombie. Medicine (crafted in a workshop) cures it: the infected fetch it
// like a bandage, and doctors bring it to infected patients (needs.ts, jobs.ts).

import { logEvent } from './events';
import { inInfirmary } from './medicine';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { DAY_LENGTH } from './time';

export const INFECTION_CHANCE = 0.15;
export const INFECTION_TURN = DAY_LENGTH;
/** What an infection does to mood while it lasts. */
export const INFECTED_MOOD = -15;

function roll(a: number, b: number): number {
  let h = Math.imul(a ^ 0x27d4eb2f, 0x85ebca6b) ^ Math.imul(b, 0xc2b2ae35);
  h = Math.imul(h ^ (h >>> 15), 0x2c1b3c6d);
  return ((h ^ (h >>> 13)) >>> 0) / 4294967296;
}

/** A zombie's hit: maybe it infects. */
export function maybeInfect(state: SimState, s: Survivor, zombieId: string): void {
  if (s.infection !== null) return;
  if (roll(Number(zombieId.replace(/\D/g, '')), state.tick) >= INFECTION_CHANCE) return;
  s.infection = 0;
  logEvent(state, `${s.name} was bitten and is infected: medicine can cure it`);
}

/** Runs every tick for every survivor. Returns a cause of death when the infection wins. */
export function updateInfection(state: SimState, s: Survivor): string | null {
  if (s.infection === null) return null;
  if (!inInfirmary(state, s) || state.tick % 2 === 0) s.infection += 1;
  return s.infection >= INFECTION_TURN ? 'the infection' : null;
}

export function isInfected(s: Survivor): boolean {
  return s.infection !== null;
}
