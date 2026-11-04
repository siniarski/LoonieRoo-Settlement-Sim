// Harness (not part of npm test): what a tick costs as the world fills up, plus save size and memory over 10 days.
// Run: npm run harness -- bench   (env: see the top of the file; output goes to harness/out/bench.txt)
// ONLY=1 runs just the zombie-heavy 256 case, for longer.
import { mkdirSync, writeFileSync } from 'node:fs';
import { it } from 'vitest';
import { addZombie } from '../src/sim/zombies';
import { addSurvivor } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';
import { createWorld } from '../src/sim/worldgen';
import { nearestTile } from '../src/sim/space';
import { isWalkable } from '../src/sim/grid';
import { DAY_LENGTH } from '../src/sim/time';

const out: string[] = [];
const OUT = process.env.OUT ?? 'harness/out/bench.txt';
mkdirSync('harness/out', { recursive: true });


function bench(size: number, colonists: number, zombies: number, ticks = 600): void {
  const state = createWorld(7, size);
  const start = state.survivors[0]!.pos;
  for (let i = state.survivors.length; i < colonists; i++) {
    const p = nearestTile(state.grid, { x: start.x + (i % 10) - 5, y: start.y + Math.floor(i / 10) }, (x, y) => isWalkable(state.grid, x, y))!;
    addSurvivor(state, p.x, p.y);
  }
  for (let i = 0; i < zombies; i++) {
    const a = (i / zombies) * Math.PI * 2;
    const p = nearestTile(state.grid, { x: Math.round(start.x + Math.cos(a) * 25), y: Math.round(start.y + Math.sin(a) * 25) }, (x, y) => isWalkable(state.grid, x, y));
    if (p) addZombie(state, p.x, p.y, 200, 'herd');
  }
  for (let t = 0; t < 60; t++) tick(state); // warm up
  const times: number[] = [];
  for (let t = 0; t < ticks; t++) {
    const a = performance.now();
    tick(state);
    times.push(performance.now() - a);
  }
  times.sort((a, b) => a - b);
  const mean = times.reduce((s, x) => s + x, 0) / times.length;
  const p99 = times[Math.floor(times.length * 0.99)]!;
  const max = times.at(-1)!;
  const json = JSON.stringify(state);
  out.push(`size ${size} colonists ${String(state.survivors.length).padStart(3)} zombies ${String(state.zombies.length).padStart(3)} | mean ${mean.toFixed(3)} ms  p99 ${p99.toFixed(2)}  max ${max.toFixed(2)} | ticks/s at 1 core ${(1000 / mean).toFixed(0)} | save ${(json.length / 1024).toFixed(0)} KB items ${state.items.length} furniture ${state.furniture.length}`);
}

it('bench', () => {
  for (const [size, c, z] of (process.env.ONLY ? [[256, 3, 200]] : [[128, 3, 0], [128, 20, 0], [128, 50, 0], [128, 100, 0], [128, 3, 50], [128, 3, 200], [128, 50, 200], [256, 3, 0], [256, 50, 200], [512, 3, 0]]) as [number, number, number][]) bench(size, c, z, process.env.ONLY ? 3000 : 600);
  if (process.env.ONLY) return writeFileSync(OUT, out.join('\n') + '\n');
  // A long game: save size and memory after 10 days.
  const state = createWorld(7, 128);
  const before = process.memoryUsage().heapUsed;
  const a = performance.now();
  for (let t = 0; t < 10 * DAY_LENGTH; t++) tick(state);
  const ms = performance.now() - a;
  const json = JSON.stringify(state);
  out.push(`10 days on 128: ${(ms / 1000).toFixed(1)} s (${(ms / (10 * DAY_LENGTH)).toFixed(3)} ms/tick), save ${(json.length / 1024).toFixed(0)} KB, events ${state.events.length}, commandLog ${state.commandLog.length}, explored+tiles arrays ${state.grid.tiles.length}, heap +${((process.memoryUsage().heapUsed - before) / 1e6).toFixed(1)} MB`);
  const sizes = Object.entries(state).map(([k, v]) => [k, JSON.stringify(v).length] as const).sort((x, y) => y[1] - x[1]).slice(0, 6);
  out.push('biggest parts of the save: ' + sizes.map(([k, n]) => `${k} ${(n / 1024).toFixed(0)} KB`).join(', '));
  writeFileSync(OUT, out.join('\n') + '\n');
}, 3_600_000);
