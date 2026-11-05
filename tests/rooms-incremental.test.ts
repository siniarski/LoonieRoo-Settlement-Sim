import { describe, expect, it } from 'vitest';
import { hashRoll } from '../src/sim/hash';
import { EDGE_DOOR, EDGE_NONE, EDGE_WALL, setEdge, setTile } from '../src/sim/grid';
import { computeRooms, getRoomMap } from '../src/sim/rooms';
import { CHUNK, createWorld, ensureGeneratedAround } from '../src/sim/worldgen';

describe('rooms worked out round a change', () => {
  it('match rooms worked out from scratch, change after change', () => {
    const state = createWorld(5, 128);
    for (let y = 0; y < 128; y += CHUNK) for (let x = 0; x < 128; x += CHUNK) ensureGeneratedAround(state, { x, y });
    getRoomMap(state); // the first is worked out in full
    for (let n = 0; n < 120; n++) {
      const r = (k: number) => hashRoll(n, k, 0x900d);
      // Near a room most of the time (a random border round a random room), else anywhere.
      const rooms = getRoomMap(state).rooms;
      const room = rooms.length > 0 && r(0) < 0.7 ? rooms[Math.floor(r(1) * rooms.length)] : undefined;
      const x = room ? room.minX + Math.floor(r(2) * (room.maxX - room.minX + 2)) : 1 + Math.floor(r(2) * 126);
      const y = room ? room.minY + Math.floor(r(3) * (room.maxY - room.minY + 2)) : 1 + Math.floor(r(3) * 126);
      const what = r(4);
      if (what < 0.4) setEdge(state.grid, x, y, r(5) < 0.5 ? 'n' : 'w', EDGE_NONE);
      else if (what < 0.65) setEdge(state.grid, x, y, r(5) < 0.5 ? 'n' : 'w', EDGE_WALL);
      else if (what < 0.8) setEdge(state.grid, x, y, r(5) < 0.5 ? 'n' : 'w', EDGE_DOOR);
      else if (what < 0.9) setTile(state.grid, x, y, 'wall');
      else setTile(state.grid, x, y, 'floor');
      state.walkabilityVersion++;
      const fast = getRoomMap(state);
      const full = computeRooms(state.grid);
      expect(Array.from(fast.labels), `change ${n} at ${x},${y}`).toEqual(Array.from(full.labels));
      expect(fast.rooms.map((q) => [q.index, q.tiles, q.doors]), `change ${n}`).toEqual(full.rooms.map((q) => [q.index, q.tiles, q.doors]));
    }
  });
});
