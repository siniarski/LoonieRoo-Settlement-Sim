// Rooms: flood fill over floor tiles, with walls and doors as boundaries.
// A region touching the map edge is outdoors, not a room.
//
// Rooms are derived from the grid, not stored: getRoomMap memoises per Grid
// and walkabilityVersion (every wall/door change bumps it), so they are
// recomputed only when walls or doors change.
//
// The player's room purposes are stored as designations anchored to a tile.
// A designation applies to whatever room contains its anchor, so purposes
// survive walls being moved around. If two designations end up in one room,
// the older one wins (reconcileRoomDesignations drops the newer).

import { getTile, type Point , EDGE_BARRICADE, EDGE_DOOR, EDGE_NONE, EDGE_WALL, edgeBetween, isWindow, takeChanges } from './grid';
import type { ItemKind } from './items';
import type { SimState } from './state';

export type RoomPurpose = 'housing' | 'storage' | 'armory' | 'messhall' | 'recroom' | 'infirmary' | 'barracks' | 'workshop' | 'graveyard' | 'greenhouse' | 'cell';
export const ROOM_PURPOSES: readonly RoomPurpose[] = ['housing', 'storage', 'armory', 'messhall', 'recroom', 'infirmary', 'barracks', 'workshop', 'graveyard', 'greenhouse', 'cell'];

export interface RoomDesignation {
  id: string;
  /** Anchor tile. The designation applies to the room containing it. */
  x: number;
  y: number;
  purpose: RoomPurpose;
  /** Storage rooms: kinds of thing not wanted here (the stockpile filter). */
  refuses?: StoreCategory[];
}

/** What storage rooms can be told to take or refuse. */
export type StoreCategory = 'food' | 'materials' | 'medical' | 'gear';
export const STORE_CATEGORIES: readonly StoreCategory[] = ['food', 'materials', 'medical', 'gear'];

export function categoryOf(kind: ItemKind): StoreCategory | null {
  if (kind === 'food' || kind === 'meal') return 'food';
  if (kind === 'wood' || kind === 'cloth' || kind === 'scrap') return 'materials';
  if (kind === 'bandage' || kind === 'medicine') return 'medical';
  return kind === 'corpse' ? null : 'gear';
}

