// Survivors: tile-to-tile movement along cached A* paths.
//
// A survivor's `path` is a cache stamped with the walkabilityVersion it was
// computed at. Paths are re-checked at every tile boundary, so a wall placed
// mid-route causes a repath at the next step, never a walk through a wall.
// If no path exists the survivor drops the goal and goes idle with a note
// saying why (never stuck).

import { getTile, setDirty, type Point } from './grid';
import { DEFAULT_PRIORITIES, type Priorities } from './jobTypes';
import { findPath } from './pathfinding';
import { initialSatiety, type Meal } from './food';
import { HEALTH_MAX, HURT_STEP_TICKS, isHurt } from './health';
import { initialRest, type Sleep } from './rest';
import { random } from './rng';
import { isIndoors } from './rooms';
import type { SimState } from './state';

/** Ticks to cross one tile (4 ticks = 0.4s at 1x). */
export const TICKS_PER_TILE = 4;

/** Chance that stepping onto an indoor floor tile leaves dirt on it. Uses the sim RNG. */
export const DIRT_CHANCE = 1 / 30;

const NAMES = ['Ash', 'Bea', 'Cal', 'Dee', 'Eli', 'Fen', 'Gus', 'Hal', 'Ivy', 'Jo'];

export interface Movement {
  from: Point;
  to: Point;
  /** Ticks elapsed in this step, 0..duration. */
  progress: number;
  duration: number;
}

export interface Survivor {
  id: string;
  name: string;
  /** The tile the survivor is on, or is leaving while `move` is set. */
  pos: Point;
  move: Movement | null;
  goal: Point | null;
  /** Remaining tiles to walk, excluding the current one. */
  path: Point[];
  /** walkabilityVersion the path was computed at; -1 forces a repath. */
  pathVersion: number;
  /** Last thing that happened, for the debug panel ("why is she just standing there?"). */
  note: string | null;
  priorities: Priorities;
  /** The job this survivor has reserved, if any. */
  jobId: string | null;
  /** Id of the item being carried, if any. */
  carrying: string | null;
  /** When idle and nothing was doable, don't look for work again before this tick. */
  nextSeekTick: number;
  /** 0..REST_MAX. Drains while awake; see needs.ts. */
  rest: number;
  /** Set while heading to bed or asleep. */
  sleep: Sleep | null;
  /** 0..SATIETY_MAX. Drains every tick; see needs.ts. */
  satiety: number;
  /** Set while heading to food or eating. */
  meal: Meal | null;
  /** When hungry and no food was found, don't look again before this tick. */
  nextMealTick: number;
  /** 0..HEALTH_MAX. At 0 the survivor dies; see needs.ts. */
  health: number;
  /** What last hurt them (for the death message), e.g. "a zombie attack". */
  lastHurtBy: string | null;
  /** Tick they were last hurt, -1 if never. Sleepers hurt this tick wake up. */
  hurtTick: number;
  /** Set while heading to a bandage or applying it. */
  treatment: { itemId: string; progress: number } | null;
  /** When hurt and no bandage was found, don't look again before this tick. */
  nextTreatTick: number;
}

export function addSurvivor(state: SimState, x: number, y: number): Survivor {
  const n = state.nextId++;
  const survivor: Survivor = {
    id: `s${n}`,
    name: NAMES[(n - 1) % NAMES.length]!,
    pos: { x, y },
    move: null,
    goal: null,
    path: [],
    pathVersion: -1,
    note: null,
    priorities: { ...DEFAULT_PRIORITIES },
    jobId: null,
    carrying: null,
    nextSeekTick: 0,
    rest: initialRest(state.survivors.length),
    sleep: null,
    satiety: initialSatiety(state.survivors.length),
    meal: null,
    nextMealTick: 0,
    health: HEALTH_MAX,
    lastHurtBy: null,
    hurtTick: -1,
    treatment: null,
    nextTreatTick: 0,
  };
  state.survivors.push(survivor);
  return survivor;
}

export function getSurvivor(state: SimState, id: string): Survivor | undefined {
  return state.survivors.find((s) => s.id === id);
}

/** The survivor standing on, or stepping onto, a tile. */
export function survivorAt(state: SimState, x: number, y: number): Survivor | undefined {
  return state.survivors.find(
    (s) => (s.pos.x === x && s.pos.y === y) || (s.move !== null && s.move.to.x === x && s.move.to.y === y),
  );
}

export function setGoal(survivor: Survivor, goal: Point): void {
  survivor.goal = { x: goal.x, y: goal.y };
  survivor.path = [];
  survivor.pathVersion = -1;
  survivor.note = `ordered to (${goal.x}, ${goal.y})`;
}

/** Follow a path that was already computed (by job planning) at the current walkabilityVersion. */
export function walk(s: Survivor, path: Point[], version: number): void {
  const last = path.at(-1);
  if (!last) return;
  s.goal = { x: last.x, y: last.y };
  s.path = path.slice();
  s.pathVersion = version;
}

/** Advances movement by one tick. Afterwards the survivor is mid-step, walking, or standing still. */
export function updateMovement(state: SimState, s: Survivor): void {
  if (s.move) {
    s.move.progress += 1;
    if (s.move.progress < s.move.duration) return;
    s.pos = s.move.to;
    s.move = null;
    trackDirt(state, s.pos);
    // Fall through so the next step starts this same tick (no stutter per tile).
  }

  const goal = s.goal;
  if (!goal) return;

  if (s.pos.x === goal.x && s.pos.y === goal.y) {
    // Job walks are narrated by the job; only manual orders report arrival.
    stop(s, s.jobId ? s.note : `arrived at (${goal.x}, ${goal.y})`);
    return;
  }

  if (s.pathVersion !== state.walkabilityVersion || s.path.length === 0) {
    const wasRepath = s.pathVersion !== -1;
    const path = findPath(state.grid, s.pos, goal);
    if (!path || path.length === 0) {
      stop(s, `no path to (${goal.x}, ${goal.y})`);
      return;
    }
    s.path = path;
    s.pathVersion = state.walkabilityVersion;
    s.note = wasRepath
      ? `walls changed, repathed to (${goal.x}, ${goal.y})`
      : `walking to (${goal.x}, ${goal.y}), ${path.length} tiles`;
  }

  const next = s.path.shift()!;
  s.move = { from: { ...s.pos }, to: next, progress: 0, duration: isHurt(s.health) ? HURT_STEP_TICKS : TICKS_PER_TILE };
}

function trackDirt(state: SimState, p: Point): void {
  if (getTile(state.grid, p.x, p.y) !== 'floor') return;
  // Always draw from the RNG on floor steps, even outdoors or on dirty tiles,
  // so RNG use doesn't depend on dirt or room state.
  if (random(state) < DIRT_CHANCE && isIndoors(state, p.x, p.y)) setDirty(state.grid, p.x, p.y, true);
}

function stop(s: Survivor, note: string | null): void {
  s.goal = null;
  s.path = [];
  s.pathVersion = -1;
  s.note = note;
}
