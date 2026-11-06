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

import { cancelBlueprint, getBlueprint, preconditionHolds, type Blueprint } from './designations';
import { findById } from './lookup';
import { getPlot, harvest, needsWork } from './farming';
import { getAnimal, isMenace } from './animals';
import { getFurniture, ofKind, ofKinds, markedForScavenge } from './furniture';
import { isPatient } from './medicine';
import { getExploreArea } from './fog';
import { isDirty, type Point } from './grid';
import { edgeDamage, structureDamage } from './breach';
import { dropItem, getItem, type ItemKind } from './items';
import type { JobType } from './jobTypes';
import { getZombie } from './zombies';
import { completeStoreRooms, homeFor, isIndoors, labelAt, reconcileRoomDesignations, type StoreRooms } from './rooms';
import { fireAt } from './fire';
import { FUEL_MAX, isPowered } from './power';
import { researchWanted } from './endgame';
import { TURRET_AMMO_MAX } from './defences';
import { inWorkshop, isStation, wantedCraft } from './crafting';
import type { SimState } from './state';
import { getSurvivor, type Survivor } from './survivors';
import { claim } from './jobChoice';
export { interruptForFight, updateWork, scavengeFind, interruptScouting, clearShot } from './work';
export { seekJob, chooseJob, typesByPriority, explainIdle } from './jobChoice';

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
  /** Scavenge: the crate being searched. Craft: the workbench. */
  furnitureId: string | null;
  /** Guard only: the zombie being hunted. The job's x, y follow it. */
  zombieId: string | null;
  /** Farm only: the plot to sow or harvest. */
  plotId: string | null;
  /** Doctor only: the survivor being treated. The job's x, y follow them. */
  patientId: string | null;
  /** Scout only: the explore area. The job's x, y are the current target tile. */
  areaId: string | null;
  /** Hunt only: the animal. The job's x, y follow it. */
  animalId: string | null;
  /** Store hauls only: the reserved storage tile. */
  dest: Point | null;
  /** Edge repairs only: the damaged edge (grid.ts); x, y are the tile it's the north or west side of. */
  edge?: number;
  /** Clean and scavenge: ticks of work done. */
  progress: number;
  reservedBy: string | null;
}

/** Things a cart holds, and how far round the first one a hauler gathers the rest. */
export const CART_LOAD = 4;
export const CART_REACH = 4;
/** Ticks to clean one tile. */
export const CLEAN_WORK = 15;
/** Ticks to search a crate once; each search yields one find. */
export const SCAVENGE_WORK = 40;
/** What a crate search turns up (one sim RNG roll against these, in order; they sum to 1). */
export const SCAVENGE_TABLE: readonly [ItemKind, number][] = [
  ['food', 0.29],
  ['wood', 0.2],
  ['fuel', 0.03],
  ['scrap', 0.06],
  ['book', 0.02],
  ['bandage', 0.12],
  ['cloth', 0.12],
  ['weapon', 0.05],
  ['ammo', 0.03],
  ['coat', 0.02],
  ['boots', 0.02],
  ['armour', 0.01],
  ['gun', 0.01],
  ['axe', 0.01],
  ['hammer', 0.01],
];
/** A guard hits an adjacent zombie this often, for this much (zombies have 200: 4 hits). */
export const GUARD_ATTACK_EVERY = 10;
export const GUARD_DAMAGE = 50;
/** How many nearest candidates a survivor checks per job type before giving up on that type. */
export const MAX_CANDIDATES = 8;
/** An idle survivor with nothing doable looks again after this many ticks. */
export const IDLE_RETRY_TICKS = 5;

export function getJob(state: SimState, id: string): Job | undefined {
  return findById(state.jobs, id);
}

// --- Reconcile -------------------------------------------------------------

