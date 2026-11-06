// Harness (not part of npm test): the inputs for the rendering benchmark (experiments/render-bench).
// Run: npm run harness -- renderbench   (env: SCENARIO, default city-1024; TICKS recorded, default 600)
// Builds the stress scenario, runs it 100 ticks so everything's on the move, and writes to experiments/render-bench/data/:
//   <scenario>.save.json   the state, as a save file: the browser loads it (window.view.loadSave) and plays it on
//   <scenario>.world.json  the map for Godot (as godotexport writes it)
//   <scenario>.movers.bin  every mover's place each tick for TICKS ticks, for Godot to replay: int32 little-endian,
//                          [ticks], then per tick [count] and count x [kind (0 survivor, 1 zombie, 2 animal), x, y]
//                          with x and y in motion.ts SUB units (256 a tile)
//   <scenario>.json        where the colony is (for the camera), and counts
import { mkdirSync, writeFileSync } from 'node:fs';
import { it } from 'vitest';
import { warmReach } from '../src/sim/pathfinding';
import { toSaveFile } from '../src/sim/savefile';
import { tick } from '../src/sim/tick';
import { SCENARIOS } from './stressScenarios';

const TILE: Record<string, string> = { floor: '.', wall: '#', door: 'D', water: '~', barricade: 'B' };

it('records a stress scenario for the rendering benchmark', () => {
  const name = process.env.SCENARIO ?? 'city-1024';
  const ticks = Number(process.env.TICKS ?? 600);
  const sc = SCENARIOS.find((s) => s.name === name)!;
  const state = sc.build();
  warmReach(state.grid, state.walkabilityVersion);
  for (let t = 0; t < 100; t++) tick(state);
  const dir = 'experiments/render-bench/data';
  mkdirSync(dir, { recursive: true });
  const home = state.survivors[0]!.pos;
  writeFileSync(`${dir}/${name}.save.json`, toSaveFile(state));
  const g = state.grid;
  writeFileSync(`${dir}/${name}.world.json`, JSON.stringify({
    size: g.width,
    tiles: g.tiles.map((t) => TILE[t] ?? '?').join(''),
    ground: g.ground.join(','),
    furniture: state.furniture.map((f) => [f.x, f.y, f.kind, f.look ?? '']),
    survivors: [[home.x, home.y]],
  }));
  // Every mover each tick, in the order the browser draws them.
  const chunks: number[] = [ticks];
  let most = 0;
  for (let t = 0; t < ticks; t++) {
    const movers = [
      ...state.survivors.map((s) => [0, s.at.x, s.at.y]),
      ...state.zombies.map((z) => [1, z.at.x, z.at.y]),
      ...state.animals.map((a) => [2, a.at.x, a.at.y]),
    ];
    most = Math.max(most, movers.length);
    chunks.push(movers.length);
    for (const m of movers) chunks.push(...m);
    tick(state);
  }
  writeFileSync(`${dir}/${name}.movers.bin`, Buffer.from(new Int32Array(chunks).buffer));
  writeFileSync(`${dir}/${name}.json`, JSON.stringify({ home, survivors: state.survivors.length, zombies: state.zombies.length, animals: state.animals.length, mostMovers: most, ticks }));
}, 600000);
