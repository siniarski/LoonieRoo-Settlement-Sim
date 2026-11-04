// Browser side of saving: slots in localStorage, file download and upload.
// The file formats themselves live in sim/savefile.ts (pure, tested).

import { loadFile, toSaveFile, type LoadResult } from '../sim/savefile';
import type { SimState } from '../sim/state';
import { clockText, dayOf } from '../sim/time';

export const SLOTS = ['1', '2', '3'] as const;
/** Written at every dawn; loadable like a slot, never saved to by hand. */
export const AUTO_SLOT = 'auto';

const dataKey = (slot: string) => `loonieroo.save.${slot}`;
const metaKey = (slot: string) => `loonieroo.meta.${slot}`;
const timeKey = (slot: string) => `loonieroo.time.${slot}`;

/** When a slot was last saved to (ms since 1970), or 0 if never (or unknown). */
export function slotTime(slot: string): number {
  try {
    return Number(localStorage.getItem(timeKey(slot))) || 0;
  } catch {
    return 0;
  }
}

/** One-line description of a slot's save, or null if empty. */
export function slotLabel(slot: string): string | null {
  try {
    return localStorage.getItem(metaKey(slot));
  } catch {
    return null;
  }
}

/** Returns an error message, or null on success. */
export function saveToSlot(slot: string, state: SimState): string | null {
  const label = `Day ${dayOf(state.tick)} ${clockText(state.tick)} · ${state.survivors.length} survivor${state.survivors.length === 1 ? '' : 's'} · saved ${new Date().toLocaleString()}`;
  try {
    localStorage.setItem(dataKey(slot), toSaveFile(state));
    localStorage.setItem(metaKey(slot), label);
    localStorage.setItem(timeKey(slot), String(Date.now()));
    return null;
  } catch (err) {
    return `couldn't save: ${String(err)}`;
  }
}

export function loadFromSlot(slot: string): LoadResult {
  let text: string | null = null;
  try {
    text = localStorage.getItem(dataKey(slot));
  } catch (err) {
    return { ok: false, error: `couldn't read the save: ${String(err)}` };
  }
  return text === null ? { ok: false, error: 'that slot is empty' } : loadFile(text);
}

/** Hands the browser a text file to save. */
export function downloadText(filename: string, text: string): void {
  const url = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Opens a file picker; resolves with the chosen file's text, or null if cancelled. */
export function pickTextFile(): Promise<string | null> {
  return new Promise((resolve) => {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = '.json,application/json';
    input.addEventListener('change', () => {
      const file = input.files?.[0];
      if (!file) return resolve(null);
      file.text().then(resolve, () => resolve(null));
    });
    input.addEventListener('cancel', () => resolve(null));
    input.click();
  });
}

/** Filename-safe stamp like "day3-1030". */
export function fileStamp(state: SimState): string {
  return `day${dayOf(state.tick)}-${clockText(state.tick).replace(':', '')}`;
}
