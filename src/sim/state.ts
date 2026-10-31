// Sim state types and constructors. Everything here must stay plain
// JSON-compatible data (architecture rule 7).

import type { Command } from './commands';
import type { Blueprint } from './designations';
import type { GameEvent } from './events';
import type { FarmPlot } from './farming';
import type { Furniture } from './furniture';
import { createGrid, type Grid } from './grid';
import type { Item } from './items';
import type { Job } from './jobs';
import { seedRng } from './rng';
import type { RoomDesignation } from './rooms';
import type { Survivor } from './survivors';
import type { Zombie } from './zombies';

/**
 * Bump when the shape of SimState changes. Hot reload and (later) save
 * loading discard state with a different version instead of crashing on it.
 */
export const SCHEMA_VERSION = 11;

export interface LoggedCommand {
  /** The tick the command was applied at (before that tick advanced). */
  tick: number;
  command: Command;
}

export interface SimState {
  schemaVersion: number;
  tick: number;
  /** mulberry32 state, a uint32. */
  rng: number;
  grid: Grid;
  /** Bumped whenever a wall or door changes. Caches compare against it. */
  walkabilityVersion: number;
  survivors: Survivor[];
  zombies: Zombie[];
  items: Item[];
  furniture: Furniture[];
  /** Farm plots (see farming.ts). */
  farm: FarmPlot[];
  blueprints: Blueprint[];
  /** Player-assigned room purposes, anchored to a tile. Rooms themselves are derived (rooms.ts). */
  roomDesignations: RoomDesignation[];
  /** The shared job queue. Derived from blueprints and dirt by reconcileJobs each tick. */
  jobs: Job[];
  /** Next numeric id for entities. Ids are strings like "s1", "i7", "b12", "j20". */
  nextId: number;
  /** Every command that changed state, in order. seed + initial state + this = replay. */
  commandLog: LoggedCommand[];
  /** Notable happenings (deaths, ...), newest last, capped. See events.ts. */
  events: GameEvent[];
}

export interface NewStateOptions {
  seed: number;
  width: number;
  height: number;
}

export function createState({ seed, width, height }: NewStateOptions): SimState {
  return {
    schemaVersion: SCHEMA_VERSION,
    tick: 0,
    rng: seedRng(seed),
    grid: createGrid(width, height),
    walkabilityVersion: 0,
    survivors: [],
    zombies: [],
    items: [],
    furniture: [],
    farm: [],
    blueprints: [],
    roomDesignations: [],
    jobs: [],
    nextId: 1,
    commandLog: [],
    events: [],
  };
}

/** Deep copy via JSON. Doubles as a check that state is JSON-safe. */
export function cloneState(state: SimState): SimState {
  return JSON.parse(JSON.stringify(state)) as SimState;
}
