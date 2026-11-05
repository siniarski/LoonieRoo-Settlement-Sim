// Building plans: houses with rooms, shops, civic buildings, warehouses, barns and sheds.
//
// A plan is drawn in its own coordinates with the front (the side facing the street) at the
// bottom, then stamped onto the world turned to face its street, and maybe mirrored. Plans
// are pure (a function of a roll), so tests can check every one: walls all round, every room
// reachable from the front door, furniture only on floor.
//
//   house     a front strip (living room with the front door, kitchen) and a back strip
//             (bathroom, one or two bedrooms) behind a dividing wall; each back room has a door
//             into the front strip. Some have a garage on one side, opening into the kitchen.
//   shop      a sales floor with shelves along the walls (and an island when there's room),
//             a storeroom and a toilet behind
//   civic     a corridor up the middle from a double front door, rooms either side
//             (police station, clinic, school, offices); a church is one hall with a vestry
//   warehouse a big hall with rows of pallets and shelves, and an office in a corner
//   barn, shed  one room
//
// Rooms set the floor (boards, tiles, concrete) and what's in the containers (loot.ts).

import type { Ground } from './grid';
import type { Look } from './furniture';
import type { LootTable } from './loot';

export const OUTSIDE = 0;
export const FLOOR = 1;
export const WALL = 2;
export const DOOR = 3;

export interface PlanFurniture {
  x: number;
  y: number;
  look: Look;
  /** Containers: what's in them. Decor: null. */
  table: LootTable | null;
}

export interface Plan {
  w: number;
  h: number;
  /** OUTSIDE, FLOOR, WALL or DOOR, row-major. */
  cells: number[];
  ground: (Ground | null)[];
  furniture: PlanFurniture[];
  /** What it is, for points of interest and the event log. */
  kind: BuildingKind;
  /** How many of the dead are likely asleep inside (before distance and settings). */
  sleepers: number;
  /**
   * Walls and doors on the borders between cells (grid.ts edges), from cell a to the cell b beside
   * it (b may be just outside the plan). Set by toEdges: a finished plan has no WALL or DOOR cells.
   */
  edges: PlanEdge[];
}

export interface PlanEdge {
  ax: number;
  ay: number;
  bx: number;
  by: number;
  door: boolean;
  /** Glass in an outer wall (grid.ts EDGE_WINDOW). */
  window?: boolean;
}

/** The kinds of building that get windows (toEdges). */
const WINDOWED: readonly string[] = ['house', 'grocery', 'pharmacy', 'hardware', 'bar', 'station', 'office', 'police', 'clinic', 'school', 'church'];

export type BuildingKind = 'house' | 'grocery' | 'pharmacy' | 'hardware' | 'bar' | 'station' | 'office' | 'police' | 'clinic' | 'school' | 'church' | 'warehouse' | 'factory' | 'barn' | 'shed';

/** Rolls in [0, 1) by index: the same plan for the same rolls. */
export type Rolls = (k: number) => number;

/** Containers whose look is a container (the rest are decor). */
const CONTAINERS: readonly Look[] = ['counter', 'icebox', 'shelf', 'wardrobe', 'dresser', 'cabinet', 'toolbox', 'locker', 'desk', 'pallet', 'sack', 'crate'];
export function isContainerLook(look: Look): boolean {
  return CONTAINERS.includes(look);
}

// --- Drawing a plan ---------------------------------------------------------

function newPlan(w: number, h: number, kind: BuildingKind, sleepers: number): Plan {
  return { w, h, cells: new Array<number>(w * h).fill(OUTSIDE), ground: new Array<Ground | null>(w * h).fill(null), furniture: [], kind, sleepers, edges: [] };
}

export const cellAt = (p: Plan, x: number, y: number): number => (x < 0 || y < 0 || x >= p.w || y >= p.h ? OUTSIDE : p.cells[y * p.w + x]!);
const set = (p: Plan, x: number, y: number, c: number) => {
  p.cells[y * p.w + x] = c;
};

