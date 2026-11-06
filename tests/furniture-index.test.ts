import { describe, expect, it } from 'vitest';
import { addFurniture, furnitureAt, getFurniture, markedForScavenge, ofKind, removeFurniture, setScavenge, type FurnitureKind } from '../src/sim/furniture';
import { hashRoll } from '../src/sim/hash';
import { loadScenario } from '../src/sim/scenario';

describe('the furniture index, kept up to date in place', () => {
  it('answers as an index built afresh would, through adds, removes and marks', () => {
    const state = loadScenario({ seed: 1, map: Array.from({ length: 12 }, () => '.'.repeat(12)) });
    const kinds: FurnitureKind[] = ['crate', 'tree', 'bed', 'trap'];
    furnitureAt(state, 0, 0); // build the index first, so later changes go through it in place
    for (let round = 0; round < 400; round++) {
      const roll = hashRoll(round, 1);
      if (roll < 0.55 || state.furniture.length === 0) {
        // Sometimes two on one tile: the first in the list is the one at it.
        addFurniture(state, kinds[Math.floor(hashRoll(round, 2) * kinds.length)]!, Math.floor(hashRoll(round, 3) * 12), Math.floor(hashRoll(round, 4) * 12));
      } else if (roll < 0.85) {
        removeFurniture(state, state.furniture[Math.floor(hashRoll(round, 5) * state.furniture.length)]!);
      } else {
        const f = state.furniture[Math.floor(hashRoll(round, 6) * state.furniture.length)]!;
        setScavenge(state, f, !f.scavenge);
      }
      if (round % 20 !== 0) continue;
      // A fresh index: the same list in a new array.
      const fresh = { ...state, furniture: [...state.furniture] };
      for (let y = 0; y < 12; y++) for (let x = 0; x < 12; x++) expect(furnitureAt(state, x, y), `${x},${y} round ${round}`).toBe(furnitureAt(fresh, x, y));
      for (const k of kinds) expect([...ofKind(state, k)]).toEqual([...ofKind(fresh, k)]);
      expect([...markedForScavenge(state)]).toEqual([...markedForScavenge(fresh)]);
      for (const f of fresh.furniture) expect(getFurniture(state, f.id)).toBe(f);
    }
  });
});
