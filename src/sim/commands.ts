// The only way to change the sim from outside (architecture rule 5).
// Every accepted command that changed state is appended to state.commandLog
// for replays. No-ops (painting a wall over a wall while dragging) are not
// logged, since replaying them would change nothing.

import { inBounds, setTile, type Tile } from './grid';
import type { SimState } from './state';

export type Command =
  | { type: 'placeWall'; x: number; y: number }
  | { type: 'placeDoor'; x: number; y: number }
  | { type: 'clearTile'; x: number; y: number };

export type CommandResult = { ok: true; changed: boolean } | { ok: false; reason: string };

const TILE_FOR_COMMAND: Record<Command['type'], Tile> = {
  placeWall: 'wall',
  placeDoor: 'door',
  clearTile: 'floor',
};

export function applyCommand(state: SimState, command: Command): CommandResult {
  const result = execute(state, command);
  if (result.ok && result.changed) {
    state.commandLog.push({ tick: state.tick, command: { ...command } });
  }
  return result;
}

function execute(state: SimState, command: Command): CommandResult {
  switch (command.type) {
    case 'placeWall':
    case 'placeDoor':
    case 'clearTile':
      return setStructure(state, command.x, command.y, TILE_FOR_COMMAND[command.type]);
  }
}

function setStructure(state: SimState, x: number, y: number, tile: Tile): CommandResult {
  if (!inBounds(state.grid, x, y)) {
    return { ok: false, reason: `(${x}, ${y}) is out of bounds` };
  }
  const changed = setTile(state.grid, x, y, tile);
  if (changed) {
    // Any wall/door/floor change can alter walkability or room boundaries.
    state.walkabilityVersion += 1;
  }
  return { ok: true, changed };
}
