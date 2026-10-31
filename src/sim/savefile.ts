// Save files and bug reports: plain JSON text in, plain JSON text out.
//
// A save is the whole state (it is already serialisable). A bug report is a
// starting state plus the commands applied since, so loading it replays the
// game up to the moment it was filed (rule 1: seed + initial state + commands
// = the full game). Files from a different schema version are refused, not
// migrated, same as hot reload.

import { replay } from './replay';
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

export function toSaveFile(state: SimState): string {
  const file: SaveFile = { format: 'loonieroo-save', schemaVersion: SCHEMA_VERSION, state };
  return JSON.stringify(file);
}

/**
 * `initial` is the state play started from (scenario, loaded save); the
 * report carries only the commands logged since then.
 */
export function toBugReport(initial: SimState, state: SimState, note = ''): string {
  const report: BugReport = {
    format: 'loonieroo-bug',
    schemaVersion: SCHEMA_VERSION,
    note,
    initial,
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
    return { ok: true, state: cloneState(file.state), kind: 'save' };
  }
  if (!file.initial || !file.commands || typeof file.tick !== 'number') return { ok: false, error: 'bug report is incomplete' };
  try {
    const initial = cloneState(file.initial);
    return { ok: true, state: replay(initial, file.commands, file.tick), kind: 'bug' };
  } catch (err) {
    return { ok: false, error: `bug report did not replay: ${String(err)}` };
  }
}
