// Weapons: scavenged, kept in the armory, carried by guards.
//
// Guns go in the same slot. A loaded gun shoots from up to GUN_RANGE tiles
// with a clear line (jobs.ts), loudly (noise.ts); an empty one is a club. A
// guard with an empty gun fetches ammo the way they'd fetch a weapon: each
// box loads AMMO_ROUNDS.
//
// A survivor with guarding switched on and no weapon picks up the nearest free
// one between jobs. An equipped weapon is carried (`carriedBy`, no `pos`) in
// the survivor's `weapon` slot, separate from the hauling slot. Armed guards
// hit harder; a weapon breaks after WEAPON_KILLS kills, and is dropped where
// its owner dies.

import { logEvent } from './events';
import { dropItem, getItem, removeItem } from './items';
import { nearestFreeItem, wantsSleep } from './needs';
import { findPathToAny } from './pathfinding';
import { samePoint } from './space';
import type { SimState } from './state';
import { walk, type Survivor } from './survivors';

/** Damage per blow with a weapon (unarmed is GUARD_DAMAGE, 50; zombies have 200). */
export const ARMED_DAMAGE = 100;
/** Kills before a weapon breaks. */
export const WEAPON_KILLS = 5;
/** When no weapon was found, don't look again for this many ticks. */
export const ARM_RETRY_TICKS = 50;
export const GUN_RANGE = 6;
export const GUN_DAMAGE = 150;
/** Ticks between shots. */
export const GUN_EVERY = 15;
export const AMMO_ROUNDS = 6;

function wantsWeapon(state: SimState, s: Survivor): boolean {
  return (
    !s.dressing &&
    s.priorities.guard > 0 &&
    !s.jobId &&
    !s.sleep &&
    !s.meal &&
    !s.treatment &&
    !s.breakdown &&
    !s.leisure &&
    !wantsSleep(state, s) &&
    state.tick >= s.nextArmTick
  );
}

/**
 * Runs when standing still, after treatment and before sleep: walk to the
 * reserved weapon and pick it up. Returns true if arming handled this tick.
 */
export function updateArming(state: SimState, s: Survivor): boolean {
  if (!s.arming && !startArming(state, s)) return false;
  const item = getItem(state, s.arming!);
  if (!item?.pos || item.reservedBy !== s.id) {
    cancelArming(state, s, 'weapon gone');
    return false;
  }
  if (!samePoint(s.pos, item.pos)) {
    const path = findPathToAny(state.grid, s.pos, [item.pos]);
    if (!path || path.length === 0) {
      cancelArming(state, s, "can't reach the weapon");
      s.nextArmTick = state.tick + ARM_RETRY_TICKS;
      return false;
    }
    walk(s, path, state.walkabilityVersion);
    return true;
  }
  s.arming = null;
  if (item.kind === 'ammo') {
    const gun = s.weapon ? getItem(state, s.weapon) : undefined;
    if (gun?.kind !== 'gun') {
      item.reservedBy = null;
      return true;
    }
    removeItem(state, item);
    gun.wear += AMMO_ROUNDS;
    s.note = 'loaded their gun';
    return true;
  }
  item.pos = null;
  item.carriedBy = s.id;
  item.reservedBy = null;
  s.weapon = item.id;
  s.note = item.kind === 'gun' ? 'armed with a gun' : 'armed with a weapon';
  return true;
}

/** The survivor's gun, if it has rounds in it. */
export function loadedGun(state: SimState, s: Survivor) {
  const gun = s.weapon ? getItem(state, s.weapon) : undefined;
  return gun?.kind === 'gun' && gun.wear > 0 ? gun : undefined;
}

function startArming(state: SimState, s: Survivor): boolean {
  if (!wantsWeapon(state, s)) return false;
  // Unarmed: a gun if there is one, else anything. An empty gun: ammo. Otherwise nothing to fetch.
  const held = s.weapon ? getItem(state, s.weapon) : undefined;
  const wants: ('gun' | 'weapon' | 'ammo')[] = !held ? ['gun', 'weapon'] : held.kind === 'gun' && held.wear === 0 ? ['ammo'] : [];
  if (wants.length === 0) return false;
  const weapon = wants.map((k) => nearestFreeItem(state, s, k)).find((i) => i?.pos);
  if (!weapon?.pos) {
    s.nextArmTick = state.tick + ARM_RETRY_TICKS;
    return false;
  }
  weapon.reservedBy = s.id;
  s.arming = weapon.id;
  s.note = `going for ${weapon.kind === 'ammo' ? 'ammo' : weapon.kind === 'gun' ? 'a gun' : 'a weapon'} at (${weapon.pos.x}, ${weapon.pos.y})`;
  return true;
}

/** Gives up the reserved weapon (if any). Safe to call when not arming. */
export function cancelArming(state: SimState, s: Survivor, note: string): void {
  if (!s.arming) return;
  const item = getItem(state, s.arming);
  if (item && item.reservedBy === s.id) item.reservedBy = null;
  s.arming = null;
  s.goal = null;
  s.path = [];
  s.pathVersion = -1;
  s.note = note;
}

/** Drops the reserved-weapon plan if the weapon went away between ticks. */
export function reconcileArming(state: SimState, s: Survivor): void {
  if (!s.arming) return;
  const item = getItem(state, s.arming);
  if (!item || item.reservedBy !== s.id || !item.pos) cancelArming(state, s, 'weapon gone');
}

/** Damage this survivor deals per blow. */
export function blowDamage(s: Survivor, unarmed: number): number {
  return s.weapon ? ARMED_DAMAGE : unarmed;
}

/** After an armed kill: wear the weapon, and break it at WEAPON_KILLS. (Guns don't break.) */
export function wearWeapon(state: SimState, s: Survivor): void {
  const item = s.weapon ? getItem(state, s.weapon) : undefined;
  if (!item || item.kind === 'gun') return;
  item.wear += 1;
  if (item.wear < WEAPON_KILLS) return;
  removeItem(state, item);
  s.weapon = null;
  logEvent(state, `${s.name}'s weapon broke`);
}

/** Puts an equipped weapon on the ground where the survivor stands. */
export function dropWeapon(state: SimState, s: Survivor): void {
  const item = s.weapon ? getItem(state, s.weapon) : undefined;
  s.weapon = null;
  if (item) dropItem(state, item, s.pos);
}
