// Fire.
//
// A fire sits on a tile and burns for a while: FIRE_BURN ticks on anything
// that burns (walls, doors, barricades and all furniture are wood), less on
// bare ground. Rain and snow burn it down twice as fast. While it burns it
// hurts anyone standing in it, survivor or zombie, and every SPREAD_EVERY
// ticks may catch on each burnable neighbour (a hash roll, not the sim RNG).
// When it burns out, what it was on is gone: a wall becomes floor, furniture
// and items are ash.
//
// Fires start from a stove now and then, from raiders' firebombs, or on
// purpose (the Burn order). Survivors with firefighting on put them out
// (jobs.ts).

import { logEvent } from './events';
import { furnitureAt, removeFurniture } from './furniture';
import { getTile, inBounds, neighbours4, setTile, type Point } from './grid';
import { hashRoll } from './hash';
import { itemAt, removeItem } from './items';
import type { SimState } from './state';
import { leaveCorpse } from './corpses';
import { removeZombie } from './zombies';

export interface Fire {
  x: number;
  y: number;
  /** Ticks left before it burns out. */
  left: number;
}

export const FIRE_BURN = 300;
export const GROUND_BURN = 100;
export const SPREAD_EVERY = 20;
export const SPREAD_CHANCE = 0.2;
/** Health lost per tick standing in a fire. */
export const FIRE_DAMAGE = 5;
/** Ticks of work to put a fire out. */
export const DOUSE_WORK = 30;
/** Chance a hot meal sets the stove's tile alight. */
export const STOVE_FIRE_CHANCE = 0.02;
/** Chance a raider's blow on a wall or door is a firebomb instead. */
export const FIREBOMB_CHANCE = 0.05;
/** Safety cap (not a balance lever). */
export const MAX_FIRES = 150;

export function fireAt(state: SimState, x: number, y: number): Fire | undefined {
  return state.fires.find((f) => f.x === x && f.y === y);
}

/** Can a fire here keep going (and catch from a neighbour)? */
export function burnable(state: SimState, x: number, y: number): boolean {
  const t = getTile(state.grid, x, y);
  return t === 'wall' || t === 'door' || t === 'barricade' || (t === 'floor' && furnitureAt(state, x, y) !== undefined);
}

/** Sets a tile alight. Returns false if it can't burn (water, already alight, too many fires). */
export function ignite(state: SimState, x: number, y: number): boolean {
  if (!inBounds(state.grid, x, y) || getTile(state.grid, x, y) === 'water' || fireAt(state, x, y) || state.fires.length >= MAX_FIRES) return false;
  state.fires.push({ x, y, left: burnable(state, x, y) ? FIRE_BURN : GROUND_BURN });
  return true;
}

export function douse(state: SimState, f: Fire): void {
  const i = state.fires.indexOf(f);
  if (i >= 0) state.fires.splice(i, 1);
}

export function updateFires(state: SimState): void {
  if (state.fires.length === 0) return;
  const wet = state.weather !== 'clear';
  for (const f of [...state.fires]) {
    for (const s of state.survivors) {
      if (s.pos.x !== f.x || s.pos.y !== f.y) continue;
      s.health = Math.max(0, s.health - FIRE_DAMAGE);
      s.lastHurtBy = 'burns';
      s.hurtTick = state.tick;
    }
    for (const z of [...state.zombies]) {
      if (z.pos.x !== f.x || z.pos.y !== f.y) continue;
      z.health = Math.max(0, z.health - FIRE_DAMAGE);
      if (z.health > 0) continue;
      removeZombie(state, z);
      leaveCorpse(state, z.pos, null);
    }
    if (state.tick % SPREAD_EVERY === 0 && burnable(state, f.x, f.y)) {
      for (const n of neighbours4(state.grid, f.x, f.y)) {
        if (burnable(state, n.x, n.y) && !fireAt(state, n.x, n.y) && hashRoll(n.x, n.y, state.tick) < SPREAD_CHANCE) ignite(state, n.x, n.y);
      }
    }
    f.left -= wet ? 2 : 1;
    if (f.left <= 0) burnOut(state, f);
  }
}

function burnOut(state: SimState, f: Fire): void {
  douse(state, f);
  const furniture = furnitureAt(state, f.x, f.y);
  const what = furniture?.kind ?? getTile(state.grid, f.x, f.y);
  if (furniture) {
    furniture.stored = 0; // whatever was in it burned too
    removeFurniture(state, furniture);
  }
  const t = getTile(state.grid, f.x, f.y);
  if (t === 'wall' || t === 'door' || t === 'barricade') {
    setTile(state.grid, f.x, f.y, 'floor');
    state.walkabilityVersion += 1;
  }
  const item = itemAt(state, f.x, f.y);
  if (item) removeItem(state, item);
  if (what !== 'floor') logEvent(state, `A ${what} at (${f.x}, ${f.y}) burned down`);
}

/** A tile next to the fire to fight it from. */
export function fireSite(state: SimState, f: Point): Point[] {
  return neighbours4(state.grid, f.x, f.y);
}
