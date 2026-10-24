// Sim state types and constructors. Everything here must stay plain
// JSON-compatible data (architecture rule 7).

import type { Command } from './commands';
import { createGrid, type Grid } from './grid';
import { seedRng } from './rng';
import type { Survivor } from './survivors';

/**
 * Bump when the shape of SimState changes. Hot reload and (later) save
 * loading discard state with a different version instead of crashing on it.
 */
export const SCHEMA_VERSION = 2;

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
  /** Next numeric id for entities. Ids are strings like "s1". */
  nextId: number;
  /** Every command that changed state, in order. seed + initial state + this = replay. */
  commandLog: LoggedCommand[];
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
    nextId: 1,
    commandLog: [],
  };
}

/** Deep copy via JSON. Doubles as a check that state is JSON-safe. */
export function cloneState(state: SimState): SimState {
  return JSON.parse(JSON.stringify(state)) as SimState;
}
