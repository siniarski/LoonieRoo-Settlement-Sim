// Lots: what each block of a district holds, and stamping building plans (buildings.ts) onto
// the world. Called by worldgen.ts once per chunk (a block is a chunk), after the terrain.
//
//   residential  four lots, two facing the street above and two the street below; a house on most
//   commercial   four lots of shops; a petrol station gets a paved forecourt with pumps
//   centre       one civic building on a paved square
//   industrial   one warehouse or factory on a paved yard
//   farmland     four quadrants between dirt tracks: fields in crop rows, orchards, pasture and
//                woodlots, hedged; most have a farmstead (farmhouse, barn, shed, silo) on one side
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
  /** Where the woods run (the wild forest's noise): copses in farmland. */
  wood(x: number, y: number): boolean;
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
    lots(state, site, district, ix, iy);
    return;
  }
  const whole = { x: ix, y: iy, w: BLOCK - STREET, h: BLOCK - STREET };
  const first: Facing = roll(1) < 0.5 ? 'north' : 'south';
  const facings: Facing[] = [first, first === 'north' ? 'south' : 'north'];
  // One big building on the block: the plan rolled, else turned the other way, else a smaller one.
  // The square or yard is paved round it only once it stands; if nothing fits (water, the start
  // clearing), the block is lots instead, so no block is left an empty slab.
  const big = (make: (shrink: number) => Plan, setback: number): Facing | null => {
    for (const shrink of [0, 8]) for (const facing of facings) if (place(state, site, make(shrink), whole, facing, setback, roll)) return facing;
    return null;
  };
  if (district === 'centre') {
    const kind = (['police', 'clinic', 'school', 'church', 'office'] as const)[Math.floor(roll(2) * 5)]!;
    const facing = big((s) => (kind === 'church' ? churchPlan(roll, whole.w - 4 - s, whole.h - 4 - s) : civicPlan(roll, kind, whole.w - 4 - s, whole.h - 4 - s)), 2);
    if (!facing) return lots(state, site, 'commercial', ix, iy);
    pave(state, site, whole, 'paving');
    for (let i = 0; i < 4; i++) decorAt(state, site, ix + 1 + Math.floor(roll(70 + i) * (whole.w - 2)), facing === 'north' ? iy : iy + whole.h - 1, 'bench');
    return;
  }
  if (district === 'industrial') {
    const kind = roll(2) < 0.6 ? 'warehouse' : 'factory';
    if (!big((s) => warehousePlan(roll, kind, whole.w - 2 - s, whole.h - 4 - s), 3)) return lots(state, site, 'residential', ix, iy);
    pave(state, site, whole, 'concrete');
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

/** Four 15 x 15 lots in a block: two facing the street above, two the street below. */
function lots(state: SimState, site: Site, district: 'residential' | 'commercial', ix: number, iy: number): void {
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

/**
 * Farmland: plots between dirt tracks that sit somewhere different in each block (sometimes one
 * track, so two plots run together). Some blocks have a farmstead on one side (a dirt yard with
 * the farmhouse, a barn, maybe a shed, a silo, hay bales and a tractor); the other plots are
 * fields in crop rows, orchards, pasture or woodlots, hedged with trees (a gate on the track
 * side). Copses spill in wherever the wild forest's noise runs, across tracks and block edges.
 */
function farm(state: SimState, site: Site, x0: number, y0: number): void {
  const roll = (k: number) => hashRoll(site.seed, x0, y0, 0x8100 + k);
  const half = BLOCK / 2;
  // The farmstead's side: the dry one (a lake on a side would leave a yard with no house).
  const dry = (upper: boolean) => {
    for (let y = upper ? y0 : y0 + half; y < (upper ? y0 + half : y0 + BLOCK); y++) for (let x = x0; x < x0 + BLOCK; x++) if (site.blocked(x, y)) return false;
    return true;
  };
  const first = roll(1) < 0.5;
  const top = dry(first) ? first : !first;
  const stead = roll(0) < 0.4 && dry(top);
  const open = (x: number, y: number) => !site.blocked(x, y) && !site.road(x, y) && getTile(state.grid, x, y) === 'floor';
  // Tracks: a dirt track each way, set at a different place in each block (and sometimes only
  // one), so fields come in different sizes; a farmstead keeps its track across the middle.
  const tx = stead ? x0 + half : x0 + 9 + Math.floor(roll(3) * 14);
  const ty = stead ? y0 + half : y0 + 9 + Math.floor(roll(4) * 14);
  const vertical = stead || roll(5) < 0.7;
  const horizontal = stead || roll(6) < 0.7;
  for (let i = 0; i < BLOCK; i++) {
    if (vertical && open(tx, y0 + i)) groundAt(state, tx, y0 + i, 'dirt');
    if (horizontal && open(x0 + i, ty)) groundAt(state, x0 + i, ty, 'dirt');
  }
  // The plots between them, in from the block's edge (a main road may run there).
  const cols: [number, number][] = vertical ? [[x0 + 2, tx - 1], [tx + 1, x0 + BLOCK - 1]] : [[x0 + 2, x0 + BLOCK - 1]];
  const rows: [number, number][] = horizontal ? [[y0 + 2, ty - 1], [ty + 1, y0 + BLOCK - 1]] : [[y0 + 2, y0 + BLOCK - 1]];
  // Woods spill in from the wild where the forest runs (the same noise), whatever the plot.
  const land = (x: number, y: number) => open(x, y) && !site.wood(x, y);
  let q = 0;
  for (const [ay, by] of rows) {
    for (const [ax, bx] of cols) {
      q++;
      for (let y = ay; y <= by; y++) for (let x = ax; x <= bx; x++) if (open(x, y) && site.wood(x, y)) groundAt(state, x, y, 'forest');
      if (stead && (top ? ay === y0 + 2 : ay !== y0 + 2)) {
        for (let y = ay; y <= by; y++) for (let x = ax; x <= bx; x++) if (land(x, y)) groundAt(state, x, y, 'dirt');
        continue;
      }
      const use = roll(10 + q);
      if (use < 0.1) {
        // A woodlot: its trees come with the wild woods' (worldgen.ts).
        for (let y = ay; y <= by; y++) for (let x = ax; x <= bx; x++) if (open(x, y)) groundAt(state, x, y, 'forest');
        continue;
      }
      hedge(state, site, ax, ay, bx, by, ax === x0 + 2 ? 0 : 1, ay === y0 + 2 ? 0 : 1, roll(20 + q));
      if (use < 0.65) {
        // A field in crop rows, running one way or the other.
        const across = roll(30 + q) < 0.5;
        for (let y = ay + 1; y < by; y++) {
          for (let x = ax + 1; x < bx; x++) if (land(x, y)) groundAt(state, x, y, (across ? y - ay : x - ax) % 2 === 1 ? 'field' : 'dirt');
        }
      } else if (use < 0.78) {
        // An orchard: trees in rows.
        for (let y = ay + 2; y < by - 1; y += 3) for (let x = ax + 2; x < bx - 1; x += 3) if (land(x, y) && !furnitureAt(state, x, y)) addFurniture(state, 'tree', x, y);
      } else {
        // Pasture: grass, and a few bales left out.
        for (let i = 0; i < 3; i++) decorAt(state, site, ax + 2 + Math.floor(roll(40 + q * 4 + i) * (bx - ax - 3)), ay + 2 + Math.floor(roll(60 + q * 4 + i) * (by - ay - 3)), 'hay');
      }
    }
  }
  if (!stead) return;
  // The farmhouse and the barn face the cross track.
  const ly = top ? y0 + 1 : y0 + half + 1;
  const facing: Facing = top ? 'south' : 'north';
  const r: Rolls = (k) => hashRoll(site.seed, x0, y0, 0x8200 + k);
  place(state, site, housePlan(r, { maxW: half - 3, maxH: half - 4, garage: false }), { x: x0 + 2, y: ly, w: half - 3, h: half - 2 }, facing, 1, r); // x0 + 1 may be a main road
  const rb: Rolls = (k) => hashRoll(site.seed, x0, y0, 0x8300 + k);
  place(state, site, barnPlan(rb, half - 3, half - 6), { x: x0 + half + 1, y: ly, w: half - 2, h: half - 2 }, facing, 1, rb);
  const rs: Rolls = (k) => hashRoll(site.seed, x0, y0, 0x8400 + k);
  if (rs(0) < 0.6) place(state, site, shedPlan(rs), { x: x0 + half + 1, y: ly, w: half - 2, h: half - 2 }, facing === 'south' ? 'north' : 'south', 0, rs);
  // The yard: a silo (2 x 2) in a far corner, bales and a tractor on whatever dirt is left.
  const sy = top ? y0 + 2 : y0 + BLOCK - 2;
  const sx = roll(2) < 0.5 ? x0 + 2 : x0 + BLOCK - 3;
  const dys = top ? [0, 1] : [0, -1];
  if (dys.every((dy) => [0, 1].every((dx) => yard(state, site, sx + dx, sy + dy)))) {
    for (const dy of dys) for (const dx of [0, 1]) yardDecor(state, site, sx + dx, sy + dy, 'silo');
  }
  const yardY = top ? y0 + half - 2 : y0 + half + 2;
  for (let i = 0; i < 6; i++) {
    const x = x0 + 2 + Math.floor(roll(80 + i) * (BLOCK - 4));
    const y = yardY + (top ? -1 : 1) * Math.floor(roll(90 + i) * 3);
    yardDecor(state, site, x, y, i === 0 ? 'tractor' : 'hay');
  }
}

/** A hedge of trees round a quadrant, with a gate in the side facing the cross track. */
function hedge(state: SimState, site: Site, ax: number, ay: number, bx: number, by: number, qx: number, qy: number, r: number): void {
  const gx = Math.floor((ax + bx) / 2);
  const gy = Math.floor((ay + by) / 2);
  const trackX = qx === 0 ? bx : ax;
  const trackY = qy === 0 ? by : ay;
  // Each side has a hedge only some of the time (else it's an open field edge), so plots don't
  // all come out boxed in along the block lines.
  const side = (k: number) => hashRoll(site.seed, ax, ay, 0x8600 + k) < 0.5;
  const sides = { top: side(0), bottom: side(1), left: side(2), right: side(3) };
  for (let y = ay; y <= by; y++) {
    for (let x = ax; x <= bx; x++) {
      const edge = (y === ay && sides.top) || (y === by && sides.bottom) || (x === ax && sides.left) || (x === bx && sides.right);
      if (!edge) continue;
      if ((x === trackX && Math.abs(y - gy) <= 1) || (y === trackY && Math.abs(x - gx) <= 1)) continue; // the gate
      if (hashRoll(site.seed, x, y, 0x8500) > 0.7 + r * 0.2) continue; // grown thin
      if (!site.blocked(x, y) && !site.road(x, y) && getTile(state.grid, x, y) === 'floor' && !furnitureAt(state, x, y)) addFurniture(state, 'tree', x, y);
    }
  }
}

/** Open farmyard: dirt with nothing on it (buildings have floors of their own). */
function yard(state: SimState, site: Site, x: number, y: number): boolean {
  return !site.blocked(x, y) && !site.road(x, y) && getTile(state.grid, x, y) === 'floor' && !furnitureAt(state, x, y) && GROUNDS[state.grid.ground[tileIndex(state.grid, x, y)]!] === 'dirt';
}

function yardDecor(state: SimState, site: Site, x: number, y: number, look: 'silo' | 'hay' | 'tractor'): void {
  if (yard(state, site, x, y)) addFurniture(state, 'decor', x, y).look = look;
}

/** A petrol station's forecourt: paving between the shop and the street, two pumps on it. */
function forecourt(state: SimState, site: Site, lx: number, ly: number, facing: Facing): void {
  const y0 = facing === 'north' ? ly : ly + LOT - 3;
  pave(state, site, { x: lx, y: y0, w: LOT, h: 3 }, 'paving');
  decorAt(state, site, lx + 4, y0 + 1, 'pump');
  decorAt(state, site, lx + 9, y0 + 1, 'pump');
}

/** Paves open ground (grass, dirt, forest, field) in a rectangle: never a building's floor, which is laid first. */
function pave(state: SimState, site: Site, r: { x: number; y: number; w: number; h: number }, g: Ground): void {
  for (let y = r.y; y < r.y + r.h; y++) {
    for (let x = r.x; x < r.x + r.w; x++) {
      if (site.blocked(x, y) || site.road(x, y) || getTile(state.grid, x, y) !== 'floor') continue;
      if (OPEN_GROUND.includes(GROUNDS[state.grid.ground[tileIndex(state.grid, x, y)]!]!)) groundAt(state, x, y, g);
    }
  }
}

const OPEN_GROUND: readonly Ground[] = ['grass', 'dirt', 'forest', 'field'];

function decorAt(state: SimState, site: Site, x: number, y: number, look: 'bench' | 'pump' | 'hay'): void {
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
