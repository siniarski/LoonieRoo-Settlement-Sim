// Scenario files: a seed, an ASCII map, and an optional command log.
// The map legend here is the data format; debug/text.ts renders with it too,
// so a dumped map can be pasted straight back into a scenario.

import { setTile, type Tile } from './grid';
import { replay } from './replay';
import { createState, type LoggedCommand, type SimState } from './state';

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

const CHAR_TILES: Record<string, Tile> = Object.fromEntries(
  Object.entries(TILE_CHARS).map(([tile, char]) => [char, tile as Tile]),
);

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
