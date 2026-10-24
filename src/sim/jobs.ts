// Designations -> jobs -> workers.
//
// The job queue is derived from the world: reconcileJobs (start of every
// tick) drops jobs whose target is gone and creates missing ones: one per
// blueprint (haul while it needs material, then build), one per indoor dirty
// tile (clean), one per loose item outside storage (haul to storage), and
// one per marked crate with loot left (scavenge). Survivors reserve a job when they claim it, so two survivors
// never share one. Every way a job can go wrong ends the same way:
// releaseSurvivor() frees the reservation, drops anything carried, and the
// survivor goes back to looking for work. Nobody gets stuck.

import { cancelBlueprint, completeBlueprint, getBlueprint, preconditionHolds, type Blueprint } from './designations';
import { getFurniture } from './furniture';
import { isDirty, isWalkable, neighbours4, setDirty, type Point } from './grid';
import { canHoldItem, dropItem, getItem, removeItem, spawnItem, type Item } from './items';
import { JOB_TYPES, type JobType } from './jobTypes';
import { canReach, findPathToAny } from './pathfinding';
import { random } from './rng';
import { completeStorageRooms, getRoomMap, isIndoors, labelAt, reconcileRoomDesignations } from './rooms';
import { manhattan, samePoint } from './space';
import type { SimState } from './state';
import { getSurvivor, walk, type Survivor } from './survivors';

/**
 * Haul jobs come in two kinds:
 *  - deliver: `blueprintId` set; any free wood is chosen at claim time (`itemId`).
 *  - store:   `blueprintId` null; `itemId` is the loose item the job exists for,
 *             and `dest` (a free tile in a complete storage room) is chosen at claim time.
 */
export interface Job {
  id: string;
  type: JobType;
  /** The tile the job is about: the blueprint, the dirty tile, or the item to store. */
  x: number;
  y: number;
  blueprintId: string | null;
  itemId: string | null;
  /** Scavenge only: the crate being searched. */
  furnitureId: string | null;
  /** Store hauls only: the reserved storage tile. */
  dest: Point | null;
  /** Clean and scavenge: ticks of work done. */
  progress: number;
  reservedBy: string | null;
}

/** Ticks to clean one tile. */
export const CLEAN_WORK = 15;
/** Ticks to search a crate once; each search yields one find. */
export const SCAVENGE_WORK = 40;
/** Chance a find is food rather than wood (rolled with the sim RNG). */
export const SCAVENGE_FOOD_CHANCE = 0.6;
/** How many nearest candidates a survivor checks per job type before giving up on that type. */
const MAX_CANDIDATES = 8;
/** An idle survivor with nothing doable looks again after this many ticks. */
export const IDLE_RETRY_TICKS = 5;

export function getJob(state: SimState, id: string): Job | undefined {
  return state.jobs.find((j) => j.id === id);
}

// --- Reconcile -------------------------------------------------------------

export function reconcileJobs(state: SimState): void {
  reconcileRoomDesignations(state);

  // Blueprints whose tile changed underneath them (e.g. instant placeWall) are void.
  for (const bp of [...state.blueprints]) {
    if (!preconditionHolds(state, bp)) cancelBlueprint(state, bp);
  }

  const storage = completeStorageRooms(state);
  for (const job of [...state.jobs]) {
    const reason = invalidReason(state, job, storage);
    if (reason) removeJob(state, job, reason);
  }

  const haveJob = new Set(state.jobs.map((j) => j.blueprintId));
  for (const bp of state.blueprints) {
    if (!haveJob.has(bp.id)) addJob(state, wantedJobType(bp), bp.x, bp.y, bp.id, null);
  }

  // Cleaning is indoors only: nobody scrubs the street.
  const { width, dirt } = state.grid;
  const cleaning = new Set(state.jobs.filter((j) => j.type === 'clean').map((j) => j.y * width + j.x));
  for (let i = 0; i < dirt.length; i++) {
    const x = i % width;
    const y = Math.floor(i / width);
    if (dirt[i] === 1 && !cleaning.has(i) && isIndoors(state, x, y)) addJob(state, 'clean', x, y, null, null);
  }

  // Loose items outside storage get a store job, if any storage room can take them.
  if (storage.size > 0) {
    const storing = new Set(state.jobs.filter(isStoreJob).map((j) => j.itemId));
    for (const item of state.items) {
      if (!item.pos || storing.has(item.id) || storage.has(labelAt(state, item.pos.x, item.pos.y))) continue;
      addJob(state, 'haul', item.pos.x, item.pos.y, null, item.id);
    }
  }

  // Marked crates with loot left get one scavenge job each.
  const searching = new Set(state.jobs.filter((j) => j.type === 'scavenge').map((j) => j.furnitureId));
  for (const f of state.furniture) {
    if (f.kind === 'crate' && f.scavenge && f.loot > 0 && !searching.has(f.id)) {
      addJob(state, 'scavenge', f.x, f.y, null, null, f.id);
    }
  }
}

