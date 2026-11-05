// Ages, children and growing up.
//
// Everyone has an age (years). A year passes every YEAR_DAYS (a full round of
// seasons); from ELDER on, people work at 3/4 speed. In a harsh game, two
// adults who think the world of each other (LOVERS both ways) may have a
// baby, once a day at most for the colony, if there's a spare bed for it (a
// hash roll, not the sim RNG). Children don't work (no priorities) and grow
// up after CHILDHOOD ticks, with a head start in a couple of skills.

import { logEvent } from './events';
import { ofKind } from './furniture';
import { hashRoll, idNumber } from './hash';
import { DEFAULT_PRIORITIES, NO_JOBS, type JobType } from './jobTypes';
import { opinion } from './relationships';
import { XP_PER_LEVEL } from './skills';
import { nearestTile } from './space';
import type { SimState } from './state';
import { addSurvivor, type Survivor } from './survivors';
import { DAY_LENGTH, onClock } from './time';
import { isWalkable } from './grid';

export const YEAR_DAYS = 20;
export const ELDER = 65;
export const LOVERS = 60;
export const BIRTH_CHANCE = 0.25;
export const MAX_PARENT_AGE = 50;
export let CHILDHOOD = 10 * DAY_LENGTH;
onClock(() => (CHILDHOOD = 10 * DAY_LENGTH));
/** Age a child is counted as once grown. */
export const GROWN_AGE = 16;
const HEAD_START: readonly JobType[] = ['build', 'farm', 'craft', 'cook', 'doctor', 'guard', 'scavenge'];

export function isChild(s: Survivor): boolean {
  return s.childUntil !== null;
}

export function isElder(s: Survivor): boolean {
  return s.age >= ELDER;
}

/** An adult's starting age, from their id. */
export function startingAge(id: string): number {
  return 18 + Math.floor(hashRoll(idNumber(id), 0xa9e) * 45);
}

export function updateFamily(state: SimState): void {
  if (state.tick > 0 && state.tick % (YEAR_DAYS * DAY_LENGTH) === 0) for (const s of state.survivors) s.age += 1;
  for (const s of state.survivors) if (s.childUntil !== null && state.tick >= s.childUntil) growUp(state, s);
  if (state.harsh && state.tick % DAY_LENGTH === DAY_LENGTH / 2) births(state);
}

function births(state: SimState): void {
  const spareBed = ofKind(state, 'bed').some((f) => f.owner === null);
  if (!spareBed) return;
  const adults = state.survivors.filter((s) => !isChild(s) && s.age <= MAX_PARENT_AGE);
  for (let i = 0; i < adults.length; i++) {
    for (let j = i + 1; j < adults.length; j++) {
      const a = adults[i]!;
      const b = adults[j]!;
      if (opinion(a, b) < LOVERS || opinion(b, a) < LOVERS) continue;
      if (hashRoll(idNumber(a.id), idNumber(b.id), state.tick) >= BIRTH_CHANCE) continue;
      const spot = nearestTile(state.grid, a.pos, (x, y) => isWalkable(state.grid, x, y)) ?? a.pos;
      const baby = addSurvivor(state, spot.x, spot.y);
      baby.age = 0;
      baby.childUntil = state.tick + CHILDHOOD;
      baby.priorities = { ...NO_JOBS };
      baby.opinions = { [a.id]: 50, [b.id]: 50 };
      a.opinions[baby.id] = 50;
      b.opinions[baby.id] = 50;
      logEvent(state, `A baby, ${baby.name}, was born to ${a.name} and ${b.name}`);
      return; // one a day at most
    }
  }
}

function growUp(state: SimState, s: Survivor): void {
  s.childUntil = null;
  s.age = Math.max(s.age, GROWN_AGE);
  s.priorities = { ...DEFAULT_PRIORITIES };
  // Children pick things up fast: a couple of levels in two skills.
  const n = idNumber(s.id);
  const a = HEAD_START[n % HEAD_START.length]!;
  const b = HEAD_START[(n + 3) % HEAD_START.length]!;
  for (const t of [a, b]) s.xp[t] = (s.xp[t] ?? 0) + 2 * XP_PER_LEVEL;
  logEvent(state, `${s.name} has grown up (good at ${a} and ${b})`);
}
