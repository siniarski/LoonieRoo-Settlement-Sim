// Other people: traders who come to swap goods, and raiders who come at night.
//
// Traders: from TRADER_FIRST_DAY, every TRADER_EVERY days at TRADER_HOUR, a
// trader waits at the edge of explored ground (as wanderers do) with three
// offers (sim RNG). Trading takes the asked-for goods from anything free on
// the ground and puts the trader's goods down beside them. They leave after
// TRADER_PATIENCE ticks or at nightfall.
//
// Raiders: from RAID_FIRST_DAY, each nightfall may bring a party (RAID_CHANCE).
// They're zombies of kind 'raider' as far as fighting goes (zombies.ts), but
// they open unlocked doors, hit harder, don't infect, and leave at dawn.
// Lock a door (toggleLock) and they have to batter it like anyone else.

import { logEvent } from './events';
import { getTile, type Point } from './grid';
import { spawnItem, type ItemKind } from './items';
import { pick, random } from './rng';
import type { SimState } from './state';
import { TICKS_PER_HOUR, dayOf, isNight, tickOfDay } from './time';
import { frontierSpot } from './wanderers';
import { addZombie, nightFallsNow } from './zombies';

export const TRADER_FIRST_DAY = 3;
export const TRADER_EVERY = 3;
export const TRADER_HOUR = 12;
export const TRADER_PATIENCE = 4 * TICKS_PER_HOUR;
export const RAID_FIRST_DAY = 6;
export const RAID_CHANCE = 0.3;
export const RAIDER_HEALTH = 250;
export const RAIDER_DAMAGE = 50;

export interface Offer {
  give: ItemKind;
  giveCount: number;
  want: ItemKind;
  wantCount: number;
}

export interface Trader {
  id: string;
  x: number;
  y: number;
  leavesTick: number;
  offers: Offer[];
}

const OFFERS: readonly Offer[] = [
  { give: 'weapon', giveCount: 1, want: 'food', wantCount: 4 },
  { give: 'medicine', giveCount: 1, want: 'wood', wantCount: 4 },
  { give: 'food', giveCount: 5, want: 'wood', wantCount: 3 },
  { give: 'bandage', giveCount: 2, want: 'food', wantCount: 3 },
  { give: 'wood', giveCount: 5, want: 'food', wantCount: 4 },
  { give: 'meal', giveCount: 3, want: 'wood', wantCount: 2 },
  { give: 'cloth', giveCount: 3, want: 'food', wantCount: 3 },
  { give: 'coat', giveCount: 1, want: 'food', wantCount: 4 },
  { give: 'gun', giveCount: 1, want: 'food', wantCount: 6 },
  { give: 'ammo', giveCount: 2, want: 'wood', wantCount: 3 },
];

export function updateVisitors(state: SimState): void {
  for (const t of [...state.traders]) {
    if (state.tick >= t.leavesTick || isNight(state.tick)) {
      state.traders.splice(state.traders.indexOf(t), 1);
      logEvent(state, 'The trader packed up and left');
    }
  }
  const day = dayOf(state.tick);
  if (day >= TRADER_FIRST_DAY && (day - TRADER_FIRST_DAY) % TRADER_EVERY === 0 && tickOfDay(state.tick) === TRADER_HOUR * TICKS_PER_HOUR) arrive(state);
  if (day >= RAID_FIRST_DAY && nightFallsNow(state.tick) && random(state) < RAID_CHANCE) raid(state, day);
}

function arrive(state: SimState): void {
  const spot = frontierSpot(state, new Set()) ?? state.survivors[0]?.pos;
  if (!spot) return;
  const offers: Offer[] = [];
  const left = [...OFFERS];
  for (let i = 0; i < 3; i++) {
    const o = pick(state, left);
    left.splice(left.indexOf(o), 1);
    offers.push({ ...o });
  }
  state.traders.push({ id: `t${state.nextId++}`, x: spot.x, y: spot.y, leavesTick: state.tick + TRADER_PATIENCE, offers });
  logEvent(state, `A trader is waiting at (${spot.x}, ${spot.y})`);
}

/** Swaps goods. Returns an error message, or null when done. */
export function trade(state: SimState, traderId: string, offer: number): string | null {
  const t = state.traders.find((x) => x.id === traderId);
  const o = t?.offers[offer];
  if (!t || !o) return 'no such offer';
  const free = state.items.filter((i) => i.kind === o.want && i.pos !== null && i.reservedBy === null);
  if (free.length < o.wantCount) return `not enough ${o.want} (need ${o.wantCount}, have ${free.length} free)`;
  for (const item of free.slice(0, o.wantCount)) state.items.splice(state.items.indexOf(item), 1);
  for (let n = 0; n < o.giveCount; n++) spawnItem(state, o.give, t.x, t.y);
  logEvent(state, `Traded ${o.wantCount} ${o.want} for ${o.giveCount} ${o.give}`);
  return null;
}

function raid(state: SimState, day: number): void {
  const spot = frontierSpot(state, new Set()) ?? state.survivors[0]?.pos;
  if (!spot) return;
  const size = 2 + Math.floor(day / 6);
  for (let n = 0; n < size; n++) {
    const p: Point = { x: spot.x + (n % 2), y: spot.y + Math.floor(n / 2) };
    const at = getTile(state.grid, p.x, p.y) === 'floor' ? p : spot;
    addZombie(state, at.x, at.y, RAIDER_HEALTH, 'raider');
  }
  logEvent(state, `Raiders! ${size} of them, near (${spot.x}, ${spot.y}). Locked doors will slow them.`);
}
