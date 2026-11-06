// Harness (not part of npm test): a very large stress test, to find where the sim breaks at scale.
// Run: npm run harness -- stress   (output goes to harness/out/stress.txt)
// Env: ONLY=<name> runs one scenario; TICKS (default 300) per scenario; PROFILE=0 skips the CPU profile.
// For each scenario: build it (generation time), run TICKS ticks (mean, p99, max ms), memory after a forced
// collection (needs --expose-gc to be exact; without it, heapUsed as is), save size and time to save, and
// the functions that took the most self time over the run (a V8 sampling profile).
//
// Scenarios, from big to brutal:
//   gen-512 / gen-1024     whole world generated (every chunk), 3 colonists
//   colony-512             100 colonists, 500 zombies round them (half herd, half static), on 512
//   horde-512              30 colonists, 2000 zombies all over the map, 6 herds, 20 caravans
//   spread-1024            40 colonists in 4 groups at the corners of a 1024 map (generation, fog, sight
//                          and far-off life everywhere at once), 1000 zombies
//   city-1024              200 colonists, 1500 zombies, 10 herds, 30 caravans on 1024: everything at once
import { mkdirSync, writeFileSync } from 'node:fs';
import { Session } from 'node:inspector/promises';
import { it } from 'vitest';
import { sendCaravan } from '../src/sim/caravans';
import { isWalkable } from '../src/sim/grid';
import { hashRoll } from '../src/sim/hash';
import { toSaveFile } from '../src/sim/savefile';
import { nearestTile } from '../src/sim/space';
import type { SimState } from '../src/sim/state';
import { addSurvivor } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';
import { CHUNK, createWorld, ensureGeneratedAround } from '../src/sim/worldgen';
import { addZombie } from '../src/sim/zombies';

const out: string[] = [];
const OUT = process.env.OUT ?? 'harness/out/stress.txt';
mkdirSync('harness/out', { recursive: true });
const flush = () => writeFileSync(OUT, out.join('\n') + '\n');
const TICKS = Number(process.env.TICKS ?? 300);
const gc = (globalThis as { gc?: () => void }).gc;
const heapMb = () => {
  gc?.();
  return process.memoryUsage().heapUsed / 1e6;
};

function generateAll(state: SimState): void {
  const n = state.grid.width;
  for (let y = 0; y < n; y += CHUNK) for (let x = 0; x < n; x += CHUNK) ensureGeneratedAround(state, { x, y });
}

/** A walkable tile near p, or null. */
const near = (state: SimState, p: { x: number; y: number }) =>
  nearestTile(state.grid, { x: Math.max(0, Math.min(state.grid.width - 1, Math.round(p.x))), y: Math.max(0, Math.min(state.grid.height - 1, Math.round(p.y))) }, (x, y) => isWalkable(state.grid, x, y), 4000);

function colonists(state: SimState, at: { x: number; y: number }, n: number): void {
  for (let i = 0; i < n; i++) {
    const p = near(state, { x: at.x + (i % 12) - 6, y: at.y + Math.floor(i / 12) - 4 });
    if (p) addSurvivor(state, p.x, p.y);
  }
}

function zombiesRound(state: SimState, at: { x: number; y: number }, n: number, r: number): void {
  for (let i = 0; i < n; i++) {
    const a = (i / n) * Math.PI * 2;
    const p = near(state, { x: at.x + Math.cos(a) * r, y: at.y + Math.sin(a) * r });
    if (p) addZombie(state, p.x, p.y, 200, i % 2 === 0 ? 'herd' : 'static');
  }
}

function zombiesEverywhere(state: SimState, n: number): void {
  const s = state.grid.width;
  for (let i = 0; i < n; i++) {
    const p = near(state, { x: hashRoll(i, 1) * s, y: hashRoll(i, 2) * s });
    if (p) addZombie(state, p.x, p.y, 200, i % 3 === 0 ? 'herd' : 'static');
  }
}

function herds(state: SimState, n: number): void {
  const s = state.grid.width;
  for (let i = 0; i < n; i++) {
    state.herds.push({ id: `h${state.nextId++}`, x: Math.floor(hashRoll(i, 3) * s), y: 0, to: { x: Math.floor(hashRoll(i, 4) * s), y: s - 1 }, size: 30, nextStepTick: state.tick, heard: false });
  }
}

function caravans(state: SimState, n: number): void {
  const ss = state.settlements;
  if (ss.length < 2) return;
  for (let i = 0; i < n; i++) {
    const a = ss[i % ss.length]!;
    const b = ss[(i + 1) % ss.length]!;
    sendCaravan(state, a.id, b.id, { x: b.x, y: b.y }, 5);
  }
}

