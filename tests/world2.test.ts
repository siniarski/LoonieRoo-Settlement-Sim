import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { climateAt, isRadioactive, regionCold } from '../src/sim/climate';
import { NORMAL } from '../src/sim/difficulty';
import { newGamePlus } from '../src/sim/escape';
import { getTile } from '../src/sim/grid';
import { loadScenario } from '../src/sim/scenario';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
import { SEASON_DAYS } from '../src/sim/seasons';
import { CHUNK, createWorld, ensureGeneratedAround, fenceAt, isSea, layout } from '../src/sim/worldgen';

const SIZE = 256;
const generateAll = (state: ReturnType<typeof createWorld>) => {
  for (let y = 0; y < SIZE; y += CHUNK) for (let x = 0; x < SIZE; x += CHUNK) ensureGeneratedAround(state, { x, y });
};

describe('map types', () => {
  it('an island is ringed by sea; the start is dry', () => {
    const state = createWorld(11, SIZE, { ...NORMAL, map: 'island' });
    generateAll(state);
    expect(getTile(state.grid, 0, 0)).toBe('water');
    expect(getTile(state.grid, SIZE - 1, SIZE / 2)).toBe('water');
    const s = state.survivors[0]!;
    expect(getTile(state.grid, s.pos.x, s.pos.y)).toBe('floor');
  });

  it('a quarantine zone is fenced in, with gates where roads cross', () => {
    const l = layout(11, SIZE, 'quarantine');
    expect(fenceAt(l, 2, 100)).not.toBeNull();
    expect(fenceAt(l, 10, 100)).toBeNull();
    const gate = l.roadRows[0]!;
    expect(fenceAt(l, 2, gate)).toBe('door');
    expect(isSea(layout(11, SIZE, 'mainland'), 0, 0)).toBe(false);
  });

  it('an island chain has water between islands, bridged by the roads', () => {
    const state = createWorld(11, SIZE, { ...NORMAL, map: 'islands' });
    generateAll(state);
    const bridges = state.grid.ground.filter((g) => g === 4).length;
    const plain = createWorld(11, SIZE, { ...NORMAL, map: 'mainland' });
    generateAll(plain);
    expect(bridges).toBeGreaterThan(plain.grid.ground.filter((g) => g === 4).length);
  });
});

describe('climate and radiation', () => {
  it('the north runs cold, the south hot; a cold region feels autumn nights', () => {
    const state = createWorld(11, SIZE, NORMAL);
    expect(climateAt(state, SIZE / 2, 5)).toBe('cold');
    expect(climateAt(state, SIZE / 2, SIZE - 5)).toBe('hot');
    state.tick = 2 * SEASON_DAYS * DAY_LENGTH + 16 * TICKS_PER_HOUR; // autumn, 22:00
    expect(regionCold(state, SIZE / 2, 5)).toBe(true);
    expect(regionCold(state, SIZE / 2, SIZE / 2)).toBe(false);
    expect(regionCold(state, SIZE / 2, SIZE - 5)).toBe(false);
  });

  it('hot zones only in a nuclear winter, never near the start, and they hurt', () => {
    const plain = createWorld(11, SIZE, NORMAL);
    const winter = createWorld(11, SIZE, { ...NORMAL, winter: true });
    let spot: { x: number; y: number } | null = null;
    for (let y = 0; y < SIZE && !spot; y += 2) for (let x = 0; x < SIZE && !spot; x += 2) if (isRadioactive(winter, x, y)) spot = { x, y };
    expect(spot).not.toBeNull();
    expect(isRadioactive(plain, spot!.x, spot!.y)).toBe(false);
    expect(isRadioactive(winter, SIZE / 2, SIZE / 2)).toBe(false);
    const s = winter.survivors[0]!;
    s.pos = spot!;
    const hp = s.health;
    winter.tick = 20;
    tick(winter);
    expect(s.health).toBeLessThan(hp);
    expect(s.thoughts.some((t) => t.kind === 'irradiated')).toBe(true);
  });
});

describe('escape', () => {
  const helicopterDown = () => {
    const state = loadScenario({ seed: 1, map: ['s.s.s'] });
    applyCommand(state, { type: 'takeControl', survivorId: state.survivors[0]!.id });
    state.endgame.helicopter = true;
    state.endgame.won = 'evacuation';
    return state;
  };

  it('send the others: they fly out, you stay, and can call again', () => {
    const state = helicopterDown();
    expect(applyCommand(state, { type: 'escape', who: 'others' })).toMatchObject({ ok: true });
    expect(state.survivors).toHaveLength(1);
    expect(state.endgame.escaped).toBe(2);
    expect(state.endgame.helicopter).toBe(false);
    expect(state.endgame.evacuation).toBeNull();
  });

  it('left waiting, it goes at dusk', () => {
    const state = helicopterDown();
    state.tick = 15 * TICKS_PER_HOUR;
    tick(state);
    expect(state.endgame.helicopter).toBe(false);
    expect(state.survivors).toHaveLength(3);
  });

  it('everyone: a new game+, you alone in a fresh world, with your gear and who you were', () => {
    const old = createWorld(5, 128, { ...NORMAL, start: 'solo' });
    const you = old.survivors[0]!;
    you.xp = { guard: 500 };
    you.traits = ['tough'];
    you.resolve = 77;
    old.endgame.escaped = 4;
    const fresh = newGamePlus(old, 99);
    const me = fresh.survivors[0]!;
    expect(fresh.survivors).toHaveLength(1);
    expect(fresh.control?.survivorId).toBe(me.id);
    expect(me.name).toBe(you.name);
    expect(me.xp).toEqual({ guard: 500 });
    expect(me.traits).toEqual(['tough']);
    expect(me.resolve).toBe(77);
    expect(me.weapon).not.toBeNull();
    expect(fresh.items.filter((i) => i.carriedBy === me.id)).toHaveLength(1);
    expect(fresh.endgame.escaped).toBe(4);
    expect(fresh.world!.seed).toBe(99);
  });
});
