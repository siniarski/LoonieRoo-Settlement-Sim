import { describe, expect, it } from 'vitest';
import { EDGE_DOOR, type Grid, type Point } from '../src/sim/grid';
import { hashRoll } from '../src/sim/hash';
import { DIRS, canReach, crossable, isPassable, type Mover } from '../src/sim/pathfinding';
import { CHUNK, createWorld, ensureGeneratedAround } from '../src/sim/worldgen';

/** The plain answer: flood from `from` with the doors exactly as they are. */
function reference(grid: Grid, from: Point, goal: Point, mover: Mover): boolean {
  if (!isPassable(grid, from.x, from.y, mover) || !isPassable(grid, goal.x, goal.y, mover)) return false;
  const seen = new Uint8Array(grid.width * grid.height);
  const stack = [from];
  seen[from.y * grid.width + from.x] = 1;
  while (stack.length) {
    const p = stack.pop()!;
    if (p.x === goal.x && p.y === goal.y) return true;
    for (const d of DIRS) {
      const n = { x: p.x + d.x, y: p.y + d.y };
      const i = n.y * grid.width + n.x;
      if (n.x < 0 || n.y < 0 || n.x >= grid.width || n.y >= grid.height || seen[i] || !isPassable(grid, n.x, n.y, mover) || !crossable(grid, p.x, p.y, n.x, n.y, mover)) continue;
      seen[i] = 1;
      stack.push(n);
    }
  }
  return false;
}

describe('reachability with doors swinging', () => {
  it('joining pieces through open doors gives the same answers as flooding with the doors as they are', () => {
    const state = createWorld(11, 128);
    for (let y = 0; y < 128; y += CHUNK) for (let x = 0; x < 128; x += CHUNK) ensureGeneratedAround(state, { x, y });
    const { grid } = state;
    const doorTiles = grid.tiles.flatMap((t, i) => (t === 'door' ? [i] : []));
    const edgeDoors = grid.edges.flatMap((k, e) => (k === EDGE_DOOR ? [e] : []));
    expect(edgeDoors.length).toBeGreaterThan(10);
    const floor = grid.tiles.flatMap((t, i) => (t === 'floor' ? [i] : []));
    const at = (i: number) => ({ x: i % grid.width, y: Math.floor(i / grid.width) });
    let checked = 0;
    let reachable = 0;
    for (let round = 0; round < 6; round++) {
      // Doors open and shut without the version changing, as when they're held for someone passing.
      grid.open = doorTiles.filter((i) => hashRoll(i, round, 1) < 0.5);
      grid.edgeOpen = edgeDoors.filter((e) => hashRoll(e, round, 2) < 0.5);
      for (const mover of ['zombie', 'free', 'crawler', 'survivor'] as const) {
        for (let k = 0; k < 40; k++) {
          const a = at(floor[Math.floor(hashRoll(round, k, 3) * floor.length)]!);
          // Near pairs mostly (so some are inside buildings, behind doors), some far.
          const b = k % 4 === 0 ? at(floor[Math.floor(hashRoll(round, k, 4) * floor.length)]!) : { x: Math.min(127, Math.max(0, a.x + Math.floor(hashRoll(round, k, 5) * 13) - 6)), y: Math.min(127, Math.max(0, a.y + Math.floor(hashRoll(round, k, 6) * 13) - 6)) };
          const want = reference(grid, a, b, mover);
          expect(canReach(grid, state.walkabilityVersion, a, [b], mover), `${mover} ${a.x},${a.y} -> ${b.x},${b.y}`).toBe(want);
          checked++;
          if (want) reachable++;
        }
      }
    }
    expect(checked).toBe(6 * 4 * 40);
    expect(reachable).toBeGreaterThan(50);
    expect(reachable).toBeLessThan(checked);
  }, 30000);
});