/** A walled box from (x0, y0) to (x1, y1) inclusive, floored inside with `ground`. Shares walls with boxes it touches. */
function box(p: Plan, x0: number, y0: number, x1: number, y1: number, ground: Ground): void {
  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) {
      const edge = x === x0 || x === x1 || y === y0 || y === y1;
      if (edge) {
        if (cellAt(p, x, y) !== DOOR) set(p, x, y, WALL);
      } else {
        set(p, x, y, FLOOR);
        p.ground[y * p.w + x] = ground;
      }
    }
  }
}

/** Floors a rectangle (inside a box) with another ground. */
function floorAs(p: Plan, x0: number, y0: number, x1: number, y1: number, ground: Ground): void {
  for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) if (cellAt(p, x, y) === FLOOR) p.ground[y * p.w + x] = ground;
}

function hwall(p: Plan, y: number, x0: number, x1: number): void {
  for (let x = x0; x <= x1; x++) if (cellAt(p, x, y) !== DOOR) set(p, x, y, WALL);
}

function vwall(p: Plan, x: number, y0: number, y1: number): void {
  for (let y = y0; y <= y1; y++) if (cellAt(p, x, y) !== DOOR) set(p, x, y, WALL);
}

function door(p: Plan, x: number, y: number): void {
  set(p, x, y, DOOR);
}

/** Next to a door (orthogonally): kept clear of furniture so doorways read. */
function byDoor(p: Plan, x: number, y: number): boolean {
  return cellAt(p, x + 1, y) === DOOR || cellAt(p, x - 1, y) === DOOR || cellAt(p, x, y + 1) === DOOR || cellAt(p, x, y - 1) === DOOR;
}

function free(p: Plan, x: number, y: number): boolean {
  return cellAt(p, x, y) === FLOOR && !byDoor(p, x, y) && !p.furniture.some((f) => f.x === x && f.y === y);
}

function put(p: Plan, x: number, y: number, look: Look, table: LootTable | null = null): boolean {
  if (!free(p, x, y)) return false;
  p.furniture.push({ x, y, look, table: isContainerLook(look) ? table : null });
  return true;
}

/** Puts something against a wall of the room (x0..x1, y0..y1 interior), trying the walls in an order set by the roll. */
function againstWall(p: Plan, x0: number, y0: number, x1: number, y1: number, look: Look, table: LootTable | null, roll: number): boolean {
  const spots: [number, number][] = [];
  for (let x = x0; x <= x1; x++) spots.push([x, y0], [x, y1]);
  for (let y = y0 + 1; y < y1; y++) spots.push([x0, y], [x1, y]);
  const start = Math.floor(roll * spots.length);
  for (let i = 0; i < spots.length; i++) {
    const [x, y] = spots[(start + i) % spots.length]!;
    if (put(p, x, y, look, table)) return true;
  }
  return false;
}

/** Somewhere in the middle of the room, away from the walls if there's space. */
function inMiddle(p: Plan, x0: number, y0: number, x1: number, y1: number, look: Look, table: LootTable | null): boolean {
  const cx = Math.floor((x0 + x1) / 2);
  const cy = Math.floor((y0 + y1) / 2);
  for (const [dx, dy] of [[0, 0], [1, 0], [0, 1], [-1, 0], [0, -1]] as const) {
    const x = cx + dx;
    const y = cy + dy;
    if (x > x0 && x < x1 && y > y0 && y < y1 && put(p, x, y, look, table)) return true;
  }
  return false;
}

const between = (r: number, lo: number, hi: number) => lo + Math.floor(r * (hi - lo + 1));

// --- Houses -----------------------------------------------------------------

export interface HouseOptions {
  /** Most width and depth the lot allows (outer, garage included). */
  maxW: number;
  maxH: number;
  /** A garage is allowed (town houses; not farmhouses or cabins). */
  garage: boolean;
}

