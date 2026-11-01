// Expeditions: survivors sent off the map to a point of interest.
//
// Sending takes them out of the colony (state.survivors) into an expedition
// record until it's due back: the walk there and back plus SEARCH_TICKS.
// On return (sim RNG), what the ruin's crates held comes back with them, up
// to CARRY each, rolled like scavenging; the crates keep the rest. Sleeping
// zombies near the ruin make it dangerous: members may come back hurt, or
// not at all. The ruin's sleepers are cleared either way. Abstract on
// purpose: nothing walks the route, which is what keeps far trips cheap.

import { logEvent } from './events';
import { HEALTH_MAX } from './health';
import { scavengeFind } from './jobs';
import { spawnItem } from './items';
import { mournDeath } from './mood';
import { random } from './rng';
import { manhattan } from './space';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { TICKS_PER_TILE } from './survivors';

export const SEARCH_TICKS = 400;
export const CARRY = 4;
/** Crates and sleepers within this of the point of interest count as the ruin's. */
export const RUIN_RADIUS = 10;
export const INJURY_CHANCE = 0.3;
export const INJURY = 300;

export interface Expedition {
  id: string;
  members: Survivor[];
  target: { x: number; y: number; label: string };
  /** Where they left from, and come back to. */
  home: { x: number; y: number };
  returnTick: number;
}

export function sendExpedition(state: SimState, members: Survivor[], poiId: string): string | null {
  const poi = state.pointsOfInterest.find((p) => p.id === poiId);
  if (!poi) return `no point of interest ${poiId}`;
  if (members.length === 0) return 'nobody to send';
  const home = { ...members[0]!.pos };
  const distance = manhattan(home, poi);
  for (const s of members) state.survivors.splice(state.survivors.indexOf(s), 1);
  state.expeditions.push({
    id: `x${state.nextId++}`,
    members,
    target: { x: poi.x, y: poi.y, label: poi.label },
    home,
    returnTick: state.tick + 2 * distance * TICKS_PER_TILE + SEARCH_TICKS,
  });
  logEvent(state, `${members.map((s) => s.name).join(', ')} set out for the ${poi.label} at (${poi.x}, ${poi.y})`);
  return null;
}

/** Brings back any expedition that's due. */
export function updateExpeditions(state: SimState): void {
  for (const x of [...state.expeditions]) if (state.tick >= x.returnTick) returnHome(state, x);
}

function returnHome(state: SimState, x: Expedition): void {
  state.expeditions.splice(state.expeditions.indexOf(x), 1);
  const near = (p: { x: number; y: number }) => manhattan(p, x.target) <= RUIN_RADIUS;
  const sleepers = state.dormant.filter(near);
  state.dormant = state.dormant.filter((d) => !near(d));
  // The danger: sleepers near the ruin, shared out among the party.
  const deathChance = Math.min(0.5, 0.05 + (0.08 * sleepers.length) / x.members.length);
  const back: Survivor[] = [];
  const lost: Survivor[] = [];
  const hurt: Survivor[] = [];
  for (const s of x.members) {
    if (random(state) < deathChance) {
      lost.push(s);
      continue;
    }
    if (sleepers.length > 0 && random(state) < INJURY_CHANCE) {
      s.health = Math.max(1, s.health - INJURY);
      hurt.push(s);
    }
    s.health = Math.min(HEALTH_MAX, s.health);
    s.pos = { ...x.home };
    s.note = `back from the ${x.target.label}`;
    back.push(s);
  }
  state.survivors.push(...back);
  // Loot: what the ruin's crates held, as far as the survivors could carry.
  let room = CARRY * back.length;
  let found = 0;
  for (const crate of state.furniture.filter((f) => f.kind === 'crate' && f.loot > 0 && near(f))) {
    while (crate.loot > 0 && room > 0) {
      crate.loot -= 1;
      room -= 1;
      found += 1;
      spawnItem(state, scavengeFind(random(state)), x.home.x, x.home.y);
    }
    if (crate.loot === 0) crate.scavenge = false;
  }
  state.pointsOfInterest = state.pointsOfInterest.filter((p) => !(p.x === x.target.x && p.y === x.target.y));
  for (const s of lost) mournDeath(state, s);
  const parts = [`brought back ${found} find${found === 1 ? '' : 's'}`];
  if (hurt.length) parts.push(`${hurt.map((s) => s.name).join(', ')} hurt`);
  if (lost.length) parts.push(`${lost.map((s) => s.name).join(', ')} didn't come back`);
  logEvent(state, `The expedition to the ${x.target.label} is back: ${parts.join('; ')}`);
}

/** Everyone in the colony or away on an expedition (their ids stay in use). */
export function everyone(state: SimState): Survivor[] {
  return [...state.survivors, ...state.expeditions.flatMap((x) => x.members)];
}
