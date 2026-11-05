import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { addFurniture } from '../src/sim/furniture';
import { ITEM_WEIGHT, PACK_MAX, contents, packWeight } from '../src/sim/inventory';
import { spawnItem } from '../src/sim/items';
import { killSurvivor } from '../src/sim/needs';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { checkInvariants } from '../src/sim/invariants';

/** You, controlled, at (1, 1), with a kitchen counter at (2, 1) holding `loot` finds. */
function setup(loot = 3): SimState {
  const state = loadScenario({ seed: 1, map: ['.....', '.s...', '.....'] });
  applyCommand(state, { type: 'takeControl', survivorId: state.survivors[0]!.id });
  const counter = addFurniture(state, 'crate', 2, 1);
  counter.look = 'counter';
  counter.table = 'kitchen';
  counter.loot = loot;
  return state;
}

const run = (state: SimState, ticks: number) => {
  for (let t = 0; t < ticks; t++) tick(state);
};

describe('the bag', () => {
  it('searching a container turns up everything in it, and you take it into your bag', () => {
    const state = setup(3);
    const s = state.survivors[0]!;
    const counter = state.furniture.find((f) => f.look === 'counter')!;
    expect(applyCommand(state, { type: 'use', x: 2, y: 1 }).ok).toBe(true);
    run(state, 80);
    const inside = contents(state, counter);
    expect(inside).toHaveLength(3);
    expect(checkInvariants(state)).toEqual([]);
    expect(applyCommand(state, { type: 'take', itemId: inside[0]!.id }).ok).toBe(true);
    expect(s.pack).toEqual([inside[0]!.id]);
    expect(applyCommand(state, { type: 'takeAll', containerId: counter.id }).ok).toBe(true);
    expect(s.pack).toHaveLength(3);
    expect(contents(state, counter)).toHaveLength(0);
    expect(checkInvariants(state)).toEqual([]);
  });

  it('holds only so much', () => {
    const state = setup(0);
    const s = state.survivors[0]!;
    let took = 0;
    for (let i = 0; i < 10; i++) {
      const wood = spawnItem(state, 'wood', 1, 2);
      if (applyCommand(state, { type: 'take', itemId: wood.id }).ok) took++;
    }
    expect(took).toBe(Math.floor(PACK_MAX / ITEM_WEIGHT.wood));
    expect(packWeight(state, s)).toBeLessThanOrEqual(PACK_MAX);
  });

  it('use from the bag: eat, wield (the old weapon into the bag); drop', () => {
    const state = setup(0);
    const s = state.survivors[0]!;
    s.satiety = 100;
    const food = spawnItem(state, 'food', 1, 2);
    const knife = spawnItem(state, 'weapon', 2, 2);
    applyCommand(state, { type: 'take', itemId: food.id });
    applyCommand(state, { type: 'take', itemId: knife.id });
    expect(applyCommand(state, { type: 'useItem', itemId: food.id }).ok).toBe(true);
    run(state, 40);
    expect(s.satiety).toBeGreaterThan(1000);
    expect(s.pack).not.toContain(food.id);
    expect(applyCommand(state, { type: 'useItem', itemId: knife.id }).ok).toBe(true);
    expect(s.weapon).toBe(knife.id);
    expect(s.pack).not.toContain(knife.id);
    expect(applyCommand(state, { type: 'drop', itemId: s.pack[0] ?? 'none' }).ok).toBe(s.pack.length > 0);
    expect(checkInvariants(state)).toEqual([]);
  });

  it('things you pick up go in the bag; it all falls where you fall', () => {
    const state = setup(0);
    const s = state.survivors[0]!;
    const book = spawnItem(state, 'book', 1, 2);
    expect(applyCommand(state, { type: 'use', x: 1, y: 2 }).ok).toBe(true);
    expect(s.pack).toContain(book.id);
    expect(s.carrying).toBeNull();
    killSurvivor(state, s, 'a fall');
    expect(book.pos).not.toBeNull();
    expect(book.carriedBy).toBeNull();
  });
});
