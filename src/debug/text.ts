// ASCII renderer. Reads state only. Used in tests so failing maps are
// readable in the terminal, and by window.game.dumpText().

import { TILE_CHARS } from '../sim/scenario';
import type { SimState } from '../sim/state';

export function dumpText(state: SimState): string {
  const { width, height, tiles } = state.grid;
  const rows: string[] = [];
  for (let y = 0; y < height; y++) {
    let row = '';
    for (let x = 0; x < width; x++) {
      const tile = tiles[y * width + x];
      row += tile === undefined ? '?' : TILE_CHARS[tile];
    }
    rows.push(row);
  }
  return rows.join('\n');
}
