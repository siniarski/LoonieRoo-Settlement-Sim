// ASCII renderer. Reads state only. Used in tests so failing maps are
// readable in the terminal, and by window.game.dumpText().
// Layers, top wins: survivor, item, blueprint, dirt, tile.

import { ENTITY_CHARS, TILE_CHARS } from '../sim/scenario';
import type { SimState } from '../sim/state';

const BLUEPRINT_CHARS = {
  wall: ENTITY_CHARS.wallBlueprint,
  door: ENTITY_CHARS.doorBlueprint,
  floor: ENTITY_CHARS.removeBlueprint,
} as const;

export function dumpText(state: SimState): string {
  const { width, height, tiles, dirt } = state.grid;
  const rows: string[][] = [];
  for (let y = 0; y < height; y++) {
    const row: string[] = [];
    for (let x = 0; x < width; x++) {
      const i = y * width + x;
      const tile = tiles[i];
      row.push(tile === undefined ? '?' : dirt[i] === 1 ? ENTITY_CHARS.dirt : TILE_CHARS[tile]);
    }
    rows.push(row);
  }
  const put = (x: number, y: number, char: string) => {
    const row = rows[y];
    if (row && x >= 0 && x < width) row[x] = char;
  };
  for (const bp of state.blueprints) put(bp.x, bp.y, BLUEPRINT_CHARS[bp.target]);
  for (const item of state.items) if (item.pos) put(item.pos.x, item.pos.y, ENTITY_CHARS.wood);
  for (const s of state.survivors) put(s.pos.x, s.pos.y, ENTITY_CHARS.survivor);
  return rows.map((r) => r.join('')).join('\n');
}
