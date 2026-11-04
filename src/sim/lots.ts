// Lots: what each block of a district holds, and stamping building plans (buildings.ts) onto
// the world. Called by worldgen.ts once per chunk (a block is a chunk), after the terrain.
//
//   residential  four lots, two facing the street above and two the street below; a house on most
//   commercial   four lots of shops; a petrol station gets a paved forecourt with pumps
//   centre       one civic building on a paved square
//   industrial   one warehouse or factory on a paved yard
//   farmland     four fields between dirt tracks; about half have a farmstead (farmhouse, barn, shed)
//   wild         now and then a cabin or a shed
//
// Stamping: walls fall in with the settings' ruin decay (outer walls more than inner ones), some
// doors are left open or broken, containers get loot (more further out, some already picked
// over), and the dead sleep inside the bigger and further-out buildings.

import { addFurniture, furnitureAt } from './furniture';
import { crateLoot, ruinDecay, zombieScale } from './difficulty';
import { GROUNDS, getTile, setTile, tileIndex, type Ground, type Point } from './grid';
import { hashRoll } from './hash';
import { isRadioactive } from './climate';
import type { SimState } from './state';
import { BLOCK, STREET, type District } from './towns';
import {
  DOOR, FLOOR, WALL, barnPlan, cellAt, churchPlan, civicPlan, housePlan, isContainerLook, shedPlan, shopPlan, warehousePlan, type Plan, type Rolls,
} from './buildings';

export type Facing = 'north' | 'south' | 'east' | 'west';

/** What lots need from worldgen.ts (kept as callbacks, so this module doesn't import it). */
export interface Site {
  seed: number;
  /** Not to be built on or next to: off the map, water, a fence, the start clearing. */
  blocked(x: number, y: number): boolean;
  /** A road or street: not to be built on (next to is fine: that's the front). */
  road(x: number, y: number): boolean;
  /** Finds per container this far from the start (worldgen.ts lootFor). */
  lootAt(x: number, y: number): number;
  /** Distance from the start. */
  distance(x: number, y: number): number;
  /** No sleepers this close to the start. */
  calm: number;
}

const LOT = 15;

/** Fills block (x0, y0) for its district. */
export function fillBlock(state: SimState, site: Site, district: District, x0: number, y0: number): void {
  const roll = (k: number) => hashRoll(site.seed, x0, y0, 0x8000 + k);
  const ix = x0 + STREET;
  const iy = y0 + STREET;
  if (district === 'residential' || district === 'commercial') {
    for (let q = 0; q < 4; q++) {
      const lx = ix + (q % 2) * LOT;
      const ly = iy + Math.floor(q / 2) * LOT;
      const facing: Facing = q < 2 ? 'north' : 'south';
      const r: Rolls = (k) => hashRoll(site.seed, lx, ly, k);
      if (district === 'residential') {
        if (r(0) > 0.9) continue; // an empty lot
        place(state, site, housePlan(r, { maxW: LOT - 2, maxH: LOT - 3, garage: true }), { x: lx, y: ly, w: LOT, h: LOT }, facing, 1 + Math.floor(r(90) * 2), r);
      } else {
        const kind = pickShop(r(0));
        const setback = kind === 'station' ? 4 : 1;
        if (place(state, site, shopPlan(r, kind, LOT - 2, LOT - 1 - setback), { x: lx, y: ly, w: LOT, h: LOT }, facing, setback, r) && kind === 'station') {
          forecourt(state, site, lx, ly, facing);
        }
      }
    }
    return;
  }
  const whole = { x: ix, y: iy, w: BLOCK - STREET, h: BLOCK - STREET };
  const facing: Facing = roll(1) < 0.5 ? 'north' : 'south';
  if (district === 'centre') {
    pave(state, site, whole, 'paving');
    const kind = (['police', 'clinic', 'school', 'church', 'office'] as const)[Math.floor(roll(2) * 5)]!;
    const plan = kind === 'church' ? churchPlan(roll, whole.w - 4, whole.h - 4) : civicPlan(roll, kind, whole.w - 4, whole.h - 4);
    if (place(state, site, plan, whole, facing, 2, roll)) for (let i = 0; i < 4; i++) decorAt(state, site, ix + 1 + Math.floor(roll(70 + i) * (whole.w - 2)), facing === 'north' ? iy : iy + whole.h - 1, 'bench');
    return;
  }
  if (district === 'industrial') {
    pave(state, site, whole, 'concrete');
    place(state, site, warehousePlan(roll, roll(2) < 0.6 ? 'warehouse' : 'factory', whole.w - 2, whole.h - 4), whole, facing, 3, roll);
    return;
  }
  if (district === 'farmland') {
    farm(state, site, x0, y0);
    return;
  }
  // Wild: a cabin or a shed in some half-blocks.
  for (let q = 0; q < 4; q++) {
    const lx = x0 + (q % 2) * (BLOCK / 2);
    const ly = y0 + Math.floor(q / 2) * (BLOCK / 2);
    const r: Rolls = (k) => hashRoll(site.seed, lx, ly, 0x9000 + k);
    const lot = { x: lx + 1, y: ly + 1, w: BLOCK / 2 - 2, h: BLOCK / 2 - 2 };
    const face = FACINGS[Math.floor(r(1) * 4)]!;
    if (r(0) < 0.05) place(state, site, housePlan(r, { maxW: 10, maxH: 10, garage: false }), lot, face, 2, r);
    else if (r(0) < 0.08) place(state, site, shedPlan(r), lot, face, 2, r);
  }
}

