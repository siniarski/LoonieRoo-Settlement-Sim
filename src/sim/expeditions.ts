// Expeditions: survivors sent off the map to a point of interest.
//
// Sending takes them out of the colony (state.survivors) into an expedition
// record until it's due back: the walk there and back plus SEARCH_TICKS.
// On return (sim RNG), what the ruin's crates held comes back with them, up
// to CARRY each, rolled like scavenging; the crates keep the rest. Sleeping
// zombies near the ruin make it dangerous: members may come back hurt, or
// not at all. The ruin's sleepers are cleared either way. Abstract on
// purpose: nobody walks the route, which is what keeps far trips cheap.
//
// The truck, though, is seen on the road (when it's a vehicle: outposts.ts): it
// drives there in a straight line, waits while they search, and drives back,
// and nobody else can take it meanwhile. Each hour on the road there's a
// chance the dead catch it (ROAD_AMBUSH): someone's hurt, or killed if they
// were hurt already; lose everyone and the truck stands where it stopped. An
// outpost's truck stays parked at the outpost.

import { spawnFind } from './difficulty';
import { ofKind, setScavenge } from './furniture';
import { logEvent } from './events';
import { HEALTH_MAX } from './health';
import { scavengeFind } from './jobs';
import { EXPEDITION_DOG_CHANCE, addDog } from './dogs';
import { DISTRESS_LABEL } from './radio';
import { addSurvivor } from './survivors';
import { rollTraits } from './skills';
import { hashRoll, idNumber } from './hash';
import { spawnItem } from './items';
import { mournDeath } from './mood';
import { random } from './rng';
import { manhattan } from './space';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { TICKS_PER_TILE } from './survivors';
import { NEVER, takeTruck, truckVehicleId } from './outposts';
import { parkable, vehiclesOf } from './vehicles';
import { centre } from './motion';
import { nearestTile } from './space';
import { TICKS_PER_HOUR } from './time';

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
  /** Went in the truck (outposts.ts): half the travel, twice the carrying. */
  truck: boolean;
  /** Staying as an outpost until recalled (outposts.ts). */
  outpost: boolean;
  /** The truck vehicle on the road with them, and when they left (missing: no truck, or furniture). */
  truckId?: string;
  leftTick?: number;
}

/** Chance an hour on the road that the dead catch the truck. */
export const ROAD_AMBUSH = 0.03;

export function sendExpedition(state: SimState, members: Survivor[], poiId: string, outpost = false): string | null {
  const poi = state.pointsOfInterest.find((p) => p.id === poiId);
  if (!poi) return `no point of interest ${poiId}`;
  if (members.length === 0) return 'nobody to send';
  const home = { ...members[0]!.pos };
  const distance = manhattan(home, poi);
  for (const s of members) state.survivors.splice(state.survivors.indexOf(s), 1);
  const truckId = truckVehicleId(state);
  const inTruck = takeTruck(state);
  const id = `x${state.nextId}`;
  const v = inTruck && truckId ? vehiclesOf(state).find((q) => q.id === truckId) : undefined;
  if (v) v.driverId = `x:${id}`; // out with the expedition: nobody else takes it
  state.expeditions.push({
    ...(v ? { truckId: v.id, leftTick: state.tick } : {}),
    id: `x${state.nextId++}`,
    members,
    target: { x: poi.x, y: poi.y, label: poi.label },
    home,
    returnTick: outpost ? NEVER : state.tick + (inTruck ? 1 : 2) * distance * TICKS_PER_TILE + SEARCH_TICKS,
    truck: inTruck,
    outpost,
  });
  logEvent(state, `${members.map((s) => s.name).join(', ')} set out${inTruck ? ' in the truck' : ''} for the ${poi.label} at (${poi.x}, ${poi.y})${outpost ? ' to hold it as an outpost' : ''}`);
  return null;
}

/** Brings back any expedition that's due. */
export function updateExpeditions(state: SimState): void {
  for (const x of [...state.expeditions]) {
    if (x.truckId && onTheRoad(state, x)) continue;
    if (state.tick >= x.returnTick) returnHome(state, x);
  }
}

/**
 * The truck on its way: where it is along the road (out, waiting at the ruin, back), and the dead
 * on the way. Returns true if the expedition ended on the road.
 */