export function isStoreJob(job: Job): boolean {
  return job.type === 'haul' && job.blueprintId === null;
}

function wantedJobType(bp: Blueprint): JobType {
  return bp.needsMaterial && !bp.delivered ? 'haul' : 'build';
}

function invalidReason(state: SimState, job: Job, storage: Set<number>): string | null {
  switch (job.type) {
    case 'clean':
      if (!isDirty(state.grid, job.x, job.y)) return 'tile already clean';
      return isIndoors(state, job.x, job.y) ? null : 'tile is no longer indoors';
    case 'scavenge': {
      const crate = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
      if (!crate) return 'crate gone';
      if (!crate.scavenge) return 'no longer marked for scavenging';
      return crate.loot > 0 ? null : 'crate is empty';
    }
    case 'build':
    case 'haul': {
      if (isStoreJob(job)) return storeInvalidReason(state, job, storage);
      const bp = job.blueprintId ? getBlueprint(state, job.blueprintId) : undefined;
      if (!bp) return 'designation cancelled';
      if (wantedJobType(bp) !== job.type) return 'designation moved on';
      if (job.type === 'haul' && job.itemId) {
        const item = getItem(state, job.itemId);
        if (!item) return 'item gone';
        if (item.carriedBy && item.carriedBy !== job.reservedBy) return 'item taken by someone else';
      }
      return null;
    }
  }
}

function storeInvalidReason(state: SimState, job: Job, storage: Set<number>): string | null {
  const item = job.itemId ? getItem(state, job.itemId) : undefined;
  if (!item) return 'item gone';
  if (item.carriedBy && item.carriedBy !== job.reservedBy) return 'item taken by someone else';
  if (storage.size === 0) return 'no complete storage room';
  if (item.pos) {
    if (storage.has(labelAt(state, item.pos.x, item.pos.y))) return 'already in storage';
    // Keep the job's tile in step with the item (it may have been dropped elsewhere).
    job.x = item.pos.x;
    job.y = item.pos.y;
  }
  return null;
}

function addJob(
  state: SimState,
  type: JobType,
  x: number,
  y: number,
  blueprintId: string | null,
  itemId: string | null,
  furnitureId: string | null = null,
): Job {
  const job: Job = {
    id: `j${state.nextId++}`,
    type,
    x,
    y,
    blueprintId,
    itemId,
    furnitureId,
    dest: null,
    progress: 0,
    reservedBy: null,
  };
  state.jobs.push(job);
  return job;
}

function removeJob(state: SimState, job: Job, reason: string): void {
  const worker = job.reservedBy ? getSurvivor(state, job.reservedBy) : undefined;
  if (worker && worker.jobId === job.id) releaseSurvivor(state, worker, reason);
  const i = state.jobs.indexOf(job);
  if (i >= 0) state.jobs.splice(i, 1);
}

// --- Releasing --------------------------------------------------------------

