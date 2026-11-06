// Harness (not part of npm test): the Godot benchmark's inputs, and the TypeScript side of its sim benchmark.
// Run: npm run harness -- godotexport
//   experiments/godot-bench/data/world-<size>.json  a generated world (fully generated, a day in) for Godot to draw:
//     tiles and ground as letters/numbers per tile, edges (wall, door, window) per border, furniture by kind and
//     look, survivors and zombies.
//   harness/out/godot-ts-bench.txt  the same pathfinding work the Godot side does (bench.gd): BFS over the 128
//     world's tiles from many starts, timed in Node.
import { it } from 'vitest';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { CHUNK, createWorld, ensureGeneratedAround } from '../src/sim/worldgen';
import { tick } from '../src/sim/tick';
import type { SimState } from '../src/sim/state';

const TILE: Record<string, string> = { floor: '.', wall: '#', door: 'D', water: '~', barricade: 'B' };

function exportWorld(state: SimState): object {
  const g = state.grid;
  return {
    size: g.width,
    tiles: g.tiles.map((t) => TILE[t] ?? '?').join(''),
    ground: g.ground.join(','),
    edges: g.edges.join(','),
    furniture: state.furniture.map((f) => [f.x, f.y, f.kind, f.look ?? '']),
    survivors: state.survivors.map((s) => [s.pos.x, s.pos.y]),
    zombies: state.zombies.map((z) => [z.pos.x, z.pos.y]),
  };
}

/** BFS from a start over walkable tiles (floor, door, barricade), 4 ways; returns tiles reached. */
export function bfs(tiles: string, size: number, start: number, dist: Int32Array, queue: Int32Array): number {
  dist.fill(-1);
  let head = 0;
  let tail = 0;
  queue[tail++] = start;
  dist[start] = 0;
  while (head < tail) {
    const i = queue[head++]!;
    const x = i % size;
    const y = (i - x) / size;
    const d = dist[i]! + 1;
    if (x > 0) tail = visit(tiles, i - 1, d, dist, queue, tail);
    if (x < size - 1) tail = visit(tiles, i + 1, d, dist, queue, tail);
    if (y > 0) tail = visit(tiles, i - size, d, dist, queue, tail);
    if (y < size - 1) tail = visit(tiles, i + size, d, dist, queue, tail);
  }
  return tail;
}

function visit(tiles: string, j: number, d: number, dist: Int32Array, queue: Int32Array, tail: number): number {
  if (dist[j] !== -1) return tail;
  const t = tiles.charCodeAt(j);
  if (t !== 46 && t !== 68 && t !== 66) return tail; // . D B
  dist[j] = d;
  queue[tail] = j;
  return tail + 1;
}

it('exports worlds for Godot and times the TS side of the sim benchmark', () => {
  mkdirSync('experiments/godot-bench/data', { recursive: true });
  mkdirSync('harness/out', { recursive: true });
  const out: string[] = [];
  for (const size of [128, 1024]) {
    const state = createWorld(4, size);
    for (let y = 0; y < size; y += CHUNK * 2) for (let x = 0; x < size; x += CHUNK * 2) ensureGeneratedAround(state, { x, y });
    for (let t = 0; t < 100; t++) tick(state);
    const json = JSON.stringify(exportWorld(state));
    writeFileSync(`experiments/godot-bench/data/world-${size}.json`, json);
    out.push(`world-${size}.json: ${(json.length / 1e6).toFixed(1)} MB, furniture ${state.furniture.length}`);
  }
  // The sim benchmark: 200 BFS floods over the 128 world (the same starts as bench.gd).
  const w = JSON.parse(readFileSync('experiments/godot-bench/data/world-128.json', 'utf8')) as { size: number; tiles: string };
  const n = w.size * w.size;
  const dist = new Int32Array(n);
  const queue = new Int32Array(n);
  const starts: number[] = [];
  for (let i = 0; i < n && starts.length < 200; i += 81) if (w.tiles[i] === '.') starts.push(i);
  for (let k = 0; k < 20; k++) bfs(w.tiles, w.size, starts[0]!, dist, queue); // warm up
  const t0 = performance.now();
  let reached = 0;
  for (const s of starts) reached += bfs(w.tiles, w.size, s, dist, queue);
  out.push(`TS: ${starts.length} BFS floods over 128x128 in ${(performance.now() - t0).toFixed(1)} ms (reached ${reached})`);
  writeFileSync('harness/out/godot-ts-bench.txt', out.join('\n') + '\n');
}, 600000);