const FACINGS: readonly Facing[] = ['north', 'south', 'east', 'west'];

function pickShop(r: number): 'grocery' | 'pharmacy' | 'hardware' | 'bar' | 'station' | 'office' {
  if (r < 0.25) return 'grocery';
  if (r < 0.4) return 'pharmacy';
  if (r < 0.6) return 'hardware';
  if (r < 0.75) return 'bar';
  if (r < 0.85) return 'station';
  return 'office';
}

/** Four fields between dirt tracks; maybe a farmstead on one side of the cross track. */
function farm(state: SimState, site: Site, x0: number, y0: number): void {
  const roll = (k: number) => hashRoll(site.seed, x0, y0, 0x8100 + k);
  const half = BLOCK / 2;
  const top = roll(1) < 0.5; // the farmstead's side
  const stead = roll(0) < 0.5;
  for (let y = y0; y < y0 + BLOCK; y++) {
    for (let x = x0; x < x0 + BLOCK; x++) {
      if (site.blocked(x, y) || site.road(x, y) || getTile(state.grid, x, y) !== 'floor') continue;
      const track = x === x0 + half || y === y0 + half;
      const qx = x < x0 + half ? 0 : 1;
      const qy = y < y0 + half ? 0 : 1;
      if (track) groundAt(state, x, y, 'dirt');
      else if (stead && (qy === 0) === top) continue; // the yard stays grass
      else if (roll(10 + qx + 2 * qy) < 0.65) groundAt(state, x, y, 'field');
    }
  }
  if (!stead) return;
  // The farmhouse and the barn face the cross track.
  const ly = top ? y0 + 1 : y0 + half + 1;
  const facing: Facing = top ? 'south' : 'north';
  const r: Rolls = (k) => hashRoll(site.seed, x0, y0, 0x8200 + k);
  place(state, site, housePlan(r, { maxW: half - 3, maxH: half - 4, garage: false }), { x: x0 + 1, y: ly, w: half - 2, h: half - 2 }, facing, 1, r);
  const rb: Rolls = (k) => hashRoll(site.seed, x0, y0, 0x8300 + k);
  place(state, site, barnPlan(rb, half - 3, half - 6), { x: x0 + half + 1, y: ly, w: half - 2, h: half - 2 }, facing, 1, rb);
  const rs: Rolls = (k) => hashRoll(site.seed, x0, y0, 0x8400 + k);
  if (rs(0) < 0.6) place(state, site, shedPlan(rs), { x: x0 + half + 1, y: ly, w: half - 2, h: half - 2 }, facing === 'south' ? 'north' : 'south', 0, rs);
}

/** A petrol station's forecourt: paving between the shop and the street, two pumps on it. */
function forecourt(state: SimState, site: Site, lx: number, ly: number, facing: Facing): void {
  const y0 = facing === 'north' ? ly : ly + LOT - 3;
  pave(state, site, { x: lx, y: y0, w: LOT, h: 3 }, 'paving');
  decorAt(state, site, lx + 4, y0 + 1, 'pump');
  decorAt(state, site, lx + 9, y0 + 1, 'pump');
}

function pave(state: SimState, site: Site, r: { x: number; y: number; w: number; h: number }, g: Ground): void {
  for (let y = r.y; y < r.y + r.h; y++) for (let x = r.x; x < r.x + r.w; x++) if (!site.blocked(x, y) && !site.road(x, y) && getTile(state.grid, x, y) === 'floor') groundAt(state, x, y, g);
}

function decorAt(state: SimState, site: Site, x: number, y: number, look: 'bench' | 'pump'): void {
  if (site.blocked(x, y) || site.road(x, y) || getTile(state.grid, x, y) !== 'floor' || furnitureAt(state, x, y)) return;
  addFurniture(state, 'decor', x, y).look = look;
}

function groundAt(state: SimState, x: number, y: number, g: Ground): void {
  state.grid.ground[tileIndex(state.grid, x, y)] = GROUNDS.indexOf(g);
}

// --- Stamping ----------------------------------------------------------------

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