export function housePlan(r: Rolls, o: HouseOptions): Plan {
  const garage = o.garage && r(1) < 0.4 && o.maxW >= 13;
  const W = Math.min(o.maxW - (garage ? 4 : 0), between(r(2), 9, 13));
  const H = Math.min(o.maxH, between(r(3), 9, 11));
  const p = newPlan(W + (garage ? 4 : 0), H, 'house', 1);
  box(p, 0, 0, W - 1, H - 1, 'boards');
  const back = H >= 11 ? 4 : 3; // back strip depth (interior rows 1..back)
  const div = back + 1;
  hwall(p, div, 0, W - 1);
  // Front strip: living room (left, front door) and kitchen (right).
  const inner = W - 2;
  const split = 1 + Math.max(3, Math.floor(inner * 0.55));
  vwall(p, split, div, H - 1);
  const living = { x0: 1, y0: div + 1, x1: split - 1, y1: H - 2 };
  const kitchen = { x0: split + 1, y0: div + 1, x1: W - 2, y1: H - 2 };
  floorAs(p, kitchen.x0, kitchen.y0, kitchen.x1, kitchen.y1, 'tiles');
  door(p, Math.floor((living.x0 + living.x1) / 2), H - 1);
  door(p, split, Math.floor((living.y0 + living.y1 + 1) / 2));
  // Back strip: bathroom (2 or 3 wide) on the left, then one or two bedrooms.
  const bathW = inner >= 10 ? 3 : 2;
  const bath = { x0: 1, y0: 1, x1: bathW, y1: back };
  vwall(p, bathW + 1, 0, div);
  floorAs(p, bath.x0, bath.y0, bath.x1, bath.y1, 'tiles');
  const rest = { x0: bathW + 2, x1: W - 2 };
  const two = rest.x1 - rest.x0 + 1 >= 7;
  const bedrooms = two
    ? (() => {
        const mid = rest.x0 + Math.floor((rest.x1 - rest.x0 + 1) / 2);
        vwall(p, mid, 0, div);
        return [{ x0: rest.x0, y0: 1, x1: mid - 1, y1: back }, { x0: mid + 1, y0: 1, x1: rest.x1, y1: back }];
      })()
    : [{ x0: rest.x0, y0: 1, x1: rest.x1, y1: back }];
  // A door from each back room down into the front strip, never on the living/kitchen wall.
  for (const room of [bath, ...bedrooms]) {
    let x = Math.floor((room.x0 + room.x1) / 2);
    if (x === split) x = x - 1 >= room.x0 ? x - 1 : x + 1;
    door(p, x, div);
  }
  // A garage on the right, opening onto the street and into the kitchen.
  if (garage) {
    const gy0 = Math.max(div, H - 7);
    box(p, W - 1, gy0, W + 3, H - 1, 'concrete');
    // The shared wall keeps the house's side; a door through it into the kitchen.
    vwall(p, W - 1, 0, H - 1);
    door(p, W - 1, Math.max(gy0 + 1, kitchen.y0 + Math.floor((kitchen.y1 - kitchen.y0) / 2)));
    door(p, W, H - 1);
    door(p, W + 1, H - 1);
    againstWall(p, W, gy0 + 1, W + 2, H - 2, 'toolbox', 'garage', r(30));
    againstWall(p, W, gy0 + 1, W + 2, H - 2, 'shelf', 'garage', r(31));
    if (r(32) < 0.5) inMiddle(p, W, gy0 + 1, W + 2, H - 2, 'car', null);
  } else if (r(4) < 0.5) door(p, W - 1, Math.floor((kitchen.y0 + kitchen.y1) / 2)); // a side door into the kitchen
  // Furniture, room by room.
  againstWall(p, living.x0, living.y0, living.x1, living.y1, 'sofa', null, r(10));
  againstWall(p, living.x0, living.y0, living.x1, living.y1, 'shelf', 'living', r(11));
  if (r(12) < 0.6) againstWall(p, living.x0, living.y0, living.x1, living.y1, 'tv', null, r(13));
  againstWall(p, kitchen.x0, kitchen.y0, kitchen.x1, kitchen.y1, 'counter', 'kitchen', r(14));
  againstWall(p, kitchen.x0, kitchen.y0, kitchen.x1, kitchen.y1, 'icebox', 'kitchen', r(15));
  againstWall(p, kitchen.x0, kitchen.y0, kitchen.x1, kitchen.y1, 'oven', null, r(16));
  if (r(17) < 0.5) againstWall(p, kitchen.x0, kitchen.y0, kitchen.x1, kitchen.y1, 'counter', 'kitchen', r(18));
  inMiddle(p, kitchen.x0, kitchen.y0, kitchen.x1, kitchen.y1, 'table', null);
  againstWall(p, bath.x0, bath.y0, bath.x1, bath.y1, 'toilet', null, r(19));
  againstWall(p, bath.x0, bath.y0, bath.x1, bath.y1, 'cabinet', 'bathroom', r(20));
  if (bathW === 3) againstWall(p, bath.x0, bath.y0, bath.x1, bath.y1, 'bath', null, r(21));
  bedrooms.forEach((b, i) => {
    againstWall(p, b.x0, b.y0, b.x1, b.y1, 'bed', null, r(22 + i));
    againstWall(p, b.x0, b.y0, b.x1, b.y1, i === 0 ? 'wardrobe' : 'dresser', 'bedroom', r(24 + i));
  });
  return toEdges(p);
}