/** Frees the survivor's job reservation and drops anything they carry. Always safe to call. */
export function releaseSurvivor(state: SimState, s: Survivor, reason: string): void {
  const job = s.jobId ? getJob(state, s.jobId) : undefined;
  if (job && job.reservedBy === s.id) {
    job.reservedBy = null;
    const reserved = job.itemId ? getItem(state, job.itemId) : undefined;
    if (reserved && reserved.reservedBy === s.id) reserved.reservedBy = null;
    // Delivery hauls pick their item at claim time; store hauls are *about* their item.
    if (!isStoreJob(job)) job.itemId = null;
    job.dest = null;
  }
  if (s.carrying) {
    const item = getItem(state, s.carrying);
    if (item) dropItem(state, item, s.pos);
    s.carrying = null;
  }
  s.jobId = null;
  s.goal = null;
  s.path = [];
  s.pathVersion = -1;
  s.note = `stopped: ${reason}`;
}

// --- Working ----------------------------------------------------------------

/** Runs when the survivor is standing still: continue the current job, or look for one. */
export function updateWork(state: SimState, s: Survivor): void {
  if (!s.jobId) {
    if (state.tick < s.nextSeekTick) return;
    if (!seekJob(state, s)) s.nextSeekTick = state.tick + IDLE_RETRY_TICKS;
    return;
  }
  const job = getJob(state, s.jobId);
  if (!job || job.reservedBy !== s.id) {
    releaseSurvivor(state, s, 'job vanished');
    return;
  }
  switch (job.type) {
    case 'clean':
      return workClean(state, s, job);
    case 'build':
      return workBuild(state, s, job);
    case 'haul':
      return workHaul(state, s, job);
    case 'scavenge':
      return workScavenge(state, s, job);
  }
}

/** Search a crate from next to it; each completed search drops one find by the survivor. */
function workScavenge(state: SimState, s: Survivor, job: Job): void {
  const crate = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
  if (!crate || crate.loot <= 0) return releaseSurvivor(state, s, 'crate is empty');
  if (manhattan(s.pos, crate) !== 1) return goTo(state, s, job, neighbours4(state.grid, crate.x, crate.y));
  job.progress += 1;
  if (job.progress < SCAVENGE_WORK) return;
  const kind = random(state) < SCAVENGE_FOOD_CHANCE ? 'food' : 'wood';
  spawnItem(state, kind, s.pos.x, s.pos.y);
  crate.loot -= 1;
  if (crate.loot === 0) crate.scavenge = false;
  finishJob(state, s, job, `scavenged ${kind} from (${crate.x}, ${crate.y}), ${crate.loot} left`);
}

function workClean(state: SimState, s: Survivor, job: Job): void {
  const target = { x: job.x, y: job.y };
  if (!samePoint(s.pos, target)) return goTo(state, s, job, [target]);
  job.progress += 1;
  if (job.progress < CLEAN_WORK) return;
  setDirty(state.grid, job.x, job.y, false);
  finishJob(state, s, job, `cleaned (${job.x}, ${job.y})`);
}

function workBuild(state: SimState, s: Survivor, job: Job): void {
  const bp = job.blueprintId ? getBlueprint(state, job.blueprintId) : undefined;
  if (!bp) return releaseSurvivor(state, s, 'designation cancelled');
  if (manhattan(s.pos, bp) !== 1) return goTo(state, s, job, neighbours4(state.grid, bp.x, bp.y));
  bp.workDone += 1;
  if (bp.workDone < bp.workRequired) return;
  completeBlueprint(state, bp);
  finishJob(state, s, job, bp.target === 'floor' ? `removed structure at (${bp.x}, ${bp.y})` : `built ${bp.target} at (${bp.x}, ${bp.y})`);
}

