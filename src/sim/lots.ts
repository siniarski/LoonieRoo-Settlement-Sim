// What stands on a lot (worldplan.ts cuts the lots), and drawing it a chunk at a time.
//
// A lot faces its street or road. Its layout is planned in the lot's own frame, u along the
// frontage and v back from the street, then turned onto the map:
//   residential  a house (buildings.ts) set back from the street, maybe a garden, maybe a shed behind
//   commercial   a shop at the street; a petrol station gets a paved forecourt with pumps
//   civic        a police station, clinic, school, offices or a church on a paved square, benches in front
//   industrial   a warehouse or factory on a concrete yard
//   farm         a farmhouse and barn by the road, a silo, bales and a tractor in the yard, and
//                fields behind in crop rows, hedged; or an orchard
//   field        fields only (or pasture)
//   cabin        a small house alone
// Everything is a pure function of the seed and the lot (layoutLot is cached per lot), and
// drawLot draws only what falls inside the chunk being generated, so a lot that spans chunks
// comes out whole and the same whichever chunk comes first.
//
// Drawn: walls fall in with the settings' ruin decay (outer more than inner), some doors broken
// or left open, containers hold loot (more further out, some already searched), and the dead
// sleep inside bigger and further-out buildings.

import { addFurniture, type Look } from './furniture';
import { crateLoot, ruinDecay, zombieScale } from './difficulty';
import { EDGE_DOOR, EDGE_WALL, GROUNDS, edgeIndex, setEdge, setTile, tileIndex, type Ground, type Point, type Side } from './grid';
import { hashRoll } from './hash';
import { isRadioactive } from './climate';
import type { SimState } from './state';
import type { Lot } from './worldplan';
import {
  DOOR, FLOOR, WALL, barnPlan, cellAt, churchPlan, civicPlan, housePlan, isContainerLook, shedPlan, shopPlan, warehousePlan, type Plan, type Rolls,
} from './buildings';

/** Which way a lot or building faces: where its street is, and so its front door. */
export type Facing = 'north' | 'south' | 'east' | 'west';

/** A building on a lot: its plan, its footprint's top-left corner on the map, which way it faces. */
export interface Placement {
  plan: Plan;
  ox: number;
  oy: number;
  facing: Facing;
  mirror: boolean;
}

export interface LotLayout {
  buildings: Placement[];
  /** The yard's ground at (u, v) in the lot's frame (the building's own floors are laid over it). */
  yard: (u: number, v: number) => Ground;
  /** Trees (hedges, orchards) and decor, on the map. */
  trees: Point[];
  decor: { x: number; y: number; look: Look }[];
}

/** What drawing a lot needs to know about the world. */
export interface LotContext {
  seed: number;
  lootAt(x: number, y: number): number;
  distance(x: number, y: number): number;
  /** No sleepers this close to the start. */
  calm: number;
}

/** Where a plan's cell (x, y) lands, turned to face `facing` (its front was the bottom), maybe mirrored. */
export function transform(p: Plan, x: number, y: number, facing: Facing, mirror: boolean): Point {
  const mx = mirror ? p.w - 1 - x : x;
  switch (facing) {
    case 'south': return { x: mx, y };
    case 'north': return { x: p.w - 1 - mx, y: p.h - 1 - y };
    case 'east': return { x: y, y: p.w - 1 - mx };
    case 'west': return { x: p.h - 1 - y, y: mx };
  }
}

/** The lot's frontage (along its street) and depth. */
function frame(lot: Lot): { F: number; D: number } {
  const across = lot.facing === 'north' || lot.facing === 'south';
  return across ? { F: lot.w, D: lot.h } : { F: lot.h, D: lot.w };
}

/** The map tile at (u, v) in the lot's frame (u along the frontage, v back from the street). */
export function lotToMap(lot: Lot, u: number, v: number): Point {
  switch (lot.facing) {
    case 'north': return { x: lot.x + u, y: lot.y + v };
    case 'south': return { x: lot.x + u, y: lot.y + lot.h - 1 - v };
    case 'west': return { x: lot.x + v, y: lot.y + u };
    case 'east': return { x: lot.x + lot.w - 1 - v, y: lot.y + u };
  }
}

/** (u, v) in the lot's frame for a map tile. */
function mapToLot(lot: Lot, x: number, y: number): { u: number; v: number } {
  switch (lot.facing) {
    case 'north': return { u: x - lot.x, v: y - lot.y };
    case 'south': return { u: x - lot.x, v: lot.y + lot.h - 1 - y };
    case 'west': return { u: y - lot.y, v: x - lot.x };
    case 'east': return { u: y - lot.y, v: lot.x + lot.w - 1 - x };
  }
}

