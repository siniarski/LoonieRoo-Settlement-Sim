// Harness (not part of npm test): reference outputs from the TypeScript sim for the C# port to match
// (cs/Parity). Run: npm run harness -- parity   -> cs/Parity/data/*.json
//   vectors.json  hashRoll, idNumber, mulberry32 and the clock on awkward inputs (huge ids, negatives,
//                 fractions), so the C# number handling is checked bit for bit.
//   paths.json    a generated world's grid (some doors left open) and answers to path, reach and straightening
//                 queries for every kind of mover.
//   states/*.json whole sim states (JSON.stringify, as cloneState): a generated world two days in, a colony
//                 under the stress scenario's crowd, so the C# state types are checked field for field.
import { mkdirSync, writeFileSync } from 'node:fs';
import { it } from 'vitest';
import { hashRoll, idNumber } from '../src/sim/hash';
import { mulberry32, seedRng } from '../src/sim/rng';
import { CHUNK, createWorld, ensureGeneratedAround } from '../src/sim/worldgen';
import { canReach, findPathToAny, openBetween, straighten, walksAcross, type Mover } from '../src/sim/pathfinding';
import { nearestTile } from '../src/sim/space';
import { isWalkable } from '../src/sim/grid';
import { tick } from '../src/sim/tick';
import { SCENARIOS } from './stressScenarios';
import { clockText, darkness, dayOf, hourOf, isNight, nightNumber, setDayScale, tickOfDay, ticksIntoNight } from '../src/sim/time';

it('writes reference vectors for the C# port', () => {
  mkdirSync('cs/Parity/data', { recursive: true });
  const hashInputs: number[][] = [
    [], [0], [1], [-1], [2147483647], [2147483648], [4294967295], [4294967296], [-2147483649],
    [1.5], [-1.5], [1e15], [123456789012], [3, 7], [7, 3], [0xdee7, 12], [1, 2, 3, 4, 5], [0.999, -0.999],
    [idNumber('sr12x34'), 99, 0x5ca, 2], [1e21], [-1e21], [Number.MAX_SAFE_INTEGER],
  ];
  const hashes = hashInputs.map((args) => ({ args, value: hashRoll(...args) }));
  const ids = ['z12', 'sr3x456', 'abc', 's0', 'x99999999999', 'p1e3'].map((id) => ({ id, value: idNumber(id) }));
  const rng: number[] = [];
  let state = seedRng(42);
  for (let k = 0; k < 1000; k++) {
    const { value, next } = mulberry32(state);
    rng.push(value, next);
    state = next;
  }
  const seeds = [0, 1, -1, 4294967296, 1e10, 123.9].map((s) => ({ s, value: seedRng(s) }));
  const clock: unknown[] = [];
  for (const scale of [1, 2, 3]) {
    setDayScale(scale);
    for (const t of [0, 1, 199, 200, 3000, 3001, 4799, 4800, 9599, 14400, 100000, 123457]) {
      clock.push({ scale, t, tickOfDay: tickOfDay(t), dayOf: dayOf(t), hourOf: hourOf(t), isNight: isNight(t), night: nightNumber(t), into: isNight(t) ? ticksIntoNight(t) : null, dark: darkness(t), text: clockText(t) });
    }
  }
  setDayScale(1);
  writeFileSync('cs/Parity/data/vectors.json', JSON.stringify({ hashes, ids, rng, seeds, clock }));
});

it('writes pathfinding references for the C# port', () => {
  const state = createWorld(11, 256);
  for (let y = 0; y < 256; y += CHUNK) for (let x = 0; x < 256; x += CHUNK) ensureGeneratedAround(state, { x, y });
  const g = state.grid;
  // Some doors and edge doors left open, so door movers' joins are exercised.
  let r = 777;
  const next = () => (r = (r * 1103515245 + 12345) % 2147483648) / 2147483648;
  for (let i = 0; i < g.tiles.length; i++) if (g.tiles[i] === 'door' && next() < 0.4) g.open.push(i);
  for (let e = 0; e < g.edges.length; e++) if (g.edges[e] === 2 && next() < 0.4) g.edgeOpen.push(e);
  const walkable: number[] = [];
  for (let i = 0; i < g.tiles.length; i++) if (isWalkable(g, i % 256, Math.floor(i / 256))) walkable.push(i);
  const pt = (i: number) => ({ x: i % 256, y: Math.floor(i / 256) });
  const movers: Mover[] = ['survivor', 'free', 'zombie', 'breacher', 'raider', 'crawler'];
  const queries: unknown[] = [];
  for (let k = 0; k < 1200; k++) {
    const mover = movers[k % movers.length]!;
    const from = pt(walkable[Math.floor(next() * walkable.length)]!);
    const goals = Array.from({ length: 1 + (k % 3) }, () => pt(walkable[Math.floor(next() * walkable.length)]!));
    const path = findPathToAny(g, from, goals, mover);
    const reach = canReach(g, state.walkabilityVersion, from, goals, mover);
    const straight = path && mover === 'survivor' ? straighten(g, from, path) : null;
    const b = { x: from.x + (k % 3) - 1, y: from.y + (Math.floor(k / 3) % 3) - 1 };
    const near = nearestTile(g, from, (x, y) => g.tiles[y * 256 + x] === 'door', 2000);
    queries.push({ mover, from, goals, path: path ? path.flatMap((p) => [p.x, p.y]) : null, reach, straight: straight ? straight.flatMap((p) => [p.x, p.y]) : null, b, open: openBetween(g, from, b), walks: walksAcross(g, from, b, mover), near });
  }
  writeFileSync('cs/Parity/data/paths.json', JSON.stringify({ grid: g, version: state.walkabilityVersion, queries }));
}, 600000);

it('writes whole states for the C# port', () => {
  mkdirSync('cs/Parity/data/states', { recursive: true });
  const world = createWorld(5, 128);
  for (let k = 0; k < 2 * 4800; k++) tick(world);
  writeFileSync('cs/Parity/data/states/world-128-day3.json', JSON.stringify(world));
  const colony = SCENARIOS.find((s) => s.name === 'colony-512')!.build();
  for (let k = 0; k < 600; k++) tick(colony);
  writeFileSync('cs/Parity/data/states/colony-512.json', JSON.stringify(colony));
}, 600000);