function workHaul(state: SimState, s: Survivor, job: Job): void {
  const item = job.itemId ? getItem(state, job.itemId) : undefined;
  if (!item) return releaseSurvivor(state, s, 'item gone');

  if (s.carrying !== item.id) {
    if (!item.pos) return releaseSurvivor(state, s, 'item taken by someone else');
    if (!samePoint(s.pos, item.pos)) return goTo(state, s, job, [item.pos]);
    item.pos = null;
    item.carriedBy = s.id;
    s.carrying = item.id;
    s.note = `picked up ${item.kind}`;
    // Fall through and head for the destination this same tick.
  }

  if (isStoreJob(job)) return carryToStorage(state, s, job, item);

  const bp = job.blueprintId ? getBlueprint(state, job.blueprintId) : undefined;
  if (!bp) return releaseSurvivor(state, s, 'designation cancelled');
  if (manhattan(s.pos, bp) !== 1) return goTo(state, s, job, neighbours4(state.grid, bp.x, bp.y));
  removeItem(state, item);
  s.carrying = null;
  bp.delivered = true;
  finishJob(state, s, job, `delivered ${item.kind} to (${bp.x}, ${bp.y})`);
}

function carryToStorage(state: SimState, s: Survivor, job: Job, item: Item): void {
  if (!job.dest || !storageTileFree(state, job, job.dest, completeStorageRooms(state))) {
    // The reserved tile got taken (or the room changed): pick another.
    job.dest = pickStorageTile(state, s.pos, job);
    if (!job.dest) return releaseSurvivor(state, s, 'no free storage tile');
  }
  if (!samePoint(s.pos, job.dest)) return goTo(state, s, job, [job.dest]);
  item.pos = { ...job.dest };
  item.carriedBy = null;
  item.reservedBy = null;
  s.carrying = null;
  finishJob(state, s, job, `stored ${item.kind} at (${job.dest.x}, ${job.dest.y})`);
}

/** A storage tile is free if it can hold an item and no other active store job has reserved it. */
function storageTileFree(state: SimState, job: Job, p: Point, storage: Set<number>): boolean {
  return (
    storage.has(labelAt(state, p.x, p.y)) &&
    canHoldItem(state, p.x, p.y) &&
    !state.jobs.some((j) => j !== job && j.reservedBy !== null && j.dest !== null && samePoint(j.dest, p))
  );
}

/** Nearest free, reachable tile in any complete storage room. */
function pickStorageTile(state: SimState, from: Point, job: Job): Point | null {
  const storage = completeStorageRooms(state);
  const { rooms } = getRoomMap(state);
  const { width } = state.grid;
  const candidates: Point[] = [];
  for (const index of [...storage].sort((a, b) => a - b)) {
    for (const i of rooms[index]!.tiles) {
      const p = { x: i % width, y: Math.floor(i / width) };
      if (storageTileFree(state, job, p, storage)) candidates.push(p);
    }
  }
  candidates.sort((a, b) => manhattan(from, a) - manhattan(from, b));
  return candidates.find((p) => canReach(state.grid, state.walkabilityVersion, from, [p])) ?? null;
}

function goTo(state: SimState, s: Survivor, job: Job, goals: Point[]): void {
  const path = findPathToAny(state.grid, s.pos, goals);
  if (!path || path.length === 0) {
    releaseSurvivor(state, s, `can't reach ${job.type} job at (${job.x}, ${job.y})`);
    return;
  }
  walk(s, path, state.walkabilityVersion);
}

function finishJob(state: SimState, s: Survivor, job: Job, note: string): void {
  const i = state.jobs.indexOf(job);
  if (i >= 0) state.jobs.splice(i, 1);
  s.jobId = null;
  s.note = note;
}

// --- Choosing work ----------------------------------------------------------

interface Plan {
  path: Point[];
  itemId: string | null;
  dest: Point | null;
}

/**
 * Highest-priority job type first (ties: build, haul, clean), then the
 * nearest open job of that type that is actually doable. Returns true if a
 * job was claimed.
 */
export function seekJob(state: SimState, s: Survivor): boolean {
  const choice = chooseJob(state, s);
  if (!choice) return false;
  claim(state, s, choice.job, choice.plan, choice.why);
  return true;
}

/** The job this survivor would take right now, without claiming it. Pure. */
export function chooseJob(state: SimState, s: Survivor): { job: Job; plan: Plan; why: string } | null {
  for (const type of typesByPriority(s)) {
    const open = openJobsByDistance(state, type, s.pos);
    for (const job of open.slice(0, MAX_CANDIDATES)) {
      const plan = planJob(state, s, job);
      if (plan) return { job, plan, why: `priority ${s.priorities[type]}, nearest of ${open.length} open` };
    }
  }
  return null;
}

