// Seeded RNG (mulberry32). The whole RNG state is one uint32 stored in sim state,
// so save/load and replays restore it exactly. This is the only randomness
// source allowed in the sim (tests/architecture.test.ts enforces that).

export interface RngHolder {
  rng: number;
}

/** Pure step: returns a float in [0, 1) and the next RNG state. */
export function mulberry32(rngState: number): { value: number; next: number } {
  const next = (rngState + 0x6d2b79f5) >>> 0;
  let t = next;
  t = Math.imul(t ^ (t >>> 15), t | 1);
  t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
  const value = ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  return { value, next };
}

/** Normalises any integer seed into a valid RNG state. */
export function seedRng(seed: number): number {
  return seed >>> 0;
}

/** Float in [0, 1). Advances `holder.rng`. */
export function random(holder: RngHolder): number {
  const { value, next } = mulberry32(holder.rng);
  holder.rng = next;
  return value;
}

/** Integer in [min, maxExclusive). Advances `holder.rng`. */
export function randomInt(holder: RngHolder, min: number, maxExclusive: number): number {
  if (maxExclusive <= min) {
    throw new Error(`randomInt: empty range [${min}, ${maxExclusive})`);
  }
  return min + Math.floor(random(holder) * (maxExclusive - min));
}

/** Picks one element. Advances `holder.rng`. */
export function pick<T>(holder: RngHolder, items: readonly T[]): T {
  const item = items[randomInt(holder, 0, items.length)];
  if (item === undefined) throw new Error('pick: empty array');
  return item;
}
