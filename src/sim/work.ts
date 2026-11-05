// Doing jobs: what a survivor does each tick while holding one (jobs.ts has the
// queue). Each work function ends with finishJob or releaseSurvivor; nobody gets stuck.

import { SWING_REACH, distance2 } from './motion';
import { EDGE_NONE, edgeBetween } from './grid';
import { GUARD_BLOW_COST, exert } from './body';
import { boostDamage } from './moodlets';
import { spawnFind } from './difficulty';
import { LOOT_TABLES, pickFrom, type LootTable } from './loot';
import { completeBlueprint, getBlueprint } from './designations';
import { FARM_WORK, getPlot, harvest, isRipe, needsWork, plotPoint } from './farming';
import { HUNT_WORK, VENISON, getAnimal } from './animals';
import { getFurniture } from './furniture';
import { TREAT_TICKS } from './health';
import { GUARD_DAMAGE_PER_LEVEL, HARDENED_DAMAGE, hasTrait, skillLevel, workStep } from './skills';
import { applyRemedy, isPatient, remediesFor } from './medicine';
import { getExploreArea, isExplored, scoutTarget, scoutingOver } from './fog';
import { getTile, isWalkable, neighbours4, setDirty, type Point } from './grid';
import { REPAIR_PER_TICK, repairStructure } from './breach';
import { canHoldItem, dropItem, getItem, removeItem, spawnItem, type Item, type ItemKind } from './items';
import { canReach, findPathToAny } from './pathfinding';
import { random } from './rng';
import { damageZombie, getZombie } from './zombies';
import { completeStoreRooms, getRoomMap, homeFor, labelAt } from './rooms';
import { GUN_DAMAGE, GUN_EVERY, GUN_RANGE, blowDamage, loadedGun, wearWeapon } from './weapons';
import { makeNoise } from './noise';
import { DOUSE_WORK, STOVE_FIRE_CHANCE, douse, fireAt, ignite } from './fire';
import { hashRoll } from './hash';
import { startReading } from './books';
import { isWet } from './seasons';
import { LIGHT_DAMAGE_PCT, inLight } from './power';
import { CURE_SAMPLES, RESEARCH_WORK, studied } from './endgame';
import { seen } from './defences';
import { logEvent } from './events';
import { TOWER_RANGE_BONUS, keepWatch, onTower } from './towers';
import { CRAFT_WORK, RECIPES, WORKSHOP_CRAFT_WORK, inWorkshop, isStation, stationCraft } from './crafting';
import { startLeisure } from './leisure';
import { manhattan, nearestTile, samePoint } from './space';
import type { SimState } from './state';
import { getSurvivor, setGoal, walk, type Survivor } from './survivors';
import { type Job, CART_LOAD, CART_REACH, CLEAN_WORK, SCAVENGE_WORK, SCAVENGE_TABLE, GUARD_ATTACK_EVERY, GUARD_DAMAGE, MAX_CANDIDATES, IDLE_RETRY_TICKS, getJob, isRepairJob, isStoreJob, FUEL, isFuelJob, releaseSurvivor } from './jobs';
import { seekJob } from './jobChoice';
// --- Working ----------------------------------------------------------------

