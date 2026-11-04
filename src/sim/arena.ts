// Debug arenas, from the New game screen: a small solo world with zombies already near you,
// static ones (they don't leave at dawn) so a fight is there to be had from the first tick.
//   one     a single zombie a few tiles off
//   crowd   CROWD_SIZE of them in a ring around the camp
// A plain world otherwise (settings: NORMAL, alone, normal days), so what you test is the game;
// but no neighbours (on a map this small their compound would unfold right beside you).

import { NORMAL } from './difficulty';
import { getTile, isWalkable, type Point } from './grid';
import type { SimState } from './state';
import { createWorld } from './worldgen';
import { addZombie } from './zombies';

export type Arena = 'one' | 'crowd';
export const CROWD_SIZE = 30;
const ARENA_SIZE = 64;

export function createArena(seed: number, kind: Arena): SimState {
  const state = createWorld(seed, ARENA_SIZE, { ...NORMAL, start: 'solo' });
  state.settlements = [];
  const you = state.survivors[0]!.pos;
  const count = kind === 'one' ? 1 : CROWD_SIZE;
  const radius = kind === 'one' ? 5 : 8;
  for (let i = 0; i < count; i++) {
    const a = (i / count) * Math.PI * 2;
    const spot = openNear(state, { x: Math.round(you.x + Math.cos(a) * radius), y: Math.round(you.y + Math.sin(a) * radius) });
    if (spot) addZombie(state, spot.x, spot.y, undefined, 'static');
  }
  return state;
}

/** The nearest open floor tile to a point, looking outward a few rings. */
function openNear(state: SimState, p: Point): Point | null {
  for (let r = 0; r <= 4; r++) {
    for (let dy = -r; dy <= r; dy++) {
      for (let dx = -r; dx <= r; dx++) {
        const x = p.x + dx;
        const y = p.y + dy;
        if (getTile(state.grid, x, y) === 'floor' && isWalkable(state.grid, x, y)) return { x, y };
      }
    }
  }
  return null;
}
