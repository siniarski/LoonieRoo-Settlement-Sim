// A stateless roll in [0, 1) from a few integers. For chances that shouldn't
// draw on the sim RNG (so adding them doesn't shift everything else's
// randomness): fires spreading, sickness, and the like.

export function hashRoll(...nums: number[]): number {
  let h = 0x811c9dc5;
  for (const n of nums) {
    h = Math.imul(h ^ (n | 0), 0x01000193);
    h = Math.imul(h ^ (h >>> 15), 0x2c1b3c6d);
  }
  h = Math.imul(h ^ (h >>> 13), 0x297a2d39);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

/** The number in an id like "z12". */
export function idNumber(id: string): number {
  return Number(id.replace(/\D/g, '')) || 0;
}
