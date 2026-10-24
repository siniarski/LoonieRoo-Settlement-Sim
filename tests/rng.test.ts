import { describe, expect, it } from 'vitest';
import { mulberry32, pick, random, randomInt, seedRng } from '../src/sim/rng';

describe('rng', () => {
  it('produces the same sequence for the same seed', () => {
    const a = { rng: seedRng(42) };
    const b = { rng: seedRng(42) };
    const seqA = Array.from({ length: 20 }, () => random(a));
    const seqB = Array.from({ length: 20 }, () => random(b));
    expect(seqA).toEqual(seqB);
  });

  it('produces different sequences for different seeds', () => {
    const a = { rng: seedRng(1) };
    const b = { rng: seedRng(2) };
    expect(random(a)).not.toBe(random(b));
  });

  it('is pure at the step level', () => {
    expect(mulberry32(123)).toEqual(mulberry32(123));
  });

  it('keeps state a uint32 and values in [0, 1)', () => {
    const holder = { rng: seedRng(-7) };
    for (let i = 0; i < 1000; i++) {
      const v = random(holder);
      expect(v).toBeGreaterThanOrEqual(0);
      expect(v).toBeLessThan(1);
      expect(Number.isInteger(holder.rng)).toBe(true);
      expect(holder.rng).toBeGreaterThanOrEqual(0);
      expect(holder.rng).toBeLessThanOrEqual(0xffffffff);
    }
  });

  it('resumes identically after a JSON round trip', () => {
    const holder = { rng: seedRng(99) };
    random(holder);
    random(holder);
    const restored = JSON.parse(JSON.stringify(holder)) as { rng: number };
    expect(random(restored)).toBe(random(holder));
  });

  it('randomInt stays in range and covers it', () => {
    const holder = { rng: seedRng(5) };
    const seen = new Set<number>();
    for (let i = 0; i < 500; i++) {
      const n = randomInt(holder, 3, 7);
      expect(n).toBeGreaterThanOrEqual(3);
      expect(n).toBeLessThan(7);
      seen.add(n);
    }
    expect([...seen].sort()).toEqual([3, 4, 5, 6]);
  });

  it('randomInt rejects empty ranges', () => {
    expect(() => randomInt({ rng: 1 }, 4, 4)).toThrow();
  });

  it('pick returns an element and rejects empty arrays', () => {
    const holder = { rng: seedRng(8) };
    expect(['a', 'b', 'c']).toContain(pick(holder, ['a', 'b', 'c']));
    expect(() => pick(holder, [])).toThrow();
  });
});
