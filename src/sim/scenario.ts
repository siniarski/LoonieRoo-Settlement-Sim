// Scenario files: a seed, an ASCII map, and an optional command log.
// The map legend here is the data format; debug/text.ts renders with it too,
// so a dumped map can be pasted straight back into a scenario.

import { addBlueprint } from './designations';
import { addPlot } from './farming';
import { addFurniture } from './furniture';
import { setDirty, setTile, type Tile } from './grid';
import { spawnItem } from './items';
import { replay } from './replay';
import type { RoomPurpose } from './rooms';
import { createState, type LoggedCommand, type SimState } from './state';
import { addSurvivor } from './survivors';
import { addWanderer } from './wanderers';
import { addZombie } from './zombies';
import { addDog } from './dogs';

export interface Scenario {
  name?: string;
  seed: number;
  /** One string per row, top to bottom. All rows must be the same length. */
  map: string[];
  /** Room purposes, anchored at a tile inside each room. */
  rooms?: { x: number; y: number; purpose: RoomPurpose }[];
  commands?: LoggedCommand[];
}

export const TILE_CHARS: Record<Tile, string> = {
  floor: '.',
  wall: '#',
  door: 'D',
  water: '%',
  barricade: '=',
};

/**
 * Things on floor tiles. Survivors get ids in reading order. A removal
 * designation ('x') is only ever printed, never parsed, since it needs a
 * wall underneath.
 */
export const ENTITY_CHARS = {
  survivor: 's',
  wood: 'w',
  food: 'f',
  bandage: 'm',
  weapon: '/',
  dirt: ',',
  bed: 'b',
  table: 't',
  workbench: 'k',
  trap: '^',
  stove: 'o',
  crate: 'c',
  tree: 'Y',
  zombie: 'z',
  wanderer: 'v',
  farmPlot: '~',
  wallBlueprint: '+',
  doorBlueprint: 'd',
  bedBlueprint: 'B',
  tableBlueprint: 'T',
  workbenchBlueprint: 'K',
  barricadeBlueprint: '-',
  trapBlueprint: '_',
  stoveBlueprint: 'O',
  meal: 'h',
  medicine: '!',
  cloth: 'L',
  coat: 'C',
  armour: 'A',
  boots: 'U',
  gun: 'G',
  ammo: 'a',
  corpse: '&',
  book: 'N',
  cart: 'P',
  dog: 'g',
  tower: 'H',
  towerBlueprint: 'j',
  well: 'W',
  wellBlueprint: 'Q',
  collector: 'R',
  collectorBlueprint: 'q',
  generator: 'E',
  generatorBlueprint: 'e',
  lamp: 'l',
  lampBlueprint: 'i',
  fridge: 'F',
  fridgeBlueprint: 'r',
  removeBlueprint: 'x',
} as const;

/** Item kinds placed straight from their map char (the rest have cases below). */
/** Furniture and blueprints placed straight from their map char. */
const SIMPLE_FURNITURE = ['well', 'collector', 'generator', 'lamp', 'fridge'] as const;

const SIMPLE_ITEMS = ['cloth', 'coat', 'armour', 'boots', 'gun', 'ammo', 'corpse', 'book', 'cart'] as const;

const CHAR_TILES: Record<string, Tile> = Object.fromEntries(
  Object.entries(TILE_CHARS).map(([tile, char]) => [char, tile as Tile]),
);