export function reconcileJobs(state: SimState): void {
  reconcileRoomDesignations(state);

  // Blueprints whose tile changed underneath them (e.g. instant placeWall) are void.
  for (const bp of [...state.blueprints]) {
    if (!preconditionHolds(state, bp)) cancelBlueprint(state, bp);
  }

  const stores = completeStoreRooms(state);
  for (const job of [...state.jobs]) {
    const reason = invalidReason(state, job, stores);
    if (reason) removeJob(state, job, reason);
  }

  const haveJob = new Set(state.jobs.map((j) => j.blueprintId));
  for (const bp of state.blueprints) {
    if (!haveJob.has(bp.id)) addJob(state, wantedJobType(bp), bp.x, bp.y, bp.id, null);
  }

  // Cleaning is indoors only: nobody scrubs the street.
  // (Lists, not a scan of the whole map; sorted so jobs come in map order, as they always have.)
  const { width } = state.grid;
  const cleaning = new Set(state.jobs.filter((j) => j.type === 'clean').map((j) => j.y * width + j.x));
  for (const i of [...state.grid.dirtList].sort((a, b) => a - b)) {
    const x = i % width;
    const y = Math.floor(i / width);
    if (!cleaning.has(i) && isIndoors(state, x, y)) addJob(state, 'clean', x, y, null, null);
  }

  // Loose items outside their home (armory for weapons, else storage) get a store job, if a room can take them.
  const storing = new Set(state.jobs.filter(isStoreJob).map((j) => j.itemId));
  for (const item of state.items) {
    const home = homeFor(stores, item.kind);
    if (!item.pos || home.size === 0 || storing.has(item.id) || home.has(labelAt(state, item.pos.x, item.pos.y))) continue;
    addJob(state, 'haul', item.pos.x, item.pos.y, null, item.id);
  }

  // Generators short of fuel get one fuel haul each.
  const fuelling = new Set(state.jobs.filter(isFuelJob).map((j) => j.furnitureId));
  // (By kind, in list order, as the scan of every piece of furniture found them.)
  for (const f of ofKinds(state, Object.keys(FUEL) as (keyof typeof FUEL)[])) {
    if (f.stored < fuelMax(f.kind) && !fuelling.has(f.id)) addJob(state, 'haul', f.x, f.y, null, null, f.id);
  }

  // Damaged walls and doors get one repair job each.
  const repairing = new Set(state.jobs.filter((j) => isRepairJob(j) && j.edge === undefined).map((j) => j.y * width + j.x));
  for (const i of [...state.grid.damageList].sort((a, b) => a - b)) {
    if (!repairing.has(i)) addJob(state, 'build', i % width, Math.floor(i / width), null, null);
  }
  // And damaged edge walls and doors.
  const mending = new Set(state.jobs.filter((j) => isRepairJob(j) && j.edge !== undefined).map((j) => j.edge!));
  for (const e of Object.keys(state.grid.edgeDamage).map(Number).sort((a, b) => a - b)) {
    if (mending.has(e)) continue;
    const tile = Math.floor(e / 2);
    addJob(state, 'build', tile % width, Math.floor(tile / width), null, null).edge = e;
  }

  // Every explore area gets one scout job.
  const scouting = new Set(state.jobs.filter((j) => j.type === 'scout').map((j) => j.areaId));
  for (const a of state.exploreAreas) {
    if (!scouting.has(a.id)) addJob(state, 'scout', Math.floor((a.x0 + a.x1) / 2), Math.floor((a.y0 + a.y1) / 2), null, null).areaId = a.id;
  }

  // Every patient (hurt, resting in bed) gets one doctor job.
  const treating = new Set(state.jobs.filter((j) => j.type === 'doctor').map((j) => j.patientId));
  for (const p of state.survivors) {
    if (isPatient(p) && !treating.has(p.id)) addJob(state, 'doctor', p.pos.x, p.pos.y, null, null).patientId = p.id;
  }

  // While anything a station makes is wanted, every such station offers one job: craft at a workbench, cook at a stove.
  const wanted = { workbench: wantedCraft(state, 'workbench', true), stove: wantedCraft(state, 'stove') };
  if (wanted.workbench || wanted.stove) {
    const stationed = new Set(state.jobs.filter((j) => j.type === 'craft' || j.type === 'cook').map((j) => j.furnitureId));
    for (const f of ofKinds(state, ['workbench', 'stove'])) {
      if (!isStation(f.kind) || !wanted[f.kind] || stationed.has(f.id) || !wantedCraft(state, f.kind, inWorkshop(state, f))) continue;
      addJob(state, f.kind === 'stove' ? 'cook' : 'craft', f.x, f.y, null, null, f.id);
    }
  }

  // Marked crates with loot left get one scavenge job each.
  const searching = new Set(state.jobs.filter((j) => j.type === 'scavenge').map((j) => j.furnitureId));
  for (const f of markedForScavenge(state)) {
    if (f.scavenge && (f.kind === 'crate' || f.kind === 'tree') && f.loot > 0 && !searching.has(f.id)) {
      addJob(state, 'scavenge', f.x, f.y, null, null, f.id);
    }
  }

  // Farm plots that are empty (sow) or ripe (harvest) get one farm job each.
  const tending = new Set(state.jobs.filter((j) => j.type === 'farm').map((j) => j.plotId));
  for (const plot of state.farm) {
    if (needsWork(plot) && !tending.has(plot.id)) addJob(state, 'farm', plot.x, plot.y, null, null, null, null, plot.id);
  }

  // Every fire gets one firefight job.
  const fighting = new Set(state.jobs.filter((j) => j.type === 'firefight').map((j) => j.y * width + j.x));
  for (const f of state.fires) {
    if (!fighting.has(f.y * width + f.x)) addJob(state, 'firefight', f.x, f.y, null, null);
  }

  // A powered lab bench gets a research job while there's a cure to find and something to study.
  if (researchWanted(state)) {
    const researching = new Set(state.jobs.filter((j) => j.type === 'research').map((j) => j.furnitureId));
    const samples = state.items.some((i) => i.kind === 'sample' && i.pos !== null);
    for (const f of ofKind(state, 'lab')) {
      if (!researching.has(f.id) && (f.stored > 0 || samples) && isPowered(state, f)) addJob(state, 'research', f.x, f.y, null, null, f.id);
    }
  }

  // Every animal marked for hunting gets one hunt job.
  const hunting = new Set(state.jobs.filter((j) => j.type === 'hunt').map((j) => j.animalId));
  for (const a of state.animals) if (a.hunted && !hunting.has(a.id)) addJob(state, 'hunt', a.pos.x, a.pos.y, null, null).animalId = a.id;

  // Every zombie gets one guard job.
  const hunted = new Set(state.jobs.filter((j) => j.type === 'guard').map((j) => j.zombieId));
  for (const z of state.zombies) {
    if (!hunted.has(z.id)) addJob(state, 'guard', z.pos.x, z.pos.y, null, null, null, z.id);
  }
  // So does a wolf or bear that's going for your people (animals.ts isMenace): guards fight it off.
  const guarding = new Set(state.jobs.filter((j) => j.type === 'guard' && j.animalId).map((j) => j.animalId));
  for (const a of state.animals) if (isMenace(state, a) && !guarding.has(a.id)) addJob(state, 'guard', a.pos.x, a.pos.y, null, null).animalId = a.id;
}

