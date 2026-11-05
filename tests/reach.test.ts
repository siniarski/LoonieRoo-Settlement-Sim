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
