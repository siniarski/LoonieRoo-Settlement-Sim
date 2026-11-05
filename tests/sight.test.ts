import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { SNEAK_ATTACK_PCT } from '../src/sim/control';
import { isExplored } from '../src/sim/fog';
import { edgeBetween, type Side } from '../src/sim/grid';
import { centre } from '../src/sim/motion';
import { loadScenario, type Scenario } from '../src/sim/scenario';
import { NIGHT_SIGHT, RUN_HEARD, SIGHT_RADIUS, SNEAK_HEARD, WALK_HEARD, ZOMBIE_SIGHT, heardFrom, sightLine, visibleNow, zombieSees } from '../src/sim/sight';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, isNight } from '../src/sim/time';
import { addZombie } from '../src/sim/zombies';

/** A room inside (1, 1)..(3, 3), walled on its edges, glass in the middle of its east wall, a door south. */
function room(): NonNullable<Scenario['edges']> {
  const edges: NonNullable<Scenario['edges']> = [];
  const add = (x: number, y: number, side: Side, kind: 'wall' | 'window' | 'door' = 'wall') => edges.push({ x, y, side, kind });
  for (let x = 1; x <= 3; x++) {
    add(x, 1, 'n');
    add(x, 4, 'n', x === 2 ? 'door' : 'wall');
  }
  for (let y = 1; y <= 3; y++) {
    add(1, y, 'w');
    add(4, y, 'w', y === 2 ? 'window' : 'wall');
  }
  return edges;
}
const ROW = '.'.repeat(20);
const MAP = Array.from({ length: 8 }, () => ROW);
const NOON = DAY_LENGTH / 2;
const quiet = (state: ReturnType<typeof loadScenario>) => {
  for (const s of state.survivors) for (const k of Object.keys(s.priorities)) s.priorities[k as keyof typeof s.priorities] = 0;
};

describe('line of sight', () => {
  it('walls and shut doors block it, glass does not, boards do', () => {
    const state = loadScenario({ seed: 1, map: MAP, edges: room() });
    const g = state.grid;
    expect(sightLine(g, { x: 2, y: 2 }, { x: 8, y: 2 })).toBe(true); // out through the glass
    expect(sightLine(g, { x: 2, y: 1 }, { x: 8, y: 1 })).toBe(false); // a wall
    expect(sightLine(g, { x: 2, y: 3 }, { x: 2, y: 7 })).toBe(false); // the shut door
    g.edgeOpen.push(edgeBetween(g, 2, 3, 2, 4));
    expect(sightLine(g, { x: 2, y: 3 }, { x: 2, y: 7 })).toBe(true);
    g.edgeBoarded.push(edgeBetween(g, 3, 2, 4, 2));
    expect(sightLine(g, { x: 2, y: 2 }, { x: 8, y: 2 })).toBe(false);
  });

  it('a wall tile hides what is behind it, and is itself seen', () => {
    const state = loadScenario({ seed: 1, map: ['.....', '..#..', '.....'] });
    expect(sightLine(state.grid, { x: 0, y: 1 }, { x: 2, y: 1 })).toBe(true);
    expect(sightLine(state.grid, { x: 0, y: 1 }, { x: 4, y: 1 })).toBe(false);
  });

  it('only what is in view is explored, and less of it at night', () => {
    const state = loadScenario({ seed: 1, map: MAP.map((r, y) => (y === 2 ? '..s' + r.slice(3) : r)), edges: room() });
    quiet(state);
    state.tick = NOON;
    state.grid.explored.fill(0);
    tick(state);
    expect(isExplored(state, 7, 2)).toBe(true); // through the glass
    expect(isExplored(state, 2, 0)).toBe(false); // behind the north wall
    expect(isExplored(state, 2, 6)).toBe(false); // behind the door
    const night = loadScenario({ seed: 1, map: MAP.map((r, y) => (y === 2 ? 's' + r.slice(1) : r)) });
    quiet(night);
    while (!isNight(night.tick)) night.tick += 10;
    night.grid.explored.fill(0);
    tick(night);
    expect(isExplored(night, NIGHT_SIGHT, 2)).toBe(true);
    expect(isExplored(night, NIGHT_SIGHT + 1, 2)).toBe(false);
    expect(NIGHT_SIGHT).toBeLessThan(SIGHT_RADIUS);
  });

  it('what you see now is your view alone while you are in control', () => {
    const state = loadScenario({ seed: 1, map: MAP.map((r, y) => (y === 2 ? '..s' + r.slice(3) : y === 7 ? 's' + r.slice(1) : r)), edges: room() });
    state.tick = NOON;
    applyCommand(state, { type: 'takeControl', survivorId: state.survivors[0]!.id });
    const seen = visibleNow(state);
    const at = (x: number, y: number) => seen[y * state.grid.width + x];
    expect(at(7, 2)).toBe(1);
    expect(at(0, 7)).toBe(0); // the other one stands there, but you can't see it
  });
});