describe('reachability, chunked, as the map changes', () => {
  it('re-flooding only the changed chunks gives the same answers as one flood of the whole map', async () => {
    const { setTile, setEdge, EDGE_WALL, EDGE_NONE, EDGE_WINDOW, EDGE_BARRICADE } = await import('../src/sim/grid');
    const { applyCommand } = await import('../src/sim/commands');
    const state = createWorld(12, 128);
    for (let y = 0; y < 128; y += CHUNK) for (let x = 0; x < 128; x += CHUNK) ensureGeneratedAround(state, { x, y });
    const { grid } = state;
    const at = (i: number) => ({ x: i % grid.width, y: Math.floor(i / grid.width) });
    const kinds = [EDGE_WALL, EDGE_NONE, EDGE_DOOR, EDGE_WINDOW, EDGE_BARRICADE] as const;
    let checked = 0;
    for (let round = 0; round < 12; round++) {
      // Change things here and there (some on chunk borders: x or y a multiple of 32), as the sim does, and bump the version.
      for (let k = 0; k < 25; k++) {
        const x = Math.floor(hashRoll(round, k, 11) * 128);
        const y = Math.floor(hashRoll(round, k, 12) * 128);
        const bx = k % 5 === 0 ? 32 * (1 + Math.floor(hashRoll(round, k, 13) * 3)) : x;
        const roll = hashRoll(round, k, 14);
        if (roll < 0.3) setTile(grid, bx, y, roll < 0.15 ? 'wall' : 'floor');
        else if (roll < 0.8) setEdge(grid, bx, y, roll < 0.55 ? 'n' : 'w', kinds[Math.floor(hashRoll(round, k, 15) * kinds.length)]!);
        else if (grid.tiles[y * 128 + x] === 'door') applyCommand(state, { type: 'toggleLock', x, y });
        else setTile(grid, x, y, 'door');
      }
      // And something that matters: a small room boxed in with edge walls (sometimes across a chunk
      // border), asked about from inside; then, every other round, one wall made a door.
      const cx = round % 3 === 0 ? 31 + (round % 2) : 10 + Math.floor(hashRoll(round, 31) * 100);
      const cy = 10 + Math.floor(hashRoll(round, 32) * 100);
      for (let x = cx; x <= cx + 2; x++) for (let y = cy; y <= cy + 2; y++) setTile(grid, x, y, 'floor');
      for (let x = cx; x <= cx + 2; x++) {
        setEdge(grid, x, cy, 'n', EDGE_WALL);
        setEdge(grid, x, cy + 3, 'n', round % 2 === 1 && x === cx + 1 ? EDGE_NONE : EDGE_WALL);
      }
      for (let y = cy; y <= cy + 2; y++) {
        setEdge(grid, cx, y, 'w', EDGE_WALL);
        setEdge(grid, cx + 3, y, 'w', EDGE_WALL);
      }
      state.walkabilityVersion += 1;
      for (const mover of ['zombie', 'survivor', 'breacher'] as const) {
        for (const out of [{ x: cx + 1, y: cy + 6 }, { x: cx - 4, y: cy + 1 }]) {
          expect(canReach(grid, state.walkabilityVersion, { x: cx + 1, y: cy + 1 }, [out], mover), `room round ${round} ${mover}`).toBe(reference(grid, { x: cx + 1, y: cy + 1 }, out, mover));
          checked++;
        }
      }
      const floor = grid.tiles.flatMap((t, i) => (t === 'floor' || t === 'door' ? [i] : []));
      for (const mover of ['zombie', 'survivor', 'raider', 'free', 'breacher'] as const) {
        for (let k = 0; k < 30; k++) {
          const a = at(floor[Math.floor(hashRoll(round, k, 21) * floor.length)]!);
          const b = k % 3 === 0 ? at(floor[Math.floor(hashRoll(round, k, 22) * floor.length)]!) : { x: Math.min(127, Math.max(0, a.x + Math.floor(hashRoll(round, k, 23) * 41) - 20)), y: Math.min(127, Math.max(0, a.y + Math.floor(hashRoll(round, k, 24) * 41) - 20)) };
          expect(canReach(grid, state.walkabilityVersion, a, [b], mover), `round ${round} ${mover} ${a.x},${a.y} -> ${b.x},${b.y}`).toBe(reference(grid, a, b, mover));
          checked++;
        }
      }
    }
    expect(checked).toBe(12 * 5 * 30 + 12 * 6);
  }, 60000);
});