// --- Shops ------------------------------------------------------------------

export function shopPlan(r: Rolls, kind: 'grocery' | 'pharmacy' | 'hardware' | 'bar' | 'station' | 'office', maxW: number, maxH: number): Plan {
  const W = Math.min(maxW, between(r(2), 11, 14));
  const H = Math.min(maxH, between(r(3), 9, 12));
  const table: LootTable = kind;
  const p = newPlan(W, H, kind, 1);
  box(p, 0, 0, W - 1, H - 1, kind === 'bar' || kind === 'office' ? 'boards' : 'tiles');
  // Storeroom and toilet behind a wall.
  const div = 4;
  hwall(p, div, 0, W - 1);
  vwall(p, 3, 0, div);
  floorAs(p, 4, 1, W - 2, div - 1, 'concrete');
  door(p, 2, div);
  door(p, Math.floor(W / 2) + 1, div);
  againstWall(p, 1, 1, 2, div - 1, 'toilet', null, r(5));
  const mid = Math.floor(W / 2);
  door(p, mid - 1, H - 1);
  door(p, mid, H - 1);
  // The storeroom: pallets and crates of whatever the shop sold.
  for (let i = 0; i < 2 + Math.floor(r(6) * 2); i++) againstWall(p, 4, 1, W - 2, div - 1, i % 2 === 0 ? 'pallet' : 'crate', table, r(7 + i));
  // The floor.
  const floor = { x0: 1, y0: div + 1, x1: W - 2, y1: H - 2 };
  if (kind === 'office') {
    for (let y = floor.y0 + 1; y < floor.y1; y += 2) for (let x = floor.x0 + 1; x < floor.x1; x += 3) put(p, x, y, 'desk', table);
    return toEdges(p);
  }
  if (kind === 'bar') {
    for (let x = floor.x0; x <= floor.x1 - 2; x++) put(p, x, floor.y0, 'counter', table);
    for (let y = floor.y0 + 2; y < floor.y1; y += 2) for (let x = floor.x0 + 1; x < floor.x1; x += 3) put(p, x, y, 'table', null);
    return toEdges(p);
  }
  // Shelves along the side walls, an island down the middle if it's wide enough, a till by the door.
  for (let y = floor.y0; y <= floor.y1 - 1; y++) {
    put(p, floor.x0, y, 'shelf', table);
    put(p, floor.x1, y, 'shelf', table);
  }
  if (W >= 12) for (let y = floor.y0 + 1; y <= floor.y1 - 2; y++) put(p, mid - 1, y, 'shelf', table);
  put(p, floor.x1 - 1, floor.y1, 'register', null);
  return toEdges(p);
}

