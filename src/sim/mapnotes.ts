// The map (ui/worldmap.ts): what's on it besides the ground you've seen.
//
//   notes   your own marks: a pin and a few words on a tile (addMapNote, removeMapNote commands),
//           at most MAX_NOTES of NOTE_MAX characters. Saved with the game.
//   towns   each town in the world plan has a name (townNames: derived, never saved). The first
//           names are the towns survivors say they came from (story.ts TOWNS), so the place in
//           someone's story can turn up on your map.

import { hashRoll } from './hash';
import type { Point } from './grid';
import { inBounds } from './grid';
import type { SimState } from './state';
import { TOWNS } from './story';
import { planWorld } from './worldplan';

export interface MapNote {
  id: string;
  x: number;
  y: number;
  text: string;
}

export const MAX_NOTES = 200;
export const NOTE_MAX = 40;

/** Town names: the story's first, then more. */
export const TOWN_NAMES: readonly string[] = [...TOWNS, 'Ashby Fold', 'Greyhithe', 'Stannard', 'Wexley', 'Calder Bridge', 'Oldmoor', 'Fenwick', 'Larkhill', 'Redditch Vale', 'Thornbury'];

export function notesOf(state: SimState): MapNote[] {
  return state.mapNotes ?? [];
}

/** Pins a note on a tile. Returns null, or why not. */
export function addMapNote(state: SimState, x: number, y: number, text: string): string | null {
  const clean = text.replace(/[\u0000-\u001f]/g, '').trim().slice(0, NOTE_MAX);
  if (!Number.isInteger(x) || !Number.isInteger(y) || !inBounds(state.grid, x, y)) return 'not on the map';
  if (clean === '') return 'a note needs some words';
  state.mapNotes ??= [];
  if (state.mapNotes.length >= MAX_NOTES) return `the map is full (${MAX_NOTES} notes)`;
  state.mapNotes.push({ id: `n${state.nextId++}`, x, y, text: clean });
  return null;
}

export function removeMapNote(state: SimState, id: string): string | null {
  const notes = notesOf(state);
  const i = notes.findIndex((n) => n.id === id);
  if (i < 0) return 'no such note';
  notes.splice(i, 1);
  return null;
}

/** Each town's centre and name (generated worlds; none on hand-made maps). */
export function townNames(state: SimState): (Point & { name: string })[] {
  if (!state.world) return [];
  const plan = planWorld(state.world.seed, state.world.size, state.settings.map);
  const offset = Math.floor(hashRoll(state.world.seed, 0x7041) * TOWN_NAMES.length);
  return plan.towns.map((t, i) => ({ x: t.x, y: t.y, name: TOWN_NAMES[(offset + i) % TOWN_NAMES.length]! }));
}