/** Runs when the survivor is standing still: continue the current job, or look for one. */
export function updateWork(state: SimState, s: Survivor): void {
  if (!s.jobId) {
    if (state.tick < s.nextSeekTick) return;
    if (seekJob(state, s)) return;
    s.nextSeekTick = state.tick + IDLE_RETRY_TICKS;
    // Under an order there's no wandering off to a tower, a book or the rec room.
    if (!s.order && !keepWatch(state, s) && !startReading(state, s)) startLeisure(state, s);
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
  // Close enough to swing (centre to centre: zombies close in off the grid, motion.ts).
  const inReach = distance2(s.at, z.at) <= SWING_REACH * SWING_REACH;
  if (gun && !inReach && manhattan(s.pos, z.pos) <= range && clearShot(state.grid, s.pos, z.pos)) {
    if (workStep(state, s, 'guard') === 0) return;
    job.progress += 1;
    if (job.progress % GUN_EVERY !== 1) return;
    gun.wear -= 1;
    makeNoise(state, s.pos, 'gunfire');
    if (!damageZombie(state, z, boostDamage(s, GUN_DAMAGE + GUARD_DAMAGE_PER_LEVEL * skillLevel(s, 'guard') + (hasTrait(s, 'hardened') ? HARDENED_DAMAGE : 0)), s)) return;
    return finishJob(state, s, job, `shot a zombie at (${z.pos.x}, ${z.pos.y})`);
  }
  if (!inReach) return goTo(state, s, job, neighbours4(state.grid, z.pos.x, z.pos.y), gun ? 1 : undefined);
  if (workStep(state, s, 'guard') === 0) return;
  job.progress += 1;
  // First blow lands on arrival (progress 1), then every GUARD_ATTACK_EVERY ticks.
  if (job.progress % GUARD_ATTACK_EVERY !== 1) return;
  makeNoise(state, s.pos, 'fighting');
  exert(s, GUARD_BLOW_COST);
  const blow = boostDamage(s, blowDamage(s, GUARD_DAMAGE) + GUARD_DAMAGE_PER_LEVEL * skillLevel(s, 'guard') + (hasTrait(s, 'hardened') ? HARDENED_DAMAGE : 0));
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
  const kind = crate.kind === 'tree' ? 'wood' : scavengeFind(random(state), crate.table);
  spawnFind(state, kind, s.pos.x, s.pos.y);
  crate.loot -= 1;
  if (crate.loot === 0 && crate.kind === 'crate') crate.scavenge = false; // trees stay marked: they grow back
  finishJob(state, s, job, crate.kind === 'tree' ? `chopped wood from (${crate.x}, ${crate.y}), ${crate.loot} left` : `scavenged ${kind} from (${crate.x}, ${crate.y}), ${crate.loot} left`);
}

/** Maps a [0, 1) roll to a find: from the container's loot table (loot.ts), else SCAVENGE_TABLE. */
export function scavengeFind(roll: number, table: LootTable | null = null): ItemKind {
  return pickFrom(table ? LOOT_TABLES[table] : SCAVENGE_TABLE, roll);
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
/**
 * A guard on the way to their zombie stops walking once it's within swing reach (zombies close
 * in off the grid: motion.ts), so the fight starts there rather than at the tile they were
 * heading for. A step already under way lands first.
 */
export function interruptForFight(state: SimState, s: Survivor): void {
  if (!s.goal || !s.jobId) return;
  const job = getJob(state, s.jobId);
  if (job?.type !== 'guard' || !job.zombieId) return;
  const z = getZombie(state, job.zombieId);
  if (!z || distance2(s.at, z.at) > SWING_REACH * SWING_REACH) return;
  s.goal = null;
  s.path = [];
  s.pathVersion = -1;
}

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
export function freeItemFor(state: SimState, kind: ItemKind, from: Point, site: Point[]): (Item & { pos: Point }) | undefined {
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
export function pickStorageTile(state: SimState, from: Point, job: Job, storage: Set<number>): Point | null {
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
    const px = x;
    const py = y;
    if (e2 >= dy) {
      err += dy;
      x += sx;
    }
    if (e2 <= dx) {
      err += dx;
      y += sy;
    }
    if (!seesAcross(grid, px, py, x, y)) return false;
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

/**
 * A step of a line from one tile to the next (side by side or diagonal) isn't blocked by an edge
 * wall or door: for a diagonal, either way round the corner will do.
 */
export function seesAcross(grid: SimState['grid'], ax: number, ay: number, bx: number, by: number): boolean {
  const clear = (px: number, py: number, qx: number, qy: number) => (grid.edges[edgeBetween(grid, px, py, qx, qy)] ?? EDGE_NONE) === EDGE_NONE;
  if (ax === bx || ay === by) return clear(ax, ay, bx, by);
  return (clear(ax, ay, bx, ay) && clear(bx, ay, bx, by)) || (clear(ax, ay, ax, by) && clear(ax, by, bx, by));
}
