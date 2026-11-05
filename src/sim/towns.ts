// Towns: where they stand and the streets they grow. Laid out over the natural world
// (nature.ts) once per world, as plain data (worldplan.ts caches it).
//
//   siting   towns go where the land suits them: dry, open ground (little forest), near water
//            (a river or lake just outside the town), away from the map's edge, the start
//            clearing and each other (farmland between). Biggest first; villages fill in.
//   streets  each town grows from its centre: a main street one way and a cross street the
//            other, then side streets branching off them at uneven spacing (wide enough for a
//            lot each side and room between), each as long as the town's circle allows. Streets
//            stop short of water and of the start clearing. All are axis-aligned and two wide.

import { hash, isForest, isWater, nearStart, roll, START_CLEAR_RADIUS, type Nature } from './nature';

export type TownKind = 'village' | 'town' | 'city';

export interface Town {
  x: number;
  y: number;
  radius: number;
  kind: TownKind;
  /** The main street runs east-west (true) or north-south. */
  eastWest: boolean;
}

/** An axis-aligned street: the tiles from (x0, y0) to (x1, y1) and the row or column beside them (two wide). */
export interface Street {
  x0: number;
  y0: number;
  x1: number;
  y1: number;
  /** Runs east-west. */
  eastWest: boolean;
  /** The main or cross street (lots on it are bigger, shops and civic buildings go there first). */
  main: boolean;
  town: number;
}

export const STREET = 2;
/** Side streets branch this far apart, at least (a lot's depth each side and a verge). */
export const STREET_GAP = 26;

const RADII: Record<TownKind, [number, number]> = { village: [20, 28], town: [34, 46], city: [52, 66] };

/** How many towns, and how big, for a map this size. */
function townKinds(seed: number, size: number): TownKind[] {
  // A small map is one town and its country round it.
  const count = size <= 128 ? 1 : Math.max(1, Math.round((size * size) / (170 * 170))) + (roll(seed, size, 0, 0x7100) < 0.5 ? 1 : 0);
  const kinds: TownKind[] = [];
  for (let i = 0; i < count; i++) {
    const r = roll(seed, i, 1, 0x7101);
    kinds.push(size >= 384 && r < 0.2 ? 'city' : r < 0.6 ? 'town' : 'village');
  }
  // Biggest first: they get the best ground.
  const order: Record<TownKind, number> = { city: 0, town: 1, village: 2 };
  return kinds.sort((a, b) => order[a] - order[b]);
}

/** A coarse map of the land (every 4 tiles): 0 open, 1 forest, 2 water. */
export function landGrid(n: Nature): Uint8Array {
  const N = Math.ceil(n.size / 4);
  const g = new Uint8Array(N * N);
  for (let j = 0; j < N; j++) {
    for (let i = 0; i < N; i++) {
      const x = i * 4 + 2;
      const y = j * 4 + 2;
      g[j * N + i] = isWater(n, x, y) ? 2 : isForest(n, x, y) ? 1 : 0;
    }
  }
  return g;
}

export function siteTowns(n: Nature, land: Uint8Array): Town[] {
  const N = Math.ceil(n.size / 4);
  const towns: Town[] = [];
  const kinds = townKinds(n.seed, n.size);
  kinds.forEach((kind, i) => {
    const [lo, hi] = RADII[kind];
    const radius = Math.min(Math.floor(n.size * 0.19), lo + Math.floor(roll(n.seed, i, 2, 0x7102) * (hi - lo + 1)));
    let best: { x: number; y: number; score: number } | null = null;
    const margin = radius + 3; // the whole town on the map
    for (let y = margin; y < n.size - margin; y += 8) {
      for (let x = margin; x < n.size - margin; x += 8) {
        if (Math.sqrt((x - n.start.x) ** 2 + (y - n.start.y) ** 2) < radius + START_CLEAR_RADIUS + 6) continue;
        if (towns.some((t) => Math.sqrt((x - t.x) ** 2 + (y - t.y) ** 2) < radius + t.radius + 48)) continue;
        // The land within the town (dry, wooded) and just outside it (water nearby).
        let inside = 0;
        let dry = 0;
        let wooded = 0;
        let around = 0;
        let wet = 0;
        const reach = radius + 14;
        for (let j = Math.max(0, Math.floor((y - reach) / 4)); j <= Math.min(N - 1, Math.floor((y + reach) / 4)); j++) {
          for (let k = Math.max(0, Math.floor((x - reach) / 4)); k <= Math.min(N - 1, Math.floor((x + reach) / 4)); k++) {
            const d = Math.sqrt((k * 4 + 2 - x) ** 2 + (j * 4 + 2 - y) ** 2);
            const c = land[j * N + k]!;
            if (d <= radius) {
              inside++;
              if (c !== 2) dry++;
              if (c === 1) wooded++;
            } else if (d <= reach) {
              around++;
              if (c === 2) wet++;
            }
          }
        }
        const dryShare = dry / Math.max(1, inside);
        if (dryShare < 0.9) continue;
        const wetShare = wet / Math.max(1, around);
        const score = dryShare - (wooded / Math.max(1, inside)) * 0.6 + (wetShare > 0.02 && wetShare < 0.3 ? 0.3 : 0) + roll(n.seed, x, y, 0x7103) * 0.15;
        if (!best || score > best.score) best = { x, y, score };
      }
    }
    if (best) towns.push({ x: best.x, y: best.y, radius, kind, eastWest: hash(n.seed, best.x, best.y, 0x7104) % 2 === 0 });
  });
  return towns;
}

