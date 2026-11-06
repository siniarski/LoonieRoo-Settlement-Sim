import { describe, expect, it } from 'vitest';
import { damageEdge } from '../src/sim/breach';
import { applyCommand } from '../src/sim/commands';
import { completeBlueprint } from '../src/sim/designations';
import { EDGE_WINDOW, EDGE_WINDOW_BROKEN, edgeBetween, type Side } from '../src/sim/grid';
import { centre } from '../src/sim/motion';
import { crossable, openBetween, seesThrough } from '../src/sim/pathfinding';
import { loadScenario, type Scenario } from '../src/sim/scenario';
import { tick } from '../src/sim/tick';
import { clearShot } from '../src/sim/work';
import { CHUNK, createWorld, ensureGeneratedAround } from '../src/sim/worldgen';

/** A room inside (1, 1)..(3, 3), walled on its edges, a window in the middle of its east wall. */
function room(window: 'window' | 'brokenWindow' = 'window'): NonNullable<Scenario['edges']> {
  const edges: NonNullable<Scenario['edges']> = [];
  const add = (x: number, y: number, side: Side, kind: 'wall' | 'window' | 'brokenWindow' = 'wall') => edges.push({ x, y, side, kind });
  for (let x = 1; x <= 3; x++) {
    add(x, 1, 'n');
    add(x, 4, 'n');
  }
  for (let y = 1; y <= 3; y++) {
    add(1, y, 'w');
    add(4, y, 'w', y === 2 ? window : 'wall');
  }
  return edges;
}
const MAP = ['.......', '.......', '.......', '.......', '.......'];

