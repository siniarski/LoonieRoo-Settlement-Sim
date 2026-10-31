// The only way to change the sim from outside (architecture rule 5).
// Every accepted command that changed state is appended to state.commandLog
// for replays. No-ops (painting a wall over a wall while dragging) are not
// logged, since replaying them would change nothing.

import { addBlueprint, blueprintAt, cancelBlueprint, evictFrom, hasRemovable } from './designations';
import { addFurniture, furnitureAt, removeFurniture } from './furniture';
import { getTile, inBounds, isWalkable, setTile, type Tile } from './grid';
import { spawnItem } from './items';
import { releaseSurvivor } from './jobs';
import { isJobType, isPriority, type JobType, type Priority } from './jobTypes';
import { addPlot, clearPlot, plotAt, removePlot } from './farming';
import { SATIETY_MAX } from './food';
import { HEALTH_MAX } from './health';
import { cancelMeal, wake } from './needs';
import { REST_MAX } from './rest';
import { ROOM_PURPOSES, designationOf, roomAt, type RoomPurpose } from './rooms';
import type { SimState } from './state';
import { addSurvivor, getSurvivor, setGoal, survivorAt } from './survivors';
import { addZombie, evictZombies } from './zombies';

export type Command =
  // Instant edits (debug, scenarios, Shift+paint).
  | { type: 'placeWall'; x: number; y: number }
  | { type: 'placeDoor'; x: number; y: number }
  | { type: 'clearTile'; x: number; y: number }
  | { type: 'placeBed'; x: number; y: number }
  // Designations: become jobs for survivors.
  | { type: 'designateBuild'; x: number; y: number; structure: 'wall' | 'door' | 'bed' }
  | { type: 'designateRemove'; x: number; y: number }
  /** Cancels a blueprint on the tile, else removes a farm plot, else unmarks a crate there, else clears the purpose of the room the tile is in. */
  | { type: 'cancelDesignation'; x: number; y: number }
  /** Makes this floor tile a farm plot (no furniture on it). Drag it over an area. */
  | { type: 'designateFarm'; x: number; y: number }
  /** Marks the crate on this tile (if it has loot left) for scavenging. Paint it over an area. */
  | { type: 'designateScavenge'; x: number; y: number }
  | { type: 'designateRoom'; x: number; y: number; purpose: RoomPurpose }
  // Survivors.
  | { type: 'spawnSurvivor'; x: number; y: number }
  | { type: 'moveSurvivor'; survivorId: string; x: number; y: number }
  | { type: 'setPriority'; survivorId: string; jobType: JobType; priority: Priority }
  // Debug: set a survivor's rest (0..REST_MAX) or satiety (0..SATIETY_MAX), e.g. to make them tired/hungry now.
  | { type: 'setRest'; survivorId: string; rest: number }
  | { type: 'setSatiety'; survivorId: string; satiety: number }
  | { type: 'setHealth'; survivorId: string; health: number }
  // Debug/sandbox: drop a food item on (or near) a tile.
  | { type: 'spawnFood'; x: number; y: number }
  // Debug/sandbox: put a zombie on a floor tile (they only last until dawn).
  | { type: 'spawnZombie'; x: number; y: number };

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
  if (command.type === 'setRest') return setNeed(state, command.survivorId, 'rest', command.rest, REST_MAX);
  if (command.type === 'setSatiety') return setNeed(state, command.survivorId, 'satiety', command.satiety, SATIETY_MAX);
  if (command.type === 'setHealth') return setNeed(state, command.survivorId, 'health', command.health, HEALTH_MAX);
  const { x, y } = command;
  if (!inBounds(state.grid, x, y)) return reject(`(${x}, ${y}) is out of bounds`);
  switch (command.type) {
    case 'placeWall':
      if (survivorAt(state, x, y)) return reject(`(${x}, ${y}) is occupied`);
      return setStructure(state, x, y, 'wall');
    case 'placeDoor':
      return setStructure(state, x, y, 'door');
    case 'clearTile': {
      const f = furnitureAt(state, x, y);
      if (f) {
        removeFurniture(state, f);
        return changed();
      }
      return setStructure(state, x, y, 'floor');
    }
    case 'placeBed':
      if (getTile(state.grid, x, y) !== 'floor') return reject(`(${x}, ${y}) is not floor`);
      if (furnitureAt(state, x, y)) return unchanged();
      addFurniture(state, 'bed', x, y);
      return changed();
    case 'designateBuild':
      return designateBuild(state, x, y, command.structure);
    case 'designateRemove':
      return designateRemove(state, x, y);
    case 'cancelDesignation':
      return cancelDesignation(state, x, y);
    case 'designateFarm':
      if (getTile(state.grid, x, y) !== 'floor' || furnitureAt(state, x, y)) return unchanged();
      if (plotAt(state, x, y)) return unchanged();
      addPlot(state, x, y);
      return changed();
    case 'designateScavenge': {
      const crate = furnitureAt(state, x, y);
      if (crate?.kind !== 'crate' || crate.loot === 0 || crate.scavenge) return unchanged();
      crate.scavenge = true;
      return changed();
    }
    case 'designateRoom':
      return designateRoom(state, x, y, command.purpose);
    case 'spawnSurvivor':
      return spawnSurvivor(state, x, y);
    case 'spawnZombie':
      if (getTile(state.grid, x, y) !== 'floor') return reject(`(${x}, ${y}) is not floor`);
      addZombie(state, x, y);
      return changed();
    case 'spawnFood':
      if (!isWalkable(state.grid, x, y)) return reject(`(${x}, ${y}) is not walkable`);
      spawnItem(state, 'food', x, y);
      return changed();
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
  if (tile !== 'floor') {
    // Instant walls/doors flatten furniture (debug/sandbox behaviour, no refund).
    const f = furnitureAt(state, x, y);
    if (f) removeFurniture(state, f);
  }
  if (tile === 'wall') evictFrom(state, { x, y });
  if (tile !== 'floor') {
    evictZombies(state, { x, y });
    clearPlot(state, { x, y });
  }
  return changed();
}