export function typesByPriority(s: Survivor): JobType[] {
  return JOB_TYPES.filter((t) => s.priorities[t] > 0).sort(
    (a, b) => s.priorities[a] - s.priorities[b] || JOB_TYPES.indexOf(a) - JOB_TYPES.indexOf(b),
  );
}

function openJobsByDistance(state: SimState, type: JobType, from: Point): Job[] {
  // Array.prototype.sort is stable, so ties keep queue order (deterministic).
  return state.jobs
    .filter((j) => j.type === type && j.reservedBy === null)
    .sort((a, b) => manhattan(from, a) - manhattan(from, b));
}

function planJob(state: SimState, s: Survivor, job: Job): Plan | null {
  const { grid, walkabilityVersion: v } = state;
  switch (job.type) {
    case 'clean': {
      const goal = [{ x: job.x, y: job.y }];
      if (!canReach(grid, v, s.pos, goal)) return null;
      return pathPlan(state, s.pos, goal, null);
    }
    case 'build':
    case 'scavenge': {
      // Both are done from a tile next to the target.
      const goals = neighbours4(grid, job.x, job.y);
      if (!canReach(grid, v, s.pos, goals)) return null;
      return pathPlan(state, s.pos, goals, null);
    }
    case 'haul': {
      if (isStoreJob(job)) {
        const item = job.itemId ? getItem(state, job.itemId) : undefined;
        if (!item?.pos || item.reservedBy !== null || !canReach(grid, v, s.pos, [item.pos])) return null;
        const dest = pickStorageTile(state, item.pos, job);
        return dest ? pathPlan(state, s.pos, [item.pos], item.id, dest) : null;
      }
      const site = neighbours4(grid, job.x, job.y).filter((p) => isWalkable(grid, p.x, p.y));
      if (site.length === 0) return null;
      const free = state.items
        .filter((i): i is Item & { pos: Point } => i.pos !== null && i.reservedBy === null && i.kind === 'wood')
        .sort((a, b) => manhattan(s.pos, a.pos) - manhattan(s.pos, b.pos));
      for (const item of free.slice(0, MAX_CANDIDATES)) {
        if (!canReach(grid, v, s.pos, [item.pos]) || !canReach(grid, v, item.pos, site)) continue;
        return pathPlan(state, s.pos, [item.pos], item.id);
      }
      return null;
    }
  }
}

function pathPlan(state: SimState, from: Point, goals: Point[], itemId: string | null, dest: Point | null = null): Plan | null {
  const path = findPathToAny(state.grid, from, goals);
  return path ? { path, itemId, dest } : null;
}

function claim(state: SimState, s: Survivor, job: Job, plan: Plan, why: string): void {
  job.reservedBy = s.id;
  s.jobId = job.id;
  job.dest = plan.dest;
  if (plan.itemId) {
    job.itemId = plan.itemId;
    const item = getItem(state, plan.itemId);
    if (item) item.reservedBy = s.id;
  }
  walk(s, plan.path, state.walkabilityVersion);
  s.note = `took ${job.type} job at (${job.x}, ${job.y}): ${why}`;
}

// --- Explaining (debug only, read-only) -------------------------------------

/** Why an idle survivor isn't working. Pure: safe to call from the renderer. */
export function explainIdle(state: SimState, s: Survivor): string {
  const types = typesByPriority(s);
  if (types.length === 0) return 'all job types are off';
  const open = state.jobs.filter((j) => j.reservedBy === null && types.includes(j.type));
  if (open.length === 0) return 'no open jobs of enabled types';
  const haulOnly = open.every((j) => j.type === 'haul');
  const freeWood = state.items.some((i) => i.kind === 'wood' && i.pos !== null && i.reservedBy === null);
  if (haulOnly && !freeWood) return `${open.length} haul job(s) waiting for wood`;
  return `${open.length} open job(s), none reachable right now`;
}
