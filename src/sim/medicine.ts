// Doctors and the infirmary.
//
// A patient is a hurt survivor asleep in a bed (resting, see needs.ts). Each
// patient gets one doctor job: the doctor fetches the nearest free bandage,
// carries it to the bedside, and treats them (BANDAGE_HEAL, same as treating
// yourself). The job itself lives in jobs.ts; this file says who is a patient.
//
// The infirmary is a room with beds. Hurt survivors pick its beds first and
// heal INFIRMARY_HEAL_FACTOR times as fast in them; others only use them when
// there's no other bed. Barracks beds are the guards' (see needs.ts: light sleepers).

import { getFurniture, type Furniture } from './furniture';
import { BANDAGE_HEAL, HEALTH_MAX, isHurt } from './health';
import type { ItemKind } from './items';
import { completeRooms, labelAt } from './rooms';
import type { SimState } from './state';
import type { Survivor } from './survivors';

export const INFIRMARY_HEAL_FACTOR = 2;
export const MEDICINE_HEAL = 100;

export function isPatient(s: Survivor): boolean {
  return (isHurt(s.health) || s.bleeding || s.infection !== null || s.sick !== null) && s.sleep?.asleep === true && s.sleep.bedId !== null && !s.treatment;
}

/** What would help this survivor, best first: medicine for an infection, a bandage for wounds. */
export function remediesFor(s: Survivor): ItemKind[] {
  return [...(s.infection !== null || s.sick !== null ? (['medicine'] as const) : []), ...(isHurt(s.health) || s.bleeding ? (['bandage'] as const) : [])];
}

/** Applying a remedy: medicine cures an infection (and heals a little); a bandage heals wounds. */
export function applyRemedy(s: Survivor, kind: ItemKind): void {
  if (kind === 'medicine') {
    s.infection = null;
    s.sick = null;
    s.health = Math.min(HEALTH_MAX, s.health + MEDICINE_HEAL);
  } else {
    s.health = Math.min(HEALTH_MAX, s.health + BANDAGE_HEAL);
    s.bleeding = false;
  }
}

/** Is this bed in a complete infirmary? */
export function isInfirmaryBed(state: SimState, bed: Furniture): boolean {
  return completeRooms(state, 'infirmary').has(labelAt(state, bed.x, bed.y));
}

/** Is this bed in a complete barracks? */
export function isBarracksBed(state: SimState, bed: Furniture): boolean {
  return completeRooms(state, 'barracks').has(labelAt(state, bed.x, bed.y));
}

/** Guards: anyone with guarding switched on. They prefer barracks beds and sleep lightly there. */
export function isGuard(s: Survivor): boolean {
  return s.priorities.guard > 0;
}

/** Is this survivor resting in an infirmary bed right now? */
export function inInfirmary(state: SimState, s: Survivor): boolean {
  const bed = s.sleep?.asleep && s.sleep.bedId ? getFurniture(state, s.sleep.bedId) : undefined;
  return bed !== undefined && isInfirmaryBed(state, bed);
}
