// Scenario files: a seed, an ASCII map, and an optional command log.
// The map legend here is the data format; debug/text.ts renders with it too,
// so a dumped map can be pasted straight back into a scenario.

import { addBlueprint } from './designations';
import { setDirty, setTile, type Tile } from './grid';
import { spawnItem } from './items';
import { replay } from './replay';
import { createState, type LoggedCommand, type SimState } from './state';
import { addSurvivor } from './survivors';

export interface Scenario {
  name?: string;
  seed: number;
  /** One string per row, top to bottom. All rows must be the same length. */
  map: string[];
  commands?: LoggedCommand[];
}

export const TILE_CHARS: Record<Tile, string> = {
  floor: '.',
  wall: '#',
  door: 'D',
};

/**
 * Things on floor tiles. Survivors get ids in reading order. A removal
 * designation ('x') is only ever printed, never parsed, since it needs a
 * wall underneath.
 */
export const ENTITY_CHARS = {
  survivor: 's',
  wood: 'w',
  dirt: ',',
  wallBlueprint: '+',
  doorBlueprint: 'd',
  removeBlueprint: 'x',
} as const;

const CHAR_TILES: Record<string, Tile> = Object.fromEntries(
  Object.entries(TILE_CHARS).map(([tile, char]) => [char, tile as Tile]),
);

/** Floor plus something on it. Returns false if `char` isn't an entity char. */
function placeEntity(state: SimState, char: string, x: number, y: number): boolean {
  switch (char) {
    case ENTITY_CHARS.survivor:
      addSurvivor(state, x, y);
      return true;
    case ENTITY_CHARS.wood:
      spawnItem(state, 'wood', x, y);
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
  return state;
}

/** Loads the scenario and replays its command log up to `ticks`. */
export function runScenario(scenario: Scenario, ticks: number): SimState {
  return replay(loadScenario(scenario), scenario.commands ?? [], ticks);
}