/** A plan set in the lot at (u0, v0): its front `v0` back from the street, `u0` along. */
function setIn(lot: Lot, plan: Plan, u0: number, v0: number, mirror: boolean, facing: Facing = lot.facing): Placement {
  // The footprint's corners in the lot's frame, mapped, then the top-left on the map.
  const a = lotToMap(lot, u0, v0);
  const b = lotToMap(lot, u0 + plan.w - 1, v0 + plan.h - 1);
  return { plan, ox: Math.min(a.x, b.x), oy: Math.min(a.y, b.y), facing, mirror };
}

const layouts = new WeakMap<Lot, LotLayout>();

export function layoutLot(seed: number, lot: Lot): LotLayout {
  const hit = layouts.get(lot);
  if (hit) return hit;
  const made = makeLayout(seed, lot);
  layouts.set(lot, made);
  return made;
}

function makeLayout(seed: number, lot: Lot): LotLayout {
  const r: Rolls = (k) => hashRoll(seed, lot.x, lot.y, 0x9100 + k);
  const { F, D } = frame(lot);
  const out: LotLayout = { buildings: [], yard: () => 'grass', trees: [], decor: [] };
  const mirror = r(81) < 0.5;
  const along = (w: number, margin: number) => margin + Math.floor(r(80) * Math.max(1, F - w - 2 * margin + 1));
  const decor = (u: number, v: number, look: Look) => out.decor.push({ ...lotToMap(lot, u, v), look });
  switch (lot.zone) {
    case 'residential': {
      const setback = 1 + Math.floor(r(90) * 3);
      const house = housePlan(r, { maxW: F - 2, maxH: D - setback - 1, garage: true });
      out.buildings.push(setIn(lot, house, along(house.w, 1), setback, mirror));
      const behind = D - setback - house.h;
      if (behind >= 7 && r(91) < 0.5) {
        const shed = shedPlan((k) => r(200 + k));
        out.buildings.push(setIn(lot, shed, r(92) < 0.5 ? 1 : F - 6, D - 6, false));
      }
      const garden = behind >= 5 && r(93) < 0.35;
      out.yard = (u, v) => (garden && v >= D - 4 && u > 1 && u < F - 2 ? (v % 2 === 0 ? 'field' : 'dirt') : 'grass');
      break;
    }
    case 'commercial': {
      const kinds = ['grocery', 'pharmacy', 'hardware', 'bar', 'station', 'office'] as const;
      const pick = r(0);
      const kind = kinds[pick < 0.25 ? 0 : pick < 0.4 ? 1 : pick < 0.6 ? 2 : pick < 0.75 ? 3 : pick < 0.85 ? 4 : 5];
      const setback = kind === 'station' ? 4 : 1;
      const shop = shopPlan(r, kind, F - 2, D - setback - 1);
      out.buildings.push(setIn(lot, shop, along(shop.w, 1), setback, mirror));
      out.yard = (_u, v) => (v < setback ? 'paving' : 'grass');
      if (kind === 'station') {
        decor(Math.floor(F / 3), 1, 'pump');
        decor(Math.floor((2 * F) / 3), 1, 'pump');
      }
      break;
    }
    case 'civic': {
      const kinds = ['police', 'clinic', 'school', 'church', 'office'] as const;
      const kind = kinds[Math.floor(r(0) * kinds.length)]!;
      const plan = kind === 'church' ? churchPlan(r, F - 4, D - 3) : civicPlan(r, kind, F - 4, D - 3);
      out.buildings.push(setIn(lot, plan, along(plan.w, 2), 2, mirror));
      out.yard = () => 'paving';
      for (let i = 0; i < 3; i++) decor(1 + Math.floor(r(70 + i) * (F - 2)), 0, 'bench');
      break;
    }
    case 'industrial': {
      const plan = warehousePlan(r, r(2) < 0.6 ? 'warehouse' : 'factory', F - 2, D - 4);
      out.buildings.push(setIn(lot, plan, along(plan.w, 1), 3, mirror));
      out.yard = () => 'concrete';
      break;
    }
    case 'cabin': {
      const plan = housePlan(r, { maxW: Math.min(10, F - 3), maxH: Math.min(10, D - 3), garage: false });
      out.buildings.push(setIn(lot, plan, along(plan.w, 1), 2, mirror));
      break;
    }
    case 'farm':
    case 'field':
      farmLayout(seed, lot, r, F, D, out);
      break;
  }
  return out;
}