// --- Civic buildings --------------------------------------------------------

type CivicKind = 'police' | 'clinic' | 'school' | 'office';

/** A corridor up the middle from a double front door; rooms either side, each with a door onto it. */
export function civicPlan(r: Rolls, kind: CivicKind, maxW: number, maxH: number): Plan {
  const W = Math.min(maxW, between(r(2), 17, 23));
  const H = Math.min(maxH, between(r(3), 13, 19));
  const p = newPlan(W, H, kind, kind === 'police' || kind === 'clinic' ? 3 : 2);
  box(p, 0, 0, W - 1, H - 1, kind === 'clinic' ? 'tiles' : 'boards');
  const c0 = Math.floor(W / 2) - 1; // corridor: c0 and c0 + 1
  vwall(p, c0 - 1, 0, H - 1);
  vwall(p, c0 + 2, 0, H - 1);
  door(p, c0, H - 1);
  door(p, c0 + 1, H - 1);
  floorAs(p, c0, 1, c0 + 1, H - 2, kind === 'clinic' ? 'tiles' : 'concrete');
  // Rooms: split each side's interior rows into rooms 3 to 5 deep.
  const roles = ROLES[kind];
  let n = 0;
  for (const [x0, x1, doorX] of [[1, c0 - 2, c0 - 1], [c0 + 3, W - 2, c0 + 2]] as const) {
    let y = 1;
    while (y <= H - 2) {
      let depth = between(r(40 + n), 3, 5);
      if (H - 2 - (y + depth) < 3) depth = H - 1 - y; // don't leave a sliver
      const y1 = y + depth - 1;
      if (y1 + 1 < H - 1) hwall(p, y1 + 1, x0 - 1, x1 + 1);
      door(p, doorX, y + Math.floor(depth / 2));
      fitOut(p, roles[n % roles.length]!, x0, y, x1, y1, r, 60 + n * 5);
      n++;
      y = y1 + 2;
    }
  }
  return toEdges(p);
}

type Role = 'desks' | 'lockers' | 'armoury' | 'cells' | 'toilets' | 'ward' | 'stores' | 'classroom';
const ROLES: Record<CivicKind, Role[]> = {
  police: ['desks', 'lockers', 'cells', 'armoury', 'toilets', 'desks'],
  clinic: ['ward', 'stores', 'ward', 'desks', 'toilets', 'ward'],
  school: ['classroom', 'classroom', 'desks', 'toilets', 'classroom', 'stores'],
  office: ['desks', 'desks', 'toilets', 'stores', 'desks', 'desks'],
};

function fitOut(p: Plan, role: Role, x0: number, y0: number, x1: number, y1: number, r: Rolls, k: number): void {
  const table: LootTable = p.kind === 'police' ? 'police' : p.kind === 'clinic' ? 'clinic' : p.kind === 'school' ? 'school' : 'office';
  switch (role) {
    case 'desks':
      for (let y = y0 + 1; y < y1; y += 2) for (let x = x0 + 1; x < x1; x += 2) put(p, x, y, 'desk', 'office');
      break;
    case 'lockers':
    case 'armoury':
      for (let x = x0; x <= x1; x++) put(p, x, y0, 'locker', table);
      if (role === 'armoury') for (let x = x0; x <= x1; x++) put(p, x, y1, 'locker', table);
      break;
    case 'cells':
      for (let x = x0; x <= x1; x += 2) put(p, x, y0, 'cot', null);
      break;
    case 'toilets':
      floorAs(p, x0, y0, x1, y1, 'tiles');
      for (let x = x0; x <= x1; x += 2) put(p, x, y0, 'toilet', null);
      againstWall(p, x0, y0, x1, y1, 'cabinet', 'bathroom', r(k));
      break;
    case 'ward':
      for (let x = x0; x <= x1; x += 2) put(p, x, y0, 'cot', null);
      againstWall(p, x0, y0, x1, y1, 'cabinet', table, r(k));
      againstWall(p, x0, y0, x1, y1, 'cabinet', table, r(k + 1));
      break;
    case 'stores':
      for (let i = 0; i < 3; i++) againstWall(p, x0, y0, x1, y1, i === 0 ? 'cabinet' : 'shelf', table, r(k + i));
      break;
    case 'classroom':
      for (let y = y0 + 1; y < y1; y += 2) for (let x = x0 + 1; x < x1; x += 2) put(p, x, y, 'desk', 'school');
      againstWall(p, x0, y0, x1, y1, 'shelf', 'school', r(k));
      break;
  }
}

