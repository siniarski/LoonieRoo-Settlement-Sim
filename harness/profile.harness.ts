// Harness (not part of npm test): a V8 CPU profile of the slowest tick in a zombie-heavy world.
// Run: npm run harness -- profile   (env: see the top of the file; output goes to harness/out/profile.txt)
// It runs 3000 ticks to find the slowest (past tick 20), replays to just before it (the sim is
// deterministic), and profiles that one tick through node:inspector: self time by function.
import { mkdirSync, writeFileSync } from 'node:fs';
import { Session } from 'node:inspector/promises';
import { it } from 'vitest';
import { addZombie } from '../src/sim/zombies';
import { addSurvivor } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';
import { createWorld } from '../src/sim/worldgen';
import { nearestTile } from '../src/sim/space';
import { isWalkable } from '../src/sim/grid';

function setup() {
  const state = createWorld(7, 256);
  const start = state.survivors[0]!.pos;
  for (let i = 0; i < 20; i++) {
    const p = nearestTile(state.grid, { x: start.x + (i % 10) - 5, y: start.y + Math.floor(i / 10) }, (x, y) => isWalkable(state.grid, x, y))!;
    addSurvivor(state, p.x, p.y);
  }
  for (let i = 0; i < 150; i++) {
    const a = (i / 150) * Math.PI * 2;
    const p = nearestTile(state.grid, { x: Math.round(start.x + Math.cos(a) * 30), y: Math.round(start.y + Math.sin(a) * 30) }, (x, y) => isWalkable(state.grid, x, y));
    if (p) addZombie(state, p.x, p.y, 200, 'herd');
  }
  return state;
}

it('profile', async () => {
  // First find the slowest tick, then replay to just before it and profile only that one.
  const probe = setup();
  let worst = 0;
  let worstMs = 0;
  for (let t = 0; t < 3000; t++) {
    for (const s of probe.survivors) s.health = 1000;
    const a = performance.now();
    tick(probe);
    const ms = performance.now() - a;
    if (t > 20 && ms > worstMs) [worst, worstMs] = [t, ms];
  }
  const state = setup();
  for (let t = 0; t < worst; t++) {
    for (const s of state.survivors) s.health = 1000;
    tick(state);
  }
  const session = new Session();
  session.connect();
  await session.post('Profiler.enable');
  await session.post('Profiler.setSamplingInterval', { interval: 50 });
  await session.post('Profiler.start');
  const times: number[] = [];
  for (const s of state.survivors) s.health = 1000;
  for (let rep = 0; rep < 1; rep++) {
    const a = performance.now();
    tick(state);
    times.push(performance.now() - a);
  }
  const { profile } = await session.post('Profiler.stop');
  // Self time by function.
  const self = new Map<string, number>();
  const dt = new Map<number, number>();
  profile.samples!.forEach((id, i) => dt.set(id, (dt.get(id) ?? 0) + (profile.timeDeltas![i] ?? 0)));
  for (const n of profile.nodes) {
    const f = n.callFrame;
    const key = `${f.functionName || '(anon)'} ${f.url.split('/').pop()}:${f.lineNumber + 1}`;
    self.set(key, (self.get(key) ?? 0) + (dt.get(n.id) ?? 0));
  }
  const total = [...self.values()].reduce((a, b) => a + b, 0);
  const top = [...self.entries()].sort((a, b) => b[1] - a[1]).slice(0, 25).map(([k, v]) => `${((100 * v) / total).toFixed(1).padStart(5)}%  ${k}`);
  times.sort((a, b) => a - b);
  const OUT = process.env.OUT ?? 'harness/out/profile.txt';
  mkdirSync('harness/out', { recursive: true });
  writeFileSync(OUT, [`worst tick #${worst} (${worstMs.toFixed(1)} ms in the probe) at sim tick ${state.tick - 1}; zombies ${state.zombies.length} survivors ${state.survivors.length} mean ${(times.reduce((a, b) => a + b, 0) / times.length).toFixed(3)} ms p99 ${times[Math.floor(times.length * 0.99)]!.toFixed(2)} max ${times.at(-1)!.toFixed(2)}`, ...top].join('\n'));
}, 600_000);
