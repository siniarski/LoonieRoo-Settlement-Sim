// Leisure and breakdowns: what mood does.
//
// Leisure: a survivor with nothing doable to do, in a mood below RELAX_BELOW
// and not already relaxed today, walks to a free spot in a complete rec room
// and unwinds for RELAX_TICKS ("relaxed", a good thought). A job turning up
// ends it: they go and take it.
//
// Breakdowns: a miserable survivor (see mood.ts) may break down. Rolled
// every BREAKDOWN_CHECK_EVERY ticks (sim RNG). For BREAKDOWN_TICKS they drop
// their work and won't take any; needs still run. Afterwards they've
// "vented", a good thought, so it doesn't happen again straight away.

import { logEvent } from './events';
import { isWalkable, type Point } from './grid';
import { IDLE_RETRY_TICKS, releaseSurvivor, seekJob } from './jobs';
import { addThought, isMiserable, mood } from './mood';
import { canReach, findPathToAny } from './pathfinding';
import { random } from './rng';
import { completeRooms, getRoomMap, labelAt } from './rooms';
import { manhattan, samePoint } from './space';
import type { SimState } from './state';
import { walk, type Survivor } from './survivors';

/** Idle survivors below this mood go to the rec room. */
export const RELAX_BELOW = 70;
export const RELAX_TICKS = 200;
export const BREAKDOWN_CHECK_EVERY = 200;
/** Chance per check that a miserable survivor breaks down. */
export const BREAKDOWN_CHANCE = 0.25;
export const BREAKDOWN_TICKS = 600;

// --- Leisure ----------------------------------------------------------------

/** Called when an idle survivor found no job. Returns true if they set off to relax. */
export function startLeisure(state: SimState, s: Survivor): boolean {
  if (s.leisure || s.breakdown || mood(state, s) >= RELAX_BELOW || s.thoughts.some((t) => t.kind === 'relaxed')) return false;
  const spot = pickSpot(state, s);
  if (!spot) return false;
  s.leisure = { x: spot.x, y: spot.y, progress: 0 };
  s.note = `going to relax at (${spot.x}, ${spot.y})`;
  return true;
}

/** Runs when standing still, after sleep, before work. Returns true if leisure handled this tick. */
export function updateLeisure(state: SimState, s: Survivor): boolean {
  if (!s.leisure) return false;
  // Work comes first: if a job has turned up, go and do it.
  if (state.tick >= s.nextSeekTick) {
    s.nextSeekTick = state.tick + IDLE_RETRY_TICKS;
    const was = s.leisure;
    s.leisure = null;
    if (seekJob(state, s)) return true;
    s.leisure = was;
  }
  const spot = { x: s.leisure.x, y: s.leisure.y };
  if (!spotFree(state, s, spot)) {
    s.leisure = null;
    s.note = 'rec room spot gone';
    return false;
  }
  if (!samePoint(s.pos, spot)) {
    const path = findPathToAny(state.grid, s.pos, [spot]);
    if (!path || path.length === 0) {
      s.leisure = null;
      s.note = "can't reach the rec room";
      return false;
    }
    walk(s, path, state.walkabilityVersion);
    return true;
  }
  s.leisure.progress += 1;
  if (s.leisure.progress === 1) s.note = `relaxing at (${spot.x}, ${spot.y})`;
  if (s.leisure.progress < RELAX_TICKS) return true;
  s.leisure = null;
  addThought(state, s, 'relaxed');
  s.note = 'relaxed in the rec room';
  return true;
}

/** A free floor tile in a complete rec room: walkable, no furniture, nobody else relaxing there. */
function spotFree(state: SimState, s: Survivor, p: Point): boolean {
  const rooms = completeRooms(state, 'recroom');
  return (
    rooms.has(labelAt(state, p.x, p.y)) &&
    isWalkable(state.grid, p.x, p.y) &&
    !state.furniture.some((f) => f.x === p.x && f.y === p.y) &&
    !state.survivors.some((o) => o !== s && o.leisure && o.leisure.x === p.x && o.leisure.y === p.y)
  );
}

function pickSpot(state: SimState, s: Survivor): Point | null {
  const rooms = completeRooms(state, 'recroom');
  if (rooms.size === 0) return null;
  const { rooms: all } = getRoomMap(state);
  const { width } = state.grid;
  const spots: Point[] = [];
  for (const index of [...rooms].sort((a, b) => a - b)) {
    for (const i of all[index]!.tiles) {
      const p = { x: i % width, y: Math.floor(i / width) };
      if (spotFree(state, s, p)) spots.push(p);
    }
  }
  spots.sort((a, b) => manhattan(s.pos, a) - manhattan(s.pos, b));
  return spots.find((p) => canReach(state.grid, state.walkabilityVersion, s.pos, [p])) ?? null;
}

// --- Breakdowns -------------------------------------------------------------

/** Every tick, for every survivor: maybe start a breakdown, or end one. */
export function updateBreakdown(state: SimState, s: Survivor): void {
  if (s.breakdown) {
    if (state.tick < s.breakdown.until) return;
    s.breakdown = null;
    addThought(state, s, 'vented');
    s.note = 'pulled themselves together';
    return;
  }
  if (state.tick % BREAKDOWN_CHECK_EVERY !== 0 || !isMiserable(state, s) || s.sleep) return;
  if (random(state) >= BREAKDOWN_CHANCE) return;
  if (s.jobId) releaseSurvivor(state, s, 'having a breakdown');
  s.leisure = null;
  s.breakdown = { until: state.tick + BREAKDOWN_TICKS };
  s.note = 'having a breakdown';
  logEvent(state, `${s.name} is having a breakdown`);
}
