// Neighbours living their own lives, off screen (simulation level of detail, like herds).
//
// Each settlement is a few numbers: people, food (in days of eating), defence (0-5).
// Every dawn, from a hash of its id and the day (never the sim RNG):
//   food    they farm (less in autumn, little in winter, less again in a nuclear winter), eat one a head,
//           and a little of the stores spoils
//   hunger  with nothing left, about one in seven starve; a starving one turns on you a little
//   growth  well fed, now and then someone new turns up
//   walls   a surplus goes into defences, now and then
//   the dead  a night attack can take one or two, less likely behind good walls
//   help    a fed one sends food to a starving neighbour by caravan (caravans.ts; the two warm
//           to each other when it arrives); allies send to a merely hungry one too
//   feuds   feuding neighbours raid each other now and then, starving or not
//   raids   a starving one raids the nearest neighbour for food, and both lose people
//   splits  past SPLIT_AT people, a would-be leader leaves with a few to found their own
//   falls   at none left it falls, and its compound is a ruin with the dead still in it
// When anyone of yours comes within UNFOLD_RADIUS, the settlement unfolds into a
// real compound: walls with a door, a bed a head, tables, and its people (residents.ts).

import { syncResidents } from './residents';
import { logEvent } from './events';
import { addFurniture, furnitureAt, removeFurniture } from './furniture';
import { GROUNDS, inBounds, setTile, tileIndex, type Point } from './grid';
import { hashRoll, idNumber } from './hash';
import { spawnItem } from './items';
import { seasonOf, type Season } from './seasons';
import { adjustReputation, getSettlement, isHostile, newSettlement, relation, tieSettlements, type Settlement } from './settlements';
import { caravansOf, sendCaravan } from './caravans';
import type { SimState } from './state';
import { DAY_LENGTH, dayOf } from './time';
import { ensureGeneratedAround } from './worldgen';
import { evictZombies } from './zombies';

/** Food grown a head a day, by season. */
const GROWN: Record<Season, number> = { spring: 1.25, summer: 1.4, autumn: 1.0, winter: 0.4 };
/** Share of the stores that spoils each day. */
export const SPOIL_SHARE = 0.04;
export const STARVE_SHARE = 1 / 7;
export const GROW_CHANCE = 0.25;
/** Food (in days) at which they count themselves well fed. */
export const WELL_FED_DAYS = 4;
export const MAX_DEFENCE = 5;
export const SPLIT_AT = 16;
export const SPLIT_PEOPLE = 5;
export const MAX_SETTLEMENTS = 6;
export const UNFOLD_RADIUS = 24;
/** What a starving neighbour thinks of you, each day. */
export const DESPERATE_REP = -3;
const SPLIT_NAMES = ['New Hope', 'Ashford', 'The Mill', 'Kestrel Farm', 'Lowgate', 'Stillwater'];

export function isFallen(n: Settlement): boolean {
  return n.people <= 0;
}

export function hungerOf(n: Settlement): 'starving' | 'hungry' | 'fed' {
  return n.food <= 0 ? 'starving' : n.food < n.people * 2 ? 'hungry' : 'fed';
}

/** Runs every tick: the dawn round of off-screen life, and unfolding near your people. */
export function updateColonies(state: SimState): void {
  if (state.settlements.length === 0) return;
  if (state.tick > 0 && state.tick % DAY_LENGTH === 0) dawn(state);
  if (state.tick % 50 === 0) for (const n of state.settlements) if (!n.unfolded && someoneNear(state, n)) unfold(state, n);
}