/** A farm: the farmstead by the road (house, barn, a silo, bales, a tractor), crops behind. */
function farmLayout(seed: number, lot: Lot, r: Rolls, F: number, D: number, out: LotLayout): void {
  const stead = lot.zone === 'farm';
  const yardDepth = stead ? 13 : 0;
  // Behind the yard: crop rows one way or the other, an orchard, or pasture.
  const use = r(10);
  const rowsAlong = r(11) < 0.5;
  const crop = (u: number, v: number): Ground => {
    if (v < yardDepth) return 'dirt';
    if (use < 0.7) return (rowsAlong ? v : u) % 2 === 0 ? 'field' : 'dirt';
    return 'grass';
  };
  out.yard = crop;
  // A hedge round the crops on most sides (open where it's grown thin), a gate by the yard.
  const sides = [r(20) < 0.6, r(21) < 0.6, r(22) < 0.6];
  for (let v = yardDepth; v < D; v++) {
    for (let u = 0; u < F; u++) {
      const edge = (sides[0] && u === 0) || (sides[1] && u === F - 1) || (sides[2] && v === D - 1);
      const gate = v === yardDepth && Math.abs(u - Math.floor(F / 2)) <= 1;
      if (edge && !gate && hashRoll(seed, lot.x + u, lot.y + v, 0x9200) < 0.75) out.trees.push(lotToMap(lot, u, v));
    }
  }
  if (use >= 0.7 && use < 0.85) {
    for (let v = yardDepth + 2; v < D - 1; v += 3) for (let u = 2; u < F - 1; u += 3) out.trees.push(lotToMap(lot, u, v));
  } else if (use >= 0.85) {
    for (let i = 0; i < 3; i++) out.decor.push({ ...lotToMap(lot, 2 + Math.floor(r(40 + i) * (F - 4)), yardDepth + 2 + Math.floor(r(50 + i) * Math.max(1, D - yardDepth - 4))), look: 'hay' });
  }
  if (!stead) return;
  const house = housePlan((k) => r(300 + k), { maxW: 12, maxH: 11, garage: false });
  const barn = barnPlan((k) => r(400 + k), 12, 10);
  const leftHouse = r(12) < 0.5;
  const hu = leftHouse ? 1 : F - 1 - house.w;
  const bu = leftHouse ? F - 1 - barn.w : 1;
  if (house.w + barn.w + 3 <= F) {
    out.buildings.push(setIn(lot, house, hu, 1, r(13) < 0.5));
    out.buildings.push(setIn(lot, barn, bu, 1, r(14) < 0.5));
  } else out.buildings.push(setIn(lot, house, Math.max(1, Math.floor((F - house.w) / 2)), 1, r(13) < 0.5));
  // In the yard, between and in front: a silo (2 x 2), bales, a tractor.
  const mid = Math.floor(F / 2);
  for (const [du, dv] of [[0, 0], [1, 0], [0, 1], [1, 1]] as const) out.decor.push({ ...lotToMap(lot, mid - 1 + du, 2 + dv), look: 'silo' });
  out.decor.push({ ...lotToMap(lot, mid - 1, 6), look: 'tractor' });
  for (let i = 0; i < 3; i++) out.decor.push({ ...lotToMap(lot, mid - 2 + i, 9), look: 'hay' });
}

/** Draws the part of a lot inside the rectangle (x0, y0)..(x1, y1) (a chunk). */
export function drawLot(state: SimState, ctx: LotContext, lot: Lot, x0: number, y0: number, x1: number, y1: number): void {
  const layout = layoutLot(ctx.seed, lot);
  const inside = (x: number, y: number) => x >= x0 && y >= y0 && x <= x1 && y <= y1;
  // The yard (the buildings' floors go over it).
  for (let y = Math.max(y0, lot.y); y <= Math.min(y1, lot.y + lot.h - 1); y++) {
    for (let x = Math.max(x0, lot.x); x <= Math.min(x1, lot.x + lot.w - 1); x++) {
      const { u, v } = mapToLot(lot, x, y);
      state.grid.ground[tileIndex(state.grid, x, y)] = GROUNDS.indexOf(layout.yard(u, v));
    }
  }
  const covered = new Set<string>();
  layout.buildings.forEach((b, bi) => drawBuilding(state, ctx, lot, b, bi, inside, covered));
  for (const t of layout.trees) if (inside(t.x, t.y) && !covered.has(`${t.x},${t.y}`)) addFurniture(state, 'tree', t.x, t.y);
  for (const d of layout.decor) if (inside(d.x, d.y) && !covered.has(`${d.x},${d.y}`)) addFurniture(state, 'decor', d.x, d.y).look = d.look;
}

