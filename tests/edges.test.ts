import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { EDGE_DOOR, edgeBetween, edgeIsOpen, type Side } from '../src/sim/grid';
import { centre, fits, slide, SUB } from '../src/sim/motion';
import { canReach, findPath } from '../src/sim/pathfinding';
import { getRoomMap } from '../src/sim/rooms';
import { loadFile, toSaveFile } from '../src/sim/savefile';
import { loadScenario, type Scenario } from '../src/sim/scenario';
import { tick } from '../src/sim/tick';
import { clearShot } from '../src/sim/work';

/** A room inside (x0, y0)..(x1, y1) walled on its edges, with an edge door in the middle of its south side. */
function box(x0: number, y0: number, x1: number, y1: number): NonNullable<Scenario['edges']> {
  const edges: NonNullable<Scenario['edges']> = [];
  const add = (x: number, y: number, side: Side, kind: 'wall' | 'door' = 'wall') => edges.push({ x, y, side, kind });
  const doorX = Math.floor((x0 + x1) / 2);
  for (let x = x0; x <= x1; x++) {
    add(x, y0, 'n');
    add(x, y1 + 1, 'n', x === doorX ? 'door' : 'wall');
  }
  for (let y = y0; y <= y1; y++) {
    add(x0, y, 'w');
    add(x1 + 1, y, 'w');
  }
  return edges;
}

const MAP = ['.......', '.......', '.......', '.......', '.......', '.......', '.......'];

describe('walls on tile edges', () => {
  it('paths go round an edge wall and in through the edge door', () => {
    const state = loadScenario({ seed: 1, map: MAP, edges: box(1, 1, 3, 3) });
    // From inside to outside: out through the door at the south (2, 3) -> (2, 4).
    const path = findPath(state.grid, { x: 1, y: 1 }, { x: 0, y: 1 })!;
    expect(path.some((p) => p.x === 2 && p.y === 4)).toBe(true);
    expect(path.length).toBeGreaterThan(4); // not straight through the west wall
    // A zombie can't get in while the door is shut.
    expect(canReach(state.grid, state.walkabilityVersion, { x: 0, y: 0 }, [{ x: 2, y: 2 }], 'zombie')).toBe(false);
    state.grid.edgeOpen.push(edgeBetween(state.grid, 2, 3, 2, 4));
    state.walkabilityVersion++;
    expect(canReach(state.grid, state.walkabilityVersion, { x: 0, y: 0 }, [{ x: 2, y: 2 }], 'zombie')).toBe(true);
  });

  it('an edge-walled room is a room, its edge door its door', () => {
    const state = loadScenario({ seed: 1, map: MAP, edges: box(1, 1, 3, 3) });
    const map = getRoomMap(state);
    const room = map.rooms.find((r) => r.tiles.includes(2 * 7 + 2))!;
    expect(room).toBeDefined();
    expect(room.tiles).toHaveLength(9);
    expect(room.doors).toHaveLength(1);
  });

  it("a body can't overlap an edge wall: you slide along it", () => {
    const state = loadScenario({ seed: 1, map: MAP, edges: box(1, 1, 3, 3) });
    const inside = centre({ x: 1, y: 2 });
    expect(fits(state.grid, inside, 'free')).toBe(true);
    const moved = slide(state.grid, inside, -SUB, 0, 'free'); // into the west wall
    expect(moved.x).toBeGreaterThanOrEqual(1 * SUB + 77);
    expect(fits(state.grid, { x: 1 * SUB, y: inside.y }, 'free')).toBe(false); // centred on the wall line
  });

  it('a colonist opens the edge door going through, and it shuts behind them', () => {
    const state = loadScenario({ seed: 1, map: MAP.map((r, y) => (y === 2 ? '..s....' : r)), edges: box(1, 1, 3, 3) });
    const s = state.survivors[0]!;
    for (const k of Object.keys(s.priorities)) s.priorities[k as keyof typeof s.priorities] = 0;
    const door = edgeBetween(state.grid, 2, 3, 2, 4);
    expect(state.grid.edges[door]).toBe(EDGE_DOOR);
    applyCommand(state, { type: 'moveSurvivor', survivorId: s.id, x: 2, y: 6 });
    let wasOpen = false;
    for (let t = 0; t < 60; t++) {
      tick(state);
      if (edgeIsOpen(state.grid, door)) wasOpen = true;
    }
    expect(s.pos).toEqual({ x: 2, y: 6 });
    expect(wasOpen).toBe(true);
    expect(edgeIsOpen(state.grid, door)).toBe(false);
  });

  it('you open and shut an edge door by using the tile across it', () => {
    const state = loadScenario({ seed: 1, map: MAP.map((r, y) => (y === 3 ? '..s....' : r)), edges: box(1, 1, 3, 3) });
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'takeControl', survivorId: s.id });
    const door = edgeBetween(state.grid, 2, 3, 2, 4);
    expect(applyCommand(state, { type: 'use', x: 2, y: 4 }).ok).toBe(true);
    expect(edgeIsOpen(state.grid, door)).toBe(true);
    expect(applyCommand(state, { type: 'use', x: 2, y: 4 }).ok).toBe(true);
    expect(edgeIsOpen(state.grid, door)).toBe(false);
  });

  it('nothing shoots through an edge wall', () => {
    const state = loadScenario({ seed: 1, map: MAP, edges: box(1, 1, 3, 3) });
    expect(clearShot(state.grid, { x: 0, y: 2 }, { x: 5, y: 2 })).toBe(false);
    expect(clearShot(state.grid, { x: 0, y: 5 }, { x: 5, y: 5 })).toBe(true);
  });

  it('edges survive a save and load', () => {
    const state = loadScenario({ seed: 1, map: MAP, edges: box(1, 1, 3, 3) });
    const back = loadFile(toSaveFile(state));
    expect(back.ok && back.state.grid.edges).toEqual(state.grid.edges);
  });
});
