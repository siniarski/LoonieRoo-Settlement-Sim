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

import { cancelBlueprint, completeBlueprint, getBlueprint, materialFor, preconditionHolds, type Blueprint } from './designations';
import { FARM_WORK, getPlot, harvest, isRipe, needsWork, plotPoint } from './farming';
import { HUNT_WORK, VENISON, getAnimal } from './animals';
import { getFurniture } from './furniture';
import { TREAT_TICKS } from './health';
import { GUARD_DAMAGE_PER_LEVEL, HARDENED_DAMAGE, hasTrait, skillLevel, workStep } from './skills';
import { applyRemedy, isPatient, remediesFor } from './medicine';
import { getExploreArea, isExplored, scoutTarget, scoutingOver } from './fog';
import { getTile, isDirty, isWalkable, neighbours4, setDirty, type Point } from './grid';
import { REPAIR_PER_TICK, repairStructure, structureDamage } from './breach';
import { canHoldItem, dropItem, getItem, removeItem, spawnItem, type Item, type ItemKind } from './items';
import { JOB_TYPES, type JobType } from './jobTypes';
import { canReach, findPathToAny } from './pathfinding';
import { random } from './rng';
import { damageZombie, getZombie } from './zombies';
import { completeStoreRooms, getRoomMap, homeFor, isIndoors, labelAt, reconcileRoomDesignations, type StoreRooms } from './rooms';
import { GUN_DAMAGE, GUN_EVERY, GUN_RANGE, blowDamage, loadedGun, wearWeapon } from './weapons';
import { makeNoise } from './noise';
import { DOUSE_WORK, STOVE_FIRE_CHANCE, douse, fireAt, ignite } from './fire';
import { hashRoll } from './hash';
import { startReading } from './books';
import { isWet } from './seasons';
import { FUEL_MAX, LIGHT_DAMAGE_PCT, inLight, isPowered } from './power';
import { CURE_SAMPLES, RESEARCH_WORK, researchWanted, studied } from './endgame';
import { policyOn } from './policies';
import { TURRET_AMMO_MAX, seen } from './defences';
import { isNight } from './time';
import { logEvent } from './events';
import { TOWER_RANGE_BONUS, keepWatch, onTower } from './towers';
import { CRAFT_WORK, RECIPES, WORKSHOP_CRAFT_WORK, inWorkshop, isStation, stationCraft, wantedCraft } from './crafting';
import { startLeisure } from './leisure';
import { manhattan, nearestTile, samePoint } from './space';
import type { SimState } from './state';
import { getSurvivor, setGoal, walk, type Survivor } from './survivors';

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
  for (const f of state.furniture) {
    if (f.kind in FUEL && f.stored < fuelMax(f.kind) && !fuelling.has(f.id)) addJob(state, 'haul', f.x, f.y, null, null, f.id);
  }

  // Damaged walls and doors get one repair job each.
  const repairing = new Set(state.jobs.filter(isRepairJob).map((j) => j.y * width + j.x));
  for (const i of [...state.grid.damageList].sort((a, b) => a - b)) {
    if (!repairing.has(i)) addJob(state, 'build', i % width, Math.floor(i / width), null, null);
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
    for (const f of state.furniture) {
      if (!isStation(f.kind) || !wanted[f.kind] || stationed.has(f.id) || !wantedCraft(state, f.kind, inWorkshop(state, f))) continue;
      addJob(state, f.kind === 'stove' ? 'cook' : 'craft', f.x, f.y, null, null, f.id);
    }
  }

  // Marked crates with loot left get one scavenge job each.
  const searching = new Set(state.jobs.filter((j) => j.type === 'scavenge').map((j) => j.furnitureId));
  for (const f of state.furniture) {
    if ((f.kind === 'crate' || f.kind === 'tree') && f.scavenge && f.loot > 0 && !searching.has(f.id)) {
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
    for (const f of state.furniture) {
      if (f.kind === 'lab' && !researching.has(f.id) && (f.stored > 0 || samples) && isPowered(state, f)) addJob(state, 'research', f.x, f.y, null, null, f.id);
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
}

/** Repairs are build jobs with no blueprint: they fix a damaged wall or door at x, y. */
export function isRepairJob(job: Job): boolean {
  return job.type === 'build' && job.blueprintId === null;
}

export function isStoreJob(job: Job): boolean {
  return job.type === 'haul' && job.blueprintId === null && job.furnitureId === null;
}

/** What each fed piece of furniture takes, and how much it holds. */
const FUEL = { generator: 'wood', heater: 'wood', turret: 'ammo' } as const;
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
      if (isRepairJob(job)) return structureDamage(state, job.x, job.y) > 0 ? null : 'nothing to repair';
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

// --- Working ----------------------------------------------------------------

/** Runs when the survivor is standing still: continue the current job, or look for one. */
export function updateWork(state: SimState, s: Survivor): void {
  if (!s.jobId) {
    if (state.tick < s.nextSeekTick) return;
    if (seekJob(state, s)) return;
    s.nextSeekTick = state.tick + IDLE_RETRY_TICKS;
    if (!keepWatch(state, s) && !startReading(state, s)) startLeisure(state, s);
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
    case 'craft':
    case 'cook':
      return workCraft(state, s, job);
    case 'doctor':
      return workDoctor(state, s, job);
    case 'scout':
      return workScout(state, s, job);
    case 'guard':
      return workGuard(state, s, job);
    case 'firefight':
      return workFirefight(state, s, job);
    case 'hunt':
      return workHunt(state, s, job);
    case 'research':
      return workResearch(state, s, job);
    case 'farm':
      return workFarm(state, s, job);
  }
}

/** Sow an empty plot or harvest a ripe one, standing on it. */
function workFarm(state: SimState, s: Survivor, job: Job): void {
  const plot = job.plotId ? getPlot(state, job.plotId) : undefined;
  if (!plot || !needsWork(plot)) return releaseSurvivor(state, s, 'plot no longer needs work');
  if (!samePoint(s.pos, plot)) return goTo(state, s, job, [plotPoint(plot)]);
  const step = workStep(state, s, 'farm');
  if (step === 0) return;
  job.progress += step;
  if (job.progress < FARM_WORK) return;
  if (isRipe(plot)) {
    harvest(state, plot);
    finishJob(state, s, job, `harvested ${plot.crop === 'herb' ? 'herbs' : plot.crop === 'wheat' ? 'wheat' : 'food'} at (${plot.x}, ${plot.y})`);
  } else {
    plot.planted = true;
    plot.growth = 0;
    finishJob(state, s, job, `sowed (${plot.x}, ${plot.y})`);
  }
}

/** Hunt a zombie: close in, then hit it every GUARD_ATTACK_EVERY ticks until it drops. */
function workGuard(state: SimState, s: Survivor, job: Job): void {
  const z = job.zombieId ? getZombie(state, job.zombieId) : undefined;
  if (!z) return releaseSurvivor(state, s, 'zombie gone');
  // With a loaded gun: shoot from where you stand if you can, else close in a tile at a time.
  const gun = loadedGun(state, s);
  const range = GUN_RANGE + (onTower(state, s) ? TOWER_RANGE_BONUS : 0);
  if (gun && manhattan(s.pos, z.pos) > 1 && manhattan(s.pos, z.pos) <= range && clearShot(state.grid, s.pos, z.pos)) {
    if (workStep(state, s, 'guard') === 0) return;
    job.progress += 1;
    if (job.progress % GUN_EVERY !== 1) return;
    gun.wear -= 1;
    makeNoise(state, s.pos, 'gunfire');
    if (!damageZombie(state, z, GUN_DAMAGE + GUARD_DAMAGE_PER_LEVEL * skillLevel(s, 'guard') + (hasTrait(s, 'hardened') ? HARDENED_DAMAGE : 0), s)) return;
    return finishJob(state, s, job, `shot a zombie at (${z.pos.x}, ${z.pos.y})`);
  }
  if (manhattan(s.pos, z.pos) > 1) return goTo(state, s, job, neighbours4(state.grid, z.pos.x, z.pos.y), gun ? 1 : undefined);
  if (workStep(state, s, 'guard') === 0) return;
  job.progress += 1;
  // First blow lands on arrival (progress 1), then every GUARD_ATTACK_EVERY ticks.
  if (job.progress % GUARD_ATTACK_EVERY !== 1) return;
  makeNoise(state, s.pos, 'fighting');
  const blow = blowDamage(s, GUARD_DAMAGE) + GUARD_DAMAGE_PER_LEVEL * skillLevel(s, 'guard') + (hasTrait(s, 'hardened') ? HARDENED_DAMAGE : 0);
  if (!damageZombie(state, z, inLight(state, s.pos) ? Math.round((blow * LIGHT_DAMAGE_PCT) / 100) : blow, s)) return;
  wearWeapon(state, s);
  finishJob(state, s, job, `killed a zombie at (${z.pos.x}, ${z.pos.y})`);
}

/** Bring a sample to the lab bench (it holds one), then study it. */
function workResearch(state: SimState, s: Survivor, job: Job): void {
  const lab = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
  if (!lab || lab.kind !== 'lab') return releaseSurvivor(state, s, 'lab gone');
  const site = neighbours4(state.grid, lab.x, lab.y);
  if (lab.stored === 0) {
    let item = job.itemId ? getItem(state, job.itemId) : undefined;
    if (!item) {
      item = freeItemFor(state, 'sample', s.pos, site);
      if (!item) return releaseSurvivor(state, s, 'no samples to study');
      job.itemId = item.id;
      item.reservedBy = s.id;
    }
    if (s.carrying !== item.id) {
      if (!item.pos) return releaseSurvivor(state, s, 'item taken by someone else');
      if (!samePoint(s.pos, item.pos)) return goTo(state, s, job, [item.pos]);
      item.pos = null;
      item.carriedBy = s.id;
      s.carrying = item.id;
    }
    if (manhattan(s.pos, lab) !== 1) return goTo(state, s, job, site);
    removeItem(state, item);
    s.carrying = null;
    job.itemId = null;
    lab.stored = 1;
    lab.storedKind = 'sample';
    s.note = 'brought a sample to the lab';
    return;
  }
  if (manhattan(s.pos, lab) !== 1) return goTo(state, s, job, site);
  const step = workStep(state, s, 'research');
  if (step === 0) return;
  job.progress += step;
  if (job.progress < RESEARCH_WORK) return;
  lab.stored = 0;
  lab.storedKind = null;
  studied(state);
  finishJob(state, s, job, `studied a sample (${state.endgame.cure}/${CURE_SAMPLES})`);
}

/** Close in on the animal a few tiles at a time (it moves), then take it down. */
function workHunt(state: SimState, s: Survivor, job: Job): void {
  const a = job.animalId ? getAnimal(state, job.animalId) : undefined;
  if (!a) return releaseSurvivor(state, s, 'the animal got away');
  if (manhattan(s.pos, a.pos) > 1) return goTo(state, s, job, neighbours4(state.grid, a.pos.x, a.pos.y), 3);
  const step = workStep(state, s, 'hunt');
  if (step === 0) return;
  job.progress += step;
  if (job.progress < HUNT_WORK) return;
  state.animals.splice(state.animals.indexOf(a), 1);
  for (let n = 0; n < VENISON; n++) spawnItem(state, 'food', a.pos.x, a.pos.y);
  makeNoise(state, a.pos, 'fighting');
  finishJob(state, s, job, `hunted a deer at (${a.pos.x}, ${a.pos.y})`);
}

/** Beat a fire out from next to it. */
function workFirefight(state: SimState, s: Survivor, job: Job): void {
  const fire = fireAt(state, job.x, job.y);
  if (!fire) return releaseSurvivor(state, s, 'fire is out');
  if (manhattan(s.pos, fire) !== 1) return goTo(state, s, job, neighbours4(state.grid, fire.x, fire.y));
  const step = workStep(state, s, 'firefight');
  if (step === 0) return;
  job.progress += step * (isWet(state.weather) ? 2 : 1);
  if (job.progress < DOUSE_WORK) return;
  douse(state, fire);
  finishJob(state, s, job, `put out a fire at (${fire.x}, ${fire.y})`);
}

/** Search a crate from next to it; each completed search drops one find by the survivor. */
function workScavenge(state: SimState, s: Survivor, job: Job): void {
  const crate = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
  if (!crate || crate.loot <= 0) return releaseSurvivor(state, s, 'crate is empty');
  if (manhattan(s.pos, crate) !== 1) return goTo(state, s, job, neighbours4(state.grid, crate.x, crate.y));
  const step = workStep(state, s, 'scavenge');
  if (step === 0) return;
  job.progress += step;
  if (job.progress < SCAVENGE_WORK) return;
  // Trees only ever give wood (no roll); crates roll their find.
  const kind = crate.kind === 'tree' ? 'wood' : scavengeFind(random(state));
  spawnItem(state, kind, s.pos.x, s.pos.y);
  crate.loot -= 1;
  if (crate.loot === 0 && crate.kind === 'crate') crate.scavenge = false; // trees stay marked: they grow back
  finishJob(state, s, job, crate.kind === 'tree' ? `chopped wood from (${crate.x}, ${crate.y}), ${crate.loot} left` : `scavenged ${kind} from (${crate.x}, ${crate.y}), ${crate.loot} left`);
}

/** Maps a [0, 1) roll to a find (SCAVENGE_TABLE). */
export function scavengeFind(roll: number): ItemKind {
  let at = 0;
  for (const [kind, chance] of SCAVENGE_TABLE) {
    at += chance;
    if (roll < at) return kind;
  }
  return SCAVENGE_TABLE.at(-1)![0];
}

/** Bring the input to the station one piece at a time, then make the most-wanted thing it makes. */
function workCraft(state: SimState, s: Survivor, job: Job): void {
  const bench = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
  if (!bench || !isStation(bench.kind)) return releaseSurvivor(state, s, 'station gone');
  const workshop = inWorkshop(state, bench);
  const kind = stationCraft(state, bench);
  if (!kind) return releaseSurvivor(state, s, 'stocks are full');
  if (bench.stored > 0 && bench.storedKind !== RECIPES[kind].input) {
    // Nothing wanted uses what's in it any more: put that back out.
    for (let n = 0; n < bench.stored; n++) spawnItem(state, bench.storedKind ?? 'wood', bench.x, bench.y);
    bench.stored = 0;
    bench.storedKind = null;
  }
  const site = neighbours4(state.grid, bench.x, bench.y);
  const recipe = RECIPES[kind];
  const cost = recipe.count;
  const station = bench.kind === 'stove' ? 'the stove' : 'the workbench';

  if (bench.stored < cost) {
    let item = job.itemId ? getItem(state, job.itemId) : undefined;
    if (!item) {
      item = freeItemFor(state, recipe.input, s.pos, site);
      if (!item) return releaseSurvivor(state, s, `no ${recipe.input} for ${station}`);
      job.itemId = item.id;
      item.reservedBy = s.id;
    }
    if (s.carrying !== item.id) {
      if (!item.pos) return releaseSurvivor(state, s, 'item taken by someone else');
      if (!samePoint(s.pos, item.pos)) return goTo(state, s, job, [item.pos]);
      item.pos = null;
      item.carriedBy = s.id;
      s.carrying = item.id;
      s.note = `picked up ${recipe.input} for ${station}`;
    }
    if (manhattan(s.pos, bench) !== 1) return goTo(state, s, job, site);
    removeItem(state, item);
    s.carrying = null;
    job.itemId = null;
    bench.stored += 1;
    bench.storedKind = recipe.input;
    s.note = `brought ${recipe.input} to ${station} (${bench.stored}/${cost})`;
    return;
  }

  if (manhattan(s.pos, bench) !== 1) return goTo(state, s, job, site);
  const step = workStep(state, s, job.type);
  if (step === 0) return;
  job.progress += step;
  if (job.progress < (workshop ? WORKSHOP_CRAFT_WORK : CRAFT_WORK)) return;
  bench.stored -= cost;
  if (bench.stored === 0) bench.storedKind = null;
  spawnItem(state, kind, s.pos.x, s.pos.y);
  // Now and then the stove sets something alight.
  if (kind === 'meal' && hashRoll(bench.x, bench.y, state.tick) < STOVE_FIRE_CHANCE && ignite(state, bench.x, bench.y)) {
    logEvent(state, `The stove at (${bench.x}, ${bench.y}) caught fire!`);
  }
  const made = kind === 'meal' ? 'cooked a hot meal' : kind === 'medicine' ? 'made medicine' : `crafted ${kind === 'armour' || kind === 'boots' ? '' : 'a '}${kind}`;
  finishJob(state, s, job, `${made} at (${bench.x}, ${bench.y})`);
}

/** Walk to the nearest unexplored tile in the area (seeing round you as you go), and on to the next, until it's all seen. */
/** Runs every tick before movement: a scout still out (even mid-walk) at SCOUT_HOME_HOUR turns for home. */
export function interruptScouting(state: SimState, s: Survivor): void {
  if (s.jobId && scoutingOver(state.tick) && getJob(state, s.jobId)?.type === 'scout') headHome(state, s);
}

/** Evening: drop the scouting and walk back to your own bed, or to the others. */
function headHome(state: SimState, s: Survivor): void {
  releaseSurvivor(state, s, 'heading home before dark');
  s.note = 'heading home before dark';
  const bed = state.furniture.find((f) => f.owner === s.id);
  const others = state.survivors.filter((o) => o !== s);
  const home = bed ?? (others.length > 0 ? { x: Math.round(others.reduce((a, o) => a + o.pos.x, 0) / others.length), y: Math.round(others.reduce((a, o) => a + o.pos.y, 0) / others.length) } : null);
  if (!home) return;
  const spot = nearestTile(state.grid, home, (x, y) => isWalkable(state.grid, x, y) && canReach(state.grid, state.walkabilityVersion, s.pos, [{ x, y }]));
  if (spot && !samePoint(spot, s.pos)) setGoal(s, spot);
}

function workScout(state: SimState, s: Survivor, job: Job): void {
  const area = job.areaId ? getExploreArea(state, job.areaId) : undefined;
  if (!area) return releaseSurvivor(state, s, 'area explored');
  if (scoutingOver(state.tick)) return headHome(state, s);
  const target = isExplored(state, job.x, job.y) ? scoutTarget(state, area, s.pos) : { x: job.x, y: job.y };
  if (!target) return finishJob(state, s, job, `scouted (${area.x0}, ${area.y0})-(${area.x1}, ${area.y1}) as far as it goes`);
  job.x = target.x;
  job.y = target.y;
  if (!samePoint(s.pos, target)) return goTo(state, s, job, [target]);
}

/** Fetch a bandage, carry it to the patient's bedside, and treat them. */
function workDoctor(state: SimState, s: Survivor, job: Job): void {
  const patient = job.patientId ? getSurvivor(state, job.patientId) : undefined;
  if (!patient || !isPatient(patient)) return releaseSurvivor(state, s, 'patient is up and about');
  const site = neighbours4(state.grid, patient.pos.x, patient.pos.y);
  let item = job.itemId ? getItem(state, job.itemId) : undefined;
  if (!item) {
    item = remediesFor(patient).map((k) => freeItemFor(state, k, s.pos, site)).find((i) => i !== undefined);
    if (!item) return releaseSurvivor(state, s, 'nothing to treat them with');
    job.itemId = item.id;
    item.reservedBy = s.id;
  }
  if (s.carrying !== item.id) {
    if (!item.pos) return releaseSurvivor(state, s, 'item taken by someone else');
    if (!samePoint(s.pos, item.pos)) return goTo(state, s, job, [item.pos]);
    item.pos = null;
    item.carriedBy = s.id;
    s.carrying = item.id;
    s.note = `picked up ${item.kind === 'medicine' ? 'medicine' : 'a bandage'} for ${patient.name}`;
  }
  if (manhattan(s.pos, patient.pos) !== 1) return goTo(state, s, job, site);
  const step = workStep(state, s, 'doctor');
  if (step === 0) return;
  job.progress += step;
  if (job.progress < TREAT_TICKS) return;
  removeItem(state, item);
  s.carrying = null;
  applyRemedy(patient, item.kind);
  finishJob(state, s, job, item.kind === 'medicine' ? `cured ${patient.name}'s infection` : `treated ${patient.name}'s wounds`);
}

/** Nearest free item of a kind that the survivor can reach and carry on to the site. */
function freeItemFor(state: SimState, kind: ItemKind, from: Point, site: Point[]): (Item & { pos: Point }) | undefined {
  const { grid, walkabilityVersion: v } = state;
  const free = state.items
    .filter((i): i is Item & { pos: Point } => i.pos !== null && i.reservedBy === null && i.kind === kind)
    .sort((a, b) => manhattan(from, a.pos) - manhattan(from, b.pos));
  return free.slice(0, MAX_CANDIDATES).find((i) => canReach(grid, v, from, [i.pos]) && canReach(grid, v, i.pos, site));
}

function workClean(state: SimState, s: Survivor, job: Job): void {
  const target = { x: job.x, y: job.y };
  if (!samePoint(s.pos, target)) return goTo(state, s, job, [target]);
  const step = workStep(state, s, 'clean');
  if (step === 0) return;
  job.progress += step;
  if (job.progress < CLEAN_WORK) return;
  setDirty(state.grid, job.x, job.y, false);
  finishJob(state, s, job, `cleaned (${job.x}, ${job.y})`);
}

function workBuild(state: SimState, s: Survivor, job: Job): void {
  if (isRepairJob(job)) return workRepair(state, s, job);
  const bp = job.blueprintId ? getBlueprint(state, job.blueprintId) : undefined;
  if (!bp) return releaseSurvivor(state, s, 'designation cancelled');
  if (manhattan(s.pos, bp) !== 1) return goTo(state, s, job, neighbours4(state.grid, bp.x, bp.y));
  const step = workStep(state, s, 'build');
  if (step === 0) return;
  bp.workDone += step;
  if (bp.workDone < bp.workRequired) return;
  completeBlueprint(state, bp);
  makeNoise(state, bp, 'building');
  finishJob(state, s, job, bp.target === 'floor' ? `removed structure at (${bp.x}, ${bp.y})` : `built ${bp.target} at (${bp.x}, ${bp.y})`);
}

/** Patch up a damaged wall or door from a tile next to it. */
function workRepair(state: SimState, s: Survivor, job: Job): void {
  if (manhattan(s.pos, job) !== 1) return goTo(state, s, job, neighbours4(state.grid, job.x, job.y));
  const step = workStep(state, s, 'build');
  if (step === 0) return;
  if (repairStructure(state, job.x, job.y, REPAIR_PER_TICK * step) > 0) return;
  makeNoise(state, job, 'building');
  finishJob(state, s, job, `repaired ${getTile(state.grid, job.x, job.y)} at (${job.x}, ${job.y})`);
}

function workHaul(state: SimState, s: Survivor, job: Job): void {
  if (isFuelJob(job)) return workFuel(state, s, job);
  const item = job.itemId ? getItem(state, job.itemId) : undefined;
  if (!item) return releaseSurvivor(state, s, 'item gone');

  if (s.carrying !== item.id) {
    if (!item.pos) return releaseSurvivor(state, s, 'item taken by someone else');
    if (!samePoint(s.pos, item.pos)) return goTo(state, s, job, [item.pos]);
    item.pos = null;
    item.carriedBy = s.id;
    s.carrying = item.id;
    s.note = `picked up ${item.kind}`;
    if (isStoreJob(job) && s.cart) loadCart(state, s, item);
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

/** Carry a piece of wood to a generator and feed it in. */
function workFuel(state: SimState, s: Survivor, job: Job): void {
  const g = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
  if (!g || !(g.kind in FUEL)) return releaseSurvivor(state, s, `${g?.kind ?? 'generator'} gone`);
  const fuel = FUEL[g.kind as keyof typeof FUEL];
  const site = neighbours4(state.grid, g.x, g.y);
  let item = job.itemId ? getItem(state, job.itemId) : undefined;
  if (!item) {
    item = freeItemFor(state, fuel, s.pos, site);
    if (!item) return releaseSurvivor(state, s, `no ${fuel} for the ${g.kind}`);
    job.itemId = item.id;
    item.reservedBy = s.id;
  }
  if (s.carrying !== item.id) {
    if (!item.pos) return releaseSurvivor(state, s, 'item taken by someone else');
    if (!samePoint(s.pos, item.pos)) return goTo(state, s, job, [item.pos]);
    item.pos = null;
    item.carriedBy = s.id;
    s.carrying = item.id;
  }
  if (manhattan(s.pos, g) !== 1) return goTo(state, s, job, site);
  removeItem(state, item);
  s.carrying = null;
  g.stored += 1;
  g.storedKind = fuel;
  finishJob(state, s, job, `${fuel === 'ammo' ? 'loaded' : 'fuelled'} the ${g.kind} at (${g.x}, ${g.y})`);
}

function carryToStorage(state: SimState, s: Survivor, job: Job, item: Item): void {
  const home = homeFor(completeStoreRooms(state), item.kind);
  if (!job.dest || !storageTileFree(state, job, job.dest, home)) {
    // The reserved tile got taken (or the room changed): pick another.
    job.dest = pickStorageTile(state, s.pos, job, home);
    if (!job.dest) return releaseSurvivor(state, s, 'no free storage tile');
  }
  if (!samePoint(s.pos, job.dest)) return goTo(state, s, job, [job.dest]);
  item.pos = { ...job.dest };
  item.carriedBy = null;
  item.reservedBy = null;
  s.carrying = null;
  // Unload the cart on the nearest free storage tiles (or here, if there are none).
  const extra = s.cartLoad.length;
  for (const id of s.cartLoad) {
    const load = getItem(state, id);
    if (!load) continue;
    const spot = pickStorageTile(state, s.pos, job, homeFor(completeStoreRooms(state), load.kind));
    load.carriedBy = null;
    load.reservedBy = null;
    if (spot) load.pos = spot;
    else dropItem(state, load, s.pos);
  }
  s.cartLoad = [];
  finishJob(state, s, job, `stored ${item.kind}${extra ? ` and ${extra} more from the cart` : ''} at (${job.dest.x}, ${job.dest.y})`);
}

/** With a cart: take up to CART_LOAD - 1 more loose things from round here that belong in some store room. */
function loadCart(state: SimState, s: Survivor, first: Item): void {
  const stores = completeStoreRooms(state);
  const astray = (i: Item & { pos: Point }) => {
    const home = homeFor(stores, i.kind);
    return home.size > 0 && !home.has(labelAt(state, i.pos.x, i.pos.y));
  };
  const extra = state.items
    .filter(
      (i): i is Item & { pos: Point } =>
        i.pos !== null && i.reservedBy === null && manhattan(i.pos, s.pos) <= CART_REACH && astray(i as Item & { pos: Point }),
    )
    .sort((a, b) => manhattan(a.pos, s.pos) - manhattan(b.pos, s.pos))
    .slice(0, CART_LOAD - 1);
  for (const i of extra as Item[]) {
    i.pos = null;
    i.carriedBy = s.id;
    s.cartLoad.push(i.id);
  }
  if (extra.length > 0) s.note = `loaded ${first.kind} and ${extra.length} more into the cart`;
}

/** A storage tile is free if it can hold an item and no other active store job has reserved it. */
function storageTileFree(state: SimState, job: Job, p: Point, storage: Set<number>): boolean {
  return (
    storage.has(labelAt(state, p.x, p.y)) &&
    canHoldItem(state, p.x, p.y) &&
    !state.jobs.some((j) => j !== job && j.reservedBy !== null && j.dest !== null && samePoint(j.dest, p))
  );
}

/** Nearest free, reachable tile in any of the given complete rooms. */
function pickStorageTile(state: SimState, from: Point, job: Job, storage: Set<number>): Point | null {
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

/** Walk towards the goals (only the first `steps` tiles, to look again sooner). */
function goTo(state: SimState, s: Survivor, job: Job, goals: Point[], steps?: number): void {
  const path = findPathToAny(state.grid, s.pos, goals);
  if (!path || path.length === 0) {
    releaseSurvivor(state, s, `can't reach ${job.type} job at (${job.x}, ${job.y})`);
    return;
  }
  walk(s, steps ? path.slice(0, steps) : path, state.walkabilityVersion);
}

/** No wall, door or barricade on the line between two tiles (Bresenham, ends excluded). */
export function clearShot(grid: SimState['grid'], a: Point, b: Point): boolean {
  let x = a.x;
  let y = a.y;
  const dx = Math.abs(b.x - a.x);
  const dy = -Math.abs(b.y - a.y);
  const sx = a.x < b.x ? 1 : -1;
  const sy = a.y < b.y ? 1 : -1;
  let err = dx + dy;
  for (;;) {
    const e2 = 2 * err;
    if (e2 >= dy) {
      err += dy;
      x += sx;
    }
    if (e2 <= dx) {
      err += dx;
      y += sy;
    }
    if (x === b.x && y === b.y) return true;
    const t = getTile(grid, x, y);
    if (t === 'wall' || t === 'door' || t === 'barricade') return false;
  }
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
    // Curfew: at night, only guards, firefighters and doctors work outdoors.
    const curfew = policyOn(state, 'curfew') && isNight(state.tick) && type !== 'guard' && type !== 'firefight' && type !== 'doctor';
    for (const job of open.filter((j) => !curfew || isIndoors(state, j.x, j.y)).slice(0, MAX_CANDIDATES)) {
      const plan = planJob(state, s, job);
      if (plan) return { job, plan, why: `priority ${s.priorities[type]}, nearest of ${open.length} open` };
    }
  }
  return null;
}

export function typesByPriority(s: Survivor): JobType[] {
  if (s.childUntil !== null) return []; // children don't work
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
    case 'clean':
    case 'farm': {
      // Done standing on the tile.
      const goal = [{ x: job.x, y: job.y }];
      if (!canReach(grid, v, s.pos, goal)) return null;
      return pathPlan(state, s.pos, goal, null);
    }
    case 'build':
    case 'scavenge':
    case 'firefight':
    case 'hunt':
    case 'guard': {
      // In the dark, nobody goes after a zombie they can't see (defences.ts).
      if (job.type === 'guard' && !seen(state, job)) return null;
      // All done from a tile next to the target (for guards, the zombie as of this tick).
      const goals = neighbours4(grid, job.x, job.y);
      if (!canReach(grid, v, s.pos, goals)) return null;
      return pathPlan(state, s.pos, goals, null);
    }
    case 'scout': {
      if (scoutingOver(state.tick)) return null;
      const area = job.areaId ? getExploreArea(state, job.areaId) : undefined;
      const target = area ? scoutTarget(state, area, s.pos) : null;
      if (!target) return null;
      job.x = target.x;
      job.y = target.y;
      return pathPlan(state, s.pos, [target], null);
    }
    case 'doctor': {
      const patient = job.patientId ? getSurvivor(state, job.patientId) : undefined;
      if (!patient) return null;
      const site = neighbours4(grid, patient.pos.x, patient.pos.y).filter((p) => isWalkable(grid, p.x, p.y));
      const remedy = remediesFor(patient).map((k) => freeItemFor(state, k, s.pos, site)).find((i) => i !== undefined);
      return remedy ? pathPlan(state, s.pos, [remedy.pos], remedy.id) : null;
    }
    case 'research': {
      const lab = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
      if (!lab) return null;
      const site = neighbours4(grid, lab.x, lab.y).filter((p) => isWalkable(grid, p.x, p.y));
      if (lab.stored > 0) return canReach(grid, v, s.pos, site) ? pathPlan(state, s.pos, site, null) : null;
      const sample = freeItemFor(state, 'sample', s.pos, site);
      return sample ? pathPlan(state, s.pos, [sample.pos], sample.id) : null;
    }
    case 'craft':
    case 'cook': {
      const bench = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
      if (!bench || !isStation(bench.kind)) return null;
      const kind = stationCraft(state, bench);
      if (!kind) return null;
      const site = neighbours4(grid, bench.x, bench.y).filter((p) => isWalkable(grid, p.x, p.y));
      if (bench.stored >= RECIPES[kind].count && bench.storedKind === RECIPES[kind].input) return canReach(grid, v, s.pos, site) ? pathPlan(state, s.pos, site, null) : null;
      const input = freeItemFor(state, RECIPES[kind].input, s.pos, site);
      return input ? pathPlan(state, s.pos, [input.pos], input.id) : null;
    }
    case 'haul': {
      if (isStoreJob(job)) {
        const item = job.itemId ? getItem(state, job.itemId) : undefined;
        if (!item?.pos || item.reservedBy !== null || !canReach(grid, v, s.pos, [item.pos])) return null;
        const dest = pickStorageTile(state, item.pos, job, homeFor(completeStoreRooms(state), item.kind));
        return dest ? pathPlan(state, s.pos, [item.pos], item.id, dest) : null;
      }
      const site = neighbours4(grid, job.x, job.y).filter((p) => isWalkable(grid, p.x, p.y));
      if (site.length === 0) return null;
      if (isFuelJob(job)) {
        const f = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
        const fuel = f && f.kind in FUEL ? FUEL[f.kind as keyof typeof FUEL] : 'wood';
        const item = freeItemFor(state, fuel, s.pos, site);
        return item ? pathPlan(state, s.pos, [item.pos], item.id) : null;
      }
      const bp = job.blueprintId ? getBlueprint(state, job.blueprintId) : undefined;
      const material = bp ? materialFor(bp.target) : 'wood';
      const free = state.items
        .filter((i): i is Item & { pos: Point } => i.pos !== null && i.reservedBy === null && i.kind === material)
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
  // A guard with a loaded gun looks before walking in: they may be able to shoot from here.
  if (!(job.type === 'guard' && loadedGun(state, s))) walk(s, plan.path, state.walkabilityVersion);
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