interface Scenario {
  name: string;
  build: () => SimState;
}

const SCENARIOS: Scenario[] = [
  { name: 'gen-512', build: () => { const s = createWorld(7, 512); generateAll(s); return s; } },
  { name: 'gen-1024', build: () => { const s = createWorld(7, 1024); generateAll(s); return s; } },
  {
    name: 'colony-512',
    build: () => {
      const s = createWorld(7, 512);
      const home = s.survivors[0]!.pos;
      colonists(s, home, 100);
      zombiesRound(s, home, 500, 30);
      return s;
    },
  },
  {
    name: 'horde-512',
    build: () => {
      const s = createWorld(7, 512);
      generateAll(s);
      colonists(s, s.survivors[0]!.pos, 30);
      zombiesEverywhere(s, 2000);
      herds(s, 6);
      caravans(s, 20);
      return s;
    },
  },
  {
    name: 'spread-1024',
    build: () => {
      const s = createWorld(7, 1024);
      for (const c of [{ x: 100, y: 100 }, { x: 920, y: 100 }, { x: 100, y: 920 }, { x: 920, y: 920 }]) {
        ensureGeneratedAround(s, c);
        colonists(s, c, 10);
      }
      zombiesEverywhere(s, 1000);
      return s;
    },
  },
  {
    name: 'city-1024',
    build: () => {
      const s = createWorld(7, 1024);
      generateAll(s);
      const home = s.survivors[0]!.pos;
      colonists(s, home, 200);
      zombiesRound(s, home, 700, 40);
      zombiesEverywhere(s, 800);
      herds(s, 10);
      caravans(s, 30);
      return s;
    },
  },
];

async function run(sc: Scenario): Promise<void> {
  const h0 = heapMb();
  const g0 = performance.now();
  const state = sc.build();
  const genMs = performance.now() - g0;
  const h1 = heapMb();
  for (let t = 0; t < 20; t++) tick(state); // warm up
  const session = process.env.PROFILE === '0' ? null : new Session();
  if (session) {
    session.connect();
    await session.post('Profiler.enable');
    await session.post('Profiler.setSamplingInterval', { interval: 200 });
    await session.post('Profiler.start');
  }
  const times: number[] = [];
  for (let t = 0; t < TICKS; t++) {
    const a = performance.now();
    tick(state);
    times.push(performance.now() - a);
  }
  const top: string[] = [];
  if (session) {
    const { profile } = await session.post('Profiler.stop');
    const byId = new Map(profile.nodes.map((n) => [n.id, n]));
    const self = new Map<string, number>();
    let total = 0;
    for (const id of profile.samples ?? []) {
      const n = byId.get(id)!;
      const k = `${n.callFrame.functionName || '(anon)'} ${n.callFrame.url.split('/').pop()}:${n.callFrame.lineNumber + 1}`;
      self.set(k, (self.get(k) ?? 0) + 1);
      total++;
    }
    for (const [k, c] of [...self].sort((x, y) => y[1] - x[1]).slice(0, 12)) top.push(`      ${((100 * c) / total).toFixed(1).padStart(5)}%  ${k}`);
    session.disconnect();
  }
  times.sort((a, b) => a - b);
  const mean = times.reduce((s, x) => s + x, 0) / times.length;
  const s0 = performance.now();
  const save = toSaveFile(state);
  const saveMs = performance.now() - s0;
  const h2 = heapMb();
  out.push(
    `${sc.name.padEnd(12)} size ${state.grid.width} | colonists ${state.survivors.length} zombies ${state.zombies.length} herds ${state.herds.length} caravans ${(state.caravans ?? []).length} furniture ${state.furniture.length} items ${state.items.length}`,
    `   build ${(genMs / 1000).toFixed(1)} s | tick mean ${mean.toFixed(2)} ms  p99 ${times[Math.floor(times.length * 0.99)]!.toFixed(1)}  max ${times.at(-1)!.toFixed(1)} | ${(1000 / mean).toFixed(0)} ticks/s (10 needed at 1x, 40 at 4x)`,
    `   heap ${h0.toFixed(0)} -> ${h1.toFixed(0)} after build -> ${h2.toFixed(0)} MB after run | save ${(save.length / 1e6).toFixed(1)} MB in ${saveMs.toFixed(0)} ms${gc ? '' : ' (no --expose-gc: heap figures include garbage)'}`,
    ...top,
  );
  flush();
}

it('stress', async () => {
  for (const sc of SCENARIOS.filter((s) => !process.env.ONLY || s.name === process.env.ONLY)) await run(sc);
}, 3_600_000);
