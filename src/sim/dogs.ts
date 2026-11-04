// Dogs. One sometimes comes back with an expedition, and strays wander in now
// and then (a hash of the day, not the sim RNG). A dog stays near the
// colony, following whoever is nearest. When a zombie comes within
// DOG_SENSE it barks (once in a while: it wakes the guards) and goes for it,
// biting for DOG_BITE. Zombies with nobody else to hit go for the dog.
// Being near a dog cheers people up.

import { inReach } from './motion';
import { placedAt } from './motion';
import { logEvent } from './events';
import { neighbours4, type Point } from './grid';
import { hashRoll } from './hash';
import { addThought } from './mood';
import { isGuard } from './medicine';
import { wake } from './needs';
import { findPathToAny } from './pathfinding';
import { manhattan, samePoint } from './space';
import type { SimState } from './state';
import type { Movement } from './survivors';
import { dayOf, tickOfDay, TICKS_PER_HOUR } from './time';
import { leaveCorpse } from './corpses';
import { removeZombie, type Zombie } from './zombies';
import { frontierSpot } from './wanderers';

export interface Dog {
  id: string;
  name: string;
  pos: Point;
  /** Where exactly, in motion.ts SUB units (pos is the tile under it); and where at the tick's start. */
  at: Point;
  was: Point;
  move: Movement | null;
  path: Point[];
  health: number;
  cooldown: number;
  nextThinkTick: number;
  /** Last time it barked (so it isn't every tick). */
  barkedTick: number;
  /** A lost pet sitting at its old home until someone comes for it (talk.ts). */
  waiting: boolean;
}

export const DOG_HEALTH = 300;
export const DOG_SENSE = 10;
export const DOG_BITE = 40;
export const DOG_BITE_EVERY = 10;
export const DOG_TICKS_PER_TILE = 3;
export const DOG_FOLLOW = 3;
export const BARK_EVERY = 400;
export const PET_EVERY = 200;
/** Each day at noon, the chance a stray turns up. */
export const STRAY_CHANCE = 0.12;
/** The chance an expedition comes home with a dog. */
export const EXPEDITION_DOG_CHANCE = 0.25;
const DOG_NAMES = ['Rex', 'Bess', 'Scout', 'Moss', 'Patch', 'Juno', 'Biscuit', 'Tank'];

export function addDog(state: SimState, x: number, y: number): Dog {
  const n = state.nextId++;
  const dog: Dog = { id: `g${n}`, name: DOG_NAMES[n % DOG_NAMES.length]!, pos: { x, y }, ...placedAt({ x, y }), move: null, path: [], health: DOG_HEALTH, cooldown: 0, nextThinkTick: 0, barkedTick: -BARK_EVERY, waiting: false };
  state.dogs.push(dog);
  return dog;
}

export function updateDogs(state: SimState): void {
  const day = dayOf(state.tick);
  if (day >= 2 && tickOfDay(state.tick) === 6 * TICKS_PER_HOUR && hashRoll(day, 0xd06) < STRAY_CHANCE && state.survivors.length > 0) {
    const spot = frontierSpot(state, new Set()) ?? state.survivors[0]!.pos;
    const dog = addDog(state, spot.x, spot.y);
    logEvent(state, `A stray dog, ${dog.name}, has wandered in at (${spot.x}, ${spot.y})`);
  }
  for (const dog of state.dogs) updateDog(state, dog);
  if (state.tick % PET_EVERY === 0 && state.dogs.length > 0) {
    for (const s of state.survivors) if (!s.sleep?.asleep && state.dogs.some((d) => manhattan(d.pos, s.pos) <= DOG_FOLLOW)) addThought(state, s, 'dog');
  }
}

export function getDogById(state: SimState, id: string): Dog | undefined {
  return state.dogs.find((d) => d.id === id);
}

function updateDog(state: SimState, dog: Dog): void {
  dog.cooldown = Math.max(0, dog.cooldown - 1);
  if (dog.waiting) {
    const found = state.survivors.find((s) => manhattan(s.pos, dog.pos) <= 2);
    if (!found) return;
    dog.waiting = false;
    logEvent(state, `${found.name} found ${dog.name}, waiting at the house`);
  }
  if (dog.move) {
    dog.move.progress += 1;
    if (dog.move.progress < dog.move.duration) return;
    dog.pos = dog.move.to;
    dog.move = null;
  }
  const zombie = state.zombies
    .filter((z) => manhattan(z.pos, dog.pos) <= DOG_SENSE)
    .sort((a, b) => manhattan(a.pos, dog.pos) - manhattan(b.pos, dog.pos))[0];
  if (zombie && state.tick - dog.barkedTick >= BARK_EVERY) bark(state, dog, zombie);
  if (zombie && inReach(zombie, dog)) {
    if (dog.cooldown === 0) bite(state, dog, zombie);
    return;
  }
  if (state.tick >= dog.nextThinkTick || dog.path.length === 0) {
    dog.nextThinkTick = state.tick + 15;
    dog.path = plan(state, dog, zombie);
  }
  const next = dog.path.shift();
  if (next) dog.move = { from: { ...dog.pos }, to: next, progress: 0, duration: DOG_TICKS_PER_TILE };
}

/** Towards a zombie if there is one, else to the nearest survivor if they're getting away. */
function plan(state: SimState, dog: Dog, zombie: Zombie | undefined): Point[] {
  if (zombie) return findPathToAny(state.grid, dog.pos, neighbours4(state.grid, zombie.pos.x, zombie.pos.y)) ?? [];
  const owner = state.survivors.slice().sort((a, b) => manhattan(a.pos, dog.pos) - manhattan(b.pos, dog.pos))[0];
  if (!owner || manhattan(owner.pos, dog.pos) <= DOG_FOLLOW) return [];
  const path = findPathToAny(state.grid, dog.pos, neighbours4(state.grid, owner.pos.x, owner.pos.y)) ?? [];
  return path.slice(0, 15);
}

function bark(state: SimState, dog: Dog, z: Zombie): void {
  dog.barkedTick = state.tick;
  let woke = 0;
  for (const s of state.survivors) {
    if (!s.sleep?.asleep || !isGuard(s) || manhattan(s.pos, dog.pos) > 2 * DOG_SENSE) continue;
    wake(state, s, `woken by ${dog.name}'s barking`);
    woke += 1;
  }
  logEvent(state, `${dog.name} is barking: something's coming near (${z.pos.x}, ${z.pos.y})${woke ? `; ${woke} guard${woke === 1 ? '' : 's'} up` : ''}`);
}

function bite(state: SimState, dog: Dog, z: Zombie): void {
  dog.cooldown = DOG_BITE_EVERY;
  z.health = Math.max(0, z.health - DOG_BITE);
  if (z.health > 0) return;
  removeZombie(state, z);
  leaveCorpse(state, z.pos, null);
  logEvent(state, `${dog.name} brought down a zombie`);
}

/** A zombie's blow on a dog. Returns true if the dog died. */
export function hitDog(state: SimState, dog: Dog, damage: number): boolean {
  dog.health = Math.max(0, dog.health - damage);
  if (dog.health > 0) return false;
  state.dogs.splice(state.dogs.indexOf(dog), 1);
  logEvent(state, `${dog.name} the dog was killed`);
  return true;
}

export function dogAt(state: SimState, p: Point): Dog | undefined {
  return state.dogs.find((d) => samePoint(d.pos, p));
}
