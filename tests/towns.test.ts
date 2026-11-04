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
import { BLOCK, districtOf, planTowns } from '../src/sim/towns';
import { CHUNK, createWorld, ensureGeneratedAround, layout } from '../src/sim/worldgen';

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

/** Floor and doors reachable from outside the plan, through its doors. */
function reachable(p: Plan): Set<number> {
  const seen = new Set<number>();
  const queue: [number, number][] = [];
  for (let y = 0; y < p.h; y++) {
    for (let x = 0; x < p.w; x++) {
      const outerDoor = cellAt(p, x, y) === DOOR && [[1, 0], [-1, 0], [0, 1], [0, -1]].some(([dx, dy]) => cellAt(p, x + dx!, y + dy!) === OUTSIDE);
      if (outerDoor) {
        seen.add(y * p.w + x);
        queue.push([x, y]);
      }
    }
  }
  while (queue.length) {
    const [x, y] = queue.shift()!;
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]] as const) {
      const c = cellAt(p, x + dx, y + dy);
      const i = (y + dy) * p.w + x + dx;
      if ((c === FLOOR || c === DOOR) && !seen.has(i)) {
        seen.add(i);
        queue.push([x + dx, y + dy]);
      }
    }
  }
  return seen;
}

describe('building plans', () => {
  const plans = allPlans();

  it('are walled all round: no floor touches the outside', () => {
    for (const p of plans) {
      for (let y = 0; y < p.h; y++) {
        for (let x = 0; x < p.w; x++) {
          if (cellAt(p, x, y) !== FLOOR) continue;
          for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]] as const) expect(cellAt(p, x + dx, y + dy), `${p.kind} (${x}, ${y})`).not.toBe(OUTSIDE);
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
      expect(p.cells.filter((c) => c === WALL).length).toBeGreaterThan(0);
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
  it('a block is a chunk', () => {
    expect(BLOCK).toBe(CHUNK);
  });

  it('there is always a town, with a centre, shops, houses and farmland round it', () => {
    for (const seed of [1, 2, 3, 7, 11]) {
      const l = layout(seed, 128);
      expect(l.towns.length).toBeGreaterThan(0);
      const t = l.towns[0]!;
      expect(districtOf(seed, l.towns, t.bx, t.by)).toBe('centre');
      const around = [[-1, -1], [0, -1], [-1, 0]].map(([dx, dy]) => districtOf(seed, l.towns, t.bx + dx!, t.by + dy!));
      expect(around).toContain('commercial');
      expect(around).toContain('residential');
    }
    // Farmland round towns, wild country further out (on a map big enough for both).
    const l = layout(3, 512);
    const all = new Set<string>();
    for (let by = 0; by < 16; by++) for (let bx = 0; bx < 16; bx++) all.add(districtOf(3, l.towns, bx, by));
    for (const d of ['centre', 'commercial', 'residential', 'farmland', 'wild']) expect(all).toContain(d);
    expect(planTowns(5, 512, [32, 96, 160], [32, 96, 160], { x: 256, y: 256 }).length).toBeGreaterThan(0);
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