/** One hall of pews facing an altar, a vestry in a back corner. */
export function churchPlan(r: Rolls, maxW: number, maxH: number): Plan {
  const W = Math.min(maxW, between(r(2), 11, 15));
  const H = Math.min(maxH, between(r(3), 15, 20));
  const p = newPlan(W, H, 'church', 1);
  box(p, 0, 0, W - 1, H - 1, 'boards');
  box(p, 0, 0, 4, 4, 'boards');
  door(p, 4, 2);
  const mid = Math.floor(W / 2);
  door(p, mid, H - 1);
  againstWall(p, 1, 1, 3, 3, 'cabinet', 'church', r(5));
  againstWall(p, 1, 1, 3, 3, 'shelf', 'church', r(6));
  put(p, mid, 2, 'altar', null);
  for (let y = 6; y < H - 3; y += 2) {
    for (let x = 1; x < W - 1; x++) if (Math.abs(x - mid) > 1) put(p, x, y, 'pew', null);
  }
  return toEdges(p);
}

// --- Industry and farms -----------------------------------------------------

export function warehousePlan(r: Rolls, kind: 'warehouse' | 'factory', maxW: number, maxH: number): Plan {
  const W = Math.min(maxW, between(r(2), 18, 26));
  const H = Math.min(maxH, between(r(3), 13, 20));
  const p = newPlan(W, H, kind, 2);
  box(p, 0, 0, W - 1, H - 1, 'concrete');
  // Loading doors at the front, a door at the back, an office in the back-left corner.
  for (let x = 3; x <= 5; x++) door(p, x, H - 1);
  door(p, W - 3, 0);
  box(p, 0, 0, 5, 4, 'boards');
  door(p, 3, 4);
  put(p, 2, 2, 'desk', 'office');
  againstWall(p, 1, 1, 4, 3, 'cabinet', 'office', r(5));
  const table: LootTable = kind;
  // Rows of pallets (or machines, in a factory), with aisles between.
  for (let x = 8; x < W - 2; x += 3) {
    for (let y = 2; y < H - 3; y++) {
      if (kind === 'factory' && (y % 3 === 0)) put(p, x, y, 'machine', null);
      else if ((x + y) % 2 === 0) put(p, x, y, r(10 + x * 31 + y) < 0.5 ? 'pallet' : 'shelf', table);
    }
  }
  return toEdges(p);
}

export function barnPlan(r: Rolls, maxW: number, maxH: number): Plan {
  const W = Math.min(maxW, between(r(2), 9, 13));
  const H = Math.min(maxH, between(r(3), 8, 10));
  const p = newPlan(W, H, 'barn', 1);
  box(p, 0, 0, W - 1, H - 1, 'concrete');
  const mid = Math.floor(W / 2);
  door(p, mid - 1, H - 1);
  door(p, mid, H - 1);
  for (let i = 0; i < 4; i++) againstWall(p, 1, 1, W - 2, H - 2, 'hay', null, r(10 + i));
  for (let i = 0; i < 2; i++) againstWall(p, 1, 1, W - 2, H - 2, 'sack', 'barn', r(20 + i));
  againstWall(p, 1, 1, W - 2, H - 2, 'toolbox', 'barn', r(30));
  if (r(31) < 0.5) inMiddle(p, 1, 1, W - 2, H - 2, 'tractor', null);
  return toEdges(p);
}

