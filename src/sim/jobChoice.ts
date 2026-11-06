// Choosing work: which job an idle survivor takes (by priority, then distance), and
// planning the walk there. Pure apart from claim(). Also explainIdle for the debug panel.

import { ORDER_JOBS, withinOrder } from './orders';
import { getBlueprint, materialFor } from './designations';
import { getFurniture } from './furniture';
import { remediesFor } from './medicine';
import { getExploreArea, scoutTarget, scoutingOver } from './fog';
import { isWalkable, neighbours4, type Point } from './grid';
import { getItem, type Item } from './items';
import { JOB_TYPES, type JobType } from './jobTypes';
import { canReach, findPathToAny } from './pathfinding';
import { completeStoreRooms, homeFor, isIndoors } from './rooms';
import { loadedGun } from './weapons';
import { policyOn } from './policies';
import { seen } from './defences';
import { isNight } from './time';
import { RECIPES, isStation, stationCraft } from './crafting';
import { manhattan } from './space';
import type { SimState } from './state';
import { getSurvivor, walk, type Survivor } from './survivors';
import { type Job, MAX_CANDIDATES, isStoreJob, FUEL, isFuelJob } from './jobs';
import { freeItemFor, pickStorageTile } from './work';
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
    for (const job of open.filter((j) => (!curfew || isIndoors(state, j.x, j.y)) && withinOrder(state, s, j)).slice(0, MAX_CANDIDATES)) {
      const plan = planJob(state, s, job);
      if (plan) return { job, plan, why: `priority ${s.priorities[type]}, nearest of ${open.length} open` };
    }
  }
  return null;
}

export function typesByPriority(s: Survivor): JobType[] {
  if (s.childUntil !== null) return []; // children don't work
  return JOB_TYPES.filter((t) => s.priorities[t] > 0 && (!s.order || ORDER_JOBS.includes(t))).sort(
    (a, b) => s.priorities[a] - s.priorities[b] || JOB_TYPES.indexOf(a) - JOB_TYPES.indexOf(b),
  );
}

function openJobsByDistance(state: SimState, type: JobType, from: Point): Job[] {
  // Nearest first, ties in queue order (as a stable sort by distance would give). Each job's
  // distance is worked out once and folded with its place in the queue into one number, sorted as
  // numbers: with a guard job per zombie and hundreds of colonists looking, the comparator sort was
  // the slowest part of the worst ticks (the stress test, 2026-11-06).
  const open = state.jobs.filter((j) => j.type === type && j.reservedBy === null);
  const keys = new Float64Array(open.length);
  for (let i = 0; i < open.length; i++) keys[i] = manhattan(from, open[i]!) * 1048576 + i;
  keys.sort();
  return Array.from(keys, (k) => open[k % 1048576]!);
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

export function claim(state: SimState, s: Survivor, job: Job, plan: Plan, why: string): void {
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
