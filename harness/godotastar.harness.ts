// Harness (not part of npm test): the pathfinding benchmark for the Godot comparison (experiments/godot-bench).
// Run after godotexport: npm run harness -- godotastar
//   experiments/godot-bench/data/pairs-<size>.json  start and goal tiles (in the world's biggest open area), the same
//     for every language: 200 pairs on the 128 world, 60 on the 1024.
//   harness/out/godot-astar-ts.txt  A* as written in every language (astar below: 4 ways, Manhattan, a binary heap of
//     f * 2^21 + tile, stale entries skipped), and the game's own findPath on the same world (which also respects
//     edge walls and doors, so its paths can be longer).
import { it } from 'vitest';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { findPath } from '../src/sim/pathfinding';
import { CHUNK, createWorld, ensureGeneratedAround } from '../src/sim/worldgen';
import { tick } from '../src/sim/tick';

const SHIFT = 2097152; // 2^21: room for a 1024x1024 tile index

export interface Scratch {
  g: Int32Array;
  seen: Int32Array;
  heap: Float64Array;
  stamp: number;
}

export function scratch(n: number): Scratch {
  return { g: new Int32Array(n), seen: new Int32Array(n), heap: new Float64Array(n * 4), stamp: 0 };
}

/** A* from start to goal over walkable tiles; the path's length in steps, or -1. */
export function astar(walk: Uint8Array, size: number, start: number, goal: number, s: Scratch): number {
  const { g, seen, heap } = s;
  const stamp = ++s.stamp;
  const gx = goal % size;
  const gy = (goal - gx) / size;
  const h = (i: number) => {
    const x = i % size;
    return Math.abs(x - gx) + Math.abs((i - x) / size - gy);
  };
  let len = 0;
  const push = (key: number) => {
    let k = len++;
    heap[k] = key;
    while (k > 0) {
      const p = (k - 1) >> 1;
      if (heap[p]! <= heap[k]!) break;
      const t = heap[p]!;
      heap[p] = heap[k]!;
      heap[k] = t;
      k = p;
    }
  };
  const pop = (): number => {
    const top = heap[0]!;
    len--;
    if (len > 0) {
      heap[0] = heap[len]!;
      let k = 0;
      for (;;) {
        const l = 2 * k + 1;
        const r = l + 1;
        let m = k;
        if (l < len && heap[l]! < heap[m]!) m = l;
        if (r < len && heap[r]! < heap[m]!) m = r;
        if (m === k) break;
        const t = heap[m]!;
        heap[m] = heap[k]!;
        heap[k] = t;
        k = m;
      }
    }
    return top;
  };
  g[start] = 0;
  seen[start] = stamp;
  push(h(start) * SHIFT + start);
  const n = walk.length;
  while (len > 0) {
    const key = pop();
    const i = key % SHIFT;
    const f = (key - i) / SHIFT;
    if (f > g[i]! + h(i)) continue; // stale
    if (i === goal) return g[i]!;
    const x = i % size;
    const ng = g[i]! + 1;
    for (let d = 0; d < 4; d++) {
      const j = d === 0 ? (x > 0 ? i - 1 : -1) : d === 1 ? (x < size - 1 ? i + 1 : -1) : d === 2 ? i - size : i + size;
      if (j < 0 || j >= n || walk[j] === 0) continue;
      if (seen[j] === stamp && g[j]! <= ng) continue;
      seen[j] = stamp;
      g[j] = ng;
      push((ng + h(j)) * SHIFT + j);
    }
  }
  return -1;
}

/** Walkable tiles (floor, door, barricade) from the export's tile letters. */
export function walkOf(tiles: string): Uint8Array {
  const w = new Uint8Array(tiles.length);
  for (let i = 0; i < tiles.length; i++) {
    const t = tiles[i];
    w[i] = t === '.' || t === 'D' || t === 'B' ? 1 : 0;
  }
  return w;
}

/** The tiles of the biggest 4-way connected open area. */
function biggestArea(walk: Uint8Array, size: number): number[] {
  const label = new Int32Array(walk.length).fill(-1);
  let best: number[] = [];
  for (let s = 0; s < walk.length; s++) {
    if (walk[s] === 0 || label[s] !== -1) continue;
    const area = [s];
    label[s] = s;
    for (let k = 0; k < area.length; k++) {
      const i = area[k]!;
      const x = i % size;
      for (const j of [x > 0 ? i - 1 : -1, x < size - 1 ? i + 1 : -1, i - size, i + size]) {
        if (j < 0 || j >= walk.length || walk[j] === 0 || label[j] !== -1) continue;
        label[j] = s;
        area.push(j);
      }
    }
    if (area.length > best.length) best = area;
  }
  return best;
}

it('pathfinding benchmark, TypeScript side', () => {
  mkdirSync('harness/out', { recursive: true });
  const out: string[] = [];
  for (const [size, count] of [[128, 200], [1024, 60]] as const) {
    const w = JSON.parse(readFileSync(`experiments/godot-bench/data/world-${size}.json`, 'utf8')) as { tiles: string };
    const walk = walkOf(w.tiles);
    const area = biggestArea(walk, size);
    // Pairs by a fixed LCG, so every language gets the same.
    let r = 12345;
    const next = () => (r = (r * 1103515245 + 12345) % 2147483648) / 2147483648;
    const pairs: number[] = [];
    for (let k = 0; k < count; k++) pairs.push(area[Math.floor(next() * area.length)]!, area[Math.floor(next() * area.length)]!);
    writeFileSync(`experiments/godot-bench/data/pairs-${size}.json`, JSON.stringify({ pairs }));
    const s = scratch(size * size);
    for (let k = 0; k < 3; k++) for (let p = 0; p < pairs.length; p += 2) astar(walk, size, pairs[p]!, pairs[p + 1]!, s); // warm up
    const t0 = performance.now();
    let steps = 0;
    for (let p = 0; p < pairs.length; p += 2) steps += astar(walk, size, pairs[p]!, pairs[p + 1]!, s);
    const ms = performance.now() - t0;
    out.push(`${size}: ts_astar paths=${count} ms=${ms.toFixed(1)} steps=${steps} (open area ${area.length} tiles)`);
    // The game's own A* on the same world (edge walls and doors count, so paths can be longer or missing).
    const state = createWorld(4, size);
    for (let y = 0; y < size; y += CHUNK * 2) for (let x = 0; x < size; x += CHUNK * 2) ensureGeneratedAround(state, { x, y });
    for (let t = 0; t < 100; t++) tick(state);
    const pt = (i: number) => ({ x: i % size, y: Math.floor(i / size) });
    for (let p = 0; p < Math.min(pairs.length, 20); p += 2) findPath(state.grid, pt(pairs[p]!), pt(pairs[p + 1]!)); // warm up
    const t1 = performance.now();
    let found = 0;
    let gameSteps = 0;
    for (let p = 0; p < pairs.length; p += 2) {
      const path = findPath(state.grid, pt(pairs[p]!), pt(pairs[p + 1]!));
      if (path) {
        found++;
        gameSteps += path.length;
      }
    }
    out.push(`${size}: game_findPath paths=${count} found=${found} ms=${(performance.now() - t1).toFixed(1)} steps=${gameSteps}`);
  }
  writeFileSync('harness/out/godot-astar-ts.txt', out.join('\n') + '\n');
}, 600000);