function onTheRoad(state: SimState, x: Expedition): boolean {
  const v = vehiclesOf(state).find((q) => q.id === x.truckId);
  if (!v || x.leftTick === undefined) return false;
  const leg = Math.max(1, Math.floor(manhattan(x.home, x.target) * TICKS_PER_TILE / 2));
  const e = state.tick - x.leftTick;
  const driving = e < leg || (!x.outpost && e >= leg + SEARCH_TICKS);
  const f = e < leg ? e / leg : x.outpost || e < leg + SEARCH_TICKS ? 1 : Math.max(0, 1 - (e - leg - SEARCH_TICKS) / leg);
  const p = { x: Math.round(x.home.x + (x.target.x - x.home.x) * f), y: Math.round(x.home.y + (x.target.y - x.home.y) * f) };
  v.pos = p;
  v.at = centre(p);
  if (x.outpost && e >= leg) {
    // At the outpost: parked there.
    v.driverId = null;
    delete x.truckId;
    return false;
  }
  if (!driving || state.tick % TICKS_PER_HOUR !== 0) return false;
  if (hashRoll(idNumber(x.id), state.tick, 0x70ad) >= (ROAD_AMBUSH * state.settings.zombies) / 100) return false;
  // The dead catch the truck: someone's hurt, or killed if they were hurt already.
  const s = x.members[Math.floor(hashRoll(idNumber(x.id), state.tick, 1) * x.members.length)]!;
  if (s.health > HEALTH_MAX - INJURY) {
    s.health -= INJURY;
    logEvent(state, `The dead caught the truck on the road near (${p.x}, ${p.y}): ${s.name} was hurt`);
    return false;
  }
  x.members.splice(x.members.indexOf(s), 1);
  mournDeath(state, s);
  logEvent(state, `The dead caught the truck on the road near (${p.x}, ${p.y}): ${s.name} was killed`);
  if (x.members.length > 0) return false;
  // Nobody left: the truck stands where it stopped.
  state.expeditions.splice(state.expeditions.indexOf(x), 1);
  v.driverId = null;
  logEvent(state, `Nobody from the expedition to the ${x.target.label} is coming back. The truck stands empty near (${p.x}, ${p.y})`);
  return true;
}

function returnHome(state: SimState, x: Expedition): void {
  state.expeditions.splice(state.expeditions.indexOf(x), 1);
  // The truck parks back at home.
  const v = x.truckId ? vehiclesOf(state).find((q) => q.id === x.truckId) : undefined;
  if (v) {
    const spot = parkable(state, x.home) ? x.home : nearestTile(state.grid, x.home, (px, py) => parkable(state, { x: px, y: py }), 400) ?? x.home;
    v.pos = { ...spot };
    v.at = centre(spot);
    v.was = { ...v.at };
    v.driverId = null;
  }
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
      // What they carried is lost with them.
      for (const id of [s.weapon, s.body, s.feet, s.cart, s.tool, s.carrying]) {
        const item = id ? state.items.find((i) => i.id === id) : undefined;
        if (item) state.items.splice(state.items.indexOf(item), 1);
      }
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
  let room = CARRY * back.length * (x.truck ? 2 : 1);
  let found = 0;
  for (const crate of ofKind(state, 'crate').filter((f) => f.loot > 0 && near(f))) {
    while (crate.loot > 0 && room > 0) {
      crate.loot -= 1;
      room -= 1;
      found += 1;
      spawnFind(state, scavengeFind(random(state)), x.home.x, x.home.y);
    }
    if (crate.loot === 0) setScavenge(state, crate, false);
  }
  state.pointsOfInterest = state.pointsOfInterest.filter((p) => !(p.x === x.target.x && p.y === x.target.y));
  for (const s of lost) mournDeath(state, s);
  const parts = [`brought back ${found} find${found === 1 ? '' : 's'}`];
  if (hurt.length) parts.push(`${hurt.map((s) => s.name).join(', ')} hurt`);
  if (lost.length) parts.push(`${lost.map((s) => s.name).join(', ')} didn't come back`);
  // A distress call (radio.ts): whoever called comes back with them.
  if (x.target.label === DISTRESS_LABEL && back.length > 0) {
    const n = 1 + Math.floor(hashRoll(idNumber(x.id), 0xd15) * 2);
    const names: string[] = [];
    for (let k = 0; k < n; k++) {
      const s = addSurvivor(state, x.home.x, x.home.y);
      s.traits = rollTraits(s.id);
      names.push(s.name);
    }
    parts.push(`and brought in ${names.join(' and ')}`);
  }
  if (back.length > 0 && hashRoll(idNumber(x.id), 0xd09) < EXPEDITION_DOG_CHANCE) {
    const dog = addDog(state, x.home.x, x.home.y);
    parts.push(`and a dog, ${dog.name}, followed them home`);
  }
  logEvent(state, `The expedition to the ${x.target.label} is back: ${parts.join('; ')}`);
}

/** Everyone in the colony or away on an expedition (their ids stay in use). */
export function everyone(state: SimState): Survivor[] {
  return [...state.survivors, ...state.expeditions.flatMap((x) => x.members)];
}
