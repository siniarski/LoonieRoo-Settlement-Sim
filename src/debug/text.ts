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
  stove: ENTITY_CHARS.stoveBlueprint,
  tower: ENTITY_CHARS.towerBlueprint,
  well: ENTITY_CHARS.wellBlueprint,
  collector: ENTITY_CHARS.collectorBlueprint,
  generator: ENTITY_CHARS.generatorBlueprint,
  lamp: ENTITY_CHARS.lampBlueprint,
  fridge: ENTITY_CHARS.fridgeBlueprint,
  heater: ENTITY_CHARS.heaterBlueprint,
  coop: ENTITY_CHARS.coopBlueprint,
  radio: ENTITY_CHARS.radioBlueprint,
  lab: ENTITY_CHARS.labBlueprint,
  beacon: ENTITY_CHARS.beaconBlueprint,
  turret: ENTITY_CHARS.turretBlueprint,
  lure: ENTITY_CHARS.lureBlueprint,
  pit: ENTITY_CHARS.pitBlueprint,
  reinforce: ENTITY_CHARS.reinforceBlueprint,
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
  for (const a of state.animals) put(a.pos.x, a.pos.y, ENTITY_CHARS.deer);
  for (const d of state.dogs) put(d.pos.x, d.pos.y, ENTITY_CHARS.dog);
  for (const z of state.zombies) put(z.pos.x, z.pos.y, ENTITY_CHARS.zombie);
  for (const s of state.survivors) put(s.pos.x, s.pos.y, ENTITY_CHARS.survivor);
  const map = rows.map((r) => r.join('')).join('\n');
  // With edge walls (grid.ts), the same map again at double resolution, borders between the tiles.
  return state.grid.edges.some((e) => e !== 0) ? `${map}\n\nedges:\n${dumpEdges(state, rows)}` : map;
}

/**
 * The map with its edges: tiles at odd positions, borders between them: '-' and '|' an edge wall,
 * '=' an edge door ('o' when open), ':' a barricade, '+' where borders meet.
 */
export function dumpEdges(state: SimState, rows: string[][]): string {
  const { width, height, edges, edgeOpen } = state.grid;
  const out: string[][] = Array.from({ length: height * 2 + 1 }, () => new Array<string>(width * 2 + 1).fill(' '));
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) out[y * 2 + 1]![x * 2 + 1] = rows[y]![x]!;
  const mark = (kind: number, e: number, across: boolean) => (kind === 1 ? (across ? '-' : '|') : kind === 2 ? (edgeOpen.includes(e) ? 'o' : '=') : ':');
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const n = (y * width + x) * 2;
      if (edges[n]) {
        out[y * 2]![x * 2 + 1] = mark(edges[n]!, n, true);
        out[y * 2]![x * 2] = '+';
        out[y * 2]![x * 2 + 2] = '+';
      }
      if (edges[n + 1]) {
        out[y * 2 + 1]![x * 2] = mark(edges[n + 1]!, n + 1, false);
        out[y * 2]![x * 2] = '+';
        out[y * 2 + 2]![x * 2] = '+';
      }
    }
  }
  return out.map((r) => r.join('').trimEnd()).join('\n');
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
