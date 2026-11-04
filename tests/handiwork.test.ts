import { describe, expect, it } from 'vitest';
import { applyCommand, type Command } from '../src/sim/commands';
import { damageStructure, structureDamage } from '../src/sim/breach';
import { BUILD_WORK } from '../src/sim/designations';
import { NORMAL } from '../src/sim/difficulty';
import { isReinforced } from '../src/sim/defences';
import { getTile, GROUNDS } from '../src/sim/grid';
import { FORAGE_TICKS } from '../src/sim/handiwork';
import { spawnItem } from '../src/sim/items';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
import { createWorld } from '../src/sim/worldgen';

const cmd = (state: SimState, c: Command) => expect(applyCommand(state, c), JSON.stringify(c)).toMatchObject({ ok: true });
const run = (state: SimState, n: number) => {
  for (let i = 0; i < n; i++) tick(state);
};
/** Put a piece of wood in the controlled survivor's hands. */
const holdWood = (state: SimState) => {
  const s = state.survivors.find((x) => x.id === state.control!.survivorId)!;
  const wood = spawnItem(state, 'wood', s.pos.x, s.pos.y);
  wood.pos = null;
  wood.carriedBy = s.id;
  s.carrying = wood.id;
};

describe('handiwork', () => {
  it('repair a damaged door, then barricade it with wood, then reinforce it', () => {
    const state = loadScenario({ seed: 1, map: ['sD.'] });
    const s = state.survivors[0]!;
    cmd(state, { type: 'takeControl', survivorId: s.id });
    damageStructure(state, 1, 0, 100);
    expect(structureDamage(state, 1, 0)).toBeGreaterThan(0);
    expect(applyCommand(state, { type: 'work', x: 1, y: 0 }).ok).toBe(true);
    run(state, 80);
    expect(structureDamage(state, 1, 0)).toBe(0);
    expect(applyCommand(state, { type: 'work', x: 1, y: 0 }).ok).toBe(false); // no wood
    holdWood(state);
    cmd(state, { type: 'work', x: 1, y: 0 });
    run(state, BUILD_WORK.barricade + 2);
    expect(getTile(state.grid, 1, 0)).toBe('barricade');
    expect(s.carrying).toBeNull();
    holdWood(state);
    cmd(state, { type: 'work', x: 1, y: 0 });
    run(state, BUILD_WORK.reinforce + 2);
    expect(isReinforced(state, 1, 0)).toBe(true);
  });

  it('put up a wall with wood; moving away stops the work', () => {
    const state = loadScenario({ seed: 1, map: ['s..', '...'] });
    const s = state.survivors[0]!;
    cmd(state, { type: 'takeControl', survivorId: s.id });
    holdWood(state);
    cmd(state, { type: 'work', x: 1, y: 0 });
    run(state, 5);
    cmd(state, { type: 'steer', dx: 0, dy: 1 });
    run(state, BUILD_WORK.wall);
    expect(getTile(state.grid, 1, 0)).toBe('floor');
    cmd(state, { type: 'steer', dx: 0, dy: 0 });
    run(state, 5);
    s.pos = { x: 0, y: 0 };
    cmd(state, { type: 'work', x: 1, y: 0 });
    run(state, BUILD_WORK.wall + 2);
    expect(getTile(state.grid, 1, 0)).toBe('wall');
  });

  it('forage on open grass by day: sometimes food; each tile once a day', () => {
    let found = 0;
    for (let seed = 1; seed <= 12; seed++) {
      const state = loadScenario({ seed, map: ['s..'] });
      state.grid.ground.fill(GROUNDS.indexOf('grass'));
      const s = state.survivors[0]!;
      s.id = `s${seed * 7}`;
      state.control = { survivorId: s.id, dx: 0, dy: 0, facing: { x: 1, y: 0 }, nextSwing: 0, busy: null };
      state.tick = 2 * TICKS_PER_HOUR; // 08:00
      cmd(state, { type: 'use', x: 0, y: 0 });
      run(state, FORAGE_TICKS + 2);
      found += state.items.filter((i) => i.kind === 'food').length;
      state.items = []; // (E would eat what you found)
      const again = applyCommand(state, { type: 'use', x: 0, y: 0 });
      expect(again.ok).toBe(true); // a reply, not a forage
      expect(state.control!.busy).toBeNull();
    }
    expect(found).toBeGreaterThan(0);
    expect(found).toBeLessThan(12);
  });

  it('hunt: swing at a deer next to you; when it drops, venison', () => {
    let dropped = false;
    for (let t = 0; t < 30 && !dropped; t++) {
      const state = loadScenario({ seed: 1, map: ['s.'] });
      const s = state.survivors[0]!;
      cmd(state, { type: 'takeControl', survivorId: s.id });
      state.tick = t;
      state.animals.push({ id: `a${t + 50}`, kind: 'deer', pos: { x: 1, y: 0 }, move: null, hunted: false, leavesTick: DAY_LENGTH });
      cmd(state, { type: 'swing', x: 1, y: 0 });
      dropped = state.animals.length === 0 && state.items.filter((i) => i.kind === 'food').length > 0;
    }
    expect(dropped).toBe(true);
  });

  it('alone, you set out with a weapon in hand and a bandage', () => {
    const state = createWorld(7, 128, { ...NORMAL, start: 'solo' });
    const you = state.survivors[0]!;
    expect(you.weapon).not.toBeNull();
    expect(state.items.some((i) => i.kind === 'bandage')).toBe(true);
  });
});
