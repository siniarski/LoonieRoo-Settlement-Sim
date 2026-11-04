import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { BODY_RADIUS, SUB, centre } from '../src/sim/motion';
import { SATIETY_MAX } from '../src/sim/food';
import { HYDRATION_MAX } from '../src/sim/water';
import { spawnItem } from '../src/sim/items';
import { ENTITY_CHARS } from '../src/sim/scenario';

const WELL = (ENTITY_CHARS as Record<string, string>).well!;
import { checkInvariants } from '../src/sim/invariants';
import { replay } from '../src/sim/replay';
import { loadScenario } from '../src/sim/scenario';
import { cloneState, type SimState } from '../src/sim/state';
import { TICKS_PER_TILE } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';

function run(state: SimState, ticks: number): void {
  for (let t = 0; t < ticks; t++) {
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
}

const cmd = (state: SimState, c: Parameters<typeof applyCommand>[1]) => {
  const r = applyCommand(state, c);
  expect(r, JSON.stringify(c)).toMatchObject({ ok: true });
  return r;
};

describe('direct control', () => {
  it('taking over a survivor stops their work; handing back lets them pick it up again', () => {
    const state = loadScenario({ seed: 1, map: ['sw...', '..+..'] });
    const s = state.survivors[0]!;
    run(state, 2);
    expect(s.jobId).not.toBeNull();
    cmd(state, { type: 'takeControl', survivorId: s.id });
    expect(s.jobId).toBeNull();
    run(state, 50);
    expect(s.jobId).toBeNull();
    expect(state.blueprints).toHaveLength(1);
    cmd(state, { type: 'takeControl', survivorId: null });
    run(state, 5);
    expect(s.jobId).not.toBeNull();
  });

  it('a held direction moves you smoothly, a tile every TICKS_PER_TILE ticks, and stops when let go', () => {
    const state = loadScenario({ seed: 1, map: ['s.......'] });
    const s = state.survivors[0]!;
    cmd(state, { type: 'takeControl', survivorId: s.id });
    cmd(state, { type: 'steer', dx: 1, dy: 0 });
    run(state, 1);
    expect(s.at.x).toBe(centre({ x: 0, y: 0 }).x + SUB / TICKS_PER_TILE); // a quarter of a tile, not a hop
    run(state, TICKS_PER_TILE * 3 - 1);
    expect(s.at.x).toBe(centre({ x: 3, y: 0 }).x);
    expect(s.pos).toEqual({ x: 3, y: 0 });
    cmd(state, { type: 'steer', dx: 0, dy: 0 });
    const stopped = s.at.x;
    run(state, 5);
    expect(s.at.x).toBe(stopped); // no finishing a step: you stop where you are
    expect(s.move).toBeNull();
  });

  it('any direction is the same speed; walls stop you, and you slide along them', () => {
    const state = loadScenario({ seed: 1, map: ['.#....', '.#....', 's#....', '......'] });
    const s = state.survivors[0]!;
    cmd(state, { type: 'takeControl', survivorId: s.id });
    s.pos = { x: 3, y: 1 };
    s.at = centre(s.pos); // open ground for the speed check
    const start = { ...s.at };
    cmd(state, { type: 'steer', dx: 1, dy: 1 });
    run(state, 2);
    const moved = Math.sqrt((s.at.x - start.x) ** 2 + (s.at.y - start.y) ** 2);
    expect(Math.abs(moved - (2 * SUB) / TICKS_PER_TILE)).toBeLessThan(2); // diagonal: same distance a tick
    // Up into the wall at x=1: you slide up the open column, never into the wall.
    s.at = centre({ x: 0, y: 2 });
    s.pos = { x: 0, y: 2 };
    cmd(state, { type: 'steer', dx: 1, dy: -1 });
    run(state, 12);
    expect(s.pos.x).toBe(0);
    expect(s.at.x).toBeLessThanOrEqual(SUB - BODY_RADIUS);
    expect(s.pos.y).toBe(0);
  });

  it('swinging at a zombie next to you hits it, and not faster than a guard could', () => {
    const state = loadScenario({ seed: 1, map: ['s.'] });
    state.zombies = [];
    applyCommand(state, { type: 'spawnZombie', x: 1, y: 0 });
    const z = state.zombies[0]!;
    z.kind = 'static';
    const s = state.survivors[0]!;
    cmd(state, { type: 'takeControl', survivorId: s.id });
    const before = z.health;
    cmd(state, { type: 'swing', x: 1, y: 0 });
    expect(z.health).toBeLessThan(before);
    expect(applyCommand(state, { type: 'swing', x: 1, y: 0 }).ok).toBe(false); // still recovering
  });

  it('use: picks up a weapon into the weapon slot, eats food (taking time), searches a crate', () => {
    const state = loadScenario({ seed: 1, map: ['/sf', '.c.'] });
    const s = state.survivors[0]!;
    cmd(state, { type: 'takeControl', survivorId: s.id });
    cmd(state, { type: 'use', x: 0, y: 0 });
    expect(s.weapon).not.toBeNull();
    s.satiety = 100;
    cmd(state, { type: 'use', x: 2, y: 0 });
    run(state, 5);
    expect(s.satiety).toBeLessThan(200); // not yet
    run(state, 40);
    expect(s.satiety).toBeGreaterThan(1000);
    expect(s.satiety).toBeLessThanOrEqual(SATIETY_MAX);
    const items = state.items.length;
    cmd(state, { type: 'use', x: 1, y: 1 });
    run(state, 60);
    expect(state.items.length).toBe(items + 1);
  });

  it('use: drink at a well, sleep in a bed until rested, wake when hurt', () => {
    const state = loadScenario({ seed: 1, map: [`s${WELL}.`, 'b..'] });
    const s = state.survivors[0]!;
    cmd(state, { type: 'takeControl', survivorId: s.id });
    s.hydration = 10;
    cmd(state, { type: 'use', x: 1, y: 0 });
    run(state, 30);
    expect(s.hydration).toBeGreaterThan(HYDRATION_MAX - 40);
    s.rest = 100;
    cmd(state, { type: 'use', x: 0, y: 1 }); // climbs in
    run(state, 6);
    expect(s.pos).toEqual({ x: 0, y: 1 });
    cmd(state, { type: 'use', x: 0, y: 1 });
    run(state, 100);
    expect(s.rest).toBeGreaterThan(400);
    s.hurtTick = state.tick;
    tick(state);
    expect(state.control!.busy).toBeNull();
  });

  it('use: medicine you carry cures your infection; one on the ground you need is used, not picked up', () => {
    const state = loadScenario({ seed: 1, map: ['sm.'] });
    const s = state.survivors[0]!;
    cmd(state, { type: 'takeControl', survivorId: s.id });
    s.health = 500;
    cmd(state, { type: 'use', x: 1, y: 0 }); // the bandage
    run(state, 40);
    expect(s.health).toBeGreaterThan(500);
    expect(state.items.filter((i) => i.kind === 'bandage')).toEqual([]);
    s.infection = 100;
    const med = spawnItem(state, 'medicine', 0, 0);
    med.pos = null;
    med.carriedBy = s.id;
    s.carrying = med.id;
    cmd(state, { type: 'use', x: 0, y: 0 });
    run(state, 40);
    expect(s.infection).toBeNull();
    expect(s.carrying).toBeNull();
  });

  it('moving interrupts a search', () => {
    const state = loadScenario({ seed: 1, map: ['sc..', '....'] });
    const s = state.survivors[0]!;
    cmd(state, { type: 'takeControl', survivorId: s.id });
    cmd(state, { type: 'use', x: 1, y: 0 });
    run(state, 10);
    cmd(state, { type: 'steer', dx: 0, dy: 1 });
    run(state, 60);
    expect(state.control!.busy).toBeNull();
    expect(state.items).toEqual([]);
  });

  it('control ends when the survivor dies', () => {
    const state = loadScenario({ seed: 1, map: ['s.', '..'] });
    const s = state.survivors[0]!;
    cmd(state, { type: 'takeControl', survivorId: s.id });
    s.health = 0;
    tick(state);
    expect(state.control).toBeNull();
  });

  it('replays exactly', () => {
    const state = loadScenario({ seed: 3, map: ['s.....', '.c..f.', '......'] });
    const start = cloneState(state);
    const s = state.survivors[0]!;
    cmd(state, { type: 'takeControl', survivorId: s.id });
    cmd(state, { type: 'steer', dx: 1, dy: 1 });
    run(state, 9);
    cmd(state, { type: 'steer', dx: 0, dy: 0 });
    run(state, 6);
    applyCommand(state, { type: 'use', x: 1, y: 1 });
    run(state, 50);
    expect(replay(start, state.commandLog, state.tick)).toEqual(state);
  });
});
