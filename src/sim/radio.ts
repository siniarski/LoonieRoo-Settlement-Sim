// The radio. A powered radio picks something up most mornings at RADIO_HOUR
// (a hash of the day): either a distress call, which marks a point of
// interest out there (an expedition to it brings the callers home,
// expeditions.ts), or a neighbour asking for supplies (state.requests:
// send them within REQUEST_DAYS for a lot of goodwill and something back).

import { logEvent } from './events';
import { ofKind } from './furniture';
import { hashRoll } from './hash';
import type { ItemKind } from './items';
import { removeItem, spawnItem } from './items';
import { isPowered } from './power';
import { adjustReputation, getSettlement } from './settlements';
import type { SimState } from './state';
import { DAY_LENGTH, TICKS_PER_HOUR, dayOf, tickOfDay } from './time';

export interface SupplyRequest {
  id: string;
  settlementId: string;
  kind: ItemKind;
  count: number;
  until: number;
}

export const RADIO_HOUR = 10;
export const RADIO_CHANCE = 0.5;
export const REQUEST_DAYS = 2;
export const REQUEST_REP = 20;
export const DISTRESS_LABEL = 'distress call';
const ASKS: readonly [ItemKind, number][] = [
  ['food', 5],
  ['wood', 8],
  ['bandage', 2],
  ['cloth', 3],
];
const THANKS: readonly ItemKind[] = ['medicine', 'ammo', 'coat', 'book'];

export function hasRadio(state: SimState): boolean {
  return ofKind(state, 'radio').some((f) => isPowered(state, f));
}

export function updateRadio(state: SimState): void {
  state.requests = state.requests.filter((r) => r.until > state.tick);
  if (tickOfDay(state.tick) !== RADIO_HOUR * TICKS_PER_HOUR || !hasRadio(state) || state.survivors.length === 0) return;
  const day = dayOf(state.tick);
  if (hashRoll(day, 0x7ad10) >= RADIO_CHANCE) return;
  const open = state.settlements.filter((s) => s.reputation > -30);
  if (open.length > 0 && hashRoll(day, 2) < 0.5) {
    const n = open[day % open.length]!;
    const [kind, count] = ASKS[Math.floor(hashRoll(day, 3) * ASKS.length)]!;
    state.requests.push({ id: `r${state.nextId++}`, settlementId: n.id, kind, count, until: state.tick + REQUEST_DAYS * DAY_LENGTH });
    logEvent(state, `On the radio: ${n.name} asks for ${count} ${kind} within ${REQUEST_DAYS} days`);
    return;
  }
  // A distress call from out past the colony.
  const home = state.survivors[0]!.pos;
  const angle = hashRoll(day, 4) * Math.PI * 2;
  const dist = 30 + Math.floor(hashRoll(day, 5) * 30);
  const x = Math.max(0, Math.min(state.grid.width - 1, Math.round(home.x + Math.cos(angle) * dist)));
  const y = Math.max(0, Math.min(state.grid.height - 1, Math.round(home.y + Math.sin(angle) * dist)));
  state.pointsOfInterest.push({ id: `poi${state.nextId++}`, x, y, label: DISTRESS_LABEL });
  logEvent(state, `On the radio: a distress call from near (${x}, ${y}). An expedition could bring them in`);
}

/** Send what a neighbour asked for. Returns an error, or null when sent. */
export function fulfilRequest(state: SimState, id: string): string | null {
  const r = state.requests.find((q) => q.id === id);
  if (!r) return 'no such request';
  const free = state.items.filter((i) => i.kind === r.kind && i.pos !== null && i.reservedBy === null);
  if (free.length < r.count) return `need ${r.count} free ${r.kind}`;
  for (const i of free.slice(0, r.count)) removeItem(state, i);
  state.requests.splice(state.requests.indexOf(r), 1);
  const n = getSettlement(state, r.settlementId);
  if (n) adjustReputation(state, n, REQUEST_REP);
  const thanks = THANKS[Math.floor(hashRoll(state.tick, 6) * THANKS.length)]!;
  const at = state.survivors[0]?.pos ?? { x: 0, y: 0 };
  for (let k = 0; k < 2; k++) spawnItem(state, thanks, at.x, at.y);
  logEvent(state, `Sent ${r.count} ${r.kind} to ${n?.name ?? 'the neighbours'}; they sent back 2 ${thanks}`);
  return null;
}