function designateBuild(state: SimState, x: number, y: number, structure: 'wall' | 'door' | 'bed'): CommandResult {
  const existing = blueprintAt(state, x, y);
  if (existing?.target === structure) return unchanged();
  if (getTile(state.grid, x, y) !== 'floor' || furnitureAt(state, x, y)) {
    return reject(`(${x}, ${y}) is not empty floor; remove what's there first`);
  }
  if (existing) cancelBlueprint(state, existing);
  addBlueprint(state, x, y, structure);
  return changed();
}

function designateRemove(state: SimState, x: number, y: number): CommandResult {
  const existing = blueprintAt(state, x, y);
  if (existing?.target === 'floor') return unchanged();
  if (existing) {
    // Removing a planned build just cancels the plan.
    cancelBlueprint(state, existing);
    return changed();
  }
  if (!hasRemovable(state, x, y)) return unchanged();
  addBlueprint(state, x, y, 'floor');
  return changed();
}

function cancelDesignation(state: SimState, x: number, y: number): CommandResult {
  const bp = blueprintAt(state, x, y);
  if (bp) {
    cancelBlueprint(state, bp);
    return changed();
  }
  const plot = plotAt(state, x, y);
  if (plot) {
    removePlot(state, plot);
    return changed();
  }
  const crate = furnitureAt(state, x, y);
  if (crate?.kind === 'crate') {
    if (!crate.scavenge) return unchanged();
    crate.scavenge = false;
    return changed();
  }
  const room = roomAt(state, x, y);
  const d = room ? designationOf(state, room) : state.roomDesignations.find((r) => r.x === x && r.y === y);
  if (!d) return unchanged();
  state.roomDesignations.splice(state.roomDesignations.indexOf(d), 1);
  return changed();
}

function designateRoom(state: SimState, x: number, y: number, purpose: unknown): CommandResult {
  if (!ROOM_PURPOSES.includes(purpose as RoomPurpose)) return reject(`unknown room purpose ${String(purpose)}`);
  const room = roomAt(state, x, y);
  if (!room) return reject(`(${x}, ${y}) is not inside an enclosed room`);
  const existing = designationOf(state, room);
  if (existing?.purpose === purpose) return unchanged();
  if (existing) existing.purpose = purpose as RoomPurpose;
  else state.roomDesignations.push({ id: `r${state.nextId++}`, x, y, purpose: purpose as RoomPurpose });
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
  // A direct order overrides work and sleep. The survivor returns to jobs (or bed, if still tired) after arriving.
  if (survivor.jobId) releaseSurvivor(state, survivor, 'ordered elsewhere');
  wake(state, survivor, 'woken by an order');
  cancelMeal(state, survivor, 'meal interrupted by an order');
  setGoal(survivor, { x, y });
  return changed();
}

function setNeed(state: SimState, id: string, need: 'rest' | 'satiety' | 'health', value: unknown, max: number): CommandResult {
  const survivor = getSurvivor(state, id);
  if (!survivor) return reject(`no survivor ${id}`);
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 0 || value > max) {
    return reject(`${need} must be an integer 0-${max}`);
  }
  if (survivor[need] === value) return unchanged();
  survivor[need] = value;
  if (need === 'satiety') survivor.nextMealTick = 0; // re-check food straight away
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
