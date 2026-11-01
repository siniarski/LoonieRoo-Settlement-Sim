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
import { FARM_WORK, getPlot, isRipe, needsWork, plotPoint } from './farming';
import { getFurniture } from './furniture';
import { BANDAGE_HEAL, HEALTH_MAX, TREAT_TICKS } from './health';
import { GUARD_DAMAGE_PER_LEVEL, skillLevel, workStep } from './skills';
import { isPatient } from './medicine';
import { getExploreArea, isExplored, scoutTarget, scoutingOver } from './fog';
import { getTile, isDirty, isWalkable, neighbours4, setDirty, type Point } from './grid';
import { REPAIR_PER_TICK, repairStructure, structureDamage } from './breach';
import { canHoldItem, dropItem, getItem, removeItem, spawnItem, type Item, type ItemKind } from './items';
import { JOB_TYPES, type JobType } from './jobTypes';
import { canReach, findPathToAny } from './pathfinding';
import { random } from './rng';
import { damageZombie, getZombie } from './zombies';
import { completeStoreRooms, getRoomMap, homeFor, isIndoors, labelAt, reconcileRoomDesignations, type StoreRooms } from './rooms';
import { blowDamage, wearWeapon } from './weapons';
import { CRAFT_WORK, RECIPES, isStation, wantedCraft } from './crafting';
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
/** Chance a find is food (rolled with the sim RNG). */
export const SCAVENGE_FOOD_CHANCE = 0.45;
/** Chance a find is wood. */
export const SCAVENGE_WOOD_CHANCE = 0.3;
/** Chance a find is a bandage; the rest are weapons. */
export const SCAVENGE_BANDAGE_CHANCE = 0.15;
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
  const wanted = { workbench: wantedCraft(state, 'workbench'), stove: wantedCraft(state, 'stove') };
  if (wanted.workbench || wanted.stove) {
    const stationed = new Set(state.jobs.filter((j) => j.type === 'craft' || j.type === 'cook').map((j) => j.furnitureId));
    for (const f of state.furniture) {
      if (!isStation(f.kind) || !wanted[f.kind] || stationed.has(f.id)) continue;
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
  return job.type === 'haul' && job.blueprintId === null;
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
      return wantedCraft(state, station.kind) ? null : 'stocks are full';
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
    if (seekJob(state, s)) return;
    s.nextSeekTick = state.tick + IDLE_RETRY_TICKS;
    startLeisure(state, s);
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
    plot.planted = false;
    plot.growth = 0;
    spawnItem(state, 'food', plot.x, plot.y);
    finishJob(state, s, job, `harvested food at (${plot.x}, ${plot.y})`);
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
  if (manhattan(s.pos, z.pos) > 1) return goTo(state, s, job, neighbours4(state.grid, z.pos.x, z.pos.y));
  if (workStep(state, s, 'guard') === 0) return;
  job.progress += 1;
  // First blow lands on arrival (progress 1), then every GUARD_ATTACK_EVERY ticks.
  if (job.progress % GUARD_ATTACK_EVERY !== 1) return;
  if (!damageZombie(state, z, blowDamage(s, GUARD_DAMAGE) + GUARD_DAMAGE_PER_LEVEL * skillLevel(s, 'guard'), s)) return;
  wearWeapon(state, s);
  finishJob(state, s, job, `killed a zombie at (${z.pos.x}, ${z.pos.y})`);
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

/** Maps a [0, 1) roll to a find: food, wood, bandage, or weapon. */
export function scavengeFind(roll: number): ItemKind {
  if (roll < SCAVENGE_FOOD_CHANCE) return 'food';
  if (roll < SCAVENGE_FOOD_CHANCE + SCAVENGE_WOOD_CHANCE) return 'wood';
  if (roll < SCAVENGE_FOOD_CHANCE + SCAVENGE_WOOD_CHANCE + SCAVENGE_BANDAGE_CHANCE) return 'bandage';
  return 'weapon';
}

/** Bring the input to the station one piece at a time, then make the most-wanted thing it makes. */
function workCraft(state: SimState, s: Survivor, job: Job): void {
  const bench = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
  if (!bench || !isStation(bench.kind)) return releaseSurvivor(state, s, 'station gone');
  const kind = wantedCraft(state, bench.kind);
  if (!kind) return releaseSurvivor(state, s, 'stocks are full');
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
    s.note = `brought ${recipe.input} to ${station} (${bench.stored}/${cost})`;
    return;
  }

  if (manhattan(s.pos, bench) !== 1) return goTo(state, s, job, site);
  const step = workStep(state, s, job.type);
  if (step === 0) return;
  job.progress += step;
  if (job.progress < CRAFT_WORK) return;
  bench.stored -= cost;
  spawnItem(state, kind, s.pos.x, s.pos.y);
  const made = { weapon: 'crafted a weapon', bandage: 'crafted a bandage', meal: 'cooked a hot meal' }[kind];
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
    item = freeItemFor(state, 'bandage', s.pos, site);
    if (!item) return releaseSurvivor(state, s, 'no bandages');
    job.itemId = item.id;
    item.reservedBy = s.id;
  }
  if (s.carrying !== item.id) {
    if (!item.pos) return releaseSurvivor(state, s, 'item taken by someone else');
    if (!samePoint(s.pos, item.pos)) return goTo(state, s, job, [item.pos]);
    item.pos = null;
    item.carriedBy = s.id;
    s.carrying = item.id;
    s.note = `picked up a bandage for ${patient.name}`;
  }
  if (manhattan(s.pos, patient.pos) !== 1) return goTo(state, s, job, site);
  const step = workStep(state, s, 'doctor');
  if (step === 0) return;
  job.progress += step;
  if (job.progress < TREAT_TICKS) return;
  removeItem(state, item);
  s.carrying = null;
  patient.health = Math.min(HEALTH_MAX, patient.health + BANDAGE_HEAL);
  finishJob(state, s, job, `treated ${patient.name}'s wounds`);
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
  finishJob(state, s, job, bp.target === 'floor' ? `removed structure at (${bp.x}, ${bp.y})` : `built ${bp.target} at (${bp.x}, ${bp.y})`);
}

