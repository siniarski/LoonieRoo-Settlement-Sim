// ASCII renderers. Read state only. Used in tests so failing maps are
// readable in the terminal, and by window.game.dumpText().

import { ENTITY_CHARS, TILE_CHARS } from '../sim/scenario';
import { OUTDOORS, getRoomMap } from '../sim/rooms';
import type { SimState } from '../sim/state';

const BLUEPRINT_CHARS = {
  wall: ENTITY_CHARS.wallBlueprint,
  door: ENTITY_CHARS.doorBlueprint,
  bed: ENTITY_CHARS.bedBlueprint,
  table: ENTITY_CHARS.tableBlueprint,
  workbench: ENTITY_CHARS.workbenchBlueprint,
  barricade: ENTITY_CHARS.barricadeBlueprint,
  trap: ENTITY_CHARS.trapBlueprint,
  floor: ENTITY_CHARS.removeBlueprint,
} as const;

/** The map. Layers, top wins: survivor, zombie, wanderer, item, blueprint, furniture, farm plot, dirt, tile. */
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
  for (const plot of state.farm) put(plot.x, plot.y, ENTITY_CHARS.farmPlot);
  for (const f of state.furniture) put(f.x, f.y, ENTITY_CHARS[f.kind]);
  for (const bp of state.blueprints) put(bp.x, bp.y, BLUEPRINT_CHARS[bp.target]);
  for (const item of state.items) if (item.pos) put(item.pos.x, item.pos.y, ENTITY_CHARS[item.kind]);
  for (const w of state.wanderers) put(w.x, w.y, ENTITY_CHARS.wanderer);
  for (const z of state.zombies) put(z.pos.x, z.pos.y, ENTITY_CHARS.zombie);
  for (const s of state.survivors) put(s.pos.x, s.pos.y, ENTITY_CHARS.survivor);
  return rows.map((r) => r.join('')).join('\n');
}

/** Rooms: each room's floor gets a letter (a, b, c, ...), outdoors '.', walls '#', doors 'D'. */
export function dumpRooms(state: SimState): string {
  const { labels } = getRoomMap(state);
  const { width, height, tiles } = state.grid;
  const rows: string[] = [];
  for (let y = 0; y < height; y++) {
    let row = '';
    for (let x = 0; x < width; x++) {
      const i = y * width + x;
      const label = labels[i]!;
      if (label >= 0) row += String.fromCharCode(97 + (label % 26));
      else if (label === OUTDOORS) row += '.';
      else row += TILE_CHARS[tiles[i]!];
    }
    rows.push(row);
  }
  return rows.join('\n');
}
