// Save files and bug reports: plain JSON text in, plain JSON text out.
//
// A save is the whole state (it is already serialisable) but its command log: a save is a
// snapshot, and the log (every key change and turn of the mouse) only grows. A bug report is a
// starting state plus the commands applied since, so loading it replays the
// game up to the moment it was filed (rule 1: seed + initial state + commands
// = the full game). Files from a different schema version are refused, not
// migrated, same as hot reload.
//
// The grid's per-tile layers are run-length encoded on the way out (a world is
// mostly long runs of grass and fog), and decoded on the way in. Furniture (tens
// of thousands of trees and fittings in a big world) goes out as rows of values in
// FURNITURE_FIELDS order, trailing defaults dropped, instead of objects with every
// key (packFurniture): a fully explored 512 world's save fell from 3.5 MB. Files
// without either encoding still load.

import { replay } from './replay';
import type { Furniture } from './furniture';
import { SCHEMA_VERSION, cloneState, type LoggedCommand, type SimState } from './state';

interface SaveFile {
  format: 'loonieroo-save';
  schemaVersion: number;
  state: SimState;
}

interface BugReport {
  format: 'loonieroo-bug';
  schemaVersion: number;
  /** What the player says went wrong (optional). */
  note: string;
  initial: SimState;
  commands: LoggedCommand[];
  /** The tick the report was filed at: replay runs up to here. */
  tick: number;
}

export type LoadResult = { ok: true; state: SimState; kind: 'save' | 'bug' } | { ok: false; error: string };

/** The grid layers worth compressing. */
const LAYERS = ['tiles', 'ground', 'explored', 'dirt', 'damage', 'edges'] as const;

/** [value, count, value, count, ...]. */
export function runLengthEncode<T>(values: readonly T[]): (T | number)[] {
  const out: (T | number)[] = [];
  for (let i = 0; i < values.length; ) {
    let j = i + 1;
    while (j < values.length && values[j] === values[i]) j++;
    out.push(values[i]!, j - i);
    i = j;
  }
  return out;
}

export function runLengthDecode<T>(encoded: readonly (T | number)[]): T[] {
  const out: T[] = [];
  for (let k = 0; k < encoded.length; k += 2) {
    const value = encoded[k] as T;
    for (let n = 0; n < (encoded[k + 1] as number); n++) out.push(value);
  }
  return out;
}

/** A furniture piece's fields, in the order addFurniture makes them, and what each is when new. */
const FURNITURE_FIELDS = ['id', 'kind', 'x', 'y', 'occupant', 'owner', 'loot', 'scavenge', 'stored', 'storedKind', 'look', 'table'] as const;
const FURNITURE_DEFAULTS: readonly unknown[] = [undefined, undefined, undefined, undefined, null, null, 0, false, 0, null, null, null];

/**
 * Furniture as rows: each piece's values in FURNITURE_FIELDS order, trailing defaults dropped. A
 * piece whose keys aren't exactly those, in that order, stays an object (so a save still comes
 * back exactly as it went out).
 */
function packFurniture(list: readonly Furniture[]): { rows: unknown[] } {
  const rows = list.map((f) => {
    const keys = Object.keys(f);
    if (keys.length !== FURNITURE_FIELDS.length || keys.some((k, i) => k !== FURNITURE_FIELDS[i])) return f;
    const row: unknown[] = FURNITURE_FIELDS.map((k) => f[k]);
    while (row.length > 4 && row[row.length - 1] === FURNITURE_DEFAULTS[row.length - 1]) row.pop();
    return row;
  });
  return { rows };
}

function unpackFurniture(packed: { rows: unknown[] }): Furniture[] {
  return packed.rows.map((r) => {
    if (!Array.isArray(r)) return r as Furniture;
    const f: Record<string, unknown> = {};
    FURNITURE_FIELDS.forEach((k, i) => (f[k] = i < r.length ? r[i] : FURNITURE_DEFAULTS[i]));
    return f as unknown as Furniture;
  });
}

/** A copy of the state with the grid's layers run-length encoded (marked `rle: true`) and the furniture as rows. */
function pack(state: SimState): unknown {
  const grid: Record<string, unknown> = { ...state.grid, rle: true };
  for (const layer of LAYERS) grid[layer] = runLengthEncode(state.grid[layer] as unknown[]);
  return { ...state, grid, furniture: packFurniture(state.furniture) };
}

/** A save: the state as it is, without the history of how it got there. */
function snapshot(state: SimState): unknown {
  return { ...(pack(state) as object), commandLog: [] };
}

/** Undoes pack (and passes unpacked states through). */
function unpack(packed: SimState): SimState {
  const rows = packed.furniture as unknown;
  const state = rows && !Array.isArray(rows) ? { ...packed, furniture: unpackFurniture(rows as { rows: unknown[] }) } : packed;
  const grid = state.grid as unknown as Record<string, unknown>;
  if (grid.rle !== true) return state;
  const out: Record<string, unknown> = { ...grid };
  delete out.rle;
  for (const layer of LAYERS) out[layer] = runLengthDecode(grid[layer] as unknown[]);
  return { ...state, grid: out as unknown as SimState['grid'] };
}

export function toSaveFile(state: SimState): string {
  return JSON.stringify({ format: 'loonieroo-save', schemaVersion: SCHEMA_VERSION, state: snapshot(state) });
}

/**
 * `initial` is the state play started from (scenario, loaded save); the
 * report carries only the commands logged since then.
 */
export function toBugReport(initial: SimState, state: SimState, note = ''): string {
  const report = {
    format: 'loonieroo-bug',
    schemaVersion: SCHEMA_VERSION,
    note,
    initial: pack(initial),
    commands: state.commandLog.slice(initial.commandLog.length),
    tick: state.tick,
  };
  return JSON.stringify(report);
}

/** Reads a save or a bug report (replaying the latter). Never throws. */
export function loadFile(text: string): LoadResult {
  let data: unknown;
  try {
    data = JSON.parse(text);
  } catch {
    return { ok: false, error: 'not a JSON file' };
  }
  if (typeof data !== 'object' || data === null || !('format' in data)) return { ok: false, error: 'not a LoonieRoo file' };
  const file = data as Partial<Omit<SaveFile, 'format'> & Omit<BugReport, 'format'>> & { format: unknown };
  if (file.format !== 'loonieroo-save' && file.format !== 'loonieroo-bug') return { ok: false, error: 'not a LoonieRoo file' };
  if (file.schemaVersion !== SCHEMA_VERSION) {
    return { ok: false, error: `made by a different version of the game (schema ${String(file.schemaVersion)}, this is ${SCHEMA_VERSION})` };
  }
  if (file.format === 'loonieroo-save') {
    if (!file.state) return { ok: false, error: 'save has no state' };
    return { ok: true, state: cloneState(unpack(file.state)), kind: 'save' };
  }
  if (!file.initial || !file.commands || typeof file.tick !== 'number') return { ok: false, error: 'bug report is incomplete' };
  try {
    const initial = cloneState(unpack(file.initial));
    return { ok: true, state: replay(initial, file.commands, file.tick), kind: 'bug' };
  } catch (err) {
    return { ok: false, error: `bug report did not replay: ${String(err)}` };
  }
}