function dawn(state: SimState): void {
  const day = dayOf(state.tick);
  const season = seasonOf(state.tick);
  for (const n of [...state.settlements]) {
    if (isFallen(n)) continue;
    const id = idNumber(n.id);
    const roll = (salt: number) => hashRoll(id, day, salt);
    const grown = GROWN[season] * (state.settings.winter ? 0.5 : 1);
    n.food = Math.round((n.food * (1 - SPOIL_SHARE) + n.people * grown - n.people) * 10) / 10;
    if (n.food < 0) {
      n.food = 0;
      const lost = Math.max(1, Math.round(n.people * STARVE_SHARE));
      n.people -= lost;
      news(state, n, `${lost} starved at ${n.name}`);
      if (!isHostile(n)) adjustReputation(state, n, DESPERATE_REP);
    } else if (n.food >= n.people * WELL_FED_DAYS && roll(1) < GROW_CHANCE) {
      n.people += 1;
      if (n.defence < MAX_DEFENCE && roll(2) < 0.3) n.defence += 1;
    }
    // A night attack: the later it gets, the likelier; good walls turn most away.
    if (roll(3) < ((0.06 + day * 0.002) * state.settings.zombies) / 100 - n.defence * 0.015) {
      const dead = roll(4) < 0.3 ? 2 : 1;
      n.people -= dead;
      news(state, n, `The dead got into ${n.name} in the night: ${dead} lost`);
    }
    if (isFallen(n)) {
      n.people = 0;
      logEvent(state, `${n.name} has fallen. Nobody is left.`);
      continue;
    }
    if (hungerOf(n) === 'starving') raidOrBeg(state, n, roll);
    else if (hungerOf(n) === 'hungry') allyHelps(state, n, roll);
    feud(state, n, roll);
    if (n.people >= SPLIT_AT && state.settlements.length < MAX_SETTLEMENTS) split(state, n, roll);
  }
  for (const n of state.settlements) syncResidents(state, n);
}

/**
 * Raid a neighbour with your armed people (settled off screen, from their numbers):
 * win and half their food comes home; lose and one of yours is badly hurt. Either way
 * they count you an enemy. Once a day each. Returns an error, or null.
 */
export function raidSettlement(state: SimState, id: string): string | null {
  const n = getSettlement(state, id);
  if (!n) return 'no such settlement';
  if (isFallen(n)) return 'nobody is left there';
  if (dayOf(n.lastRaid) === dayOf(state.tick) && n.lastRaid >= 0) return 'already raided today';
  const armed = state.survivors.filter((s) => s.weapon && s.childUntil === null && !s.sleep?.asleep);
  if (armed.length === 0) return 'nobody armed to send';
  n.lastRaid = state.tick;
  const guns = armed.filter((s) => state.items.find((i) => i.id === s.weapon)?.kind === 'gun').length;
  const ours = armed.length * 2 + guns * 2;
  const theirs = n.people + n.defence * 2;
  const roll = hashRoll(idNumber(n.id), state.tick, 0xa1);
  adjustReputation(state, n, -60);
  if (ours * (0.5 + roll) > theirs) {
    const took = Math.ceil(n.food / 2);
    n.food -= took;
    const killed = 1 + (roll > 0.8 ? 1 : 0);
    n.people = Math.max(0, n.people - killed);
    const home = armed[0]!.pos;
    for (let i = 0; i < Math.min(10, took); i++) spawnItem(state, 'food', home.x, home.y);
    logEvent(state, `Your raid on ${n.name} worked: ${Math.min(10, took)} food brought home, ${killed} of theirs dead`);
  } else {
    const hurt = armed[Math.floor(roll * armed.length)]!;
    hurt.health = Math.max(1, hurt.health - 150);
    hurt.lastHurtBy = 'a raid gone wrong';
    hurt.hurtTick = state.tick;
    logEvent(state, `Your raid on ${n.name} failed. ${hurt.name} came back badly hurt`);
  }
  if (isFallen(n)) logEvent(state, `${n.name} has fallen. Nobody is left.`);
  syncResidents(state, n);
  return null;
}

