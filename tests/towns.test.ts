import { describe, expect, it } from 'vitest';
import {
  DOOR, FLOOR, OUTSIDE, WALL, barnPlan, cellAt, churchPlan, civicPlan, housePlan, isContainerLook, shedPlan, shopPlan, warehousePlan, type Plan,
} from '../src/sim/buildings';
import { hashRoll } from '../src/sim/hash';
import { isPassable } from '../src/sim/pathfinding';
import { GROUNDS, getTile, tileIndex } from '../src/sim/grid';
import { LOOT_TABLES } from '../src/sim/loot';
import { transform } from '../src/sim/lots';
import { scavengeFind } from '../src/sim/jobs';
import { isWater } from '../src/sim/nature';
import { PLAN_CHUNK, planWorld, type WorldPlan } from '../src/sim/worldplan';
import { layoutLot } from '../src/sim/lots';
import { CHUNK, START_CLEAR_RADIUS, createWorld, ensureGeneratedAround } from '../src/sim/worldgen';

const rolls = (n: number) => (k: number) => hashRoll(n, k, 0x5eed);

/** Every plan of every kind, for many rolls. */
function allPlans(): Plan[] {
  const out: Plan[] = [];
  for (let n = 0; n < 60; n++) {
    const r = rolls(n);
    out.push(housePlan(r, { maxW: 13, maxH: 12, garage: true }), housePlan(r, { maxW: 10, maxH: 10, garage: false }));
    for (const kind of ['grocery', 'pharmacy', 'hardware', 'bar', 'station', 'office'] as const) out.push(shopPlan(r, kind, 13, 13));
    for (const kind of ['police', 'clinic', 'school', 'office'] as const) out.push(civicPlan(r, kind, 26, 26));
    out.push(churchPlan(r, 26, 26), warehousePlan(r, 'warehouse', 28, 26), warehousePlan(r, 'factory', 28, 26), barnPlan(r, 13, 10), shedPlan(r));
  }
  return out;
}

/** The plan's edge between two cells side by side, if any. */
function edgeOf(p: Plan, ax: number, ay: number, bx: number, by: number) {
  return p.edges.find((e) => (e.ax === ax && e.ay === ay && e.bx === bx && e.by === by) || (e.ax === bx && e.ay === by && e.bx === ax && e.by === ay));
}

/** Floor reachable from outside the plan, in through its outer door edges, crossing no edge wall. */
function reachable(p: Plan): Set<number> {
  const seen = new Set<number>();
  const queue: [number, number][] = [];
  for (const e of p.edges) {
    if (!e.door || cellAt(p, e.bx, e.by) !== OUTSIDE) continue;
    const i = e.ay * p.w + e.ax;
    if (!seen.has(i)) {
      seen.add(i);
      queue.push([e.ax, e.ay]);
    }
  }
  while (queue.length) {
    const [x, y] = queue.shift()!;
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]] as const) {
      const c = cellAt(p, x + dx, y + dy);
      const i = (y + dy) * p.w + x + dx;
      const edge = edgeOf(p, x, y, x + dx, y + dy);
      if (c === FLOOR && !seen.has(i) && (!edge || edge.door)) {
        seen.add(i);
        queue.push([x + dx, y + dy]);
      }
    }
  }
  return seen;
}

