// ASCII renderer. Reads state only. Used in tests so failing maps are
// readable in the terminal, and by window.game.dumpText().
// Survivors are drawn on the tile they occupy (`pos`), hiding the tile under them.

import { SURVIVOR_CHAR, TILE_CHARS } from '../sim/scenario';
import type { SimState } from '../sim/state';

export function dumpText(state: SimState): string {
  const { width, height, tiles } = state.grid;
  const rows: string[][] = [];
  for (let y = 0; y < height; y++) {
    const row: string[] = [];
    for (let x = 0; x < width; x++) {
      const tile = tiles[y * width + x];
      row.push(tile === undefined ? '?' : TILE_CHARS[tile]);
    }
    rows.push(row);
  }
  for (const s of state.survivors) {
    const row = rows[s.pos.y];
    if (row && s.pos.x >= 0 && s.pos.x < width) row[s.pos.x] = SURVIVOR_CHAR;
  }
  return rows.map((r) => r.join('')).join('\n');
}