/** A starving settlement: a fed neighbour may help; otherwise it may raid the nearest one with food. */
function raidOrBeg(state: SimState, n: Settlement, roll: (salt: number) => number): void {
  const others = state.settlements
    .filter((o) => o !== n && !isFallen(o) && o.food > 0)
    .sort((a, b) => Math.abs(a.x - n.x) + Math.abs(a.y - n.y) - (Math.abs(b.x - n.x) + Math.abs(b.y - n.y)));
  const giver = others.find((o) => o.food >= o.people * WELL_FED_DAYS && (o.ties[n.id] ?? 0) >= 0);
  if (giver && roll(5) < 0.5 && !caravansOf(state).some((c) => c.to === n.id)) {
    const sent = Math.floor(giver.food / 3);
    giver.food -= sent;
    sendCaravan(state, giver.id, n.id, { x: n.x, y: n.y }, sent);
    tie(state, giver, n, 10);
    news(state, n, `${giver.name} sent a caravan of food to a starving ${n.name}`);
    return;
  }
  const target = others[0];
  if (!target || roll(6) > 0.4) return;
  const took = Math.ceil(target.food / 2);
  target.food -= took;
  n.food += took;
  n.people = Math.max(0, n.people - 1);
  target.people = Math.max(0, target.people - (roll(7) < 0.5 ? 1 : 0));
  tie(state, target, n, -40);
  news(state, n, `${n.name} raided ${target.name} for food`);
}

/** A hungry settlement's well-fed ally sends a little, by caravan (once one's on the way, no more). */
function allyHelps(state: SimState, n: Settlement, roll: (salt: number) => number): void {
  if (caravansOf(state).some((c) => c.to === n.id) || roll(10) >= 0.5) return;
  const ally = state.settlements.find((o) => o !== n && !isFallen(o) && relation(o, n) === 'allies' && o.food >= o.people * WELL_FED_DAYS);
  if (!ally) return;
  const sent = Math.floor(ally.food / 4);
  ally.food -= sent;
  sendCaravan(state, ally.id, n.id, { x: n.x, y: n.y }, sent);
  news(state, n, `${ally.name} sent a caravan to its ally ${n.name}`);
}

/** Feuding neighbours raid each other now and then (FEUD_RAID_CHANCE a day), fed or not. */
export const FEUD_RAID_CHANCE = 0.08;
function feud(state: SimState, n: Settlement, roll: (salt: number) => number): void {
  const foe = state.settlements.find((o) => o !== n && !isFallen(o) && relation(n, o) === 'feud');
  if (!foe || roll(11) >= FEUD_RAID_CHANCE) return;
  const took = Math.ceil(foe.food / 3);
  foe.food -= took;
  n.food += took;
  const win = roll(12) < 0.5;
  (win ? foe : n).people = Math.max(0, (win ? foe : n).people - 1);
  tie(state, n, foe, -10);
  news(state, n, `${n.name} raided ${foe.name}, their old enemy: ${took} food taken, one of ${win ? foe.name : n.name} dead`);
}

/** Ties between two settlements, with the news when they become allies or fall out for good. */
function tie(state: SimState, a: Settlement, b: Settlement, by: number): void {
  const before = tieSettlements(a, b, by);
  const after = relation(a, b);
  if (after === before) return;
  if (after === 'allies') logEvent(state, `${a.name} and ${b.name} are allies now`);
  else if (after === 'feud') logEvent(state, `${a.name} and ${b.name} have fallen out: a feud`);
  else logEvent(state, before === 'allies' ? `${a.name} and ${b.name} are no longer allies` : `The feud between ${a.name} and ${b.name} has cooled`);
}

