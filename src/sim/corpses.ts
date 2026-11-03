// Corpses and sickness. Only in a harsh game (state.harsh: generated worlds;
// hand-made maps opt in).
//
// The dead stay where they fall, as 'corpse' items (tag: the survivor's
// name, or null for a zombie; wear: the tick they died). Haulers carry them
// to a graveyard (a room purpose); one left out more than CORPSE_ROT ticks
// rots. Rotting corpses sour the mood of anyone within STENCH_RADIUS ("the
// stench") and may make them sick. A zombie's corpse is gone after
// CORPSE_GONE ticks; survivors' stay until buried.
//
// Sickness lasts SICK_TICKS: slowed like the hurt, a slow loss of health, and
// a worse mood. Medicine cures it. The grieving pay their respects at a
// graveyard (leisure.ts).

import { logEvent } from './events';
import { hashRoll, idNumber } from './hash';
import { spawnItem, removeItem, type Item } from './items';
import { completeRooms, labelAt } from './rooms';
import { manhattan } from './space';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { addThought } from './mood';
import { DAY_LENGTH } from './time';

export const CORPSE_ROT = DAY_LENGTH / 2;
export const CORPSE_GONE = 3 * DAY_LENGTH;
export const STENCH_RADIUS = 4;
export const CORPSE_CHECK_EVERY = 200;
/** Chance per check that someone breathing in the stench falls sick. */
export const SICK_CHANCE = 0.08;
export const SICK_TICKS = DAY_LENGTH;
/** A sick survivor loses 1 health this often. */
export const SICK_DAMAGE_EVERY = 40;
export const SICK_MOOD = -10;

export function leaveCorpse(state: SimState, p: { x: number; y: number }, name: string | null): void {
  if (!state.harsh) return;
  const item = spawnItem(state, 'corpse', p.x, p.y);
  item.tag = name;
  item.wear = state.tick;
}

export function isRotting(state: SimState, corpse: Item, graves: Set<number>): boolean {
  return corpse.kind === 'corpse' && corpse.pos !== null && state.tick - corpse.wear >= CORPSE_ROT && !graves.has(labelAt(state, corpse.pos.x, corpse.pos.y));
}

export function updateCorpses(state: SimState): void {
  if (!state.harsh) return;
  for (const s of state.survivors) updateSickness(state, s);
  if (state.tick % CORPSE_CHECK_EVERY !== 0) return;
  const corpses = state.items.filter((i) => i.kind === 'corpse');
  if (corpses.length === 0) return;
  // Zombies' bodies go in the end; a survivor's waits for burial.
  for (const c of corpses) if (c.tag === null && c.pos && state.tick - c.wear >= CORPSE_GONE) removeItem(state, c);
  const graves = completeRooms(state, 'graveyard');
  const rotting = state.items.filter((i) => isRotting(state, i, graves));
  for (const s of state.survivors) {
    if (!rotting.some((c) => manhattan(c.pos!, s.pos) <= STENCH_RADIUS)) continue;
    addThought(state, s, 'stench');
    if (s.sick === null && hashRoll(idNumber(s.id), state.tick) < SICK_CHANCE) {
      s.sick = SICK_TICKS;
      logEvent(state, `${s.name} fell sick from the rotting dead (medicine helps)`);
    }
  }
}

function updateSickness(state: SimState, s: Survivor): void {
  if (s.sick === null) return;
  s.sick -= 1;
  if (state.tick % SICK_DAMAGE_EVERY === 0) {
    s.health = Math.max(0, s.health - 1);
    s.lastHurtBy = 'sickness';
  }
  if (s.sick > 0) return;
  s.sick = null;
  s.note = 'got over being sick';
}

/** "Bea's body" or "a zombie's body". */
export function corpseLabel(c: Item): string {
  return c.tag ? `${c.tag}'s body` : "a zombie's body";
}
