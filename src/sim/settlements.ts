// Other settlements: neighbours out in a generated world, each with a
// reputation for you, -100..100.
//
// Traders come from them in turn. Trading with one warms it to you; sending
// its trader away empty-handed cools it; a gift of food warms it a lot. At
// HOSTILE or worse it counts you an enemy, and raids come more often. At
// FRIENDLY or better it sends someone to join you every FRIENDLY_SEND_DAYS.
//
// Between themselves (ties, -100..100): at ALLY or better two settlements are allies (they
// send each other food by caravan when one runs short: colonies.ts); at FEUD or worse they
// feud (they raid each other now and then, starving or not). How you're seen spills over:
// what you do for or to one, its allies feel half as much, and those feuding with it a
// quarter as much the other way.

import { logEvent } from './events';
import { hashRoll } from './hash';
import type { Point } from './grid';
import { addWanderer, frontierSpot } from './wanderers';
import type { SimState } from './state';
import { TICKS_PER_HOUR, dayOf, tickOfDay } from './time';

export interface Settlement {
  id: string;
  name: string;
  x: number;
  y: number;
  reputation: number;
  /** Off-screen life (colonies.ts): how many live there, food in days of eating, walls 0-5. */
  people: number;
  food: number;
  defence: number;
  /** What it thinks of the other settlements, by id, -100..100. */
  ties: Record<string, number>;
  /** Built as a real compound once you came near. */
  unfolded: boolean;
  /** Day you last talked with one of its people in person (residents.ts), -1 if never. */
  lastTalk: number;
  /** Tick you last raided it, -1 if never. */
  lastRaid: number;
}

export const TRADE_REP = 5;
export const SNUB_REP = -5;
export const GIFT_REP = 10;
export const GIFT_FOOD = 3;
export const HOSTILE = -30;
export const FRIENDLY = 40;
export const FRIENDLY_SEND_DAYS = 5;
const NAMES = ['Rivermouth', 'Hollow Creek', 'Saint Agnes', 'The Quarry', 'Fort Ember', 'Greyfield'];

export function isHostile(s: Settlement): boolean {
  return s.reputation <= HOSTILE;
}

export function getSettlement(state: SimState, id: string | null): Settlement | undefined {
  return id ? state.settlements.find((s) => s.id === id) : undefined;
}

/** Three neighbours far from the start, at hashed spots (worldgen). */
export function placeSettlements(state: SimState, start: Point, seed: number): void {
  const size = state.grid.width;
  for (let i = 0; i < 3; i++) {
    let p: Point = { x: 0, y: 0 };
    for (let attempt = 0; attempt < 20; attempt++) {
      p = { x: Math.floor(hashRoll(seed, i, attempt, 1) * size), y: Math.floor(hashRoll(seed, i, attempt, 2) * size) };
      if (Math.abs(p.x - start.x) + Math.abs(p.y - start.y) >= size / 3) break;
    }
    const people = 5 + Math.floor(hashRoll(seed, i, 3) * 8);
    newSettlement(state, NAMES[(seed + i) % NAMES.length]!, p.x, p.y, people, people * 3, Math.floor(hashRoll(seed, i, 4) * 3));
  }
}

export function newSettlement(state: SimState, name: string, x: number, y: number, people: number, food: number, defence: number): Settlement {
  const n: Settlement = { id: `n${state.nextId++}`, name, x, y, reputation: 0, people, food, defence, ties: {}, unfolded: false, lastTalk: -1, lastRaid: -1 };
  state.settlements.push(n);
  return n;
}

export function adjustReputation(state: SimState, s: Settlement, by: number, spill = true): void {
  const was = isHostile(s);
  s.reputation = Math.max(-100, Math.min(100, s.reputation + by));
  if (!was && isHostile(s)) logEvent(state, `${s.name} now counts you an enemy: expect more raids`);
  if (was && !isHostile(s)) logEvent(state, `${s.name} no longer counts you an enemy`);
  if (!spill) return;
  // Allies feel half of it; those feuding with it, a quarter the other way.
  for (const o of state.settlements) {
    if (o === s || o.people <= 0) continue;
    const t = s.ties[o.id] ?? 0;
    if (t >= ALLY) adjustReputation(state, o, Math.trunc(by / 2), false);
    else if (t <= FEUD) adjustReputation(state, o, -Math.trunc(by / 4), false);
  }
}

export const ALLY = 40;
export const FEUD = -40;

/** How two settlements stand: allies, feuding, or neither. */
export function relation(a: Settlement, b: Settlement): 'allies' | 'feud' | null {
  const t = a.ties[b.id] ?? 0;
  return t >= ALLY ? 'allies' : t <= FEUD ? 'feud' : null;
}

/** Warms (or cools) two settlements to each other, both ways. Returns how they stood before. */
export function tieSettlements(a: Settlement, b: Settlement, by: number): 'allies' | 'feud' | null {
  const before = relation(a, b);
  for (const [x, y] of [[a, b], [b, a]] as const) x.ties[y.id] = Math.max(-100, Math.min(100, (x.ties[y.id] ?? 0) + by));
  return before;
}

/** Whose turn it is to send a trader: friendly-ish settlements in turn, or none. */
export function traderFrom(state: SimState): Settlement | null {
  const open = state.settlements.filter((s) => !isHostile(s));
  return open.length > 0 ? open[dayOf(state.tick) % open.length]! : null;
}

/** Friendly settlements send someone to join, every FRIENDLY_SEND_DAYS at noon. */
export function updateSettlements(state: SimState): void {
  if (tickOfDay(state.tick) !== 12 * TICKS_PER_HOUR || dayOf(state.tick) % FRIENDLY_SEND_DAYS !== 0) return;
  for (const s of state.settlements) {
    // They send one of their own: a place with few left keeps them.
    if (s.reputation < FRIENDLY || s.people <= 3) continue;
    s.people -= 1;
    const spot = frontierSpot(state, new Set(state.wanderers.map((w) => `${w.x},${w.y}`)));
    if (!spot) return;
    const w = addWanderer(state, spot.x, spot.y);
    logEvent(state, `${s.name} sent ${w.name} to join you; they're waiting at (${spot.x}, ${spot.y})`);
  }
}

/** A gift of food. Returns an error, or null when given. */
export function giveGift(state: SimState, id: string): string | null {
  const s = getSettlement(state, id);
  if (!s) return 'no such settlement';
  const food = state.items.filter((i) => i.kind === 'food' && i.pos !== null && i.reservedBy === null);
  if (food.length < GIFT_FOOD) return `need ${GIFT_FOOD} free food`;
  for (const f of food.slice(0, GIFT_FOOD)) state.items.splice(state.items.indexOf(f), 1);
  // Food to a starving place is remembered twice over.
  adjustReputation(state, s, s.food <= 0 ? GIFT_REP * 2 : GIFT_REP);
  s.food += GIFT_FOOD;
  logEvent(state, `Sent ${GIFT_FOOD} food to ${s.name} (they think better of you)`);
  return null;
}