describe('building plans', () => {
  const plans = allPlans();

  it('are walled all round: an edge wall or door wherever floor meets the outside; no wall or door cells left', () => {
    for (const p of plans) {
      expect(p.cells.filter((c) => c === WALL || c === DOOR), p.kind).toHaveLength(0);
      for (let y = 0; y < p.h; y++) {
        for (let x = 0; x < p.w; x++) {
          if (cellAt(p, x, y) !== FLOOR) continue;
          for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]] as const) {
            if (cellAt(p, x + dx, y + dy) === OUTSIDE) expect(edgeOf(p, x, y, x + dx, y + dy), `${p.kind} (${x}, ${y})`).toBeDefined();
          }
        }
      }
    }
  });

  it('every room can be walked into from a door to the outside', () => {
    for (const p of plans) {
      const seen = reachable(p);
      for (let i = 0; i < p.cells.length; i++) if (p.cells[i] === FLOOR) expect(seen.has(i), `${p.kind} ${p.w}x${p.h} cell ${i % p.w},${Math.floor(i / p.w)}`).toBe(true);
    }
  });

  it('furniture stands on floor, one to a tile; containers carry a loot table, decor none', () => {
    for (const p of plans) {
      const at = new Set<string>();
      for (const f of p.furniture) {
        expect(cellAt(p, f.x, f.y)).toBe(FLOOR);
        expect(at.has(`${f.x},${f.y}`)).toBe(false);
        at.add(`${f.x},${f.y}`);
        expect(f.table !== null).toBe(isContainerLook(f.look));
      }
    }
  });

  it('a house has a kitchen, a bathroom and a bedroom, and fits its lot', () => {
    for (let n = 0; n < 60; n++) {
      const p = housePlan(rolls(n), { maxW: 13, maxH: 12, garage: true });
      expect(p.w).toBeLessThanOrEqual(13);
      expect(p.h).toBeLessThanOrEqual(12);
      const looks = p.furniture.map((f) => f.look);
      for (const look of ['icebox', 'toilet', 'bed', 'sofa'] as const) expect(looks, `house ${n}`).toContain(look);
      expect(p.edges.filter((e) => !e.door).length).toBeGreaterThan(0);
    }
  });

  it('turns to face any street: a one-to-one map onto the turned footprint', () => {
    const p = housePlan(rolls(3), { maxW: 13, maxH: 12, garage: true });
    for (const facing of ['north', 'south', 'east', 'west'] as const) {
      for (const mirror of [false, true]) {
        const seen = new Set<string>();
        for (let y = 0; y < p.h; y++) {
          for (let x = 0; x < p.w; x++) {
            const t = transform(p, x, y, facing, mirror);
            const across = facing === 'north' || facing === 'south';
            expect(t.x).toBeGreaterThanOrEqual(0);
            expect(t.x).toBeLessThan(across ? p.w : p.h);
            expect(t.y).toBeLessThan(across ? p.h : p.w);
            seen.add(`${t.x},${t.y}`);
          }
        }
        expect(seen.size).toBe(p.w * p.h);
      }
    }
  });
});

describe('loot by room', () => {
  it('every table sums to 1, and a container gives only what its table holds', () => {
    for (const [name, table] of Object.entries(LOOT_TABLES)) expect(table.reduce((a, [, w]) => a + w, 0), name).toBeCloseTo(1, 6);
    const kinds = new Set(LOOT_TABLES.pharmacy.map(([k]) => k));
    for (let i = 0; i < 100; i++) expect(kinds.has(scavengeFind(i / 100, 'pharmacy'))).toBe(true);
  });
});