/** Repairs are build jobs with no blueprint: they fix a damaged wall or door at x, y. */
export function isRepairJob(job: Job): boolean {
  return job.type === 'build' && job.blueprintId === null;
}

export function isStoreJob(job: Job): boolean {
  return job.type === 'haul' && job.blueprintId === null && job.furnitureId === null;
}

/** What each fed piece of furniture takes, and how much it holds. */
export const FUEL = { generator: 'wood', heater: 'wood', turret: 'ammo' } as const;
function fuelMax(kind: string): number {
  return kind === 'turret' ? TURRET_AMMO_MAX : FUEL_MAX;
}

/** Fuel hauls: wood to a generator or heater, ammo to a turret (`furnitureId`). */
export function isFuelJob(job: Job): boolean {
  return job.type === 'haul' && job.furnitureId !== null;
}

function wantedJobType(bp: Blueprint): JobType {
  return bp.needsMaterial && !bp.delivered ? 'haul' : 'build';
}

function invalidReason(state: SimState, job: Job, stores: StoreRooms): string | null {
  switch (job.type) {
    case 'clean':
      if (!isDirty(state.grid, job.x, job.y)) return 'tile already clean';
      return isIndoors(state, job.x, job.y) ? null : 'tile is no longer indoors';
    case 'farm': {
      const plot = job.plotId ? getPlot(state, job.plotId) : undefined;
      if (!plot) return 'plot gone';
      return needsWork(plot) ? null : 'crop is growing';
    }
    case 'guard': {
      if (job.animalId) {
        const a = getAnimal(state, job.animalId);
        if (!a || !isMenace(state, a)) return 'the animal is gone';
        job.x = a.pos.x;
        job.y = a.pos.y;
        return null;
      }
      const z = job.zombieId ? getZombie(state, job.zombieId) : undefined;
      if (!z) return 'zombie gone';
      job.x = z.pos.x;
      job.y = z.pos.y;
      return null;
    }
    case 'scout':
      return job.areaId && getExploreArea(state, job.areaId) ? null : 'area explored';
    case 'firefight':
      return fireAt(state, job.x, job.y) ? null : 'fire is out';
    case 'research': {
      const lab = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
      if (!lab || lab.kind !== 'lab') return 'lab gone';
      if (!researchWanted(state)) return 'nothing left to find';
      if (job.itemId) {
        const item = getItem(state, job.itemId);
        if (!item) return 'item gone';
        if (item.carriedBy && item.carriedBy !== job.reservedBy) return 'item taken by someone else';
      }
      return isPowered(state, lab) ? null : 'the lab has no power';
    }
    case 'hunt': {
      const a = job.animalId ? getAnimal(state, job.animalId) : undefined;
      if (!a) return 'the animal got away';
      job.x = a.pos.x;
      job.y = a.pos.y;
      return null;
    }
    case 'doctor': {
      const patient = job.patientId ? getSurvivor(state, job.patientId) : undefined;
      if (!patient || !isPatient(patient)) return 'patient is up and about';
      job.x = patient.pos.x;
      job.y = patient.pos.y;
      if (job.itemId) {
        const item = getItem(state, job.itemId);
        if (!item) return 'item gone';
        if (item.carriedBy && item.carriedBy !== job.reservedBy) return 'item taken by someone else';
      }
      return null;
    }
    case 'craft':
    case 'cook': {
      const station = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
      if (!station || !isStation(station.kind)) return 'station gone';
      if (job.itemId) {
        const item = getItem(state, job.itemId);
        if (!item) return 'item gone';
        if (item.carriedBy && item.carriedBy !== job.reservedBy) return 'item taken by someone else';
      }
      return wantedCraft(state, station.kind, inWorkshop(state, station)) ? null : 'stocks are full';
    }
    case 'scavenge': {
      const crate = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
      if (!crate) return 'crate gone';
      if (!crate.scavenge) return 'no longer marked for scavenging';
      return crate.loot > 0 ? null : crate.kind === 'tree' ? 'tree is bare' : 'crate is empty';
    }
    case 'build':
    case 'haul': {
      if (isRepairJob(job)) return (job.edge !== undefined ? edgeDamage(state.grid, job.edge) : structureDamage(state, job.x, job.y)) > 0 ? null : 'nothing to repair';
      if (isStoreJob(job)) return storeInvalidReason(state, job, stores);
      if (isFuelJob(job)) {
        const g = getFurniture(state, job.furnitureId!);
        if (!g || !(g.kind in FUEL)) return 'generator gone';
        if (job.itemId) {
          const item = getItem(state, job.itemId);
          if (!item) return 'item gone';
          if (item.carriedBy && item.carriedBy !== job.reservedBy) return 'item taken by someone else';
        }
        return g.stored < fuelMax(g.kind) ? null : `${g.kind} is full`;
      }
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

function storeInvalidReason(state: SimState, job: Job, stores: StoreRooms): string | null {
  const item = job.itemId ? getItem(state, job.itemId) : undefined;
  if (!item) return 'item gone';
  if (item.carriedBy && item.carriedBy !== job.reservedBy) return 'item taken by someone else';
  const home = homeFor(stores, item.kind);
  if (home.size === 0) return 'no complete storage room';
  if (item.pos) {
    if (home.has(labelAt(state, item.pos.x, item.pos.y))) return 'already in storage';
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
  zombieId: string | null = null,
  plotId: string | null = null,
): Job {
  const job: Job = {
    id: `j${state.nextId++}`,
    type,
    x,
    y,
    blueprintId,
    itemId,
    furnitureId,
    zombieId,
    plotId,
    patientId: null,
    areaId: null,
    animalId: null,
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
  for (const id of s.cartLoad) {
    const item = getItem(state, id);
    if (item) dropItem(state, item, s.pos);
  }
  s.cartLoad = [];
  s.jobId = null;
  s.goal = null;
  s.path = [];
  s.pathVersion = -1;
  s.note = `stopped: ${reason}`;
}