/** Streets for a town: main and cross streets through the centre, then side streets off them. */
export function growStreets(n: Nature, t: Town, ti: number): Street[] {
  const streets: Street[] = [];
  const r = (k: number) => roll(n.seed, t.x, t.y, 0x7200 + k);
  const blocked = (x: number, y: number) =>
    x < 3 || y < 3 || x >= n.size - 4 || y >= n.size - 4 || isWater(n, x, y) || isWater(n, x + 1, y + 1) || nearStart(n, x, y, START_CLEAR_RADIUS + 3);
  const laid = new Set<number>();
  const isStreet = (x: number, y: number) => laid.has(y * n.size + x);
  /**
   * A street from (x, y) along (dx, dy) for up to `len` tiles. It stops short of water, the start
   * clearing and the town's edge; it stops where it meets another street (a junction), and
   * where it would run alongside one too close for lots between. Returns the tiles laid.
   */
  const run = (x: number, y: number, dx: number, dy: number, len: number, main: boolean): number => {
    if (blocked(x, y) || isStreet(x, y)) return 0;
    const [px, py] = [Math.abs(dy), Math.abs(dx)]; // across the street
    const crowded = (sx: number, sy: number) => {
      for (let o = 2; o <= 9; o++) if (isStreet(sx + px * o, sy + py * o) || isStreet(sx - px * (o - 1), sy - py * (o - 1))) return true;
      return false;
    };
    let steps = 0;
    while (steps < len) {
      const nx = x + dx * (steps + 1);
      const ny = y + dy * (steps + 1);
      if (blocked(nx, ny) || Math.sqrt((nx - t.x) ** 2 + (ny - t.y) ** 2) > t.radius * 1.05) break;
      if (isStreet(nx, ny) || isStreet(nx + px, ny + py)) {
        steps++; // meet it
        break;
      }
      if (steps >= 2 && crowded(nx, ny)) break;
      steps++;
    }
    if (steps < 8) return 0;
    const x1 = x + dx * steps;
    const y1 = y + dy * steps;
    const s: Street = { x0: Math.min(x, x1), y0: Math.min(y, y1), x1: Math.max(x, x1), y1: Math.max(y, y1), eastWest: dy === 0, main, town: ti };
    streets.push(s);
    for (let yy = s.y0; yy <= s.y1 + (s.eastWest ? 1 : 0); yy++) for (let xx = s.x0; xx <= s.x1 + (s.eastWest ? 0 : 1); xx++) laid.add(yy * n.size + xx);
    return steps;
  };
  // Branches: side streets off a street at uneven spacing, each way with some chance; in towns
  // and cities, side streets of their own.
  const levels = t.kind === 'village' ? 1 : 2;
  const odds = t.kind === 'village' ? 0.55 : 0.75;
  let k = 10;
  const grow = (x: number, y: number, dx: number, dy: number, len: number, level: number) => {
    const steps = run(x, y, dx, dy, len, level === 0);
    if (steps === 0 || level >= levels) return;
    const [px, py] = [Math.abs(dy), Math.abs(dx)];
    let off = STREET_GAP - 8 + Math.floor(r(k++) * 7);
    while (off < steps - 4) {
      const bx = x + dx * off;
      const by = y + dy * off;
      for (const side of [1, -1]) {
        if (r(k++) > odds) continue;
        // Streets are two wide (their line and the next): a branch the other way starts just before.
        const gap = side === 1 ? 2 : -1;
        grow(bx + px * gap, by + py * gap, px * side, py * side, Math.floor(t.radius * (0.5 + r(k++) * 0.6)), level + 1);
      }
      off += STREET_GAP + Math.floor(r(k++) * 6);
    }
  };
  // The main street both ways from the centre, then the cross street.
  const [ax, ay] = t.eastWest ? [1, 0] : [0, 1];
  const [cx, cy] = t.eastWest ? [0, 1] : [1, 0];
  grow(t.x, t.y, ax, ay, Math.floor(t.radius * (0.9 + r(1) * 0.2)), 0);
  grow(t.x - ax, t.y - ay, -ax, -ay, Math.floor(t.radius * (0.9 + r(2) * 0.2)), 0);
  grow(t.x + cx * 2, t.y + cy * 2, cx, cy, Math.floor(t.radius * (0.6 + r(3) * 0.4)), 0);
  grow(t.x - cx, t.y - cy, -cx, -cy, Math.floor(t.radius * (0.6 + r(4) * 0.4)), 0);
  return streets;
}
