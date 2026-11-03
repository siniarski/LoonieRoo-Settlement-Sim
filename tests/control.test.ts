import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { DIAGONAL_PCT, stepTarget } from '../src/sim/control';
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

  it('a held direction walks tile to tile until let go', () => {
    const state = loadScenario({ seed: 1, map: ['s.......'] });
    const s = state.survivors[0]!;
    cmd(state, { type: 'takeControl', survivorId: s.id });
    cmd(state, { type: 'steer', dx: 1, dy: 0 });
    run(state, TICKS_PER_TILE * 3 + 1); // the first step starts on the first tick
    expect(s.pos).toEqual({ x: 3, y: 0 });
    cmd(state, { type: 'steer', dx: 0, dy: 0 });
    run(state, TICKS_PER_TILE * 3);
    expect(s.pos.x).toBe(4); // finishes the step it was on, then stops
    expect(s.move).toBeNull();
  });

  it('diagonals cost more and never cut a corner; a blocked one slides along the wall', () => {
    const state = loadScenario({ seed: 1, map: ['s#..', '....', '....'] });
    expect(stepTarget(state, { x: 0, y: 0 }, 1, 1)).toEqual({ x: 0, y: 1 }); // the wall to the east: slide south
    expect(stepTarget(state, { x: 1, y: 1 }, 1, 1)).toEqual({ x: 2, y: 2 });
    const s = state.survivors[0]!;
    s.pos = { x: 1, y: 1 };
    cmd(state, { type: 'takeControl', survivorId: s.id });
    cmd(state, { type: 'steer', dx: 1, dy: 1 });
    tick(state);
    expect(s.move!.duration).toBe(Math.round((TICKS_PER_TILE * DIAGONAL_PCT) / 100));
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