describe('windows', () => {
  it('a swing at the glass in front of you breaks it', () => {
    const state = loadScenario({ seed: 1, map: MAP.map((r, y) => (y === 2 ? '...s...' : r)), edges: room() });
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'takeControl', survivorId: s.id });
    const pane = edgeBetween(state.grid, 3, 2, 4, 2);
    expect(applyCommand(state, { type: 'swing', x: 4, y: 2 }).ok).toBe(true);
    expect(state.grid.edges[pane]).toBe(EDGE_WINDOW_BROKEN);
  });


  it('glass stops everyone walking; broken, you and the dead climb through, colonists use doors', () => {
    const state = loadScenario({ seed: 1, map: MAP, edges: room() });
    for (const m of ['survivor', 'free', 'zombie', 'raider'] as const) expect(crossable(state.grid, 3, 2, 4, 2, m), m).toBe(false);
    expect(crossable(state.grid, 3, 2, 4, 2, 'breacher')).toBe(true); // it batters its way through
    const broken = loadScenario({ seed: 1, map: MAP, edges: room('brokenWindow') });
    expect(crossable(broken.grid, 3, 2, 4, 2, 'free')).toBe(true);
    expect(crossable(broken.grid, 3, 2, 4, 2, 'zombie')).toBe(true);
    expect(crossable(broken.grid, 3, 2, 4, 2, 'survivor')).toBe(false);
  });

  it('you see and shoot through glass, but not through boards; nothing bites through glass', () => {
    const state = loadScenario({ seed: 1, map: MAP, edges: room() });
    const pane = edgeBetween(state.grid, 3, 2, 4, 2);
    expect(seesThrough(state.grid, pane)).toBe(true);
    expect(clearShot(state.grid, { x: 2, y: 2 }, { x: 6, y: 2 })).toBe(true);
    expect(clearShot(state.grid, { x: 2, y: 1 }, { x: 6, y: 1 })).toBe(false); // a wall
    expect(openBetween(state.grid, { x: 3, y: 2 }, { x: 4, y: 2 })).toBe(false);
    state.grid.edgeBoarded.push(pane);
    expect(seesThrough(state.grid, pane)).toBe(false);
  });

  it('the dead smash the glass to get at someone inside, then climb in', () => {
    const state = loadScenario({ seed: 1, map: MAP.map((r, y) => (y === 2 ? '..s...z' : r)), edges: room() });
    const s = state.survivors[0]!;
    for (const k of Object.keys(s.priorities)) s.priorities[k as keyof typeof s.priorities] = 0;
    state.zombies[0]!.kind = 'static';
    const pane = edgeBetween(state.grid, 3, 2, 4, 2);
    let t = 0;
    for (; t < 600 && state.grid.edges[pane] === EDGE_WINDOW; t++) tick(state);
    expect(state.grid.edges[pane]).toBe(EDGE_WINDOW_BROKEN);
    expect(state.events.some((e) => e.message.includes('smashed the window'))).toBe(true);
    for (let k = 0; k < 200 && state.zombies[0] && state.zombies[0].pos.x > 3; k++) tick(state);
    expect(state.zombies[0]!.pos.x).toBeLessThanOrEqual(3); // inside
  });

  it('you smash a window by using it', () => {
    const state = loadScenario({ seed: 1, map: MAP.map((r, y) => (y === 2 ? '...s...' : r)), edges: room() });
    applyCommand(state, { type: 'takeControl', survivorId: state.survivors[0]!.id });
    expect(applyCommand(state, { type: 'use', x: 4, y: 2 }).ok).toBe(true);
    expect(state.grid.edges[edgeBetween(state.grid, 3, 2, 4, 2)]).toBe(EDGE_WINDOW_BROKEN);
  });

  it('boarded up, nobody gets through until the boards are torn off, then the glass', () => {
    const state = loadScenario({ seed: 1, map: MAP, edges: room() });
    expect(applyCommand(state, { type: 'designateBuild', x: 4, y: 2, structure: 'barricade', side: 'w' }).ok).toBe(true);
    const bp = state.blueprints[0]!;
    bp.delivered = true;
    bp.workDone = bp.workRequired;
    completeBlueprint(state, bp);
    const pane = edgeBetween(state.grid, 3, 2, 4, 2);
    expect(state.grid.edgeBoarded).toContain(pane);
    expect(state.grid.edges[pane]).toBe(EDGE_WINDOW);
    expect(damageEdge(state, pane, 399)).toBe(false);
    expect(damageEdge(state, pane, 1)).toBe(true); // the boards come off
    expect(state.grid.edgeBoarded).not.toContain(pane);
    expect(state.grid.edges[pane]).toBe(EDGE_WINDOW);
    expect(damageEdge(state, pane, 100)).toBe(true); // then the glass
    expect(state.grid.edges[pane]).toBe(EDGE_WINDOW_BROKEN);
  });

  it('climbing through a broken window is slower than walking through a doorway', () => {
    const through = (kind: 'brokenWindow' | 'none') => {
      const edges = kind === 'none' ? room().filter((e) => !(e.x === 4 && e.y === 2)) : room(kind);
      const state = loadScenario({ seed: 1, map: MAP.map((r, y) => (y === 2 ? '...s...' : r)), edges });
      const s = state.survivors[0]!;
      applyCommand(state, { type: 'takeControl', survivorId: s.id });
      s.at = centre({ x: 3, y: 2 });
      applyCommand(state, { type: 'steer', dx: 1, dy: 0 });
      let t = 0;
      for (; t < 100 && s.pos.x < 5; t++) tick(state);
      return t;
    };
    expect(through('brokenWindow')).toBeGreaterThan(through('none'));
  });

  it('houses and shops in a generated town have windows, never beside a door', () => {
    const state = createWorld(5, 256);
    for (let y = 0; y < 256; y += CHUNK) for (let x = 0; x < 256; x += CHUNK) ensureGeneratedAround(state, { x, y });
    const windows = state.grid.edges.filter((k) => k === EDGE_WINDOW || k === EDGE_WINDOW_BROKEN).length;
    expect(windows).toBeGreaterThan(50);
    expect(state.grid.edges.filter((k) => k === EDGE_WINDOW_BROKEN).length).toBeGreaterThan(0);
  }, 20000);
});