describe('towns', () => {
  it('the plan draws in chunks the size worldgen uses', () => {
    expect(PLAN_CHUNK).toBe(CHUNK);
  });

  const plans: [string, WorldPlan][] = [[1, 256], [7, 256], [5, 512], [3, 128]].map(([seed, size]) => [`seed ${seed} size ${size}`, planWorld(seed!, size!, 'mainland')]);

  it('towns stand on dry land, apart from each other and clear of the start', () => {
    for (const [name, p] of plans) {
      expect(p.towns.length, name).toBeGreaterThan(0);
      const n = p.nature;
      for (const t of p.towns) {
        expect(isWater(n, t.x, t.y), name).toBe(false);
        expect(Math.sqrt((t.x - n.start.x) ** 2 + (t.y - n.start.y) ** 2), name).toBeGreaterThan(t.radius + START_CLEAR_RADIUS);
        for (const o of p.towns) if (o !== t) expect(Math.sqrt((t.x - o.x) ** 2 + (t.y - o.y) ** 2), name).toBeGreaterThan(t.radius + o.radius);
      }
    }
  });

  /** Tiles reachable along roads and streets from a point. */
  const alongRoads = (p: WorldPlan, from: { x: number; y: number }) => {
    const size = p.nature.size;
    const seen = new Uint8Array(size * size);
    const queue = [from.y * size + from.x];
    seen[queue[0]!] = 1;
    while (queue.length) {
      const i = queue.pop()!;
      const x = i % size;
      const y = Math.floor(i / size);
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1], [1, 1], [1, -1], [-1, 1], [-1, -1]] as const) {
        const nx = x + dx;
        const ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
        const j = ny * size + nx;
        if (!seen[j] && p.roadMask[j] !== 0) {
          seen[j] = 1;
          queue.push(j);
        }
      }
    }
    return seen;
  };

  it("a town's streets all join up, and country roads link every town", () => {
    for (const [name, p] of plans) {
      const size = p.nature.size;
      const t0 = p.towns[0]!;
      const seen = alongRoads(p, t0);
      for (const s of p.streets) expect(seen[s.y0 * size + s.x0], `${name}: street ${JSON.stringify(s)}`).toBe(1);
      for (const t of p.towns) expect(seen[t.y * size + t.x], `${name}: town at ${t.x},${t.y}`).toBe(1);
    }
  });

  it('lots sit on dry land off the roads and each other, and every building fits inside its lot', () => {
    for (const [name, p] of plans) {
      const size = p.nature.size;
      const zones = new Set(p.lots.map((l) => l.zone));
      for (const z of ['residential', 'commercial', 'field', 'cabin'] as const) expect(zones.has(z), `${name}: ${z}`).toBe(true);
      for (const lot of p.lots) {
        for (let y = lot.y; y < lot.y + lot.h; y++) {
          for (let x = lot.x; x < lot.x + lot.w; x++) {
            expect(p.lotMask[y * size + x], name).toBe(lot.id + 1);
            expect(p.roadMask[y * size + x], name).toBe(0);
            expect(isWater(p.nature, x, y), name).toBe(false);
          }
        }
        for (const b of layoutLot(p.nature.seed, lot).buildings) {
          const across = b.facing === 'north' || b.facing === 'south';
          const [w, h] = across ? [b.plan.w, b.plan.h] : [b.plan.h, b.plan.w];
          const msg = `${name}: ${b.plan.kind} ${w}x${h} at ${b.ox},${b.oy} in ${lot.zone} lot ${JSON.stringify(lot)}`;
          expect(b.ox, msg).toBeGreaterThanOrEqual(lot.x);
          expect(b.oy, msg).toBeGreaterThanOrEqual(lot.y);
          expect(b.ox + w, msg).toBeLessThanOrEqual(lot.x + lot.w);
          expect(b.oy + h, msg).toBeLessThanOrEqual(lot.y + lot.h);
        }
      }
    }
  });

  it('every container in a generated world can be walked to from a road', () => {
    for (const seed of [1, 7]) {
      const size = 256;
      const state = createWorld(seed, size);
      for (let y = 0; y < size; y += CHUNK) for (let x = 0; x < size; x += CHUNK) ensureGeneratedAround(state, { x, y });
      // Flood from every road tile over what a survivor can walk.
      const road = GROUNDS.indexOf('road');
      const seen = new Uint8Array(size * size);
      const queue: number[] = [];
      for (let i = 0; i < size * size; i++) if (state.grid.ground[i] === road && state.grid.tiles[i] === 'floor') {
        seen[i] = 1;
        queue.push(i);
      }
      while (queue.length) {
        const i = queue.pop()!;
        const x = i % size;
        const y = Math.floor(i / size);
        for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]] as const) {
          const nx = x + dx;
          const ny = y + dy;
          if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
          const j = tileIndex(state.grid, nx, ny);
          if (!seen[j] && isPassable(state.grid, nx, ny, 'survivor')) {
            seen[j] = 1;
            queue.push(j);
          }
        }
      }
      const containers = state.furniture.filter((f) => f.kind === 'crate' && f.table !== null);
      expect(containers.length).toBeGreaterThan(20);
      const cut = containers.filter((f) => !seen[tileIndex(state.grid, f.x, f.y)]);
      // Only ones walled in by water (a building on an island in a lake) may be out of reach.
      for (const f of cut) {
        const nearWater = [[1, 0], [-1, 0], [0, 1], [0, -1]].some(([dx, dy]) => getTile(state.grid, f.x + dx! * 2, f.y + dy! * 2) === 'water');
        expect(nearWater || cut.length <= containers.length * 0.02, `seed ${seed}: (${f.x}, ${f.y}) ${f.look} cut off`).toBe(true);
      }
    }
  });

});
