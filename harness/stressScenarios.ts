// The stress scenarios (stress.harness.ts) as a module, so other harnesses can build them (renderbench).
import { sendCaravan } from '../src/sim/caravans';
import { isWalkable } from '../src/sim/grid';
import { hashRoll } from '../src/sim/hash';
import { nearestTile } from '../src/sim/space';
import type { SimState } from '../src/sim/state';
import { addSurvivor } from '../src/sim/survivors';
import { CHUNK, createWorld, ensureGeneratedAround } from '../src/sim/worldgen';
import { addZombie } from '../src/sim/zombies';

export function generateAll(state: SimState): void {
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

export interface Scenario {
  name: string;
  build: () => SimState;
}

export const SCENARIOS: Scenario[] = [
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

