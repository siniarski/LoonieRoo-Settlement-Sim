import { describe, expect, it } from 'vitest';
import { SPLIT_AT, SPLIT_PEOPLE, compound, hungerOf, raidSettlement, unfold, updateColonies } from '../src/sim/colonies';
import { applyCommand } from '../src/sim/commands';
import { getTile } from '../src/sim/grid';
import { checkInvariants } from '../src/sim/invariants';
import { spawnItem } from '../src/sim/items';
import { newSettlement } from '../src/sim/settlements';
import { createState, type SimState } from '../src/sim/state';
import { addSurvivor } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH } from '../src/sim/time';

function world(): SimState {
  return createState({ seed: 1, width: 120, height: 120 });
}

const dawn = (state: SimState, day: number) => {
  state.tick = day * DAY_LENGTH;
  updateColonies(state);
};

describe('settlements on their own', () => {
  it('a settlement with no food starves, and one with none left falls', () => {
    const state = world();
    const n = newSettlement(state, 'Dry Hollow', 90, 90, 6, 0, 0);
    n.unfolded = true; // keep the map out of it
    let day = 1;
    while (n.people > 0 && day < 60) dawn(state, day++);
    expect(n.people).toBe(0);
    expect(state.events.some((e) => /starved at Dry Hollow/.test(e.message))).toBe(true);
    expect(state.events.some((e) => /Dry Hollow has fallen/.test(e.message))).toBe(true);
  });

  it('a well-fed one sends food to a starving neighbour, or the starving raid', () => {
    const state = world();
    const rich = newSettlement(state, 'Plenty', 20, 20, 6, 200, 2);
    const poor = newSettlement(state, 'Want', 40, 20, 6, 0, 0);
    rich.unfolded = poor.unfolded = true;
    state.settings.winter = true; // too little grows: Want can't feed itself
    const helped = () => state.events.some((e) => /sent food|raided/.test(e.message));
    for (let day = 1; day <= 15 && !helped(); day++) dawn(state, day);
    expect(helped()).toBe(true);
    expect(rich.ties[poor.id]).not.toBe(0);
  });

  it('a crowded one splits, founding another', () => {
    const state = world();
    const n = newSettlement(state, 'Crowded', 60, 60, SPLIT_AT, 500, 3);
    n.unfolded = true;
    dawn(state, 1);
    expect(state.settlements).toHaveLength(2);
    expect(state.settlements[1]!.people).toBe(SPLIT_PEOPLE);
  });

  it('unfolds into a walled compound with beds and food when someone comes near', () => {
    const state = world();
    const n = newSettlement(state, 'Near', 60, 60, 6, 20, 1);
    addSurvivor(state, 60, 80);
    state.tick = 50;
    updateColonies(state);
    expect(n.unfolded).toBe(true);
    const box = compound(n);
    expect(getTile(state.grid, box.x0, box.y0)).toBe('wall');
    expect(getTile(state.grid, n.x, box.y1)).toBe('door');
    expect(state.furniture.filter((f) => f.kind === 'bed')).toHaveLength(6);
    expect(state.furniture.some((f) => f.kind === 'table')).toBe(true);
    expect(n.residents).toHaveLength(6);
    for (let t = 0; t < 30; t++) {
      tick(state);
      expect(checkInvariants(state)).toEqual([]);
    }
  });

  it('a fallen one unfolds as a ruin, with the dead asleep inside', () => {
    const state = world();
    const n = newSettlement(state, 'Gone', 60, 60, 0, 0, 0);
    unfold(state, n);
    expect(state.dormant.length).toBeGreaterThan(0);
    expect(n.residents).toEqual([]);
  });
});

describe('you and them', () => {
  it('food to a starving place counts double, and feeds them', () => {
    const state = world();
    const n = newSettlement(state, 'Want', 100, 100, 6, 0, 0);
    const s = addSurvivor(state, 5, 5);
    for (let i = 0; i < 3; i++) spawnItem(state, 'food', s.pos.x + 1 + i, s.pos.y);
    expect(hungerOf(n)).toBe('starving');
    expect(applyCommand(state, { type: 'giftSettlement', settlementId: n.id })).toMatchObject({ ok: true });
    expect(n.reputation).toBe(20);
    expect(n.food).toBe(3);
  });

  it('a raid needs someone armed, and makes an enemy either way', () => {
    const state = world();
    const n = newSettlement(state, 'Mark', 100, 100, 2, 20, 0);
    const s = addSurvivor(state, 5, 5);
    expect(raidSettlement(state, n.id)).toMatch(/armed/);
    applyCommand(state, { type: 'spawnWeapon', x: 5, y: 5 });
    s.weapon = state.items.find((i) => i.kind === 'weapon')!.id;
    state.items.find((i) => i.kind === 'weapon')!.pos = null;
    state.items.find((i) => i.kind === 'weapon')!.carriedBy = s.id;
    expect(raidSettlement(state, n.id)).toBeNull();
    expect(n.reputation).toBeLessThan(-30);
    expect(raidSettlement(state, n.id)).toMatch(/already/);
  });
});
