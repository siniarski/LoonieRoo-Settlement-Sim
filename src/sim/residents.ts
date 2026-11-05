// Residents: a neighbouring settlement's people, once its compound has unfolded (colonies.ts).
//
// One resident per head (synced at unfolding and every dawn: newcomers appear at the
// door, the dead and the departed vanish). They potter about inside their walls. If
// their settlement counts you an enemy, they come out at your people within
// HOSTILE_SIGHT and hit them; hitting one of a peaceful settlement's makes it an enemy
// on the spot. A resident who dies is one fewer at home, and leaves a body.
// In person you can talk to them (warms their settlement, a little, once a day), trade
// (hand over what you carry for food from their stores), and, when their settlement
// thinks well of you, ask one to come with you.

import { REACH, inReach } from './motion';
import { placedAt } from './motion';
import { compound, isFallen } from './colonies';
import { leaveCorpse } from './corpses';
import { armourDamage } from './clothing';
import { logEvent } from './events';
import { inBounds, type Point } from './grid';
import { hashRoll, idNumber } from './hash';
import { getItem, removeItem, spawnItem, type ItemKind } from './items';
import { findPath, findPathToAny } from './pathfinding';
import { FRIENDLY, HOSTILE, adjustReputation, getSettlement, isHostile, type Settlement } from './settlements';
import type { SimState } from './state';
import { addSurvivor, type Movement, type Survivor } from './survivors';
import { dayOf } from './time';
import { neighbours4 } from './grid';

export interface Resident {
  id: string;
  settlementId: string;
  name: string;
  pos: Point;
  /** Where exactly, in motion.ts SUB units (pos is the tile under it); and where at the tick's start. */
  at: Point;
  was: Point;
  move: Movement | null;
  path: Point[];
  health: number;
  /** Ticks until they can hit again. */
  cooldown: number;
  nextThinkTick: number;
}

export const RESIDENT_HEALTH = 100;
export const RESIDENT_TICKS_PER_TILE = 5;
export const HOSTILE_SIGHT = 8;
export const RESIDENT_DAMAGE = 25;
export const RESIDENT_HIT_EVERY = 12;
/** How far under HOSTILE their settlement's opinion of you drops when you hit one of them. */
export const ATTACKED_BELOW_HOSTILE = 20;
export const TALK_REP = 2;
/** Food from their stores for each kind of thing you hand over. */
export const TRADE_FOOD: Partial<Record<ItemKind, number>> = { medicine: 3, ammo: 2, gun: 4, weapon: 2, bandage: 1, coat: 2, armour: 3, boots: 1, book: 1, scrap: 1, fuel: 1, wood: 1, cloth: 1 };
const NAMES = ['Ada', 'Bram', 'Cora', 'Dov', 'Esme', 'Fitz', 'Greta', 'Hank', 'Ines', 'Jules', 'Kai', 'Lotte', 'Mabel', 'Ned', 'Olga', 'Pim'];

export function residentsOf(state: SimState, n: Settlement): Resident[] {
  return state.residents.filter((r) => r.settlementId === n.id);
}

export function residentAt(state: SimState, p: Point): Resident | undefined {
  return state.residents.find((r) => r.pos.x === p.x && r.pos.y === p.y);
}

/** One resident a head: newcomers at the door, and the last ones gone when there are fewer. */
export function syncResidents(state: SimState, n: Settlement): void {
  const here = residentsOf(state, n);
  const want = n.unfolded && !isFallen(n) ? n.people : 0;
  for (const r of here.slice(want)) state.residents.splice(state.residents.indexOf(r), 1);
  const box = compound(n);
  for (let i = here.length; i < want; i++) {
    const id = `r${state.nextId++}`;
    const spot = { x: box.x0 + 1 + (i % Math.max(1, box.x1 - box.x0 - 1)), y: box.y1 - 1 - (Math.floor(i / Math.max(1, box.x1 - box.x0 - 1)) % 3) };
    const where = inBounds(state.grid, spot.x, spot.y) ? spot : { x: n.x, y: n.y };
    state.residents.push({ id, settlementId: n.id, name: NAMES[idNumber(id) % NAMES.length]!, pos: where, ...placedAt(where), move: null, path: [], health: RESIDENT_HEALTH, cooldown: 0, nextThinkTick: 0 });
  }
}

export function updateResidents(state: SimState): void {
  for (const r of [...state.residents]) {
    const n = getSettlement(state, r.settlementId);
    if (!n) {
      state.residents.splice(state.residents.indexOf(r), 1);
      continue;
    }
    r.cooldown = Math.max(0, r.cooldown - 1);
    if (r.move) {
      r.move.progress += 1;
      if (r.move.progress < r.move.duration) continue;
      r.pos = r.move.to;
      r.move = null;
    }
    if (isHostile(n) && attack(state, r, n)) continue;
    if (state.tick >= r.nextThinkTick && r.path.length === 0) {
      r.nextThinkTick = state.tick + 40 + Math.floor(hashRoll(idNumber(r.id), state.tick) * 60);
      r.path = potter(state, r, n);
    }
    step(r);
  }
}

