import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { BODY_RADIUS, SUB, centre, distance2, fits, slide } from '../src/sim/motion';
import { loadScenario } from '../src/sim/scenario';
import { tick } from '../src/sim/tick';
import { CROWD, REACH } from '../src/sim/zombies';

describe('motion', () => {
  it('a body fits only where every tile it overlaps is open; slide stops at walls but keeps the free axis', () => {
    const state = loadScenario({ seed: 1, map: ['..#', '...'] });
    expect(fits(state.grid, centre({ x: 1, y: 0 }), 'survivor')).toBe(true);
    expect(fits(state.grid, { x: 2 * SUB - BODY_RADIUS + 1, y: 128 }, 'survivor')).toBe(false); // touching the wall
    const moved = slide(state.grid, centre({ x: 1, y: 0 }), 200, 60, 'survivor');
    expect(moved.x).toBeLessThanOrEqual(2 * SUB - BODY_RADIUS);
    expect(moved.y).toBe(128 + 60);
  });

  it('a zombie closes in on you where you stand, between tiles, and stops within reach', () => {
    const state = loadScenario({ seed: 1, map: ['s.....', '......'] });
    const you = state.survivors[0]!;
    applyCommand(state, { type: 'takeControl', survivorId: you.id });
    you.at = { x: 128 + 90, y: 128 + 60 }; // off-centre
    applyCommand(state, { type: 'spawnZombie', x: 2, y: 1 });
    const z = state.zombies[0]!;
    z.kind = 'static'; // (night ones leave by day)
    for (let t = 0; t < 40; t++) tick(state);
    const d = Math.sqrt(distance2(z.at, you.at));
    expect(d).toBeLessThanOrEqual(REACH);
    expect(d).toBeGreaterThanOrEqual(CROWD - 2);
    expect(you.health).toBeLessThan(1000); // and it's been hitting
  });

  it('a crowd pushes apart instead of stacking', () => {
    const state = loadScenario({ seed: 1, map: ['......', '......', '......'] });
    for (let i = 0; i < 3; i++) applyCommand(state, { type: 'spawnZombie', x: 2, y: 1 });
    for (const z of state.zombies) {
      z.kind = 'static';
      z.at = centre({ x: 2, y: 1 });
      z.pos = { x: 2, y: 1 };
    }
    for (let t = 0; t < 10; t++) tick(state);
    const [a, b, c] = state.zombies as [NonNullable<typeof state.zombies[0]>, NonNullable<typeof state.zombies[0]>, NonNullable<typeof state.zombies[0]>];
    for (const [p, q] of [[a, b], [a, c], [b, c]] as const) expect(distance2(p.at, q.at)).toBeGreaterThan((CROWD / 2) ** 2);
  });

  it('your swing lands on what is in the arc in front of you, not behind you', () => {
    const state = loadScenario({ seed: 1, map: ['.....', '.....', '.....'] });
    const you = state.survivors.length ? state.survivors[0]! : (applyCommand(state, { type: 'spawnSurvivor', x: 2, y: 1 }), state.survivors[0]!);
    applyCommand(state, { type: 'takeControl', survivorId: you.id });
    you.at = centre({ x: 2, y: 1 });
    you.pos = { x: 2, y: 1 };
    applyCommand(state, { type: 'spawnZombie', x: 1, y: 1 }); // behind (west)
    const behind = state.zombies[0]!;
    behind.kind = 'static';
    state.control!.facing = { x: 1, y: 0 }; // facing east
    applyCommand(state, { type: 'swing', x: 3, y: 1 });
    expect(behind.health).toBe(behind.maxHealth);
    applyCommand(state, { type: 'spawnZombie', x: 3, y: 0 }); // ahead and up a bit: inside the arc
    const ahead = state.zombies[1]!;
    ahead.kind = 'static';
    ahead.at = { x: 3 * SUB + 60, y: SUB + 40 };
    state.control!.nextSwing = 0;
    applyCommand(state, { type: 'swing', x: 3, y: 1 });
    expect(ahead.health).toBeLessThan(ahead.maxHealth);
  });
});
