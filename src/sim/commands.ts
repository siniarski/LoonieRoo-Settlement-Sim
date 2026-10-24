// The only way to change the sim from outside (architecture rule 5).
// Every accepted command that changed state is appended to state.commandLog
// for replays. No-ops (painting a wall over a wall while dragging) are not
// logged, since replaying them would change nothing.

import { addBlueprint, blueprintAt, cancelBlueprint, evictFrom } from './designations';
import { getTile, inBounds, isWalkable, setTile, type Tile } from './grid';
import { releaseSurvivor } from './jobs';
import { isJobType, isPriority, type JobType, type Priority } from './jobTypes';
import type { SimState } from './state';
import { addSurvivor, getSurvivor, setGoal, survivorAt } from './survivors';

export type Command =
  // Instant edits (debug, scenarios, Shift+paint).
  | { type: 'placeWall'; x: number; y: number }
  | { type: 'placeDoor'; x: number; y: number }
  | { type: 'clearTile'; x: number; y: number }
  // Designations: become jobs for survivors.
  | { type: 'designateBuild'; x: number; y: number; structure: 'wall' | 'door' }
  | { type: 'designateRemove'; x: number; y: number }
  | { type: 'cancelDesignation'; x: number; y: number }
  // Survivors.
  | { type: 'spawnSurvivor'; x: number; y: number }
  | { type: 'moveSurvivor'; survivorId: string; x: number; y: number }
  | { type: 'setPriority'; survivorId: string; jobType: JobType; priority: Priority };

export type CommandResult = { ok: true; changed: boolean } | { ok: false; reason: string };

export function applyCommand(state: SimState, command: Command): CommandResult {
  const result = execute(state, command);
  if (result.ok && result.changed) {
    state.commandLog.push({ tick: state.tick, command: { ...command } });
  }
  return result;
}

function execute(state: SimState, command: Command): CommandResult {
  if (command.type === 'setPriority') return setPriority(state, command.survivorId, command.jobType, command.priority);
  const { x, y } = command;
  if (!inBounds(state.grid, x, y)) return reject(`(${x}, ${y}) is out of bounds`);
  switch (command.type) {
    case 'placeWall':
      if (survivorAt(state, x, y)) return reject(`(${x}, ${y}) is occupied`);
      return setStructure(state, x, y, 'wall');
    case 'placeDoor':
      return setStructure(state, x, y, 'door');
    case 'clearTile':
      return setStructure(state, x, y, 'floor');
    case 'designateBuild':
      return designateBuild(state, x, y, command.structure);
    case 'designateRemove':
      return designateRemove(state, x, y);
    case 'cancelDesignation': {
      const bp = blueprintAt(state, x, y);
      if (!bp) return unchanged();
      cancelBlueprint(state, bp);
      return changed();
    }
    case 'spawnSurvivor':
      return spawnSurvivor(state, x, y);
    case 'moveSurvivor':
      return moveSurvivor(state, command.survivorId, x, y);
  }
}

const reject = (reason: string): CommandResult => ({ ok: false, reason });
const changed = (): CommandResult => ({ ok: true, changed: true });
const unchanged = (): CommandResult => ({ ok: true, changed: false });

function setStructure(state: SimState, x: number, y: number, tile: Tile): CommandResult {
  if (!setTile(state.grid, x, y, tile)) return unchanged();
  // Any wall/door/floor change can alter walkability or room boundaries.
  state.walkabilityVersion += 1;
  if (tile === 'wall') evictFrom(state, { x, y });
  return changed();
}

function designateBuild(state: SimState, x: number, y: number, structure: 'wall' | 'door'): CommandResult {
  const existing = blueprintAt(state, x, y);
  if (existing?.target === structure) return unchanged();
  if (getTile(state.grid, x, y) !== 'floor') return reject(`(${x}, ${y}) is not empty floor; remove what's there first`);
  if (existing) cancelBlueprint(state, existing);
  addBlueprint(state, x, y, structure);
  return changed();
}

function designateRemove(state: SimState, x: number, y: number): CommandResult {
  const existing = blueprintAt(state, x, y);
  if (existing?.target === 'floor') return unchanged();
  if (existing) {
    // Removing a planned wall/door just cancels the plan.
    cancelBlueprint(state, existing);
    return changed();
  }
  const tile = getTile(state.grid, x, y);
  if (tile !== 'wall' && tile !== 'door') return unchanged();
  addBlueprint(state, x, y, 'floor');
  return changed();
}

function spawnSurvivor(state: SimState, x: number, y: number): CommandResult {
  if (!isWalkable(state.grid, x, y)) return reject(`(${x}, ${y}) is not walkable`);
  if (survivorAt(state, x, y)) return reject(`(${x}, ${y}) is occupied`);
  addSurvivor(state, x, y);
  return changed();
}

function moveSurvivor(state: SimState, id: string, x: number, y: number): CommandResult {
  const survivor = getSurvivor(state, id);
  if (!survivor) return reject(`no survivor ${id}`);
  if (!isWalkable(state.grid, x, y)) return reject(`(${x}, ${y}) is not walkable`);
  if (survivor.goal?.x === x && survivor.goal.y === y && !survivor.jobId) return unchanged();
  // A direct order overrides work. The survivor returns to jobs after arriving.
  if (survivor.jobId) releaseSurvivor(state, survivor, 'ordered elsewhere');
  setGoal(survivor, { x, y });
  return changed();
}

function setPriority(state: SimState, id: string, jobType: unknown, priority: unknown): CommandResult {
  const survivor = getSurvivor(state, id);
  if (!survivor) return reject(`no survivor ${id}`);
  if (!isJobType(jobType)) return reject(`unknown job type ${String(jobType)}`);
  if (!isPriority(priority)) return reject(`priority must be 0-4, got ${String(priority)}`);
  if (survivor.priorities[jobType] === priority) return unchanged();
  survivor.priorities[jobType] = priority;
  // Re-evaluate soon rather than waiting out an idle back-off.
  survivor.nextSeekTick = 0;
  return changed();
}
