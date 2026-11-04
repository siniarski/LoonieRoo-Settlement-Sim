// Wild animals to hunt: deer.
//
// Most mornings (a hash of the day) a deer wanders in somewhere at the edge
// of explored ground, browses about (a hash step every DEER_STEP_EVERY ticks,
// on open floor), and leaves after DEER_STAYS. The Hunt order marks deer in a
// box; each marked deer gets one hunt job (jobs.ts): get next to it, take it
// down (HUNT_WORK, noisy), and it leaves VENISON food where it fell.

import { placedAt } from './motion';
import { logEvent } from './events';
import { getTile, neighbours4, type Point } from './grid';
import { hashRoll, idNumber } from './hash';
import { manhattan } from './space';
import type { SimState } from './state';
import type { Movement } from './survivors';
import { DAY_LENGTH, TICKS_PER_HOUR, dayOf, tickOfDay, onClock } from './time';
import { frontierSpot } from './wanderers';

export interface Animal {
  id: string;
  kind: 'deer';
  pos: Point;
  /** Where exactly, in motion.ts SUB units (pos is the tile under it); and where at the tick's start. */
  at: Point;
  was: Point;
  move: Movement | null;
  /** Marked by the Hunt order. */
  hunted: boolean;
  leavesTick: number;
}

export const DEER_CHANCE = 0.6;
export let DEER_STAYS = 2 * DAY_LENGTH;
onClock(() => (DEER_STAYS = 2 * DAY_LENGTH));
export const DEER_STEP_EVERY = 20;
export const DEER_TICKS_PER_TILE = 5;
/** Deer keep this far from survivors when they can (they're wary, not fast). */
export const DEER_WARY = 3;
export const HUNT_WORK = 20;
export const VENISON = 3;

export function getAnimal(state: SimState, id: string): Animal | undefined {
  return state.animals.find((a) => a.id === id);
}

export function addDeer(state: SimState, x: number, y: number): Animal {
  const a: Animal = { id: `d${state.nextId++}`, kind: 'deer', pos: { x, y }, ...placedAt({ x, y }), move: null, hunted: false, leavesTick: state.tick + DEER_STAYS };
  state.animals.push(a);
  return a;
}

export function updateAnimals(state: SimState): void {
  const day = dayOf(state.tick);
  if (tickOfDay(state.tick) === 2 * TICKS_PER_HOUR && hashRoll(day, 0xdee7) < DEER_CHANCE && state.survivors.length > 0) {
    const spot = frontierSpot(state, new Set());
    if (spot) {
      addDeer(state, spot.x, spot.y);
      logEvent(state, `Deer seen at (${spot.x}, ${spot.y}) (the Hunt order marks them)`);
    }
  }
  for (const a of [...state.animals]) {
    if (state.tick >= a.leavesTick) {
      state.animals.splice(state.animals.indexOf(a), 1);
      continue;
    }
    if (a.move) {
      a.move.progress += 1;
      if (a.move.progress < a.move.duration) continue;
      a.pos = a.move.to;
      a.move = null;
    }
    if (state.tick % DEER_STEP_EVERY !== 0) continue;
    const options = neighbours4(state.grid, a.pos.x, a.pos.y).filter((p) => getTile(state.grid, p.x, p.y) === 'floor');
    if (options.length === 0) continue;
    // Away from anyone close; otherwise a browsing step (a hash, not the sim RNG).
    const near = state.survivors.find((s) => manhattan(s.pos, a.pos) <= DEER_WARY);
    const to = near
      ? options.sort((p, q) => manhattan(q, near.pos) - manhattan(p, near.pos))[0]!
      : options[Math.floor(hashRoll(idNumber(a.id), state.tick) * options.length)]!;
    a.move = { from: { ...a.pos }, to, progress: 0, duration: DEER_TICKS_PER_TILE };
  }
}