export interface Room {
  /** Index into RoomMap.rooms. Stable only until walls or doors change. */
  index: number;
  /** Tile indices (y * width + x), in scan order. */
  tiles: number[];
  /** Door tiles on the room's boundary. */
  doors: Point[];
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

/** Label for floor tiles that are outside: see computeRooms. */
export const OUTDOORS = -1;
/**
 * A region of floor bigger than this is outdoors, however it is bounded. With
 * "touches water", this keeps "outdoors" from depending on the map edge, which a
 * big world can't afford to flood-fill out to.
 */
export const MAX_ROOM_TILES = 600;
/** Label for walls and doors. */
export const NOT_FLOOR = -2;

export interface RoomMap {
  /** Per tile: room index, OUTDOORS, or NOT_FLOOR. */
  labels: Int32Array;
  rooms: Room[];
}

const DIRS: readonly [number, number][] = [
  [0, -1],
  [1, 0],
  [0, 1],
  [-1, 0],
];

const cache = new WeakMap<SimState['grid'], { version: number; map: RoomMap }>();

/**
 * Memoised per grid + walkabilityVersion. Derived data, outside sim state. When walls change, the
 * rooms are updated round the changed tiles (updateRooms) if the grid knows which they were
 * (grid.ts takeChanges), else worked out in full. Either way the result is the same.
 */
export function getRoomMap(state: SimState): RoomMap {
  const cached = cache.get(state.grid);
  if (cached && cached.version === state.walkabilityVersion) return cached.map;
  const changed = takeChanges(state.grid);
  const map = cached && changed ? updateRooms(state.grid, cached.map, changed) : computeRooms(state.grid);
  cache.set(state.grid, { version: state.walkabilityVersion, map });
  return map;
}

export function computeRooms(grid: SimState['grid']): RoomMap {
  const { width, height, tiles } = grid;
  const labels = new Int32Array(width * height).fill(NOT_FLOOR);
  const UNSEEN = -3;
  for (let i = 0; i < tiles.length; i++) if (tiles[i] === 'floor') labels[i] = UNSEEN;

  const rooms: Room[] = [];
  for (let start = 0; start < labels.length; start++) {
    if (labels[start] !== UNSEEN) continue;
    // Flood fill one region. Label it temporarily, then decide room vs outdoors.
    const region: number[] = [start];
    const doorSet = new Set<number>();
    labels[start] = -4;
    // Outdoors: touches the map edge or water, or is too big to be a room.
    let touchesEdge = false;
    for (let head = 0; head < region.length; head++) {
      const i = region[head]!;
      const x = i % width;
      const y = Math.floor(i / width);
      if (x === 0 || y === 0 || x === width - 1 || y === height - 1) touchesEdge = true;
      for (const [dx, dy] of DIRS) {
        const nx = x + dx;
        const ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
        const n = ny * width + nx;
        // An edge wall bounds the room; an edge door does too, and counts as its door.
        const edge = grid.edges[edgeBetween(grid, x, y, nx, ny)] ?? EDGE_NONE;
        if (edge === EDGE_WALL || edge === EDGE_BARRICADE || isWindow(edge)) continue;
        if (edge === EDGE_DOOR) {
          doorSet.add(n);
          continue;
        }
        if (tiles[n] === 'door') doorSet.add(n);
        if (tiles[n] === 'water') touchesEdge = true;
        if (labels[n] !== UNSEEN) continue;
        labels[n] = -4;
        region.push(n);
      }
    }
    if (touchesEdge || region.length > MAX_ROOM_TILES) {
      for (const i of region) labels[i] = OUTDOORS;
      continue;
    }
    const index = rooms.length;
    let minX = width;
    let minY = height;
    let maxX = 0;
    let maxY = 0;
    for (const i of region) {
      labels[i] = index;
      const x = i % width;
      const y = Math.floor(i / width);
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x);
      maxY = Math.max(maxY, y);
    }
    region.sort((a, b) => a - b);
    const doors = [...doorSet].sort((a, b) => a - b).map((i) => ({ x: i % width, y: Math.floor(i / width) }));
    rooms.push({ index, tiles: region, doors, minX, minY, maxX, maxY });
  }
  return { labels, rooms };
}

export function labelAt(state: SimState, x: number, y: number): number {
  if (x < 0 || y < 0 || x >= state.grid.width || y >= state.grid.height) return NOT_FLOOR;
  return getRoomMap(state).labels[y * state.grid.width + x]!;
}

/** The enclosed room containing (x, y), or null for outdoors/walls/doors. */
export function roomAt(state: SimState, x: number, y: number): Room | null {
  const label = labelAt(state, x, y);
  return label >= 0 ? getRoomMap(state).rooms[label]! : null;
}

export function isIndoors(state: SimState, x: number, y: number): boolean {
  return labelAt(state, x, y) >= 0;
}

/** The designation that applies to a room (first in state order), if any. */
export function designationOf(state: SimState, room: Room): RoomDesignation | undefined {
  return state.roomDesignations.find((d) => labelAt(state, d.x, d.y) === room.index);
}

/** Keeps anchors on floor and at most one designation per room. Runs every tick (cheap). */
export function reconcileRoomDesignations(state: SimState): void {
  for (const d of state.roomDesignations) {
    if (getTile(state.grid, d.x, d.y) === 'floor') continue;
    // The anchor got built over: slide it to an adjacent floor tile (N, E, S, W).
    const next = DIRS.map(([dx, dy]) => ({ x: d.x + dx, y: d.y + dy })).find(
      (p) => getTile(state.grid, p.x, p.y) === 'floor',
    );
    if (next) {
      d.x = next.x;
      d.y = next.y;
    }
  }
  const claimed = new Set<number>();
  state.roomDesignations = state.roomDesignations.filter((d) => {
    if (getTile(state.grid, d.x, d.y) !== 'floor') return false; // nowhere to go
    const label = labelAt(state, d.x, d.y);
    if (label < 0) return true; // outdoors: kept, shown as "not enclosed"
    if (claimed.has(label)) return false; // rooms merged: older designation wins
    claimed.add(label);
    return true;
  });
}

