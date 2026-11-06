// Caravans: what settlements send each other (and you) travels the map.
//
// A caravan is a marker, like a herd (herds.ts): a few people and a cart, a tile every
// CARAVAN_STEP ticks in a straight line from where it set out to where it's going. Food a
// fed neighbour sends a starving one (colonies.ts), allies' trade runs, and the thanks for an
// errand or a radio request (errands.ts, radio.ts) all go by caravan now, not in an instant.
//   the road   every hour on the road there's a chance (AMBUSH_CHANCE, more as the days go on
//              and with more of the dead about) that the dead catch it: one of its people is
//              lost, and with them a share of the load. Lose them all and the caravan is gone,
//              and what's left of the load lies in the road (items, if that ground exists yet).
//   arriving   at a settlement, the food goes into its stores (and the two warm to each other);
//              at your place, the goods are set down where it stops.
// Shown on the map and the minimap where explored; drawn in the world where you can see.
// Rolls are hashes of its id and the tick, not the sim RNG.

import { logEvent } from './events';
import { inBounds, isWalkable, type Point } from './grid';
import { hashRoll, idNumber } from './hash';
import { spawnItem, type ItemKind } from './items';
import { getSettlement, isHostile, tieSettlements } from './settlements';
import { nearestTile } from './space';
import type { SimState } from './state';
import { TICKS_PER_HOUR, dayOf } from './time';

export interface Caravan {
  id: string;
  /** Who sent it (a settlement id). */
  from: string;
  /** Where it's going: a settlement id, or 'you'. */
  to: string;
  x: number;
  y: number;
  /** The end of the road. */
  dest: Point;
  people: number;
  /** Food for a settlement's stores (a number); goods for you (items). */
  food: number;
  goods: ItemKind[];
  nextStepTick: number;
}

export const CARAVAN_STEP = 6;
export const CARAVAN_PEOPLE = 3;
/** Chance an hour on the road that the dead catch it (grows with the days and the zombie setting). */
export const AMBUSH_CHANCE = 0.03;

export function caravansOf(state: SimState): Caravan[] {
  return state.caravans ?? [];
}

/** Sends a caravan from a settlement to another settlement, or to you ('you': to `dest`). */
export function sendCaravan(state: SimState, fromId: string, to: string, dest: Point, food: number, goods: ItemKind[] = []): Caravan | null {
  const from = getSettlement(state, fromId);
  if (!from) return null;
  state.caravans ??= [];
  const c: Caravan = { id: `c${state.nextId++}`, from: fromId, to, x: from.x, y: from.y, dest: { ...dest }, people: CARAVAN_PEOPLE, food, goods: [...goods], nextStepTick: state.tick + CARAVAN_STEP };
  state.caravans.push(c);
  return c;
}

/** Every tick: caravans move on, run into trouble, and arrive. */
export function updateCaravans(state: SimState): void {
  const list = caravansOf(state);
  if (list.length === 0) return;
  for (const c of [...list]) {
    if (state.tick % TICKS_PER_HOUR === 0 && ambushed(state, c)) continue;
    if (state.tick < c.nextStepTick) continue;
    c.nextStepTick = state.tick + CARAVAN_STEP;
    c.x += Math.sign(c.dest.x - c.x);
    c.y += Math.sign(c.dest.y - c.y);
    if (c.x === c.dest.x && c.y === c.dest.y) arrive(state, c);
  }
}

/** The dead catch up with it: a person and a share of the load lost; all of them, and it's gone. */
function ambushed(state: SimState, c: Caravan): boolean {
  const chance = (AMBUSH_CHANCE * (1 + dayOf(state.tick) / 20) * state.settings.zombies) / 100;
  if (hashRoll(idNumber(c.id), state.tick, 0xca2) >= chance) return false;
  const share = 1 / c.people;
  c.people -= 1;
  c.food = Math.floor(c.food * (1 - share));
  c.goods = c.goods.slice(0, Math.floor(c.goods.length * (1 - share)));
  const from = getSettlement(state, c.from);
  if (from) from.people = Math.max(0, from.people - 1);
  const heard = c.to === 'you' || (from && !isHostile(from));
  if (c.people > 0) {
    if (heard) logEvent(state, `The dead caught ${from?.name ?? 'a'} caravan near (${c.x}, ${c.y}): one of them lost`);
    return false;
  }
  if (heard) logEvent(state, `${from?.name ?? 'A'} caravan never made it: the dead got them all near (${c.x}, ${c.y})`);
  // What's left lies in the road, if the ground there is real yet.
  const spot = inBounds(state.grid, c.x, c.y) ? nearestTile(state.grid, c, (x, y) => isWalkable(state.grid, x, y), 50) : null;
  if (spot) {
    for (const kind of c.goods) spawnItem(state, kind, spot.x, spot.y);
    for (let k = 0; k < Math.min(5, c.food); k++) spawnItem(state, 'food', spot.x, spot.y);
  }
  state.caravans!.splice(state.caravans!.indexOf(c), 1);
  return true;
}

function arrive(state: SimState, c: Caravan): void {
  state.caravans!.splice(state.caravans!.indexOf(c), 1);
  const from = getSettlement(state, c.from);
  if (c.to === 'you') {
    const spot = nearestTile(state.grid, c, (x, y) => isWalkable(state.grid, x, y), 200) ?? c;
    for (const kind of c.goods) spawnItem(state, kind, spot.x, spot.y);
    for (let k = 0; k < c.food; k++) spawnItem(state, 'food', spot.x, spot.y);
    const what = [...(c.food > 0 ? [`${c.food} food`] : []), ...c.goods].join(', ') || 'nothing left';
    logEvent(state, `A caravan from ${from?.name ?? 'the neighbours'} reached you: ${what}`);
    return;
  }
  const to = getSettlement(state, c.to);
  if (!to) return;
  to.food += c.food;
  if (from) tieSettlements(from, to, 10);
  if (!isHostile(to)) logEvent(state, `A caravan from ${from?.name ?? 'somewhere'} reached ${to.name} with ${c.food} food`);
}
