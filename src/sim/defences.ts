// Defences beyond walls, and the dark.
//
// Reinforcing: a wall, door or barricade can be reinforced with scrap (a
// blueprint like any other, delivering scrap instead of wood). Reinforced
// structures take REINFORCE_FACTOR times the battering and don't burn.
//
// Turrets: a gun emplacement fed ammo by haulers (fuel jobs, jobs.ts). Every
// TURRET_EVERY ticks it shoots the nearest zombie it can see within
// TURRET_RANGE with a clear line, loudly.
//
// Lures: a noisemaker that rattles every hour at night (noise.ts), drawing
// the world's zombies and herds to it, away from where they'd otherwise go.
//
// Pits: a zombie that steps in one is stuck for PIT_TICKS (no moving, no
// hitting). Pits don't wear out, and survivors step round them.
//
// Darkness (harsh games): at night a zombie can only be fought if someone can see it: a
// survivor within NIGHT_SIGHT (more on a tower or vigilant), a lit lamp's
// pool, a fire's glow, or a dog's nose.

import { logEvent } from './events';
import { furnitureAt } from './furniture';
import { tileIndex, type Point } from './grid';
import { clearShot } from './jobs';
import { leaveCorpse } from './corpses';
import { maybeSample } from './endgame';
import { makeNoise } from './noise';
import { LAMP_LIGHT, litLamps } from './power';
import { hasTrait, VIGILANT_SIGHT } from './skills';
import { manhattan } from './space';
import type { SimState } from './state';
import { TICKS_PER_HOUR, isNight } from './time';
import { onTower } from './towers';
import { AMMO_ROUNDS } from './weapons';
import { DOG_SENSE } from './dogs';
import { removeZombie, type Zombie } from './zombies';

export const REINFORCE_FACTOR = 2.5;
export const TURRET_RANGE = 7;
export const TURRET_EVERY = 20;
export const TURRET_DAMAGE = 100;
export const TURRET_AMMO_MAX = 2;
export const PIT_TICKS = 100;
export const NIGHT_SIGHT = 8;
export const TOWER_NIGHT_SIGHT = 10;

export function isReinforced(state: SimState, x: number, y: number): boolean {
  return state.grid.reinforced.includes(tileIndex(state.grid, x, y));
}

/** Can someone see this point? Always by day. */
export function seen(state: SimState, p: Point): boolean {
  if (!state.harsh || !isNight(state.tick)) return true; // (a harsh-game rule)
  for (const s of state.survivors) {
    const sight = (onTower(state, s) ? TOWER_NIGHT_SIGHT : NIGHT_SIGHT) + (hasTrait(s, 'vigilant') ? VIGILANT_SIGHT : 0);
    if (manhattan(s.pos, p) <= sight) return true;
  }
  if (litLamps(state).some((l) => manhattan(l, p) <= LAMP_LIGHT + 1)) return true;
  if (state.fires.some((f) => manhattan(f, p) <= 3)) return true;
  return state.dogs.some((d) => manhattan(d.pos, p) <= DOG_SENSE);
}

/** Turrets shoot; lures rattle. Every tick. */
export function updateDefences(state: SimState): void {
  if (state.tick % TICKS_PER_HOUR === 0 && isNight(state.tick)) for (const f of state.furniture) if (f.kind === 'lure') makeNoise(state, f, 'lure');
  if (state.tick % TURRET_EVERY !== 0 || state.zombies.length === 0) return;
  for (const t of state.furniture) {
    if (t.kind !== 'turret' || (t.loot === 0 && t.stored === 0)) continue;
    const z = state.zombies
      .filter((q) => manhattan(q.pos, t) <= TURRET_RANGE && seen(state, q.pos) && clearShot(state.grid, t, q.pos))
      .sort((a, b) => manhattan(a.pos, t) - manhattan(b.pos, t))[0];
    if (!z) continue;
    if (t.loot === 0) {
      t.stored -= 1;
      if (t.stored === 0) t.storedKind = null;
      t.loot = AMMO_ROUNDS;
    }
    t.loot -= 1;
    makeNoise(state, t, 'gunfire');
    z.health = Math.max(0, z.health - TURRET_DAMAGE);
    if (z.health > 0) continue;
    removeZombie(state, z);
    leaveCorpse(state, z.pos, null);
    maybeSample(state, z.id, z.pos);
    logEvent(state, `The turret at (${t.x}, ${t.y}) brought down a zombie`);
  }
}

/** A zombie that just stepped onto a tile: a pit holds it. */
export function fallInPit(state: SimState, z: Zombie): void {
  if (furnitureAt(state, z.pos.x, z.pos.y)?.kind !== 'pit') return;
  z.stuckUntil = state.tick + PIT_TICKS;
  z.path = [];
}
