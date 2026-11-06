import { describe, expect, it } from 'vitest';
import { hashRoll } from '../src/sim/hash';
import { findById } from '../src/sim/lookup';

describe('findById', () => {
  it('always answers as list.find would, through pushes, splices and lookups of ids long gone', () => {
    const list: { id: string }[] = [];
    let next = 0;
    for (let round = 0; round < 3000; round++) {
      const r = hashRoll(round, 1);
      if (r < 0.4) list.push({ id: `x${next++}` });
      else if (r < 0.6 && list.length > 0) list.splice(Math.floor(hashRoll(round, 2) * list.length), 1);
      const id = `x${Math.floor(hashRoll(round, 3) * (next + 2))}`;
      expect(findById(list, id)).toBe(list.find((x) => x.id === id));
    }
  });
});