export function shedPlan(r: Rolls): Plan {
  const p = newPlan(5, 5, 'shed', 0);
  box(p, 0, 0, 4, 4, 'boards');
  door(p, 2, 4);
  againstWall(p, 1, 1, 3, 3, 'toolbox', 'shed', r(1));
  againstWall(p, 1, 1, 3, 3, 'shelf', 'shed', r(2));
  return toEdges(p);
}

// --- Walls onto edges -----------------------------------------------------------

const DIR4 = [[0, 1], [1, 0], [0, -1], [-1, 0]] as const; // S, E, N, W: the order a wall cell joins a room

/**
 * Turns a plan drawn with wall cells into one with edge walls (grid.ts): walls on the borders
 * between cells, so rooms get the floor the walls stood on.
 *   - Wall cells on the outside (next to the outside or the plan's edge) become outside: the
 *     building's outer walls are edges round its floor. An outer door becomes a door edge there.
 *   - Inside, each wall cell becomes floor of a room beside it (south, else east, north, west of it,
 *     whichever is a room's floor first; a cell with none takes a neighbour's), so a wall line goes
 *     to the room below or right of it.
 *   - Then an edge wall wherever two cells side by side are in different rooms, or a room meets the
 *     outside; an edge door where a door cell stood (between the door's two sides).
 */
