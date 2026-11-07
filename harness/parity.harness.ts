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
import { applyCommand, type Command } from '../src/sim/commands';
import { SCENARIOS } from './stressScenarios';
import { PRESETS } from '../src/sim/difficulty';
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

// Whole runs: a start state and the state at each checkpoint (checkpoints.json), for the C# sim to replay from the
// start and match at every checkpoint (the first one that differs says where to look). Runs cross nights, so
// zombies, raids and the rest of the night come in; settings vary (solo, a year on, nuclear winter, islands).
it('writes whole runs for the C# port', () => {
  const DAY = 4800;
  const runs: [string, () => ReturnType<typeof createWorld>, number[]][] = [
    ['world-64', () => {
      const s = createWorld(5, 64);
      for (let k = 0; k < 400; k++) tick(s);
      return s;
    }, [1, 2, 5, 10, 30, 100, 300, 1000, 3000, DAY, 2 * DAY]],
    ['solo-apocalypse-96', () => createWorld(9, 96, PRESETS.apocalypse.settings), [1, 10, 100, 1000, 3000, DAY, 2 * DAY]],
    ['winter-islands-96', () => createWorld(13, 96, PRESETS.winter.settings), [1, 10, 100, 1000, DAY, 2 * DAY]],
    ['colony-512', () => {
      const s = SCENARIOS.find((x) => x.name === 'colony-512')!.build();
      for (let k = 0; k < 50; k++) tick(s);
      return s;
    }, [1, 2, 5, 10, 30, 100, 300, 1000, 3000]],
    ['horde-512', () => SCENARIOS.find((x) => x.name === 'horde-512')!.build(), [1, 10, 100, 300, 1000]],
  ];
  for (const [name, build, checkpoints] of runs) {
    const dir = `cs/Parity/data/runs/${name}`;
    mkdirSync(dir, { recursive: true });
    const s = build();
    writeFileSync(`${dir}/start.json`, JSON.stringify(s));
    writeFileSync(`${dir}/checkpoints.json`, JSON.stringify(checkpoints));
    let done = 0;
    for (const at of checkpoints) {
      for (; done < at; done++) tick(s);
      writeFileSync(`${dir}/t${at}.json`, JSON.stringify(s));
    }
  }
}, 1800000);

// Command runs: the colony player's opening (colony.harness.ts) as commands through the command path, so the C#
// replays the command log and must match too. start.json is before any command; commands.json is the log
// ({tick, command}: applied after the tick that brought the clock there, or at 0 before the first tick).
it('writes command runs for the C# port', () => {
  const dir = 'cs/Parity/data/runs/colony-commands-128';
  mkdirSync(dir, { recursive: true });
  const s = createWorld(1, 128);
  writeFileSync(`${dir}/start.json`, JSON.stringify(s));
  const start = { ...s.survivors[0]!.pos };
  const cmd = (c: Command) => applyCommand(s, c);
  cmd({ type: 'designateRoom', x: start.x - 3, y: start.y - 5, purpose: 'housing' });
  for (const [kind, count] of [['weapon', 2], ['bandage', 2], ['medicine', 2], ['coat', 3]] as const) cmd({ type: 'setCraftTarget', kind, count });
  cmd({ type: 'designateBuild', x: start.x + 6, y: start.y - 3, structure: 'workbench' });
  for (const job of ['craft', 'doctor', 'cook'] as const) cmd({ type: 'setPriority', survivorId: s.survivors[0]!.id, jobType: job, priority: 1 });
  cmd({ type: 'designateBuild', x: start.x + 6, y: start.y - 1, structure: 'stove' });
  cmd({ type: 'designateBuild', x: start.x + 8, y: start.y - 1, structure: 'well' });
  for (let x = start.x + 4; x <= start.x + 9; x++) for (let y = start.y + 6; y <= start.y + 10; y++) {
    if (x === start.x + 4 || x === start.x + 9 || y === start.y + 6 || y === start.y + 10) cmd({ type: 'designateBuild', x, y, structure: x === start.x + 6 && y === start.y + 6 ? 'door' : 'wall' });
  }
  cmd({ type: 'designateHunt', x0: 0, y0: 0, x1: 127, y1: 127, kinds: ['deer'] });
  for (let i = 0; i < 6; i++) cmd({ type: 'designateFarm', x: start.x - 8 + i, y: start.y + 6, crop: i % 3 === 2 ? 'wheat' : 'potato' });
  cmd({ type: 'designateExplore', x0: start.x + 16, y0: start.y - 10, x1: start.x + 36, y1: start.y + 10 });
  for (const f of s.furniture) if ((f.kind === 'crate' || f.kind === 'tree') && !f.scavenge) cmd({ type: 'designateScavenge', x: f.x, y: f.y });
  const checkpoints = [1, 10, 100, 300, 1000, 2000, 3000, 4800, 7200, 9600];
  writeFileSync(`${dir}/checkpoints.json`, JSON.stringify(checkpoints));
  let done = 0;
  for (const at of checkpoints) {
    for (; done < at; done++) {
      tick(s);
      for (const r of [...s.requests]) cmd({ type: 'fulfilRequest', requestId: r.id });
      for (const tr of s.traders) if (!tr.traded) tr.offers.forEach((_, i) => cmd({ type: 'trade', traderId: tr.id, offer: i }));
      for (const w of [...s.wanderers]) cmd({ type: 'acceptWanderer', wandererId: w.id });
      if (done === 2400) cmd({ type: 'planPerimeter', x0: start.x - 16, y0: start.y - 16, x1: start.x + 16, y1: start.y + 16 });
    }
    writeFileSync(`${dir}/t${at}.json`, JSON.stringify(s));
  }
  writeFileSync(`${dir}/commands.json`, JSON.stringify(s.commandLog));
}, 600000);

// Worlds made from nothing: the C# generates the same seed, size and settings and must match (worlds/<name>.json,
// with the arguments in worlds/index.json).
it('writes generated worlds for the C# port', () => {
  mkdirSync('cs/Parity/data/worlds', { recursive: true });
  const cases: { name: string; seed: number; size: number; preset: keyof typeof PRESETS | null }[] = [];
  for (const seed of [1, 2, 3, 4, 5, 6, 7, 42]) for (const size of [64, 128]) cases.push({ name: `s${seed}-${size}`, seed, size, preset: null });
  for (const seed of [1, 7]) for (const size of [256, 512]) cases.push({ name: `s${seed}-${size}`, seed, size, preset: null });
  for (const preset of Object.keys(PRESETS) as (keyof typeof PRESETS)[]) cases.push({ name: `s9-96-${preset}`, seed: 9, size: 96, preset });
  for (const c of cases) writeFileSync(`cs/Parity/data/worlds/${c.name}.json`, JSON.stringify(c.preset ? createWorld(c.seed, c.size, PRESETS[c.preset].settings) : createWorld(c.seed, c.size)));
  writeFileSync('cs/Parity/data/worlds/index.json', JSON.stringify(cases));
}, 600000);