// --- Requirements -----------------------------------------------------------

export interface Requirement {
  label: string;
  met: boolean;
}

export interface RoomStatus {
  purpose: RoomPurpose;
  complete: boolean;
  requirements: Requirement[];
  beds: number;
  tables: number;
  size: number;
}

/** Tiles of floor per bed for housing not to be cramped. */
export const TILES_PER_BED = 4;
export const MIN_STORAGE_TILES = 4;

/** Requirements for a purpose. `room` is null when the anchor is outdoors. */
export function evaluateRoom(state: SimState, room: Room | null, purpose: RoomPurpose): RoomStatus {
  const size = room?.tiles.length ?? 0;
  const count = (kind: string) => (room ? state.furniture.filter((f) => f.kind === kind && labelAt(state, f.x, f.y) === room.index).length : 0);
  const beds = count('bed');
  const tables = count('table');
  const requirements: Requirement[] = [
    { label: 'enclosed by walls', met: room !== null },
    { label: 'has a door', met: (room?.doors.length ?? 0) > 0 },
  ];
  if (purpose === 'housing' || purpose === 'barracks') {
    requirements.push({ label: 'at least 1 bed', met: beds >= 1 });
    requirements.push({ label: `${TILES_PER_BED} tiles per bed`, met: beds === 0 || size >= beds * TILES_PER_BED });
  } else if (purpose === 'infirmary') {
    requirements.push({ label: 'at least 1 bed', met: beds >= 1 });
  } else if (purpose === 'workshop') {
    requirements.push({ label: 'a workbench', met: count('workbench') >= 1 });
  } else if (purpose === 'messhall') {
    requirements.push({ label: 'at least 1 table', met: tables >= 1 });
  } else {
    requirements.push({ label: `at least ${MIN_STORAGE_TILES} tiles`, met: size >= MIN_STORAGE_TILES });
  }
  return { purpose, complete: requirements.every((r) => r.met), requirements, beds, tables, size };
}

/** Status for a designation (finds its room). */
export function designationStatus(state: SimState, d: RoomDesignation): RoomStatus {
  return evaluateRoom(state, roomAt(state, d.x, d.y), d.purpose);
}

/** Room indices of complete rooms with this purpose. */
export function completeRooms(state: SimState, purpose: RoomPurpose): Set<number> {
  const result = new Set<number>();
  for (const d of state.roomDesignations) {
    if (d.purpose !== purpose) continue;
    const room = roomAt(state, d.x, d.y);
    if (room && evaluateRoom(state, room, purpose).complete) result.add(room.index);
  }
  return result;
}

/** Complete rooms that loose items get hauled to. */
export interface StoreRooms {
  storage: Set<number>;
  armory: Set<number>;
  graveyard: Set<number>;
  /** Storage rooms that take each category (the stockpile filters). */
  storageFor: Record<StoreCategory, Set<number>>;
}

export function completeStoreRooms(state: SimState): StoreRooms {
  const storage = completeRooms(state, 'storage');
  const refusing = (c: StoreCategory) =>
    new Set(state.roomDesignations.filter((d) => d.purpose === 'storage' && d.refuses?.includes(c)).map((d) => labelAt(state, d.x, d.y)));
  const storageFor = Object.fromEntries(
    STORE_CATEGORIES.map((c) => {
      const no = refusing(c);
      return [c, new Set([...storage].filter((r) => !no.has(r)))];
    }),
  ) as Record<StoreCategory, Set<number>>;
  return { storage, armory: completeRooms(state, 'armory'), graveyard: completeRooms(state, 'graveyard'), storageFor };
}

/** Where an item belongs: the dead in a graveyard; weapons, guns and ammo in an armory if there is one, everything else (and those otherwise) in storage that takes it. */
export function homeFor(rooms: StoreRooms, kind: ItemKind): Set<number> {
  if (kind === 'corpse') return rooms.graveyard; // the dead go nowhere else
  if ((kind === 'weapon' || kind === 'gun' || kind === 'ammo') && rooms.armory.size > 0) return rooms.armory;
  return rooms.storageFor[categoryOf(kind) ?? 'gear'];
}

