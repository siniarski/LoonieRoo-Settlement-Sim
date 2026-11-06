// Errands: neighbours asking you for help, out in the world.
//
// Talk to one of a settlement's people in person (residents.ts meetResident) when it thinks
// well enough of you (not hostile) and has nothing open with you, and some days they ask:
//   nest     the dead have gathered near X (NEST_SIZE of them, asleep on their feet: static):
//            kill them all.
//   missing  one of theirs went out and hasn't come back: find them, near X (anyone of yours
//            coming within FOUND_REACH tiles of the spot finds them, and they go home).
// The spot is ERRAND_MIN..ERRAND_MAX tiles from the settlement. On the map it's marked, with
// what it is. Done within ERRAND_DAYS: they think a lot more of you (ERRAND_REP), and a
// caravan (caravans.ts) brings the thanks to your place. Not done in time: they think a little
// less of you (LAPSED_REP). One errand open per settlement at a time.
// Rolls are hashes of the settlement, the day and the person, not the sim RNG.

import { sendCaravan } from './caravans';
import { logEvent } from './events';
import { getTile, type Point } from './grid';
import { hashRoll, idNumber } from './hash';
import type { ItemKind } from './items';
import { adjustReputation, getSettlement, isHostile, type Settlement } from './settlements';
import { nearestTile } from './space';
import type { SimState } from './state';
import { DAY_LENGTH, dayOf } from './time';
import { ensureGeneratedAround } from './worldgen';
import { addZombie } from './zombies';

export interface Errand {
  id: string;
  settlementId: string;
  kind: 'nest' | 'missing';
  x: number;
  y: number;
  /** nest: the dead to kill. */
  zombieIds: string[];
  /** missing: who. */
  who: string;
  until: number;
}

export const ASK_CHANCE = 0.5;
export const ERRAND_DAYS = 4;
export const ERRAND_MIN = 18;
export const ERRAND_MAX = 36;
export const NEST_SIZE = 4;
export const FOUND_REACH = 2;
export const ERRAND_REP = 25;
export const LAPSED_REP = -5;
const THANKS: readonly ItemKind[] = ['medicine', 'ammo', 'coat', 'bandage', 'fuel', 'book'];
const MISSING = ['Rafe', 'Dot', 'Kit', 'Jonah', 'Mae', 'Tobias', 'Wren', 'Ines'];

export function errandsOf(state: SimState): Errand[] {
  return state.errands ?? [];
}

/** When you talk with one of their people: maybe they ask you for something. Returns what they said, or null. */
export function maybeAsk(state: SimState, n: Settlement, salt: number): string | null {
  if (isHostile(n) || errandsOf(state).some((e) => e.settlementId === n.id)) return null;
  const roll = (k: number) => hashRoll(idNumber(n.id), dayOf(state.tick), salt, k);
  if (roll(0) >= ASK_CHANCE) return null;
  const angle = roll(1) * Math.PI * 2;
  const dist = ERRAND_MIN + Math.floor(roll(2) * (ERRAND_MAX - ERRAND_MIN));
  const { width, height } = state.grid;
  const want = { x: Math.max(2, Math.min(width - 3, Math.round(n.x + Math.cos(angle) * dist))), y: Math.max(2, Math.min(height - 3, Math.round(n.y + Math.sin(angle) * dist))) };
  ensureGeneratedAround(state, want);
  const spot = nearestTile(state.grid, want, (x, y) => getTile(state.grid, x, y) === 'floor', 400);
  if (!spot) return null;
  state.errands ??= [];
  const kind = roll(3) < 0.5 ? 'nest' : 'missing';
  const e: Errand = { id: `e${state.nextId++}`, settlementId: n.id, kind, x: spot.x, y: spot.y, zombieIds: [], who: MISSING[Math.floor(roll(4) * MISSING.length)]!, until: state.tick + ERRAND_DAYS * DAY_LENGTH };
  if (kind === 'nest') {
    for (let k = 0; k < NEST_SIZE; k++) {
      const p = nearestTile(state.grid, { x: spot.x + (k % 2), y: spot.y + Math.floor(k / 2) }, (x, y) => getTile(state.grid, x, y) === 'floor' && !state.zombies.some((z) => z.pos.x === x && z.pos.y === y), 100);
      if (p) e.zombieIds.push(addZombie(state, p.x, p.y, undefined, 'static').id);
    }
  }
  state.errands.push(e);
  const said = kind === 'nest' ? `the dead have gathered near (${spot.x}, ${spot.y}): would you clear them out?` : `${e.who} went out and never came back. Last seen near (${spot.x}, ${spot.y})`;
  logEvent(state, `${n.name} asks: ${said} (within ${ERRAND_DAYS} days)`);
  return said;
}

/** Every tick: errands done, or run out. */
export function updateErrands(state: SimState): void {
  const list = errandsOf(state);
  if (list.length === 0) return;
  for (const e of [...list]) {
    const n = getSettlement(state, e.settlementId);
    if (!n || n.people <= 0) {
      list.splice(list.indexOf(e), 1);
      continue;
    }
    if (done(state, e)) {
      list.splice(list.indexOf(e), 1);
      reward(state, e, n);
    } else if (state.tick >= e.until) {
      list.splice(list.indexOf(e), 1);
      adjustReputation(state, n, LAPSED_REP);
      logEvent(state, `${n.name} gave up waiting: ${e.kind === 'nest' ? 'the dead near them are still there' : `nobody found ${e.who}`}`);
    }
  }
}

function done(state: SimState, e: Errand): boolean {
  if (e.kind === 'nest') return e.zombieIds.length > 0 && !state.zombies.some((z) => e.zombieIds.includes(z.id));
  return state.survivors.some((s) => Math.max(Math.abs(s.pos.x - e.x), Math.abs(s.pos.y - e.y)) <= FOUND_REACH);
}

function reward(state: SimState, e: Errand, n: Settlement): void {
  adjustReputation(state, n, ERRAND_REP);
  if (e.kind === 'missing') n.people += 1; // home again
  const roll = (k: number) => hashRoll(idNumber(e.id), k);
  const goods: ItemKind[] = [THANKS[Math.floor(roll(1) * THANKS.length)]!, THANKS[Math.floor(roll(2) * THANKS.length)]!];
  const home: Point = state.survivors[0]?.pos ?? { x: n.x, y: n.y };
  sendCaravan(state, n.id, 'you', home, 3, goods);
  logEvent(state, e.kind === 'nest' ? `The dead near ${n.name} are gone. They're sending thanks by caravan` : `You found ${e.who}, and sent them home to ${n.name}. They're sending thanks by caravan`);
}