function drawBuilding(state: SimState, ctx: LotContext, lot: Lot, b: Placement, bi: number, inside: (x: number, y: number) => boolean, covered: Set<string>): void {
  const { plan } = b;
  const r = (k: number) => hashRoll(ctx.seed, lot.x, lot.y, 0x9300 + bi * 1000 + k);
  const decay = ruinDecay(state);
  const at = (x: number, y: number) => {
    const t = transform(plan, x, y, b.facing, b.mirror);
    return { x: b.ox + t.x, y: b.oy + t.y };
  };
  const floors: Point[] = [];
  for (let y = 0; y < plan.h; y++) {
    for (let x = 0; x < plan.w; x++) {
      const c = cellAt(plan, x, y);
      if (c === 0) continue;
      const w = at(x, y);
      covered.add(`${w.x},${w.y}`);
      if (c === FLOOR) floors.push(w);
      if (!inside(w.x, w.y)) continue;
      const fall = hashRoll(ctx.seed, w.x, w.y, 19);
      const outer = cellAt(plan, x - 1, y) === 0 || cellAt(plan, x + 1, y) === 0 || cellAt(plan, x, y - 1) === 0 || cellAt(plan, x, y + 1) === 0;
      state.grid.ground[tileIndex(state.grid, w.x, w.y)] = GROUNDS.indexOf(plan.ground[y * plan.w + x] ?? 'dirt');
      if (c === WALL) {
        // Ruined: some walls have fallen in, outer ones more than inner ones.
        setTile(state.grid, w.x, w.y, fall > (outer ? 0.08 : 0.04) + decay ? 'wall' : 'floor');
      } else if (c === DOOR) {
        if (fall < 0.08) setTile(state.grid, w.x, w.y, 'floor'); // broken off its hinges
        else {
          setTile(state.grid, w.x, w.y, 'door');
          if (fall > 0.8) state.grid.open.push(tileIndex(state.grid, w.x, w.y)); // left open
        }
      } else setTile(state.grid, w.x, w.y, 'floor');
    }
  }
  // Edge walls and doors (buildings.ts toEdges), on the borders between tiles. Each edge is drawn by
  // the chunk that owns it (the tile it's the north or west side of), decayed by a roll of its own.
  for (const e of plan.edges) {
    const a = at(e.ax, e.ay);
    const b = at(e.bx, e.by);
    const [ox, oy, side] = b.y === a.y - 1 ? [a.x, a.y, 'n'] : b.y === a.y + 1 ? [b.x, b.y, 'n'] : b.x === a.x - 1 ? [a.x, a.y, 'w'] : [b.x, b.y, 'w'];
    if (!inside(ox, oy)) continue;
    const fall = hashRoll(ctx.seed, ox, oy, side === 'n' ? 23 : 29);
    const outer = cellAt(plan, e.bx, e.by) === 0;
    if (e.door) {
      if (fall < 0.08) continue; // broken off its hinges
      setEdge(state.grid, ox, oy, side as Side, EDGE_DOOR);
      if (fall > 0.8) state.grid.edgeOpen.push(edgeIndex(state.grid, ox, oy, side as Side)); // left open
    } else if (fall > (outer ? 0.08 : 0.04) + decay) setEdge(state.grid, ox, oy, side as Side, EDGE_WALL);
  }
  // Furniture: containers hold loot (some already searched), the rest is for show.
  plan.furniture.forEach((f, i) => {
    const w = at(f.x, f.y);
    if (!inside(w.x, w.y)) return;
    if (isContainerLook(f.look)) {
      const c = addFurniture(state, 'crate', w.x, w.y);
      c.look = f.look;
      c.table = f.table;
      const n = 1 + Math.floor(r(100 + i) * Math.max(1, Math.floor(ctx.lootAt(w.x, w.y) / 3)));
      const searched = ctx.distance(w.x, w.y) < 80 && r(200 + i) < 0.25;
      c.loot = searched ? 0 : crateLoot(state, n) * (isRadioactive(state, w.x, w.y) ? 2 : 1);
    } else addFurniture(state, 'decor', w.x, w.y).look = f.look;
  });
  // The dead asleep inside: none near the start, more in bigger buildings and further out.
  const d = ctx.distance(b.ox, b.oy);
  if (d < ctx.calm || floors.length === 0) return;
  const sleepers = zombieScale(state, Math.floor(r(300) * (plan.sleepers + 1 + Math.floor(d / 60))));
  for (let k = 0; k < sleepers; k++) {
    const p = floors[Math.floor(r(310 + k) * floors.length)]!;
    if (inside(p.x, p.y)) state.dormant.push({ id: `d${state.nextId++}`, x: p.x, y: p.y });
  }
}