/**
 * Rooms after some tiles changed: the rooms touching a changed tile (or its neighbours) are taken
 * apart, and their tiles and the changed ones flooded again; a flood may run into outdoor tiles
 * (a new wall can close off part of the outdoors), and stops as outdoors once it's bigger than a
 * room can be or reaches the map's edge or water. Rooms are then ordered as computeRooms orders
 * them (by their first tile), so the result is the same as working it all out again.
 */
export function updateRooms(grid: SimState['grid'], old: RoomMap, changed: readonly number[]): RoomMap {
  const { width, height, tiles } = grid;
  const labels = old.labels.slice();
  const UNSEEN = -3;
  const removed = new Set<number>();
  const seeds: number[] = [];
  const near = (i: number) => {
    const x = i % width;
    const y = Math.floor(i / width);
    const out = [i];
    for (const [dx, dy] of DIRS) {
      const nx = x + dx;
      const ny = y + dy;
      if (nx >= 0 && ny >= 0 && nx < width && ny < height) out.push(ny * width + nx);
    }
    return out;
  };
  for (const c of changed) {
    if (c < 0 || c >= labels.length) continue;
    for (const j of near(c)) {
      if (labels[j]! >= 0) removed.add(labels[j]!);
      seeds.push(j);
    }
  }
  for (const r of removed) for (const t of old.rooms[r]!.tiles) seeds.push(t);
  for (const j of seeds) labels[j] = tiles[j] === 'floor' ? UNSEEN : NOT_FLOOR;
  const fresh: Room[] = [];
  for (const start of seeds) {
    if (labels[start] !== UNSEEN) continue;
    // Flood: unseen and outdoor floor; outdoors once it's too big or touches the edge or water.
    const region: number[] = [start];
    const inRegion = new Set<number>([start]);
    const doorSet = new Set<number>();
    let outdoors = false;
    for (let head = 0; head < region.length; head++) {
      if (region.length > MAX_ROOM_TILES) {
        outdoors = true;
        break;
      }
      const i = region[head]!;
      const x = i % width;
      const y = Math.floor(i / width);
      if (x === 0 || y === 0 || x === width - 1 || y === height - 1) outdoors = true;
      for (const [dx, dy] of DIRS) {
        const nx = x + dx;
        const ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
        const n = ny * width + nx;
        const edge = grid.edges[edgeBetween(grid, x, y, nx, ny)] ?? EDGE_NONE;
        if (edge === EDGE_WALL || edge === EDGE_BARRICADE || isWindow(edge)) continue;
        if (edge === EDGE_DOOR) {
          doorSet.add(n);
          continue;
        }
        if (tiles[n] === 'door') doorSet.add(n);
        if (tiles[n] === 'water') outdoors = true;
        if (tiles[n] !== 'floor' || inRegion.has(n)) continue;
        if (labels[n] !== UNSEEN && labels[n] !== OUTDOORS) continue;
        inRegion.add(n);
        region.push(n);
      }
    }
    if (outdoors) {
      for (const i of region) labels[i] = OUTDOORS;
      continue;
    }
    region.sort((a, b) => a - b);
    let minX = width;
    let minY = height;
    let maxX = 0;
    let maxY = 0;
    for (const i of region) {
      labels[i] = -4; // placed below
      const x = i % width;
      const y = Math.floor(i / width);
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x);
      maxY = Math.max(maxY, y);
    }
    const doors = [...doorSet].sort((a, b) => a - b).map((i) => ({ x: i % width, y: Math.floor(i / width) }));
    fresh.push({ index: -1, tiles: region, doors, minX, minY, maxX, maxY });
  }
  // Rooms in computeRooms' order (by first tile), numbered, labelled.
  const rooms = [...old.rooms.filter((r) => !removed.has(r.index)), ...fresh].sort((a, b) => a.tiles[0]! - b.tiles[0]!);
  const out: Room[] = rooms.map((r, index) => ({ ...r, index }));
  for (const r of out) for (const i of r.tiles) labels[i] = r.index;
  return { labels, rooms: out };
}
