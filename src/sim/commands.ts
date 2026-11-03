// The only way to change the sim from outside (architecture rule 5).
// Every accepted command that changed state is appended to state.commandLog
// for replays. No-ops (painting a wall over a wall while dragging) are not
// logged, since replaying them would change nothing.

import type { Survivor } from './survivors';
import { addBlueprint, blueprintAt, cancelBlueprint, evictFrom, hasRemovable } from './designations';
import { addFurniture, furnitureAt, removeFurniture } from './furniture';
import { getTile, inBounds, isWalkable, setTile, type Tile } from './grid';
import { spawnItem } from './items';
import { releaseSurvivor } from './jobs';
import { isJobType, isPriority, type JobType, type Priority } from './jobTypes';
import { addPlot, clearPlot, plotAt, removePlot } from './farming';
import { SATIETY_MAX } from './food';
import { HEALTH_MAX } from './health';
import { cancelMeal, cancelTreatment, wake } from './needs';
import { REST_MAX } from './rest';
import { ROOM_PURPOSES, designationOf, roomAt, type RoomPurpose } from './rooms';
import type { SimState } from './state';
import { addSurvivor, getSurvivor, setGoal, survivorAt } from './survivors';
import { getWanderer, removeWanderer } from './wanderers';
import { cancelArming } from './weapons';
import { cancelDressing } from './clothing';
import { sendExpedition } from './expeditions';
import { trade } from './visitors';
import { MAX_EXPLORE_SIDE, isExplored } from './fog';
import { damageStructure, isStructure } from './breach';
import { MAX_CRAFT_TARGET, isCraftKind, type CraftKind } from './crafting';
import { addZombie, evictZombies } from './zombies';
import { logEvent } from './events';

export type Command =
  // Instant edits (debug, scenarios, Shift+paint).
  | { type: 'placeWall'; x: number; y: number }
  | { type: 'placeDoor'; x: number; y: number }
  | { type: 'clearTile'; x: number; y: number }
  | { type: 'placeBed'; x: number; y: number }
  | { type: 'placeTable'; x: number; y: number }
  | { type: 'placeWorkbench'; x: number; y: number }
  /** Keep this many of a craftable in stock (crafting.ts). */
  | { type: 'setCraftTarget'; kind: Exclude<CraftKind, 'meal'>; count: number }
  /** Give a bed to a survivor (their only one), or take it back with null. */
  | { type: 'assignBed'; bedId: string; survivorId: string | null }
  // Designations: become jobs for survivors.
  | { type: 'designateBuild'; x: number; y: number; structure: 'wall' | 'door' | 'barricade' | 'bed' | 'table' | 'workbench' | 'stove' | 'trap' }
  | { type: 'designateRemove'; x: number; y: number }
  /** Cancels a blueprint on the tile, else removes a farm plot, else unmarks a crate there, else clears the purpose of the room the tile is in. */
  | { type: 'cancelDesignation'; x: number; y: number }
  /** Makes this floor tile a farm plot (no furniture on it). Drag it over an area. */
  | { type: 'designateFarm'; x: number; y: number }
  /** Marks the crate on this tile (if it has loot left) for scavenging. Paint it over an area. */
  | { type: 'designateScavenge'; x: number; y: number }
  | { type: 'designateRoom'; x: number; y: number; purpose: RoomPurpose }
  /** Scout a box of unexplored ground (fog.ts); corners in any order. */
  | { type: 'designateExplore'; x0: number; y0: number; x1: number; y1: number }
  // Survivors.
  | { type: 'spawnSurvivor'; x: number; y: number }
  | { type: 'moveSurvivor'; survivorId: string; x: number; y: number }
  | { type: 'setPriority'; survivorId: string; jobType: JobType; priority: Priority }
  // Wanderers waiting at the edge: take them in (they become a survivor) or send them away.
  | { type: 'acceptWanderer'; wandererId: string }
  | { type: 'dismissWanderer'; wandererId: string }
  /** Send survivors off the map to a point of interest (expeditions.ts). */
  | { type: 'sendExpedition'; survivorIds: string[]; poiId: string }
  /** Take a trader's offer (visitors.ts). */
  | { type: 'trade'; traderId: string; offer: number }
  /** Lock or unlock a door: raiders can't open locked ones. */
  | { type: 'toggleLock'; x: number; y: number }
  // Debug: set a survivor's rest (0..REST_MAX) or satiety (0..SATIETY_MAX), e.g. to make them tired/hungry now.
  | { type: 'setRest'; survivorId: string; rest: number }
  | { type: 'setSatiety'; survivorId: string; satiety: number }
  | { type: 'setHealth'; survivorId: string; health: number }
  // Debug/sandbox: drop a food item on (or near) a tile.
  | { type: 'spawnFood'; x: number; y: number }
  | { type: 'spawnWeapon'; x: number; y: number }
  // Debug: batter the wall or door at a tile, as a zombie would (it may break).
  | { type: 'damageStructure'; x: number; y: number; amount: number }
  // Debug/sandbox: put a zombie on a floor tile (they only last until dawn).
  | { type: 'spawnZombie'; x: number; y: number };