function step(r: Resident): void {
  const next = r.path.shift();
  if (next) r.move = { from: { ...r.pos }, to: next, progress: 0, duration: RESIDENT_TICKS_PER_TILE };
}

/** A short walk to somewhere else inside the walls. */
function potter(state: SimState, r: Resident, n: Settlement): Point[] {
  const box = compound(n);
  const roll = (salt: number) => hashRoll(idNumber(r.id), state.tick, salt);
  const to = { x: box.x0 + 1 + Math.floor(roll(1) * (box.x1 - box.x0 - 1)), y: box.y0 + 2 + Math.floor(roll(2) * Math.max(1, box.y1 - box.y0 - 2)) };
  if (state.residents.some((o) => o !== r && o.pos.x === to.x && o.pos.y === to.y)) return [];
  return (findPath(state.grid, r.pos, to) ?? []).slice(0, 8);
}

/** An enemy's resident goes for the nearest of your people in sight. Returns true if it took the tick. */
function attack(state: SimState, r: Resident, n: Settlement): boolean {
  const near = (s: Survivor) => Math.max(Math.abs(s.pos.x - r.pos.x), Math.abs(s.pos.y - r.pos.y));
  const target = state.survivors.filter((s) => near(s) <= HOSTILE_SIGHT).sort((a, b) => near(a) - near(b))[0];
  if (!target) return false;
  if (inReach(target, r, REACH, state.grid)) {
    if (r.cooldown > 0) return true;
    r.cooldown = RESIDENT_HIT_EVERY;
    target.health = Math.max(0, target.health - armourDamage(state, target, RESIDENT_DAMAGE));
    target.lastHurtBy = `someone from ${n.name}`;
    target.hurtTick = state.tick;
    return true;
  }
  if (state.tick >= r.nextThinkTick || r.path.length === 0) {
    r.nextThinkTick = state.tick + 10;
    r.path = (findPathToAny(state.grid, r.pos, neighbours4(state.grid, target.pos.x, target.pos.y)) ?? []).slice(0, 6);
  }
  step(r);
  return true;
}

/** One of yours hits a resident. Peaceful places turn on you. Returns true if it died. */
export function hitResident(state: SimState, r: Resident, damage: number, by: Survivor): boolean {
  const n = getSettlement(state, r.settlementId);
  if (n && !isHostile(n)) {
    // (Worked out here, not at load: settlements.ts may not have run yet in an import cycle.)
    adjustReputation(state, n, HOSTILE - ATTACKED_BELOW_HOSTILE - n.reputation);
    logEvent(state, `${by.name} attacked ${r.name} of ${n.name}. They'll fight back now.`);
  }
  r.health -= damage;
  if (r.health > 0) return false;
  state.residents.splice(state.residents.indexOf(r), 1);
  if (n) n.people = Math.max(0, n.people - 1);
  leaveCorpse(state, r.pos, r.name);
  logEvent(state, `${by.name} killed ${r.name}${n ? ` of ${n.name}` : ''}`);
  return true;
}

/**
 * Talking to a resident in person: hand over what you carry for food, or ask them to come
 * with you if their settlement thinks well of you, or just pass the time. Returns null if
 * something happened, or why not.
 */
export function meetResident(state: SimState, you: Survivor, r: Resident): string | null {
  const n = getSettlement(state, r.settlementId);
  if (!n) return 'they have nowhere to be from';
  if (isHostile(n)) return `${r.name} won't talk to you`;
  const carried = you.carrying ? getItem(state, you.carrying) : undefined;
  const worth = carried ? (TRADE_FOOD[carried.kind] ?? 0) : 0;
  if (carried && worth > 0) {
    if (n.food < worth) return `${n.name} can't spare ${worth} food`;
    you.carrying = null;
    removeItem(state, carried);
    n.food -= worth;
    for (let i = 0; i < worth; i++) spawnItem(state, 'food', you.pos.x, you.pos.y);
    adjustReputation(state, n, 3);
    logEvent(state, `${r.name} of ${n.name} took the ${carried.kind} for ${worth} food`);
    return null;
  }
  if (n.reputation >= FRIENDLY && n.people > 3) {
    state.residents.splice(state.residents.indexOf(r), 1);
    n.people -= 1;
    const s = addSurvivor(state, r.pos.x, r.pos.y, r.name);
    s.order = { kind: 'follow' };
    logEvent(state, `${r.name} left ${n.name} to come with you`);
    return null;
  }
  // Just talk: their place warms to you a little, once a day.
  if (n.lastTalk === dayOf(state.tick)) return `${r.name} has said all they want to today`;
  n.lastTalk = dayOf(state.tick);
  adjustReputation(state, n, TALK_REP);
  logEvent(state, `${r.name} of ${n.name} talked with ${you.name} a while (${n.name} needs ${n.food < n.people * 2 ? 'food' : 'nothing much'})`);
  return null;
}
