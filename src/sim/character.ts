// Character creation: who you are, chosen before the world begins.
//
//   name        what you're called (NAME_MAX letters, spaces, hyphens and apostrophes).
//   profession  one of story.ts STORY_JOBS: what you did before. It starts some skills at a level
//               (PROFESSIONS: xp of XP_PER_LEVEL a level), puts a few things in your bag, and
//               costs or gives points (a soldier costs; a student has time on their hands).
//   traits      chosen from CHOOSABLE, each with a cost (TRAIT_COST): good ones cost points,
//               bad ones give them back. You can't end below zero (characterProblem).
// The character becomes the first survivor of a generated world (createWorld): yours in a solo
// start, the first of the three in a group. Nothing here touches the sim RNG.

import { spawnItem, type ItemKind } from './items';
import { stow } from './inventory';
import type { JobType } from './jobTypes';
import { XP_PER_LEVEL, type Trait } from './skills';
import type { SimState } from './state';
import { STORY_JOBS } from './story';
import type { Survivor } from './survivors';

export interface Character {
  name: string;
  /** Index into STORY_JOBS. */
  job: number;
  traits: Trait[];
}

export const NAME_MAX = 16;
/** Points everyone starts with, before their profession and traits. */
export const START_POINTS = 4;

export interface Profession {
  /** Starting skill levels. */
  skills: Partial<Record<JobType, number>>;
  /** In your bag (or at your feet) at the start. */
  kit: ItemKind[];
  /** Points it gives (positive) or costs (negative). */
  points: number;
  about: string;
}

/** By STORY_JOBS entry. */
export const PROFESSIONS: Record<string, Profession> = {
  'a nurse': { skills: { doctor: 3 }, kit: ['medicine', 'bandage'], points: -2, about: 'patches people up' },
  'a mechanic': { skills: { craft: 3, build: 1 }, kit: ['hammer', 'scrap'], points: -2, about: 'makes things, fixes things' },
  'a teacher': { skills: { research: 3 }, kit: ['book'], points: 0, about: 'reads, and works things out' },
  'a farmer': { skills: { farm: 3, hunt: 1 }, kit: ['food', 'food'], points: -2, about: 'grows food' },
  'a soldier': { skills: { guard: 3 }, kit: ['armour'], points: -4, about: 'fights, in armour' },
  'a cook': { skills: { cook: 3 }, kit: ['food', 'food', 'food'], points: 0, about: 'cooks, and brought food' },
  'a police officer': { skills: { guard: 2 }, kit: ['gun', 'ammo'], points: -4, about: 'fights, with a gun' },
  'a carpenter': { skills: { build: 3 }, kit: ['hammer', 'wood', 'wood'], points: -2, about: 'builds' },
  'a paramedic': { skills: { doctor: 2 }, kit: ['medkit', 'bandage'], points: -2, about: 'patches people up, with a kit' },
  'a lorry driver': { skills: { haul: 3, scout: 1 }, kit: ['fuel'], points: 0, about: 'carries, and knows the roads' },
  'an electrician': { skills: { craft: 2, build: 1 }, kit: ['scrap', 'scrap'], points: -1, about: 'makes things' },
  'a priest': { skills: { clean: 1 }, kit: [], points: 2, about: 'little to offer but faith' },
  'a shop clerk': { skills: { scavenge: 2, haul: 1 }, kit: ['food'], points: 0, about: 'knows where things are kept' },
  'a student': { skills: {}, kit: [], points: 4, about: 'nothing much yet' },
};

/** Traits you can pick, and what each costs (negative: gives points back). */
export const TRAIT_COST: Partial<Record<Trait, number>> = {
  tough: 4,
  quick: 4,
  graceful: 3,
  greenThumb: 2,
  cheerful: 2,
  lazy: -3,
  gloomy: -2,
  clumsy: -2,
};
export const CHOOSABLE = Object.keys(TRAIT_COST) as Trait[];
/** Pairs you can't have both of. */
export const OPPOSED: readonly (readonly [Trait, Trait])[] = [
  ['cheerful', 'gloomy'],
  ['graceful', 'clumsy'],
];

export function professionOf(job: number): Profession | undefined {
  const name = STORY_JOBS[job];
  return name === undefined ? undefined : PROFESSIONS[name];
}

/** Points left with this profession and these traits. */
export function pointsLeft(c: Pick<Character, 'job' | 'traits'>): number {
  return START_POINTS + (professionOf(c.job)?.points ?? 0) - c.traits.reduce((sum, t) => sum + (TRAIT_COST[t] ?? 0), 0);
}

/** Tidies a typed name: letters, spaces, hyphens, apostrophes; at most NAME_MAX. */
export function cleanName(name: string): string {
  return name.replace(/[^\p{L} '-]/gu, '').replace(/\s+/g, ' ').trim().slice(0, NAME_MAX).trim();
}

/** What's wrong with a character, or null if it's fine. */
export function characterProblem(c: Character): string | null {
  if (cleanName(c.name) === '') return 'needs a name';
  if (!professionOf(c.job)) return 'no such profession';
  if (new Set(c.traits).size !== c.traits.length) return 'a trait twice';
  if (c.traits.some((t) => TRAIT_COST[t] === undefined)) return "a trait you can't choose";
  for (const [a, b] of OPPOSED) if (c.traits.includes(a) && c.traits.includes(b)) return `can't be both ${a} and ${b}`;
  if (pointsLeft(c) < 0) return 'not enough points';
  return null;
}

/** Makes a survivor this character: name, past, skills, traits, and their kit (bagged if you control them). */
export function applyCharacter(state: SimState, s: Survivor, c: Character): void {
  const p = professionOf(c.job)!;
  s.name = cleanName(c.name);
  s.story.job = c.job;
  s.traits = [...c.traits];
  for (const [type, level] of Object.entries(p.skills) as [JobType, number][]) s.xp[type] = level * XP_PER_LEVEL;
  const yours = state.control?.survivorId === s.id;
  for (const kind of p.kit) {
    const item = spawnItem(state, kind, s.pos.x, s.pos.y);
    if (yours) stow(state, s, item); // what doesn't fit stays at your feet
  }
}
