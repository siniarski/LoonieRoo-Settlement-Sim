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

import { getTile, type Point } from './grid';
import type { ItemKind } from './items';
import type { SimState } from './state';

export type RoomPurpose = 'housing' | 'storage' | 'armory' | 'messhall' | 'recroom' | 'infirmary' | 'barracks' | 'workshop';
export const ROOM_PURPOSES: readonly RoomPurpose[] = ['housing', 'storage', 'armory', 'messhall', 'recroom', 'infirmary', 'barracks', 'workshop'];

export interface RoomDesignation {
  id: string;
  /** Anchor tile. The designation applies to the room containing it. */
  x: number;
  y: number;
  purpose: RoomPurpose;
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

/** Memoised per grid + walkabilityVersion. Derived data, outside sim state. */
export function getRoomMap(state: SimState): RoomMap {
  const cached = cache.get(state.grid);
  if (cached && cached.version === state.walkabilityVersion) return cached.map;
  const map = computeRooms(state.grid);
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
}

export function completeStoreRooms(state: SimState): StoreRooms {
  return { storage: completeRooms(state, 'storage'), armory: completeRooms(state, 'armory') };
}

/** Where an item belongs: weapons in an armory if there is one, everything else (and weapons otherwise) in storage. */
export function homeFor(rooms: StoreRooms, kind: ItemKind): Set<number> {
  return kind === 'weapon' && rooms.armory.size > 0 ? rooms.armory : rooms.storage;
}
