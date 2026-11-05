// Noise: building, fighting and gunfire carry.
//
// A noise of radius r (Manhattan) at a point wakes the dormant zombies within
// r, draws idle static and herd zombies within r to come and look, and turns
// any herd within HERD_HEARING x r towards it (on through the point and out
// the far side). Nothing is stored: a noise happens and its effects stay.

import { logEvent } from './events';
import { getTile, inBounds, type Point } from './grid';
import { manhattan, nearestTile } from './space';
import type { SimState } from './state';
import { dayOf } from './time';
import { MAX_ZOMBIES, addZombie, zombieHealthForNight } from './zombies';
import { giveVariant } from './zombieKinds';

/** How far each kind of noise carries. */
export const NOISE = { building: 10, fighting: 6, gunfire: 30, generator: 12, scream: 30, lure: 25, glass: 15 } as const;
export type NoiseKind = keyof typeof NOISE;
/** Herds hear from this many times further than a noise's radius. */
export const HERD_HEARING = 2;

export function makeNoise(state: SimState, p: Point, kind: NoiseKind): void {
  const r = NOISE[kind];
  const woke = wakeDormantNear(state, (d) => manhattan(d, p) <= r);
  if (woke > 0) logEvent(state, `The ${kind} at (${p.x}, ${p.y}) woke ${woke === 1 ? 'a zombie' : `${woke} zombies`}`);
  const { width, height } = state.grid;
  for (const z of state.zombies) {
    if ((z.kind !== 'static' && z.kind !== 'herd') || z.targetId !== null || manhattan(z.pos, p) > r) continue;
    // Static zombies come to look (and stop there); herd zombies swing through it and on.
    z.heading = z.kind === 'static' ? { x: p.x, y: p.y } : { x: clamp(2 * p.x - z.pos.x, width), y: clamp(2 * p.y - z.pos.y, height) };
    z.path = [];
  }
  for (const h of state.herds) {
    if (manhattan(h, p) > r * HERD_HEARING) continue;
    const to = { x: clamp(2 * p.x - h.x, width), y: clamp(2 * p.y - h.y, height) };
    if (to.x === h.to.x && to.y === h.to.y) continue;
    h.to = to;
    logEvent(state, `A herd of ${h.size} turned towards the ${kind} at (${p.x}, ${p.y})`);
  }
}

const clamp = (v: number, size: number) => Math.max(0, Math.min(size - 1, v));

/** Dormant zombies matching `near` get up as static zombies. Returns how many. */
export function wakeDormantNear(state: SimState, near: (d: Point) => boolean): number {
  let woke = 0;
  for (const d of [...state.dormant]) {
    if (!near(d)) continue;
    if (state.zombies.length >= MAX_ZOMBIES) break;
    state.dormant.splice(state.dormant.indexOf(d), 1);
    const p = getTile(state.grid, d.x, d.y) === 'floor' ? d : nearestTile(state.grid, d, (x, y) => getTile(state.grid, x, y) === 'floor');
    if (!p || !inBounds(state.grid, p.x, p.y)) continue;
    giveVariant(addZombie(state, p.x, p.y, zombieHealthForNight(dayOf(state.tick)), 'static'), dayOf(state.tick));
    woke += 1;
  }
  return woke;
}
