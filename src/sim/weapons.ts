// Weapons: scavenged, kept in the armory, carried by guards.
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

function wantsWeapon(state: SimState, s: Survivor): boolean {
  return (
    s.weapon === null &&
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
  item.pos = null;
  item.carriedBy = s.id;
  item.reservedBy = null;
  s.weapon = item.id;
  s.arming = null;
  s.note = 'armed with a weapon';
  return true;
}

function startArming(state: SimState, s: Survivor): boolean {
  if (!wantsWeapon(state, s)) return false;
  const weapon = nearestFreeItem(state, s, 'weapon');
  if (!weapon?.pos) {
    s.nextArmTick = state.tick + ARM_RETRY_TICKS;
    return false;
  }
  weapon.reservedBy = s.id;
  s.arming = weapon.id;
  s.note = `going for a weapon at (${weapon.pos.x}, ${weapon.pos.y})`;
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

/** After an armed kill: wear the weapon, and break it at WEAPON_KILLS. */
export function wearWeapon(state: SimState, s: Survivor): void {
  const item = s.weapon ? getItem(state, s.weapon) : undefined;
  if (!item) return;
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
