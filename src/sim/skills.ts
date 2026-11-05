// Skills and traits.
//
// Skills: every tick of work at a job type is a point of practice (xp). Each
// XP_PER_LEVEL is a level, up to MAX_LEVEL; a level adds 5% speed (an extra
// step of progress on `level` ticks in every 20). Guards turn skill into
// damage instead (guard jobs time their blows by progress).
//
// Traits: wanderers and a generated world's first survivors usually come
// with one, picked by a hash of their id (not the sim RNG, so nothing else's
// randomness shifts). Hand-made maps' survivors have none.

import { dayScale, slowTick } from './time';
import { boosted } from './moodlets';
import { isHurt, worksThisTick } from './health';
import { logEvent } from './events';
import type { JobType } from './jobTypes';
import { isMiserable } from './mood';
import { ELDER } from './family';
import { armSlowed } from './wounds';
import type { SimState } from './state';
import type { Survivor } from './survivors';

export const XP_PER_LEVEL = 400;
export const MAX_LEVEL = 10;
/** Extra guard damage per guard level. */
export const GUARD_DAMAGE_PER_LEVEL = 5;

export type Trait = 'tough' | 'lazy' | 'greenThumb' | 'cheerful' | 'gloomy' | 'quick' | 'hardened' | 'vigilant' | 'haunted' | 'lostArm' | 'lostLeg' | 'graceful' | 'clumsy';
/** The traits people are born with (rollTraits); the rest are earned (earnTrait), or only chosen (character.ts: graceful, clumsy). */
export const TRAITS: readonly Trait[] = ['tough', 'lazy', 'greenThumb', 'cheerful', 'gloomy', 'quick'];

export const HARDENED_KILLS = 15;
export const HARDENED_DAMAGE = 10;
export const VIGILANT_HOURS = 20;
export const VIGILANT_SIGHT = 3;

export const TRAIT_INFO: Record<Trait, { label: string; about: string; mood: number }> = {
  tough: { label: 'tough', about: 'takes a quarter less from zombies', mood: 0 },
  lazy: { label: 'lazy', about: 'works at 3/4 speed, but happier for it', mood: 5 },
  greenThumb: { label: 'green thumb', about: 'farms as if five levels better', mood: 0 },
  cheerful: { label: 'cheerful', about: 'in a better mood', mood: 10 },
  gloomy: { label: 'gloomy', about: 'in a worse mood', mood: -10 },
  quick: { label: 'quick', about: 'walks faster', mood: 0 },
  hardened: { label: 'hardened', about: `hits ${HARDENED_DAMAGE} harder (earned: many kills)`, mood: 0 },
  vigilant: { label: 'vigilant', about: `sees ${VIGILANT_SIGHT} tiles further (earned: nights on watch)`, mood: 0 },
  haunted: { label: 'haunted', about: 'never quite got over losing a friend', mood: -5 },
  lostArm: { label: 'one-armed', about: 'works slower (an amputation)', mood: -5 },
  lostLeg: { label: 'one-legged', about: 'walks slower (an amputation)', mood: -5 },
  graceful: { label: 'graceful', about: 'moves quietly: heard half as far', mood: 0 },
  clumsy: { label: 'clumsy', about: 'moves noisily: heard half as far again', mood: 0 },
};

/** Experience leaves its mark (once). */
export function earnTrait(state: SimState, s: Survivor, t: Trait, why: string): void {
  if (hasTrait(s, t)) return;
  s.traits.push(t);
  logEvent(state, `${s.name} is ${TRAIT_INFO[t].label} now (${why})`);
}

export function hasTrait(s: Survivor, t: Trait): boolean {
  return s.traits.includes(t);
}

export function skillLevel(s: Survivor, type: JobType): number {
  const base = Math.floor((s.xp[type] ?? 0) / XP_PER_LEVEL);
  return Math.min(MAX_LEVEL, base + (type === 'farm' && hasTrait(s, 'greenThumb') ? 5 : 0));
}

/**
 * One tick of work at a job type: 0 if the survivor loses this tick (hurt, miserable, or
 * lazy: 3/4 speed), else 1, or 2 on the ticks their skill earns. Practice counts either way it works.
 */
/** Tools and the work they help with: a tool adds a step every TOOL_EVERY ticks of it. */
export const TOOL_FOR: Record<'axe' | 'hammer' | 'medkit', JobType> = { axe: 'scavenge', hammer: 'build', medkit: 'doctor' };
export const TOOL_EVERY = 3;

function toolBonus(state: SimState, s: Survivor, type: JobType): number {
  if (!s.tool || state.tick % TOOL_EVERY !== 0) return 0;
  const kind = state.items.find((i) => i.id === s.tool)?.kind;
  return kind && kind in TOOL_FOR && TOOL_FOR[kind as keyof typeof TOOL_FOR] === type ? 1 : 0;
}

export function workStep(state: SimState, s: Survivor, type: JobType): number {
  const slowed = isHurt(s.health) || isMiserable(state, s) || hasTrait(s, 'lazy') || s.sick !== null || s.age >= ELDER || armSlowed(s);
  if (!slowTick(state.tick) || !worksThisTick(state.tick / dayScale(), slowed)) return 0;
  s.xp[type] = (s.xp[type] ?? 0) + 1;
  return 1 + (state.tick % 20 < skillLevel(s, type) ? 1 : 0) + toolBonus(state, s, type) + (boosted(s, 'faster') && state.tick % 4 === 0 ? 1 : 0);
}

/** A trait for a new arrival, from their id: three in four get one. */
export function rollTraits(id: string): Trait[] {
  let h = Math.imul(Number(id.replace(/\D/g, '')) ^ 0x5bd1e995, 0x2c1b3c6d);
  h = Math.imul(h ^ (h >>> 15), 0x297a2d39);
  h = (h ^ (h >>> 13)) >>> 0;
  return h % 4 === 0 ? [] : [TRAITS[h % TRAITS.length]!];
}

/** "tough; build 3, farm 1" for panels. */
export function describeSkills(s: Survivor): string {
  const traits = s.traits.map((t) => TRAIT_INFO[t].label);
  const skills = Object.keys(s.xp)
    .map((k) => [k, skillLevel(s, k as JobType)] as const)
    .filter(([, level]) => level > 0)
    .map(([k, level]) => `${k} ${level}`);
  return [traits.join(', '), skills.join(', ')].filter(Boolean).join('; ') || 'no skills yet';
}
