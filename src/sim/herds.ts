// The zombies that live out in the world, as opposed to the night's arrivals.
//
// Built the "simulation LOD" way, so a far bigger world costs nothing extra:
//  - Dormant zombies are just positions (state.dormant), seeded in ruins by
//    worldgen. They become real zombies only when a survivor comes within
//    WAKE_RADIUS, and then hunt only survivors within AGGRO_RADIUS (zombies.ts).
//  - Herds are single markers crossing the map a tile every
//    HERD_TICKS_PER_TILE, ignoring terrain. One that comes within
//    MATERIALIZE_RADIUS of a survivor breaks up into real zombies that keep
//    heading for the herd's destination unless someone is close.
// Neither leaves at dawn.

import { logEvent } from './events';
import { getTile, inBounds, type Point } from './grid';
import { pick, random } from './rng';
import { OUTDOORS, labelAt } from './rooms';
import { manhattan, nearestTile } from './space';
import type { SimState } from './state';
import { TICKS_PER_HOUR, dayOf, tickOfDay } from './time';
import { MAX_ZOMBIES, addZombie, zombieHealthForNight } from './zombies';

export const WAKE_RADIUS = 10;
export const HERD_TICKS_PER_TILE = 8;
export const MATERIALIZE_RADIUS = 24;
/** Each day from day 2, at this hour, a herd may set out (HERD_CHANCE), and some head past the colony. */
export const HERD_HOUR = 8;
export const HERD_CHANCE = 0.6;
export const HERD_TOWARD_COLONY = 0.4;

export interface DormantZombie {
  id: string;
  x: number;
  y: number;
}

export interface Herd {
  id: string;
  x: number;
  y: number;
  /** Where it's going: a point on the far edge. */
  to: Point;
  size: number;
  nextStepTick: number;
}

export function herdSize(day: number): number {
  return 3 + Math.floor(day / 2);
}

export function updateHerds(state: SimState): void {
  if (dayOf(state.tick) >= 2 && tickOfDay(state.tick) === HERD_HOUR * TICKS_PER_HOUR && random(state) < HERD_CHANCE) startHerd(state);
  for (const h of [...state.herds]) {
    if (state.survivors.some((s) => manhattan(s.pos, h) <= MATERIALIZE_RADIUS)) {
      materialize(state, h);
      continue;
    }
    if (state.tick < h.nextStepTick) continue;
    h.nextStepTick = state.tick + HERD_TICKS_PER_TILE;
    // Straight-ish line: step along the longer axis first.
    const dx = h.to.x - h.x;
    const dy = h.to.y - h.y;
    if (dx === 0 && dy === 0) {
      state.herds.splice(state.herds.indexOf(h), 1);
      continue;
    }
    if (Math.abs(dx) >= Math.abs(dy)) h.x += Math.sign(dx);
    else h.y += Math.sign(dy);
  }
  if (state.tick % 10 === 0) wakeDormant(state);
}

function startHerd(state: SimState): void {
  const { width, height } = state.grid;
  const edge: Point[] = [];
  for (let x = 0; x < width; x += 4) edge.push({ x, y: 0 }, { x, y: height - 1 });
  for (let y = 4; y < height - 1; y += 4) edge.push({ x: 0, y }, { x: width - 1, y });
  const from = pick(state, edge);
  let to: Point;
  if (state.survivors.length > 0 && random(state) < HERD_TOWARD_COLONY) {
    // Through the colony: aim at its middle and carry on to the far side.
    const cx = Math.round(state.survivors.reduce((a, s) => a + s.pos.x, 0) / state.survivors.length);
    const cy = Math.round(state.survivors.reduce((a, s) => a + s.pos.y, 0) / state.survivors.length);
    to = { x: clamp(2 * cx - from.x, width), y: clamp(2 * cy - from.y, height) };
  } else {
    to = { x: width - 1 - from.x, y: height - 1 - from.y };
  }
  const size = herdSize(dayOf(state.tick));
  state.herds.push({ id: `h${state.nextId++}`, x: from.x, y: from.y, to, size, nextStepTick: state.tick });
  logEvent(state, `A herd of about ${size} is on the move somewhere out there`);
}

const clamp = (v: number, size: number) => Math.max(0, Math.min(size - 1, v));

/** A herd near the colony becomes real zombies on open ground round it, heading where it was going. */
function materialize(state: SimState, h: Herd): void {
  state.herds.splice(state.herds.indexOf(h), 1);
  const n = Math.min(h.size, MAX_ZOMBIES - state.zombies.length);
  const free = (x: number, y: number) =>
    getTile(state.grid, x, y) === 'floor' && labelAt(state, x, y) === OUTDOORS && !state.zombies.some((z) => z.pos.x === x && z.pos.y === y);
  for (let i = 0; i < n; i++) {
    const p = nearestTile(state.grid, { x: h.x, y: h.y }, free);
    if (!p) break;
    const z = addZombie(state, p.x, p.y, zombieHealthForNight(dayOf(state.tick)), 'herd');
    z.heading = { ...h.to };
  }
  if (n > 0) logEvent(state, `A herd of ${n} zombies is passing near (${h.x}, ${h.y})`);
}

/** Dormant zombies near a survivor get up. */
function wakeDormant(state: SimState): void {
  let woke = 0;
  let where: Point | null = null;
  for (const d of [...state.dormant]) {
    if (!state.survivors.some((s) => manhattan(s.pos, d) <= WAKE_RADIUS)) continue;
    if (state.zombies.length >= MAX_ZOMBIES) break;
    state.dormant.splice(state.dormant.indexOf(d), 1);
    const p = getTile(state.grid, d.x, d.y) === 'floor' ? d : nearestTile(state.grid, d, (x, y) => getTile(state.grid, x, y) === 'floor');
    if (!p || !inBounds(state.grid, p.x, p.y)) continue;
    addZombie(state, p.x, p.y, zombieHealthForNight(dayOf(state.tick)), 'static');
    woke += 1;
    where ??= { x: p.x, y: p.y };
  }
  if (where) logEvent(state, woke === 1 ? `A zombie stirs at (${where.x}, ${where.y})` : `${woke} zombies stir near (${where.x}, ${where.y})`);
}