describe('stealth', () => {
  it('the dead see you through glass but not through a wall, and only as far as they see', () => {
    const state = loadScenario({ seed: 1, map: MAP, edges: room() });
    state.tick = NOON;
    expect(zombieSees(state, { x: 9, y: 2 }, { x: 2, y: 2 })).toBe(true);
    expect(zombieSees(state, { x: 9, y: 1 }, { x: 2, y: 1 })).toBe(false);
    expect(zombieSees(state, { x: 19, y: 7 }, { x: 19 - ZOMBIE_SIGHT - 1, y: 7 })).toBe(false);
    // Sneaking, half as far.
    expect(zombieSees(state, { x: 19, y: 7 }, { x: 19 - ZOMBIE_SIGHT + 1, y: 7 })).toBe(true);
    expect(zombieSees(state, { x: 19, y: 7 }, { x: 19 - ZOMBIE_SIGHT + 1, y: 7 }, true)).toBe(false);
  });

  it('someone keeping still behind a wall goes unnoticed; walking past, they are heard', () => {
    const state = loadScenario({ seed: 1, map: MAP.map((r, y) => (y === 1 ? '..s' + r.slice(3) : r)), edges: room() });
    quiet(state);
    state.tick = NOON;
    const z = addZombie(state, 5, 0, 200, 'static'); // just outside the north-east corner
    for (let i = 0; i < 30; i++) tick(state);
    expect(z.targetId).toBeNull();
    // Now you walk about in there: it hears you.
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'takeControl', survivorId: s.id });
    applyCommand(state, { type: 'steer', dx: 0, dy: 1 });
    expect(heardFrom(state, s)).toBe(WALK_HEARD);
    for (let i = 0; i < 30 && z.targetId === null; i++) tick(state);
    expect(z.targetId).toBe(s.id);
  });

  it('sneaking is slower and quieter; running is louder', () => {
    const state = loadScenario({ seed: 1, map: MAP.map((r, y) => (y === 5 ? 's' + r.slice(1) : r)) });
    quiet(state);
    const s = state.survivors[0]!;
    applyCommand(state, { type: 'takeControl', survivorId: s.id });
    applyCommand(state, { type: 'steer', dx: 1, dy: 0, run: true });
    expect(heardFrom(state, s)).toBe(RUN_HEARD);
    expect(applyCommand(state, { type: 'sneak', on: true }).ok).toBe(true);
    expect(heardFrom(state, s)).toBe(SNEAK_HEARD);
    const walked = (sneak: boolean) => {
      const st = loadScenario({ seed: 1, map: MAP.map((r, y) => (y === 5 ? 's' + r.slice(1) : r)) });
      quiet(st);
      const me = st.survivors[0]!;
      applyCommand(st, { type: 'takeControl', survivorId: me.id });
      me.at = centre({ x: 0, y: 5 });
      applyCommand(st, { type: 'sneak', on: sneak });
      applyCommand(st, { type: 'steer', dx: 1, dy: 0 });
      for (let i = 0; i < 40; i++) tick(st);
      return me.at.x;
    };
    expect(walked(true)).toBeLessThan(walked(false));
  });

  it('a blow to one of the dead that never saw you coming does far more', () => {
    const blow = (aware: boolean) => {
      const state = loadScenario({ seed: 1, map: MAP.map((r, y) => (y === 5 ? '....s' + r.slice(5) : r)) });
      quiet(state);
      state.tick = NOON;
      const s = state.survivors[0]!;
      applyCommand(state, { type: 'takeControl', survivorId: s.id });
      const z = addZombie(state, 5, 5, 10000, 'static');
      z.targetId = aware ? s.id : null;
      z.nextThinkTick = state.tick + 1000;
      applyCommand(state, { type: 'swing', x: 5, y: 5 });
      return 10000 - z.health;
    };
    const sneak = blow(false);
    const open = blow(true);
    expect(open).toBeGreaterThan(0);
    expect(sneak).toBe(Math.round((open * SNEAK_ATTACK_PCT) / 100));
  });
});