export function toEdges(p: Plan): Plan {
  const { w, h } = p;
  const at = (x: number, y: number) => (x < 0 || y < 0 || x >= w || y >= h ? OUTSIDE : p.cells[y * w + x]!);
  const solid = (c: number) => c === WALL || c === DOOR;
  // Rooms of the floor as drawn: flood fill floor cells, walls and doors between.
  const room = new Int32Array(w * h).fill(-1);
  let rooms = 0;
  for (let i = 0; i < w * h; i++) {
    if (p.cells[i] !== FLOOR || room[i] !== -1) continue;
    const queue = [i];
    room[i] = rooms;
    while (queue.length) {
      const j = queue.pop()!;
      const x = j % w;
      const y = Math.floor(j / w);
      for (const [dx, dy] of DIR4) {
        const nx = x + dx;
        const ny = y + dy;
        if (at(nx, ny) !== FLOOR || room[ny * w + nx] !== -1) continue;
        room[ny * w + nx] = rooms;
        queue.push(ny * w + nx);
      }
    }
    rooms++;
  }
  // Outer wall cells go outside; inner ones join a room.
  const outer = (x: number, y: number) => DIR4.some(([dx, dy]) => at(x + dx, y + dy) === OUTSIDE);
  const cells = p.cells.slice();
  const pending: number[] = [];
  for (let i = 0; i < w * h; i++) {
    if (!solid(p.cells[i]!)) continue;
    const x = i % w;
    const y = Math.floor(i / w);
    if (outer(x, y)) {
      cells[i] = OUTSIDE;
      continue;
    }
    cells[i] = FLOOR;
    pending.push(i);
  }
  // Join a room: a floor neighbour's, in S, E, N, W order; repeat for cells only next to other walls.
  for (let pass = 0; pass < 4 && pending.some((i) => room[i] === -1); pass++) {
    for (const i of pending) {
      if (room[i] !== -1) continue;
      const x = i % w;
      const y = Math.floor(i / w);
      for (const [dx, dy] of DIR4) {
        const nx = x + dx;
        const ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= w || ny >= h || cells[ny * w + nx] !== FLOOR) continue;
        const r = room[ny * w + nx]!;
        // First pass: only rooms as drawn (not cells joined this pass), so a wall line joins its room.
        if (r === -1 || (pass === 0 && solid(p.cells[ny * w + nx]!))) continue;
        room[i] = r;
        break;
      }
    }
  }
  // The ground of a joined wall cell: its room's (from the neighbour it joined).
  const ground = p.ground.slice();
  for (const i of pending) {
    if (ground[i]) continue;
    const x = i % w;
    const y = Math.floor(i / w);
    for (const [dx, dy] of DIR4) {
      const j = (y + dy) * w + x + dx;
      if (x + dx >= 0 && y + dy >= 0 && x + dx < w && y + dy < h && room[j] === room[i] && p.ground[j]) {
        ground[i] = p.ground[j]!;
        break;
      }
    }
  }
  // Doors: the pair of cells either side of each door cell (across its wall line).
  const doorPairs = new Set<string>();
  const pairKey = (ax: number, ay: number, bx: number, by: number) => (ay < by || (ay === by && ax < bx) ? `${ax},${ay},${bx},${by}` : `${bx},${by},${ax},${ay}`);
  for (let i = 0; i < w * h; i++) {
    if (p.cells[i] !== DOOR) continue;
    const x = i % w;
    const y = Math.floor(i / w);
    const vertical = solid(at(x, y - 1)) || solid(at(x, y + 1)); // in a north-south wall: a door east-west
    const [ax, ay, bx, by] = vertical ? [x - 1, y, x + 1, y] : [x, y - 1, x, y + 1];
    // Which side the door cell joined (or none, outside): the edge is on the other side of it.
    if (cells[i] === OUTSIDE) {
      // An outer door: the edge between it and the inside.
      const inside = cells[ay * w + ax] === FLOOR && ax >= 0 && ay >= 0 && ax < w && ay < h ? [ax, ay] : [bx, by];
      doorPairs.add(pairKey(x, y, inside[0]!, inside[1]!));
    } else {
      const aRoom = ax >= 0 && ay >= 0 && ax < w && ay < h ? room[ay * w + ax] : -2;
      const other = aRoom === room[i] ? [bx, by] : [ax, ay];
      doorPairs.add(pairKey(x, y, other[0]!, other[1]!));
    }
  }
  // Edges: between floor and outside (or off the plan), and between floors of different rooms.
  const edges: PlanEdge[] = [];
  const cellOf = (x: number, y: number) => (x < 0 || y < 0 || x >= w || y >= h ? OUTSIDE : cells[y * w + x]!);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      for (const [dx, dy] of [[0, -1], [-1, 0], [0, 1], [1, 0]] as const) {
        const nx = x + dx;
        const ny = y + dy;
        const here = cellOf(x, y);
        const there = cellOf(nx, ny);
        if (here !== FLOOR && there !== FLOOR) continue;
        // Each pair once: from the floor cell; between two floors, only looking north or west.
        if (here !== FLOOR) continue;
        if (there === FLOOR && (dx > 0 || dy > 0)) continue;
        const differs = there !== FLOOR || room[y * w + x] !== room[ny * w + nx];
        if (!differs) continue;
        edges.push({ ax: x, ay: y, bx: nx, by: ny, door: doorPairs.has(pairKey(x, y, nx, ny)) });
      }
    }
  }
  // Windows: every third edge along an outer wall, with wall either side of it along the same
  // line (never at a corner or beside a door). Lived-in and public buildings only: no barns,
  // sheds, warehouses or factories.
  if (WINDOWED.includes(p.kind)) {
    const key = (e: PlanEdge) => `${e.ax},${e.ay},${e.bx},${e.by}`;
    const outerWall = new Map(edges.filter((e) => !e.door && cellOf(e.bx, e.by) !== FLOOR).map((e) => [key(e), e]));
    for (const e of outerWall.values()) {
      const [dx, dy] = [e.bx - e.ax, e.by - e.ay];
      const [sx, sy] = dy !== 0 ? [1, 0] : [0, 1]; // along the wall
      const along = dy !== 0 ? e.ax : e.ay;
      if (along % 3 !== 1) continue;
      const beside = (k: number) => outerWall.has(`${e.ax + sx * k},${e.ay + sy * k},${e.bx + sx * k},${e.by + sy * k}`);
      if (beside(1) && beside(-1)) e.window = true;
      void dx;
    }
  }
  return { ...p, cells, ground, edges };
}
