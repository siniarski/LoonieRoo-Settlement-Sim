// Alerts: what needs the player's attention right now, worst first. Pure and
// read-only (the HUD calls it every frame or so), so it never changes state.

import { isRotting } from './corpses';
import { ofKind } from './furniture';
import { MEAL_VALUE, HOT_MEAL_VALUE, HUNGRY } from './food';
import { isCritical } from './health';
import { INFECTION_TURN } from './infection';
import { completeRooms } from './rooms';
import { SEASON_DAYS, seasonOf } from './seasons';
import { isHostile } from './settlements';
import type { SimState } from './state';
import { DAY_LENGTH, NIGHT_START, TICKS_PER_HOUR, dayOf, hourOf, isNight, onClock } from './time';
import { THIRSTY } from './water';
import { zombiesForNight } from './zombies';
import { manhattan } from './space';
import { everyone } from './expeditions';
import { daysToSiege } from './endgame';
import { canAmputate } from './wounds';

export type AlertLevel = 'danger' | 'warn' | 'info';

export interface Alert {
  level: AlertLevel;
  text: string;
  /** Where to look, if anywhere. */
  at?: { x: number; y: number };
}

/** Satiety a survivor burns through in a day. */
let DAILY_SATIETY = DAY_LENGTH;
onClock(() => (DAILY_SATIETY = DAY_LENGTH));
/** Within this of a survivor, zombies count as "at the colony". */
const NEAR = 25;

/** Days the colony's food (raw and hot) would last at the current headcount. */
export function foodDays(state: SimState): number {
  const people = everyone(state).length;
  if (people === 0) return Infinity;
  const satiety = state.items.reduce((sum, i) => sum + (i.kind === 'food' ? MEAL_VALUE : i.kind === 'meal' ? HOT_MEAL_VALUE : 0), 0);
  return satiety / (people * DAILY_SATIETY);
}

/** Days until winter starts (0 in winter). */
export function daysToWinter(tick: number): number {
  const day = dayOf(tick);
  const intoYear = (day - 1) % (4 * SEASON_DAYS);
  return seasonOf(tick) === 'winter' ? 0 : 3 * SEASON_DAYS - intoYear;
}

