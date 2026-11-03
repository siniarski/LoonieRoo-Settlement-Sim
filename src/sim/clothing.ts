// Clothing: two slots, body (a coat or padded armour) and feet (boots).
//
//   coat    no cold damage or "out in the cold" on winter nights, no "soaked" in rain;
//           wears a little every hour it keeps the weather off
//   armour  zombie and raider blows do ARMOUR_DAMAGE_PCT of their damage, and a bite
//           infects half as often; wears with every blow taken
//   boots   spike traps don't hurt the wearer (barefoot, they do: TRAP_STEP_DAMAGE);
//           wear with every trap stepped on
//
// A garment falls apart at GARMENT_LIFE wear. Between jobs, a survivor with an
// empty slot puts on the nearest free garment for it (like guards arming), and
// one whose garment is half worn mends it with a piece of cloth.
// Cloth comes from crates and traders; workbenches make clothing from it.

import { logEvent } from './events';
import { furnitureAt } from './furniture';
import { dropItem, getItem, removeItem, type Item, type ItemKind } from './items';
import { nearestFreeItem, wantsSleep } from './needs';
import { findPathToAny } from './pathfinding';
import { samePoint } from './space';
import type { SimState } from './state';
import { walk, type Survivor } from './survivors';

export type Garment = 'coat' | 'armour' | 'boots';
export type Slot = 'body' | 'feet';
export const GARMENTS: readonly Garment[] = ['coat', 'armour', 'boots'];
export const SLOT_OF: Record<Garment, Slot> = { coat: 'body', armour: 'body', boots: 'feet' };
/** Wear at which a garment falls apart: coat hours, armour blows, boots traps. */
export const GARMENT_LIFE: Record<Garment, number> = { coat: 48, armour: 12, boots: 10 };
export const ARMOUR_DAMAGE_PCT = 70;
/** What a spike trap does to a survivor stepping on it barefoot. */
export const TRAP_STEP_DAMAGE = 40;
/** When nothing was found to wear or mend with, don't look again for this many ticks. */
export const DRESS_RETRY_TICKS = 100;

export function isGarment(kind: ItemKind): kind is Garment {
  return (GARMENTS as readonly string[]).includes(kind);
}

/** The garment of this kind the survivor has on, if any. */
export function wearing(state: SimState, s: Survivor, kind: Garment): Item | undefined {
  const id = s[SLOT_OF[kind]];
  const item = id ? getItem(state, id) : undefined;
  return item?.kind === kind ? item : undefined;
}

/** Wears a garment; it falls apart at GARMENT_LIFE. */
export function wearOut(state: SimState, s: Survivor, item: Item, amount = 1): void {
  if (!isGarment(item.kind)) return;
  item.wear += amount;
  if (item.wear < GARMENT_LIFE[item.kind]) return;
  removeItem(state, item);
  s[SLOT_OF[item.kind]] = null;
  logEvent(state, `${s.name}'s ${item.kind === 'boots' ? 'boots' : item.kind} fell apart`);
}

/** Damage after armour (which takes the wear). */
export function armourDamage(state: SimState, s: Survivor, damage: number): number {
  const armour = wearing(state, s, 'armour');
  if (!armour) return damage;
  wearOut(state, s, armour);
  return Math.round((damage * ARMOUR_DAMAGE_PCT) / 100);
}

/** Stepping onto a tile: an armed spike trap hurts the barefoot (boots take it instead). */
export function trapStep(state: SimState, s: Survivor): void {
  const trap = furnitureAt(state, s.pos.x, s.pos.y);
  if (trap?.kind !== 'trap' || trap.loot <= 0) return;
  const boots = wearing(state, s, 'boots');
  if (boots) return wearOut(state, s, boots);
  s.health = Math.max(0, s.health - TRAP_STEP_DAMAGE);
  s.lastHurtBy = 'a spike trap';
  s.hurtTick = state.tick;
  s.note = `stepped on a spike trap at (${s.pos.x}, ${s.pos.y})`;
}

// --- Dressing ----------------------------------------------------------------

function free(state: SimState, s: Survivor): boolean {
  return !s.jobId && !s.sleep && !s.meal && !s.treatment && !s.arming && !s.breakdown && !s.leisure && !wantsSleep(state, s) && state.tick >= s.nextDressTick;
}

