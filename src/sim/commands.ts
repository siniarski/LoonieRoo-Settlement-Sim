// The only way to change the sim from outside (architecture rule 5).
// Every accepted command that changed state is appended to state.commandLog
// for replays. No-ops (painting a wall over a wall while dragging) are not
// logged, since replaying them would change nothing.

import { inBounds, isWalkable, setTile, type Tile } from './grid';
import type { SimState } from './state';
import { addSurvivor, getSurvivor, setGoal, survivorAt } from './survivors';

export type Command =
  | { type: 'placeWall'; x: number; y: number }
  | { type: 'placeDoor'; x: number; y: number }
  | { type: 'clearTile'; x: number; y: number }
  | { type: 'spawnSurvivor'; x: number; y: number }
  | { type: 'moveSurvivor'; survivorId: string; x: number; y: number };

export type CommandResult = { ok: true; changed: boolean } | { ok: false; reason: string };

export function applyCommand(state: SimState, command: Command): CommandResult {
  const result = execute(state, command);
  if (result.ok && result.changed) {
    state.commandLog.push({ tick: state.tick, command: { ...command } });
  }
  return result;
}

function execute(state: SimState, command: Command): CommandResult {
  if (!inBounds(state.grid, command.x, command.y)) {
    return reject(`(${command.x}, ${command.y}) is out of bounds`);
  }
  switch (command.type) {
    case 'placeWall':
      if (survivorAt(state, command.x, command.y)) {
        return reject(`(${command.x}, ${command.y}) is occupied`);
      }
      return setStructure(state, command.x, command.y, 'wall');
    case 'placeDoor':
      return setStructure(state, command.x, command.y, 'door');
    case 'clearTile':
      return setStructure(state, command.x, command.y, 'floor');
    case 'spawnSurvivor':
      return spawnSurvivor(state, command.x, command.y);
    case 'moveSurvivor':
      return moveSurvivor(state, command.survivorId, command.x, command.y);
  }
}

function reject(reason: string): CommandResult {
  return { ok: false, reason };
}

function setStructure(state: SimState, x: number, y: number, tile: Tile): CommandResult {
  const changed = setTile(state.grid, x, y, tile);
  if (changed) {
    // Any wall/door/floor change can alter walkability or room boundaries.
    state.walkabilityVersion += 1;
  }
  return { ok: true, changed };
}

function spawnSurvivor(state: SimState, x: number, y: number): CommandResult {
  if (!isWalkable(state.grid, x, y)) return reject(`(${x}, ${y}) is not walkable`);
  if (survivorAt(state, x, y)) return reject(`(${x}, ${y}) is occupied`);
  addSurvivor(state, x, y);
  return { ok: true, changed: true };
}

function moveSurvivor(state: SimState, id: string, x: number, y: number): CommandResult {
  const survivor = getSurvivor(state, id);
  if (!survivor) return reject(`no survivor ${id}`);
  if (!isWalkable(state.grid, x, y)) return reject(`(${x}, ${y}) is not walkable`);
  if (survivor.goal?.x === x && survivor.goal.y === y) return { ok: true, changed: false };
  setGoal(survivor, { x, y });
  return { ok: true, changed: true };
}
