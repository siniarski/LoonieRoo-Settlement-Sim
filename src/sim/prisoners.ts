// Prisoners. A raider a guard brings down may be taken alive (half the time,
// by a hash) if there's a complete cell room with a free spot: they're taken
// to it. Each dawn every prisoner eats a free food from the stores; with none,
// or with the cell no longer complete, they get away. After RECRUIT_DAYS
// held, a prisoner can be talked round (recruitPrisoner: they join, and their
// settlement thinks less of you), or let go home (releasePrisoner: their
// settlement thinks better of you).

import { logEvent } from './events';
import { isWalkable, type Point } from './grid';
import { hashRoll, idNumber } from './hash';
import { furnitureAt } from './furniture';
import { removeItem } from './items';
import { completeRooms, getRoomMap, labelAt } from './rooms';
import { adjustReputation, getSettlement } from './settlements';
import type { SimState } from './state';
import { addSurvivor, nextSurvivorName } from './survivors';
import { DAY_LENGTH } from './time';
import type { Zombie } from './zombies';

export interface Prisoner {
  id: string;
  name: string;
  x: number;
  y: number;
  /** The settlement they raided for, if known. */
  from: string | null;
  since: number;
}

export const CAPTURE_CHANCE = 0.5;
export const RECRUIT_DAYS = 2;
export const RECRUIT_REP = -10;
export const RELEASE_REP = 15;

function freeCellSpot(state: SimState): Point | null {
  const cells = completeRooms(state, 'cell');
  const { rooms } = getRoomMap(state);
  const { width } = state.grid;
  for (const index of [...cells].sort((a, b) => a - b)) {
    for (const i of rooms[index]!.tiles) {
      const p = { x: i % width, y: Math.floor(i / width) };
      if (isWalkable(state.grid, p.x, p.y) && !furnitureAt(state, p.x, p.y) && !state.prisoners.some((q) => q.x === p.x && q.y === p.y)) return p;
    }
  }
  return null;
}

/** A raider brought down: maybe taken alive. Returns true if so (the caller removes the raider). */
export function maybeCapture(state: SimState, z: Zombie, by: string): boolean {
  if (z.kind !== 'raider' || hashRoll(idNumber(z.id), 0xca9) >= CAPTURE_CHANCE) return false;
  const spot = freeCellSpot(state);
  if (!spot) return false;
  const from = state.settlements.find((s) => s.reputation < 0) ?? null;
  const p: Prisoner = { id: `q${state.nextId++}`, name: nextSurvivorName(state), x: spot.x, y: spot.y, from: from?.id ?? null, since: state.tick };
  state.prisoners.push(p);
  logEvent(state, `${by} took a raider alive: ${p.name} is in the cells`);
  return true;
}

/** Dawn: feed them or lose them. */
export function updatePrisoners(state: SimState): void {
  if (state.prisoners.length === 0 || state.tick % DAY_LENGTH !== 0) return;
  const cells = completeRooms(state, 'cell');
  for (const p of [...state.prisoners]) {
    const food = state.items.find((i) => i.kind === 'food' && i.pos !== null && i.reservedBy === null);
    if (!cells.has(labelAt(state, p.x, p.y)) || !food) {
      state.prisoners.splice(state.prisoners.indexOf(p), 1);
      logEvent(state, `${p.name} got away (${food ? 'the cell is open' : 'there was nothing to eat'})`);
      continue;
    }
    removeItem(state, food);
  }
}

/** Talk them round (joins), or let them go home. Returns an error, or null. */
export function decidePrisoner(state: SimState, id: string, recruit: boolean): string | null {
  const p = state.prisoners.find((q) => q.id === id);
  if (!p) return 'no such prisoner';
  if (recruit && state.tick - p.since < RECRUIT_DAYS * DAY_LENGTH) return `${p.name} isn't ready to join yet`;
  state.prisoners.splice(state.prisoners.indexOf(p), 1);
  const home = getSettlement(state, p.from);
  if (recruit) {
    addSurvivor(state, p.x, p.y, p.name);
    if (home) adjustReputation(state, home, RECRUIT_REP);
    logEvent(state, `${p.name} has thrown in with you`);
  } else {
    if (home) adjustReputation(state, home, RELEASE_REP);
    logEvent(state, `${p.name} was let go${home ? ` (${home.name} thinks better of you)` : ''}`);
  }
  return null;
}