/** A garment that's half worn, if any. */
function needsMending(state: SimState, s: Survivor): Item | undefined {
  return [s.body, s.feet]
    .map((id) => (id ? getItem(state, id) : undefined))
    .find((i) => i && isGarment(i.kind) && i.wear * 2 >= GARMENT_LIFE[i.kind]);
}

function startDressing(state: SimState, s: Survivor): boolean {
  if (!free(state, s)) return false;
  // Guards would rather have armour; everyone else a coat.
  const body: Garment[] = s.priorities.guard > 0 ? ['armour', 'coat'] : ['coat', 'armour'];
  const wants: ItemKind[] = [...(s.body ? [] : body), ...(s.feet ? [] : (['boots'] as const)), ...(needsMending(state, s) ? (['cloth'] as const) : [])];
  const item = wants.map((k) => nearestFreeItem(state, s, k)).find((i) => i?.pos);
  if (!item?.pos) {
    s.nextDressTick = state.tick + DRESS_RETRY_TICKS;
    return false;
  }
  item.reservedBy = s.id;
  s.dressing = item.id;
  s.note = item.kind === 'cloth' ? `going for cloth to mend with at (${item.pos.x}, ${item.pos.y})` : `going for ${item.kind} at (${item.pos.x}, ${item.pos.y})`;
  return true;
}

/** Runs when standing still, after arming and before sleep. Returns true if dressing handled this tick. */
export function updateDressing(state: SimState, s: Survivor): boolean {
  if (!s.dressing && !startDressing(state, s)) return false;
  const item = getItem(state, s.dressing!);
  if (!item?.pos || item.reservedBy !== s.id) {
    cancelDressing(state, s, 'clothing gone');
    return false;
  }
  if (!samePoint(s.pos, item.pos)) {
    const path = findPathToAny(state.grid, s.pos, [item.pos]);
    if (!path || path.length === 0) {
      cancelDressing(state, s, "can't reach the clothing");
      s.nextDressTick = state.tick + DRESS_RETRY_TICKS;
      return false;
    }
    walk(s, path, state.walkabilityVersion);
    return true;
  }
  s.dressing = null;
  if (item.kind === 'cloth') {
    const worn = needsMending(state, s);
    if (!worn) {
      item.reservedBy = null;
      return true;
    }
    removeItem(state, item);
    worn.wear = 0;
    s.note = `mended their ${worn.kind}`;
    return true;
  }
  const slot = isGarment(item.kind) ? SLOT_OF[item.kind] : null;
  if (!slot || s[slot]) {
    item.reservedBy = null;
    return true;
  }
  item.pos = null;
  item.carriedBy = s.id;
  item.reservedBy = null;
  s[slot] = item.id;
  s.note = `put on ${item.kind}`;
  return true;
}

/** Gives up the reserved garment or cloth (if any). Safe to call when not dressing. */
export function cancelDressing(state: SimState, s: Survivor, note: string): void {
  if (!s.dressing) return;
  const item = getItem(state, s.dressing);
  if (item && item.reservedBy === s.id) item.reservedBy = null;
  s.dressing = null;
  s.goal = null;
  s.path = [];
  s.pathVersion = -1;
  s.note = note;
}

/** Drops the plan if the item went away between ticks. */
export function reconcileDressing(state: SimState, s: Survivor): void {
  if (!s.dressing) return;
  const item = getItem(state, s.dressing);
  if (!item || item.reservedBy !== s.id || !item.pos) cancelDressing(state, s, 'clothing gone');
}

/** Takes everything off where the survivor stands (death). */
export function undress(state: SimState, s: Survivor): void {
  for (const slot of ['body', 'feet'] as const) {
    const item = s[slot] ? getItem(state, s[slot]!) : undefined;
    s[slot] = null;
    if (item) dropItem(state, item, s.pos);
  }
}

/** "coat, boots" for panels. */
export function describeClothing(state: SimState, s: Survivor): string {
  return [s.body, s.feet]
    .map((id) => (id ? getItem(state, id) : undefined))
    .filter((i): i is Item => i !== undefined && isGarment(i.kind))
    .map((i) => `${i.kind}${i.wear * 2 >= GARMENT_LIFE[i.kind as Garment] ? ' (worn)' : ''}`)
    .join(', ');
}
