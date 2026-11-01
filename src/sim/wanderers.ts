// Wanderers: new survivors arriving by day.
//
// From day 2 a wanderer turns up at ARRIVAL_HOUR at the edge of explored ground,
// a short walk from the colony (on a fully explored map: at the map edge; sim RNG); a second comes at SECOND_ARRIVAL_HOUR if there is a spare bed.
// They wait where they arrived. The player takes them in (they become a
// survivor) or turns them away; ignored for WANDERER_PATIENCE ticks, or still
// waiting at nightfall, they leave on their own.

import { logEvent } from './events';
import { getTile, inBounds, isWalkable, type Point } from './grid';
import { DIRS } from './pathfinding';
import { pick, randomInt } from './rng';
import { isExplored } from './fog';
import { OUTDOORS, labelAt } from './rooms';
import { nearestTile } from './space';
import type { SimState } from './state';
import { nextSurvivorName } from './survivors';
import { rollTraits, type Trait } from './skills';
import { DAY_LENGTH, TICKS_PER_HOUR, dayOf, isNight, tickOfDay } from './time';

export const ARRIVAL_HOUR = 10;
/** How far from a survivor to look for an arrival spot (roughly). */
const ARRIVAL_DISTANCE = 24;
/** A second wanderer comes at this hour, but only if there is a spare bed. */
export const SECOND_ARRIVAL_HOUR = 14;
/** How long a wanderer waits for an answer: 3 in-game hours. */
export const WANDERER_PATIENCE = 3 * TICKS_PER_HOUR;

export interface Wanderer {
  id: string;
  name: string;
  x: number;
  y: number;
  /** Tick at which they give up and leave. */
  leavesTick: number;
  /** What they'd bring (skills.ts): shown before you decide. */
  traits: Trait[];
}

export function getWanderer(state: SimState, id: string): Wanderer | undefined {
  return state.wanderers.find((w) => w.id === id);
}

export function addWanderer(state: SimState, x: number, y: number): Wanderer {
  const id = `v${state.nextId++}`;
  const w: Wanderer = {
    id,
    traits: rollTraits(id),
    name: nextSurvivorName(state),
    x,
    y,
    leavesTick: state.tick + WANDERER_PATIENCE,
  };
  state.wanderers.push(w);
  return w;
}

export function removeWanderer(state: SimState, w: Wanderer): void {
  const i = state.wanderers.indexOf(w);
  if (i >= 0) state.wanderers.splice(i, 1);
}

/** Beds nobody would be left without: beds minus survivors minus wanderers already waiting. */
export function spareBeds(state: SimState): number {
  const beds = state.furniture.filter((f) => f.kind === 'bed').length;
  return beds - state.survivors.length - state.wanderers.length;
}

export function updateWanderers(state: SimState): void {
  reconcileWanderers(state);
  for (const w of [...state.wanderers]) {
    if (state.tick >= w.leavesTick || isNight(state.tick)) {
      removeWanderer(state, w);
      logEvent(state, `${w.name} gave up waiting and left`);
    }
  }
  if (dayOf(state.tick) < 2) return; // day 1 belongs to the starting colony
  const t = tickOfDay(state.tick);
  if (t === ARRIVAL_HOUR * TICKS_PER_HOUR) arrive(state);
  if (t === SECOND_ARRIVAL_HOUR * TICKS_PER_HOUR && spareBeds(state) > 0) arrive(state);
}

/** A wall built where a wanderer waits moves them to a free neighbour; with none, they leave. */
export function reconcileWanderers(state: SimState): void {
  for (const w of [...state.wanderers]) {
    if (isWalkable(state.grid, w.x, w.y)) continue;
    const to = DIRS.map((d) => ({ x: w.x + d.x, y: w.y + d.y })).find((p) => isWalkable(state.grid, p.x, p.y));
    if (to) {
      w.x = to.x;
      w.y = to.y;
    } else {
      removeWanderer(state, w);
      logEvent(state, `${w.name} was walled out and left`);
    }
  }
}

/** Out at the edge of what you've seen, a short walk from someone, on open ground. */
export function frontierSpot(state: SimState, taken: Set<string>): Point | null {
  if (state.survivors.length === 0 || !state.grid.explored.includes(0)) return null;
  for (let attempt = 0; attempt < 8; attempt++) {
    const s = pick(state, state.survivors);
    const near = {
      x: Math.max(0, Math.min(state.grid.width - 1, s.pos.x + randomInt(state, -ARRIVAL_DISTANCE, ARRIVAL_DISTANCE + 1))),
      y: Math.max(0, Math.min(state.grid.height - 1, s.pos.y + randomInt(state, -ARRIVAL_DISTANCE, ARRIVAL_DISTANCE + 1))),
    };
    const ok = (x: number, y: number) =>
      isExplored(state, x, y) &&
      getTile(state.grid, x, y) === 'floor' &&
      labelAt(state, x, y) === OUTDOORS &&
      !taken.has(`${x},${y}`) &&
      DIRS.some((d) => inBounds(state.grid, x + d.x, y + d.y) && !isExplored(state, x + d.x, y + d.y));
    const p = nearestTile(state.grid, near, ok);
    if (p) return p;
  }
  return null;
}

function arrive(state: SimState): void {
  const taken0 = new Set(state.wanderers.map((w) => `${w.x},${w.y}`));
  const spot = frontierSpot(state, taken0);
  if (spot) {
    const w = addWanderer(state, spot.x, spot.y);
    logEvent(state, `${w.name}, a wanderer, is waiting at (${spot.x}, ${spot.y}): take them in?`);
    return;
  }
  // A fully explored map (a scenario): they come in from the edge.
  const { width, height } = state.grid;
  const edge: Point[] = [];
  for (let x = 0; x < width; x++) edge.push({ x, y: 0 }, { x, y: height - 1 });
  for (let y = 1; y < height - 1; y++) edge.push({ x: 0, y }, { x: width - 1, y });
  const taken = new Set(state.wanderers.map((w) => `${w.x},${w.y}`));
  const candidates = edge.filter(
    (p) => getTile(state.grid, p.x, p.y) === 'floor' && !taken.has(`${p.x},${p.y}`),
  );
  if (candidates.length === 0) return;
  const p = pick(state, candidates);
  const w = addWanderer(state, p.x, p.y);
  logEvent(state, `${w.name}, a wanderer, is waiting at (${p.x}, ${p.y}): take them in?`);
}

/** Ticks left before a wanderer gives up (for the UI). */
export function patienceLeft(state: SimState, w: Wanderer): number {
  return Math.max(0, Math.min(w.leavesTick - state.tick, DAY_LENGTH));
}