/** Patch up a damaged wall or door from a tile next to it. */
function workRepair(state: SimState, s: Survivor, job: Job): void {
  if (manhattan(s.pos, job) !== 1) return goTo(state, s, job, neighbours4(state.grid, job.x, job.y));
  const step = workStep(state, s, 'build');
  if (step === 0) return;
  if (repairStructure(state, job.x, job.y, REPAIR_PER_TICK * step) > 0) return;
  finishJob(state, s, job, `repaired ${getTile(state.grid, job.x, job.y)} at (${job.x}, ${job.y})`);
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
    case 'clean':
    case 'farm': {
      // Done standing on the tile.
      const goal = [{ x: job.x, y: job.y }];
      if (!canReach(grid, v, s.pos, goal)) return null;
      return pathPlan(state, s.pos, goal, null);
    }
    case 'build':
    case 'scavenge':
    case 'guard': {
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
      const bandage = freeItemFor(state, 'bandage', s.pos, site);
      return bandage ? pathPlan(state, s.pos, [bandage.pos], bandage.id) : null;
    }
    case 'craft':
    case 'cook': {
      const bench = job.furnitureId ? getFurniture(state, job.furnitureId) : undefined;
      if (!bench || !isStation(bench.kind)) return null;
      const kind = wantedCraft(state, bench.kind);
      if (!kind) return null;
      const site = neighbours4(grid, bench.x, bench.y).filter((p) => isWalkable(grid, p.x, p.y));
      if (bench.stored >= RECIPES[kind].count) return canReach(grid, v, s.pos, site) ? pathPlan(state, s.pos, site, null) : null;
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