/** Floor plus something on it. Returns false if `char` isn't an entity char. */
function placeEntity(state: SimState, char: string, x: number, y: number): boolean {
  const simple = SIMPLE_ITEMS.find((k) => ENTITY_CHARS[k] === char);
  if (simple) {
    spawnItem(state, simple, x, y);
    return true;
  }
  const furniture = SIMPLE_FURNITURE.find((k) => ENTITY_CHARS[k] === char);
  if (furniture) {
    addFurniture(state, furniture, x, y);
    return true;
  }
  const planned = SIMPLE_FURNITURE.find((k) => ENTITY_CHARS[`${k}Blueprint`] === char);
  if (planned) {
    addBlueprint(state, x, y, planned);
    return true;
  }
  switch (char) {
    case ENTITY_CHARS.survivor:
      addSurvivor(state, x, y);
      return true;
    case ENTITY_CHARS.wood:
      spawnItem(state, 'wood', x, y);
      return true;
    case ENTITY_CHARS.food:
      spawnItem(state, 'food', x, y);
      return true;
    case ENTITY_CHARS.bandage:
      spawnItem(state, 'bandage', x, y);
      return true;
    case ENTITY_CHARS.weapon:
      spawnItem(state, 'weapon', x, y);
      return true;
    case ENTITY_CHARS.dirt:
      setDirty(state.grid, x, y, true);
      return true;
    case ENTITY_CHARS.wallBlueprint:
      addBlueprint(state, x, y, 'wall');
      return true;
    case ENTITY_CHARS.doorBlueprint:
      addBlueprint(state, x, y, 'door');
      return true;
    case ENTITY_CHARS.bed:
      addFurniture(state, 'bed', x, y);
      return true;
    case ENTITY_CHARS.farmPlot:
      addPlot(state, x, y);
      return true;
    case ENTITY_CHARS.wanderer:
      addWanderer(state, x, y);
      return true;
    case ENTITY_CHARS.zombie:
      addZombie(state, x, y);
      return true;
    case ENTITY_CHARS.crate:
      addFurniture(state, 'crate', x, y);
      return true;
    case ENTITY_CHARS.tree:
      addFurniture(state, 'tree', x, y);
      return true;
    case ENTITY_CHARS.bedBlueprint:
      addBlueprint(state, x, y, 'bed');
      return true;
    case ENTITY_CHARS.table:
      addFurniture(state, 'table', x, y);
      return true;
    case ENTITY_CHARS.tableBlueprint:
      addBlueprint(state, x, y, 'table');
      return true;
    case ENTITY_CHARS.workbench:
      addFurniture(state, 'workbench', x, y);
      return true;
    case ENTITY_CHARS.trap:
      addFurniture(state, 'trap', x, y);
      return true;
    case ENTITY_CHARS.stove:
      addFurniture(state, 'stove', x, y);
      return true;
    case ENTITY_CHARS.barricadeBlueprint:
      addBlueprint(state, x, y, 'barricade');
      return true;
    case ENTITY_CHARS.trapBlueprint:
      addBlueprint(state, x, y, 'trap');
      return true;
    case ENTITY_CHARS.stoveBlueprint:
      addBlueprint(state, x, y, 'stove');
      return true;
    case ENTITY_CHARS.meal:
      spawnItem(state, 'meal', x, y);
      return true;
    case ENTITY_CHARS.medicine:
      spawnItem(state, 'medicine', x, y);
      return true;
    case ENTITY_CHARS.dog:
      addDog(state, x, y);
      return true;
    case ENTITY_CHARS.tower:
      addFurniture(state, 'tower', x, y);
      return true;
    case ENTITY_CHARS.towerBlueprint:
      addBlueprint(state, x, y, 'tower');
      return true;
    case ENTITY_CHARS.workbenchBlueprint:
      addBlueprint(state, x, y, 'workbench');
      return true;
    default:
      return false;
  }
}

/** Builds the initial state (tick 0, empty log). Does not apply commands. */
export function loadScenario(scenario: Scenario): SimState {
  const height = scenario.map.length;
  const width = scenario.map[0]?.length ?? 0;
  if (height === 0 || width === 0) throw new Error('loadScenario: map is empty');

  const state = createState({ seed: scenario.seed, width, height });
  scenario.map.forEach((row, y) => {
    if (row.length !== width) {
      throw new Error(`loadScenario: row ${y} has length ${row.length}, expected ${width}`);
    }
    [...row].forEach((char, x) => {
      if (placeEntity(state, char, x, y)) return;
      const tile = CHAR_TILES[char];
      if (tile === undefined) throw new Error(`loadScenario: unknown char '${char}' at (${x}, ${y})`);
      setTile(state.grid, x, y, tile);
    });
  });
  for (const room of scenario.rooms ?? []) {
    state.roomDesignations.push({ id: `r${state.nextId++}`, x: room.x, y: room.y, purpose: room.purpose });
  }
  return state;
}

/** Loads the scenario and replays its command log up to `ticks`. */
export function runScenario(scenario: Scenario, ticks: number): SimState {
  return replay(loadScenario(scenario), scenario.commands ?? [], ticks);
}
