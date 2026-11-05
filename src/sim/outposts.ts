// Vehicles and outposts.
//
// The truck: a generated world leaves one somewhere out past the camp
// (worldgen). Once someone has seen it, expeditions take it when there's
// TRUCK_FUEL free fuel to burn: there and back in half the time, carrying
// twice as much (expeditions.ts).
//
// Outposts: an expedition can stay where it went (claimOutpost). Each dawn
// an outpost sends home OUTPOST_FINDS per member (rolled like scavenging, by
// hash) and runs a risk (OUTPOST_DANGER a day, by hash) that someone there is
// hurt, or killed if they were hurt already. Recalling one sets it on its way
// home like any expedition.

import { spawnFind } from './difficulty';
import { ofKind } from './furniture';
import { logEvent } from './events';
import { isExplored } from './fog';
import { hashRoll, idNumber } from './hash';
import { HEALTH_MAX } from './health';
import { scavengeFind } from './jobs';
import { spawnItem } from './items';
import { mournDeath } from './mood';
import type { SimState } from './state';
import { DAY_LENGTH } from './time';
import { TICKS_PER_TILE } from './survivors';
import { manhattan } from './space';

export const TRUCK_FUEL = 2;
export const OUTPOST_FINDS = 1;
export const OUTPOST_DANGER = 0.15;
export const OUTPOST_HURT = 350;
/** An outpost isn't due back until it's recalled. */
export const NEVER = Number.MAX_SAFE_INTEGER;

/** The colony's truck, if anyone's seen it. */
export function truck(state: SimState) {
  return ofKind(state, 'truck').find((f) => isExplored(state, f.x, f.y));
}

/** Takes the truck (and its fuel) for a trip, if it can go. */
export function takeTruck(state: SimState): boolean {
  if (!truck(state)) return false;
  const fuel = state.items.filter((i) => i.kind === 'fuel' && i.pos !== null && i.reservedBy === null);
  if (fuel.length < TRUCK_FUEL) return false;
  for (const f of fuel.slice(0, TRUCK_FUEL)) state.items.splice(state.items.indexOf(f), 1);
  return true;
}

export function updateOutposts(state: SimState): void {
  if (state.tick % DAY_LENGTH !== 0 || state.tick === 0) return;
  for (const x of state.expeditions) {
    if (!x.outpost) continue;
    for (let k = 0; k < x.members.length * OUTPOST_FINDS; k++) {
      spawnFind(state, scavengeFind(hashRoll(idNumber(x.id), state.tick, k)), x.home.x, x.home.y);
    }
    if (hashRoll(idNumber(x.id), state.tick, 0xd4) >= OUTPOST_DANGER || x.members.length === 0) continue;
    const s = x.members[Math.floor(hashRoll(idNumber(x.id), state.tick, 1) * x.members.length)]!;
    if (s.health > HEALTH_MAX - OUTPOST_HURT) {
      s.health -= OUTPOST_HURT;
      logEvent(state, `Trouble at the ${x.target.label} outpost: ${s.name} was hurt`);
      continue;
    }
    x.members.splice(x.members.indexOf(s), 1);
    for (const id of [s.weapon, s.body, s.feet, s.cart, s.tool, s.carrying]) {
      const item = id ? state.items.find((i) => i.id === id) : undefined;
      if (item) state.items.splice(state.items.indexOf(item), 1);
    }
    for (const f of state.furniture) if (f.owner === s.id) f.owner = null;
    logEvent(state, `${s.name} was killed at the ${x.target.label} outpost`);
    mournDeath(state, s);
    if (x.members.length === 0) state.expeditions.splice(state.expeditions.indexOf(x), 1);
  }
  // Wounded at the outpost heal slowly while they're there.
  for (const x of state.expeditions) if (x.outpost) for (const s of x.members) s.health = Math.min(HEALTH_MAX, s.health + 100);
}

/** Recall an outpost: they set off home. Returns an error, or null. */
export function recallOutpost(state: SimState, id: string): string | null {
  const x = state.expeditions.find((e) => e.id === id);
  if (!x?.outpost) return 'no such outpost';
  x.outpost = false;
  x.returnTick = state.tick + manhattan(x.home, x.target) * TICKS_PER_TILE;
  logEvent(state, `The ${x.target.label} outpost is packing up and heading home`);
  return null;
}