export function alerts(state: SimState): Alert[] {
  const out: Alert[] = [];
  const add = (level: AlertLevel, text: string, at?: { x: number; y: number }) => out.push({ level, text, ...(at ? { at } : {}) });
  const people = state.survivors;

  // Threats.
  const near = state.zombies.filter((z) => people.some((s) => manhattan(s.pos, z.pos) <= NEAR));
  const raiders = near.filter((z) => z.kind === 'raider');
  if (raiders.length > 0) add('danger', `Raiders at the colony: ${raiders.length}`, raiders[0]!.pos);
  if (near.length > raiders.length) add('danger', `${near.length - raiders.length} zombie${near.length - raiders.length === 1 ? '' : 's'} near the colony`, near.find((z) => z.kind !== 'raider')!.pos);
  if (state.fires.length > 0) add('danger', `Fire! ${state.fires.length} tile${state.fires.length === 1 ? '' : 's'} burning`, state.fires[0]);
  for (const h of state.herds) if (h.heard) add('warn', `A herd of ${h.size} is out there, heading for (${h.to.x}, ${h.to.y})`, h);
  const hour = hourOf(state.tick);
  if (!isNight(state.tick) && hour >= NIGHT_START - 3) {
    const night = dayOf(state.tick);
    add('info', `Night in ${NIGHT_START - hour}h: about ${zombiesForNight(night, people.length)} zombies expected`);
  }

  // The endgame and sieges.
  const eg = state.endgame;
  if (eg.won) add('info', eg.won === 'cure' ? 'You found the cure. You have won' : 'The helicopter came. You have won');
  else if (eg.evacuation !== null) add('warn', `The helicopter comes at dawn on day ${dayOf(eg.evacuation)}: hold out (the last night is a siege)`);
  const toSiege = daysToSiege(state.tick);
  if (toSiege <= 2) add(toSiege === 0 ? 'danger' : 'warn', toSiege === 0 ? 'Siege tonight: a quarter more zombies' : `Siege in ${toSiege} night${toSiege === 1 ? '' : 's'}`);

  // People.
  for (const s of people) {
    if (s.infection !== null) add('danger', `${s.name} is infected: about ${Math.ceil((INFECTION_TURN - s.infection) / TICKS_PER_HOUR)}h to cure them (medicine)`, s.pos);
    else if (isCritical(s.health)) add('danger', `${s.name} is badly hurt`, s.pos);
    if (s.satiety === 0) add('danger', `${s.name} is starving`, s.pos);
    else if (s.satiety < HUNGRY / 2) add('warn', `${s.name} is hungry`, s.pos);
    if (state.harsh && s.hydration === 0) add('danger', `${s.name} is dying of thirst`, s.pos);
    else if (state.harsh && s.hydration < THIRSTY / 2) add('warn', `${s.name} is thirsty`, s.pos);
    if (s.sick !== null) add('warn', `${s.name} is sick (medicine helps)`, s.pos);
    if (s.bleeding) add('warn', `${s.name} is bleeding (a bandage stops it)`, s.pos);
    if (canAmputate(s)) add('danger', `${s.name}'s bitten ${s.bitten} could come off and save them (overview: amputate)`, s.pos);
  }

  // Stocks and the season.
  const days = foodDays(state);
  const toWinter = daysToWinter(state.tick);
  if (people.length > 0 && days < 1) add('danger', `Food for less than a day (${people.length} to feed)`);
  else if (people.length > 0 && toWinter > 0 && toWinter <= 5 && days < SEASON_DAYS) {
    add('warn', `Winter in ${toWinter} day${toWinter === 1 ? '' : 's'}: food for about ${days.toFixed(1)} days, and nothing grows outdoors in winter (chicken coops lay all year; a greenhouse grows all year)`);
  } else if (people.length > 0 && days < 2) add('warn', `Food for about ${days.toFixed(1)} days`);
  if (toWinter > 0 && toWinter <= 2 && people.some((s) => !s.body)) add('info', 'Winter is coming: coats keep the cold off outdoors at night');
  const beds = ofKind(state, 'bed').length;
  if (people.length > beds) add('warn', `${people.length - beds} without a bed`);
  if (state.harsh) {
    const clean = state.furniture.some((f) => f.kind === 'well' || (f.kind === 'collector' && f.loot > 0));
    if (!clean) add('info', 'No clean water: build a well or a rain collector');
    const graves = completeRooms(state, 'graveyard');
    const rotting = state.items.filter((i) => isRotting(state, i, graves));
    if (rotting.length > 0) add('warn', `${rotting.length} bod${rotting.length === 1 ? 'y' : 'ies'} rotting: make a graveyard and haulers will bury them`, rotting[0]!.pos!);
  }
  for (const g of state.furniture) {
    if (g.kind === 'generator' && g.stored === 0) add('warn', `The generator at (${g.x}, ${g.y}) has no fuel`, g);
  }

  // Visitors and neighbours.
  for (const n of state.settlements) if (isHostile(n)) add('warn', `${n.name} is hostile: more raids`);
  if (state.wanderers.length > 0) add('info', `${state.wanderers.length} waiting to be taken in`, state.wanderers[0]);
  for (const t of state.traders) add('info', `A trader is waiting at (${t.x}, ${t.y})`, t);

  const rank: Record<AlertLevel, number> = { danger: 0, warn: 1, info: 2 };
  return out.sort((a, b) => rank[a.level] - rank[b.level]);
}

/** Counts of every item kind the colony has (on the ground, carried, worn), for the overview. */
export function stockCounts(state: SimState): [string, number][] {
  const counts = new Map<string, number>();
  for (const i of state.items) if (i.kind !== 'corpse') counts.set(i.kind, (counts.get(i.kind) ?? 0) + 1);
  return [...counts.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));
}