/** Puts a plan on a lot, its front `setback` tiles from the lot's street side. False if it didn't fit or the ground wasn't clear. */
export function place(state: SimState, site: Site, plan: Plan, lot: { x: number; y: number; w: number; h: number }, facing: Facing, setback: number, r: Rolls): boolean {
  const across = facing === 'north' || facing === 'south';
  const ww = across ? plan.w : plan.h;
  const wh = across ? plan.h : plan.w;
  if (across ? ww > lot.w || wh + setback > lot.h : ww + setback > lot.w || wh > lot.h) return false;
  const slackX = across ? lot.w - ww : lot.w - ww - setback;
  const slackY = across ? lot.h - wh - setback : lot.h - wh;
  const along = (slack: number) => Math.floor(r(80) * (slack + 1));
  const ox = facing === 'west' ? lot.x + setback : facing === 'east' ? lot.x + lot.w - setback - ww : lot.x + along(slackX);
  const oy = facing === 'north' ? lot.y + setback : facing === 'south' ? lot.y + lot.h - setback - wh : lot.y + along(slackY);
  const mirror = r(81) < 0.5;
  // The footprint must be open ground with nothing on it, and the ring round it off roads and water.
  for (let y = oy - 1; y <= oy + wh; y++) {
    for (let x = ox - 1; x <= ox + ww; x++) {
      if (site.blocked(x, y)) return false;
      const inside = x >= ox && x < ox + ww && y >= oy && y < oy + wh;
      if (inside && (site.road(x, y) || getTile(state.grid, x, y) !== 'floor' || furnitureAt(state, x, y))) return false;
    }
  }
  stamp(state, site, plan, ox, oy, facing, mirror, r);
  return true;
}

function stamp(state: SimState, site: Site, plan: Plan, ox: number, oy: number, facing: Facing, mirror: boolean, r: Rolls): void {
  const decay = ruinDecay(state);
  const at = (x: number, y: number) => {
    const t = transform(plan, x, y, facing, mirror);
    return { x: ox + t.x, y: oy + t.y };
  };
  const floors: Point[] = [];
  for (let y = 0; y < plan.h; y++) {
    for (let x = 0; x < plan.w; x++) {
      const c = cellAt(plan, x, y);
      if (c === 0) continue;
      const w = at(x, y);
      const fall = hashRoll(site.seed, w.x, w.y, 19);
      const outer = cellAt(plan, x - 1, y) === 0 || cellAt(plan, x + 1, y) === 0 || cellAt(plan, x, y - 1) === 0 || cellAt(plan, x, y + 1) === 0;
      groundAt(state, w.x, w.y, plan.ground[y * plan.w + x] ?? 'dirt');
      if (c === WALL) {
        // Ruined: some walls have fallen in, outer ones more than inner ones.
        if (fall > (outer ? 0.08 : 0.04) + decay) setTile(state.grid, w.x, w.y, 'wall');
        else setTile(state.grid, w.x, w.y, 'floor');
      } else if (c === DOOR) {
        if (fall < 0.08) setTile(state.grid, w.x, w.y, 'floor'); // broken off its hinges
        else {
          setTile(state.grid, w.x, w.y, 'door');
          if (fall > 0.8) state.grid.open.push(tileIndex(state.grid, w.x, w.y)); // left open
        }
      } else if (c === FLOOR) {
        setTile(state.grid, w.x, w.y, 'floor');
        floors.push(w);
      }
    }
  }
  // Furniture: containers hold loot (some already searched), the rest is for show.
  plan.furniture.forEach((f, i) => {
    const w = at(f.x, f.y);
    if (getTile(state.grid, w.x, w.y) !== 'floor') return;
    if (isContainerLook(f.look)) {
      const c = addFurniture(state, 'crate', w.x, w.y);
      c.look = f.look;
      c.table = f.table;
      const base = site.lootAt(w.x, w.y);
      const n = 1 + Math.floor(r(100 + i) * Math.max(1, Math.floor(base / 3)));
      const searched = site.distance(w.x, w.y) < 80 && r(200 + i) < 0.25;
      c.loot = searched ? 0 : crateLoot(state, n) * (isRadioactive(state, w.x, w.y) ? 2 : 1);
    } else {
      addFurniture(state, 'decor', w.x, w.y).look = f.look;
    }
  });
  // The dead asleep inside: none near the start, more in bigger buildings and further out.
  const d = site.distance(ox, oy);
  if (d < site.calm || floors.length === 0) return;
  const sleepers = zombieScale(state, Math.floor(r(300) * (plan.sleepers + 1 + Math.floor(d / 60))));
  for (let n = 0; n < sleepers; n++) {
    const p = floors[Math.floor(r(310 + n) * floors.length)]!;
    state.dormant.push({ id: `d${state.nextId++}`, x: p.x, y: p.y });
  }
}
