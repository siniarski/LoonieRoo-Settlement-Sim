// Kinds of zombie, beyond the plain walker. As the nights go on, some of the
// night's arrivals, herds and ruin sleepers are something worse (a hash of the
// zombie's id, not the sim RNG):
//   runner    quick (a tile every 4 ticks, walkers take 6), but frail
//   brute     twice the health, slow, hits hard, and batters walls three times as fast
//   screamer  frail; when it first spots someone it screams (noise.ts), waking and drawing others
//   crawler   frail; slips under barricades
// Each counters a different defence: runners outpace guards, brutes break walls,
// screamers punish fighting near ruins, crawlers make barricades no wall.

import { hashRoll, idNumber } from './hash';
import type { Zombie } from './zombies';

export type Variant = 'walker' | 'runner' | 'brute' | 'screamer' | 'crawler';

export interface VariantInfo {
  /** Health as a percentage of a walker's. */
  healthPct: number;
  ticksPerTile: number;
  damage: number;
  /** Multiplies damage to walls and doors. */
  battering: number;
  /** First night it turns up on. */
  from: number;
  /** Chance (once it's turning up) that a zombie is one. */
  chance: number;
}

export const VARIANTS: Record<Variant, VariantInfo> = {
  walker: { healthPct: 100, ticksPerTile: 6, damage: 40, battering: 1, from: 1, chance: 0 },
  runner: { healthPct: 60, ticksPerTile: 4, damage: 30, battering: 1, from: 3, chance: 0.12 },
  screamer: { healthPct: 80, ticksPerTile: 6, damage: 30, battering: 1, from: 4, chance: 0.06 },
  brute: { healthPct: 200, ticksPerTile: 8, damage: 70, battering: 3, from: 5, chance: 0.08 },
  crawler: { healthPct: 70, ticksPerTile: 7, damage: 35, battering: 1, from: 6, chance: 0.08 },
};
const ORDER: readonly Variant[] = ['runner', 'screamer', 'brute', 'crawler'];

/** Turns a fresh walker into something worse, maybe, by its id and the night. */
export function giveVariant(z: Zombie, night: number): void {
  const roll = hashRoll(idNumber(z.id), 0x2b1d);
  let at = 0;
  for (const v of ORDER) {
    if (night < VARIANTS[v].from) continue;
    at += VARIANTS[v].chance;
    if (roll >= at) continue;
    z.variant = v;
    z.health = z.maxHealth = Math.max(1, Math.round((z.maxHealth * VARIANTS[v].healthPct) / 100));
    return;
  }
}