function split(state: SimState, n: Settlement, roll: (salt: number) => number): void {
  const angle = roll(8) * Math.PI * 2;
  const { width, height } = state.grid;
  const x = Math.max(8, Math.min(width - 9, Math.round(n.x + Math.cos(angle) * 30)));
  const y = Math.max(8, Math.min(height - 9, Math.round(n.y + Math.sin(angle) * 30)));
  const name = SPLIT_NAMES[state.settlements.length % SPLIT_NAMES.length]!;
  n.people -= SPLIT_PEOPLE;
  const food = Math.floor(n.food / 3);
  n.food -= food;
  const child = newSettlement(state, name, x, y, SPLIT_PEOPLE, food, 0);
  child.reputation = n.reputation;
  tie(state, n, child, -10);
  logEvent(state, `A falling-out at ${n.name}: ${SPLIT_PEOPLE} left to found ${name}, near (${x}, ${y})`);
}

/** Only news you'd hear: from a neighbour that isn't hostile to you. */
function news(state: SimState, n: Settlement, text: string): void {
  if (!isHostile(n)) logEvent(state, text);
}

function someoneNear(state: SimState, n: Settlement): boolean {
  return state.survivors.some((s) => Math.max(Math.abs(s.pos.x - n.x), Math.abs(s.pos.y - n.y)) <= UNFOLD_RADIUS);
}

/** The compound's box: wider for more people. */
export function compound(n: Settlement): { x0: number; y0: number; x1: number; y1: number } {
  const w = Math.max(7, Math.min(15, 5 + Math.ceil(n.people / 2)));
  return { x0: n.x - Math.floor(w / 2), y0: n.y - 3, x1: n.x - Math.floor(w / 2) + w - 1, y1: n.y + 3 };
}

/** Builds the real thing: walls and a door, beds, food, and residents (or a ruin, if it fell). */
export function unfold(state: SimState, n: Settlement): void {
  n.unfolded = true;
  ensureGeneratedAround(state, { x: n.x, y: n.y });
  const box = compound(n);
  const { grid } = state;
  const fallen = isFallen(n);
  const door = { x: n.x, y: box.y1 };
  for (let y = box.y0; y <= box.y1; y++) {
    for (let x = box.x0; x <= box.x1; x++) {
      if (!inBounds(grid, x, y)) continue;
      const old = furnitureAt(state, x, y);
      if (old) removeFurniture(state, old);
      state.items = state.items.filter((i) => !i.pos || i.pos.x !== x || i.pos.y !== y);
      grid.ground[tileIndex(grid, x, y)] = GROUNDS.indexOf('dirt');
      const edge = x === box.x0 || x === box.x1 || y === box.y0 || y === box.y1;
      // A fallen place has gaps in its walls where they broke in.
      const broken = fallen && hashRoll(idNumber(n.id), x, y) < 0.25;
      setTile(grid, x, y, edge && !broken ? (x === door.x && y === door.y ? 'door' : 'wall') : 'floor');
      if (edge) evictZombies(state, { x, y });
    }
  }
  state.walkabilityVersion += 1;
  const inside: Point[] = [];
  for (let y = box.y0 + 1; y < box.y1; y++) for (let x = box.x0 + 1; x < box.x1; x++) if (inBounds(grid, x, y)) inside.push({ x, y });
  const top = inside.filter((p) => p.y === box.y0 + 1);
  const beds = Math.min(top.length, Math.max(n.people, fallen ? 3 : 1));
  for (const p of top.slice(0, beds)) addFurniture(state, 'bed', p.x, p.y);
  const rest = inside.filter((p) => p.y > box.y0 + 1);
  if (fallen) {
    for (const p of rest.slice(0, 2)) addFurniture(state, 'crate', p.x, p.y).loot = 3;
    for (const p of rest.slice(-3)) state.dormant.push({ id: `d${state.nextId++}`, x: p.x, y: p.y });
    return;
  }
  // Their food stays a number (not items your haulers would walk off with): raid for it, or trade.
  for (const p of rest.slice(0, 2)) addFurniture(state, 'table', p.x, p.y);
  syncResidents(state, n);
  logEvent(state, `You can see ${n.name}: ${n.people} people behind ${n.defence >= 3 ? 'good' : 'thin'} walls`);
}

