// The endgame: two ways to win, and the siege nights on the way.
//
// A cure: zombies brought down sometimes leave a tissue sample (SAMPLE_CHANCE,
// a hash). A powered lab bench gets a research job while there are samples to
// study (jobs.ts): bring one to the bench, work RESEARCH_WORK, and it's
// studied. CURE_SAMPLES studied and the colony has a cure.
//
// Evacuation: from day EVAC_FROM_DAY, a powered beacon and a powered radio get
// through to the military. A helicopter is promised for the dawn EVAC_DAYS later; the night
// before it is the worst siege of all (EVAC_SIEGE times the zombies). Anyone
// still alive at that dawn is flown out.
//
// Siege nights: every SIEGE_EVERY nights the horde comes in earnest
// (SIEGE_FACTOR times the zombies), with a warning two days ahead.
// Winning doesn't stop the game: it's marked (state.endgame.won) and told.

import { logEvent } from './events';
import { hashRoll, idNumber } from './hash';
import { spawnItem } from './items';
import { isPowered } from './power';
import { hasRadio } from './radio';
import type { SimState } from './state';
import { DAY_LENGTH, dayOf } from './time';

export interface Endgame {
  /** Samples studied so far. */
  cure: number;
  /** The tick the helicopter comes, once the beacon got through. */
  evacuation: number | null;
  won: 'cure' | 'evacuation' | null;
}

export const SAMPLE_CHANCE = 0.15;
export const CURE_SAMPLES = 40;
export const RESEARCH_WORK = 200;
export const EVAC_DAYS = 3;
/** The military only answers from this day on (a year in). */
export const EVAC_FROM_DAY = 20;
export const EVAC_SIEGE = 3;
export const SIEGE_EVERY = 7;
/** The first siege night (balance pass 5: night 7 was too soon). */
export const FIRST_SIEGE = 14;
export const SIEGE_FACTOR = 1.25;

export function newEndgame(): Endgame {
  return { cure: 0, evacuation: null, won: null };
}

/** A zombie went down: maybe it leaves a sample. */
export function maybeSample(state: SimState, zombieId: string, at: { x: number; y: number }): void {
  if (state.endgame.won === null && hashRoll(idNumber(zombieId), 0x5a3) < SAMPLE_CHANCE) spawnItem(state, 'sample', at.x, at.y);
}

/** Is this night (by number) the evacuation night, or a siege night? */
export function siegeFactor(state: SimState, night: number): number {
  const evac = state.endgame.evacuation;
  // The helicopter comes at 06:00; the night before began at 21:00 the day before.
  if (evac !== null && dayOf(evac) - 1 === night) return EVAC_SIEGE;
  return night >= FIRST_SIEGE && night % SIEGE_EVERY === 0 ? SIEGE_FACTOR : 1;
}

/** Days to the next siege night (0: tonight). */
export function daysToSiege(tick: number): number {
  const day = dayOf(tick);
  if (day < FIRST_SIEGE) return FIRST_SIEGE - day;
  return (SIEGE_EVERY - (day % SIEGE_EVERY)) % SIEGE_EVERY;
}

export function researchWanted(state: SimState): boolean {
  return state.endgame.won === null && state.endgame.cure < CURE_SAMPLES;
}

/** A sample studied at a lab bench. */
export function studied(state: SimState): void {
  state.endgame.cure += 1;
  if (state.endgame.cure < CURE_SAMPLES) return;
  state.endgame.won = 'cure';
  logEvent(state, 'THE CURE: the last sample gave it up. The colony has a cure for the infection. You have won (play on if you like)');
}

export function updateEndgame(state: SimState): void {
  const eg = state.endgame;
  if (state.tick % DAY_LENGTH === 0 && state.tick > 0) {
    const ahead = daysToSiege(state.tick);
    if (ahead === 2) logEvent(state, `A siege is coming in two nights (night ${dayOf(state.tick) + 2}): a quarter more zombies. Get ready`);
  }
  if (eg.won !== null) return;
  if (eg.evacuation === null) {
    if (dayOf(state.tick) < EVAC_FROM_DAY || !hasRadio(state) || !state.furniture.some((f) => f.kind === 'beacon' && isPowered(state, f))) return;
    // The dawn EVAC_DAYS days on.
    eg.evacuation = (Math.floor(state.tick / DAY_LENGTH) + EVAC_DAYS) * DAY_LENGTH;
    logEvent(state, `The beacon got through: a helicopter will come at dawn on day ${dayOf(eg.evacuation)}. The night before will be the worst yet. Hold out`);
    return;
  }
  if (state.tick < eg.evacuation) return;
  if (state.survivors.length === 0) {
    eg.evacuation = null;
    return;
  }
  eg.won = 'evacuation';
  logEvent(state, `THE HELICOPTER came at dawn: ${state.survivors.map((s) => s.name).join(', ')} flew out. You have won (play on if you like)`);
}