export type CommandResult = { ok: true; changed: boolean } | { ok: false; reason: string };

/** Plans the player can only make on ground someone has seen. */
const DESIGNATIONS = new Set<Command['type']>(['designateBuild', 'designateRemove', 'designateRoom', 'designateScavenge', 'designateFarm']);

export function applyCommand(state: SimState, command: Command): CommandResult {
  const result = execute(state, command);
  if (result.ok && result.changed) {
    state.commandLog.push({ tick: state.tick, command: { ...command } });
  }
  return result;
}

function execute(state: SimState, command: Command): CommandResult {
  if (command.type === 'setPriority') return setPriority(state, command.survivorId, command.jobType, command.priority);
  if (command.type === 'designateExplore') {
    const x0 = Math.max(0, Math.min(command.x0, command.x1));
    const y0 = Math.max(0, Math.min(command.y0, command.y1));
    const x1 = Math.min(state.grid.width - 1, Math.max(command.x0, command.x1), x0 + MAX_EXPLORE_SIDE - 1);
    const y1 = Math.min(state.grid.height - 1, Math.max(command.y0, command.y1), y0 + MAX_EXPLORE_SIDE - 1);
    if (![command.x0, command.y0, command.x1, command.y1].every(Number.isInteger) || x1 < x0 || y1 < y0) return reject('not a box on the map');
    let dark = false;
    for (let y = y0; y <= y1 && !dark; y++) for (let x = x0; x <= x1 && !dark; x++) dark = !isExplored(state, x, y);
    if (!dark) return reject('already explored');
    state.exploreAreas.push({ id: `e${state.nextId++}`, x0, y0, x1, y1 });
    return changed();
  }
  if (command.type === 'assignBed') {
    const bed = state.furniture.find((f) => f.id === command.bedId && f.kind === 'bed');
    if (!bed) return reject(`no bed ${command.bedId}`);
    const who = command.survivorId;
    if (who !== null && !getSurvivor(state, who)) return reject(`no survivor ${who}`);
    if (bed.owner === who) return unchanged();
    for (const f of state.furniture) if (who !== null && f.owner === who) f.owner = null;
    bed.owner = who;
    // Someone else asleep in it (or on the way) moves out next tick.
    if (bed.occupant && bed.occupant !== who) {
      const sleeper = getSurvivor(state, bed.occupant);
      if (sleeper) wake(state, sleeper, 'their bed was given away');
    }
    return changed();
  }
  if (command.type === 'damageStructure') {
    const { x, y, amount } = command;
    if (!isStructure(getTile(state.grid, x, y))) return reject(`(${x}, ${y}) is not a wall or door`);
    if (!Number.isInteger(amount) || amount <= 0) return reject('damage must be a positive integer');
    damageStructure(state, x, y, amount);
    return changed();
  }
  if (command.type === 'setCraftTarget') {
    const { kind, count } = command;
    if (!isCraftKind(kind)) return reject(`can't craft ${String(kind)}`);
    if (!Number.isInteger(count) || count < 0 || count > MAX_CRAFT_TARGET) return reject(`target must be 0-${MAX_CRAFT_TARGET}`);
    if (state.craftTargets[kind] === count) return unchanged();
    state.craftTargets[kind] = count;
    return changed();
  }
  if (command.type === 'trade') {
    const failed = trade(state, command.traderId, command.offer);
    return failed ? reject(failed) : changed();
  }
  if (command.type === 'toggleLock') {
    const { x, y } = command;
    if (getTile(state.grid, x, y) !== 'door') return reject(`(${x}, ${y}) is not a door`);
    const i = y * state.grid.width + x;
    const at = state.grid.locked.indexOf(i);
    if (at >= 0) state.grid.locked.splice(at, 1);
    else {
      state.grid.locked.push(i);
      evictZombies(state, { x, y }); // a raider in the doorway is shut out
    }
    state.walkabilityVersion += 1; // raiders' paths change
    return changed();
  }
  if (command.type === 'sendExpedition') {
    const members = command.survivorIds.map((id) => getSurvivor(state, id));
    if (members.some((s) => !s) || new Set(command.survivorIds).size !== members.length) return reject('no such survivor');
    if (!state.pointsOfInterest.some((p) => p.id === command.poiId)) return reject(`no point of interest ${command.poiId}`);
    for (const s of members as Survivor[]) {
      // Drop everything here: work, meals, errands, bed, leisure.
      if (s.jobId) releaseSurvivor(state, s, 'off on an expedition');
      wake(state, s, 'off on an expedition');
      cancelMeal(state, s, 'off on an expedition');
      cancelTreatment(state, s, 'off on an expedition');
      cancelArming(state, s, 'off on an expedition');
      cancelDressing(state, s, 'off on an expedition');
      s.leisure = null;
      s.breakdown = null;
      s.goal = null;
      s.move = null;
      s.path = [];
    }
    const failed = sendExpedition(state, members as Survivor[], command.poiId);
    return failed ? reject(failed) : changed();
  }
  if (command.type === 'acceptWanderer' || command.type === 'dismissWanderer') {
    const w = getWanderer(state, command.wandererId);
    if (!w) return reject(`no wanderer ${command.wandererId}`);
    removeWanderer(state, w);
    if (command.type === 'acceptWanderer') {
      addSurvivor(state, w.x, w.y, w.name).traits = [...w.traits];
      logEvent(state, `${w.name} joined the colony`);
    } else {
      logEvent(state, `${w.name} was turned away`);
    }
    return changed();
  }
  if (command.type === 'setRest') return setNeed(state, command.survivorId, 'rest', command.rest, REST_MAX);
  if (command.type === 'setSatiety') return setNeed(state, command.survivorId, 'satiety', command.satiety, SATIETY_MAX);
  if (command.type === 'setHealth') return setNeed(state, command.survivorId, 'health', command.health, HEALTH_MAX);
  const { x, y } = command;
  if (!inBounds(state.grid, x, y)) return reject(`(${x}, ${y}) is out of bounds`);
  if (DESIGNATIONS.has(command.type) && !isExplored(state, x, y)) return reject(`(${x}, ${y}) is unexplored`);
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
    case 'placeTable':
    case 'placeWorkbench':
      if (getTile(state.grid, x, y) !== 'floor') return reject(`(${x}, ${y}) is not floor`);
      if (furnitureAt(state, x, y)) return unchanged();
      addFurniture(state, command.type === 'placeBed' ? 'bed' : command.type === 'placeTable' ? 'table' : 'workbench', x, y);
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
      if ((crate?.kind !== 'crate' && crate?.kind !== 'tree') || (crate.kind === 'crate' && crate.loot === 0) || crate.scavenge) return unchanged();
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
    case 'spawnWeapon':
      if (!isWalkable(state.grid, x, y)) return reject(`(${x}, ${y}) is not walkable`);
      spawnItem(state, command.type === 'spawnFood' ? 'food' : 'weapon', x, y);
      return changed();
    case 'moveSurvivor':
      return moveSurvivor(state, command.survivorId, x, y);
  }
}

const reject = (reason: string): CommandResult => ({ ok: false, reason });
const changed = (): CommandResult => ({ ok: true, changed: true });
const unchanged = (): CommandResult => ({ ok: true, changed: false });

function setStructure(state: SimState, x: number, y: number, tile: Tile): CommandResult {
  if (getTile(state.grid, x, y) === 'water') return reject(`(${x}, ${y}) is water`);
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

function designateBuild(state: SimState, x: number, y: number, structure: 'wall' | 'door' | 'barricade' | 'bed' | 'table' | 'workbench' | 'stove' | 'trap'): CommandResult {
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
  if (crate?.kind === 'crate' || crate?.kind === 'tree') {
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
  cancelTreatment(state, survivor, 'treatment interrupted by an order');
  cancelArming(state, survivor, 'interrupted by an order');
  cancelDressing(state, survivor, 'interrupted by an order');
  survivor.leisure = null;
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
