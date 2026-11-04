// Mood: 0..100, MOOD_BASE when nothing is going on, moved by recent thoughts.
//
// A thought is a timed memory ("ate at a table", "saw someone die"). One of
// each kind at a time: a repeat refreshes the timer. Being hurt or hungry
// counts as a thought while it lasts. Below MISERABLE, a survivor works at
// 3/4 speed, like a hurt one; breakdowns and the rec room are in leisure.ts.

import { wellbeingFactors } from './wellbeing';
import { steel } from './resolve';
import { HUNGRY } from './food';
import { isHurt } from './health';
import { TRAIT_INFO, earnTrait } from './skills';
import { forget } from './relationships';
import { INFECTED_MOOD } from './infection';
import { SICK_MOOD } from './corpses';
import { THIRSTY_MOOD, isThirsty } from './water';
import { LEADER_MOOD, RATIONED_MOOD, policyOn } from './policies';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { DAY_LENGTH, dayScale, onClock } from './time';

export type ThoughtKind =
  | 'ateAtTable'
  | 'ateWithoutTable'
  | 'sleptInBed'
  | 'sleptOnFloor'
  | 'sawDeath'
  | 'attacked'
  | 'relaxed'
  | 'vented'
  | 'hotMeal'
  | 'soaked'
  | 'cold'
  | 'lostFriend'
  | 'stench'
  | 'paidRespects'
  | 'dog'
  | 'lostLeader'
  | 'wentHome'
  | 'laidToRest'
  | 'petFound'
  | 'burnedThePast'
  | 'confessed'
  | 'reunited'
  | 'lostHope'
  | 'oldFriend'
  | 'wellRested'
  | 'bloodUp'
  | 'shaken'
  | 'wishMet'
  | 'promiseKept'
  | 'promiseBroken';

export interface Thought {
  kind: ThoughtKind;
  /** Forgotten at this tick. */
  until: number;
}

export const THOUGHTS: Record<ThoughtKind, { label: string; effect: number; duration: number }> = {
  ateAtTable: { label: 'ate at a table', effect: 10, duration: DAY_LENGTH },
  ateWithoutTable: { label: 'ate without a table', effect: -5, duration: DAY_LENGTH },
  sleptInBed: { label: 'slept in a bed', effect: 5, duration: DAY_LENGTH },
  sleptOnFloor: { label: 'slept on the floor', effect: -10, duration: DAY_LENGTH },
  sawDeath: { label: 'someone died', effect: -15, duration: 2 * DAY_LENGTH },
  attacked: { label: 'attacked by a zombie', effect: -10, duration: DAY_LENGTH / 2 },
  relaxed: { label: 'relaxed in the rec room', effect: 10, duration: DAY_LENGTH },
  vented: { label: 'got it out of their system', effect: 15, duration: DAY_LENGTH },
  hotMeal: { label: 'had a hot meal', effect: 5, duration: DAY_LENGTH / 2 },
  soaked: { label: 'got soaked', effect: -3, duration: DAY_LENGTH / 4 },
  cold: { label: 'out in the cold (walks slower)', effect: -10, duration: DAY_LENGTH / 4 },
  lostFriend: { label: 'lost a friend', effect: -25, duration: 3 * DAY_LENGTH },
  stench: { label: 'the stench of the dead', effect: -8, duration: DAY_LENGTH / 4 },
  paidRespects: { label: 'paid their respects', effect: 15, duration: 2 * DAY_LENGTH },
  dog: { label: 'spent time with a dog', effect: 5, duration: DAY_LENGTH / 2 },
  lostLeader: { label: 'lost our leader', effect: -20, duration: 2 * DAY_LENGTH },
  wentHome: { label: 'saw to their old home', effect: 10, duration: 3 * DAY_LENGTH },
  laidToRest: { label: 'laid their dead to rest', effect: 20, duration: 4 * DAY_LENGTH },
  petFound: { label: 'their dog was found', effect: 15, duration: 3 * DAY_LENGTH },
  burnedThePast: { label: 'burned the past', effect: 15, duration: 3 * DAY_LENGTH },
  confessed: { label: 'told the others what they did', effect: 10, duration: 2 * DAY_LENGTH },
  reunited: { label: 'found someone they lost', effect: 30, duration: 5 * DAY_LENGTH },
  lostHope: { label: 'nobody answered the radio', effect: -10, duration: 3 * DAY_LENGTH },
  oldFriend: { label: 'met someone from home', effect: 10, duration: 2 * DAY_LENGTH },
  // These also do something while they last (moodlets.ts).
  wellRested: { label: 'well rested (works and swings faster)', effect: 5, duration: DAY_LENGTH / 4 },
  bloodUp: { label: 'blood up (hits harder)', effect: 5, duration: DAY_LENGTH / 24 },
  shaken: { label: 'shaken (swings slower)', effect: -5, duration: DAY_LENGTH / 24 },
  // Wishes (wishes.ts).
  wishMet: { label: 'got what they wanted', effect: 5, duration: DAY_LENGTH },
  promiseKept: { label: 'a promise kept', effect: 15, duration: 2 * DAY_LENGTH },
  promiseBroken: { label: 'a promise broken', effect: -10, duration: 2 * DAY_LENGTH },
};

/** Thought lengths at day scale 1; THOUGHTS follows the clock (time.ts). */
const BASE_DURATION = Object.fromEntries(Object.entries(THOUGHTS).map(([k, v]) => [k, v.duration])) as Record<ThoughtKind, number>;
onClock(() => {
  for (const k of Object.keys(THOUGHTS) as ThoughtKind[]) THOUGHTS[k].duration = BASE_DURATION[k] * dayScale();
});

export const MOOD_BASE = 50;
/** Resolve lost to a friend's death, and to anyone's. */
export const FRIEND_DEATH_RESOLVE = 10;
export const DEATH_RESOLVE = 3;
export const MOOD_MAX = 100;
/** Below this a survivor is miserable and works at 3/4 speed. */
export const MISERABLE = 30;
/** While hurt (see health.ts). */
export const HURT_MOOD = -10;
/** While hungry (see food.ts). */
export const HUNGRY_MOOD = -10;

export function addThought(state: SimState, s: Survivor, kind: ThoughtKind): void {
  const until = state.tick + THOUGHTS[kind].duration;
  const existing = s.thoughts.find((t) => t.kind === kind);
  if (existing) existing.until = until;
  else s.thoughts.push({ kind, until });
}

/** Drops thoughts whose time is up. Runs every tick, so `thoughts` stays short. */
export function forgetThoughts(state: SimState, s: Survivor): void {
  if (s.thoughts.some((t) => t.until <= state.tick)) s.thoughts = s.thoughts.filter((t) => t.until > state.tick);
}

/** What moves this survivor's mood right now, as [label, effect] pairs. */
export function moodFactors(state: SimState, s: Survivor): [string, number][] {
  const factors: [string, number][] = s.thoughts
    .filter((t) => t.until > state.tick)
    .map((t) => [THOUGHTS[t.kind].label, THOUGHTS[t.kind].effect]);
  if (isHurt(s.health)) factors.push(['hurt', HURT_MOOD]);
  if (s.satiety < HUNGRY) factors.push(['hungry', HUNGRY_MOOD]);
  if (s.infection !== null) factors.push(['infected', INFECTED_MOOD]);
  if (s.sick !== null) factors.push(['sick', SICK_MOOD]);
  if (isThirsty(s)) factors.push(['thirsty', THIRSTY_MOOD]);
  if (policyOn(state, 'rationing')) factors.push(['on rations', RATIONED_MOOD]);
  if (state.leaderId && state.leaderId !== s.id && state.survivors.some((o) => o.id === state.leaderId)) factors.push(['a good leader', LEADER_MOOD]);
  for (const t of s.traits) if (TRAIT_INFO[t].mood !== 0) factors.push([TRAIT_INFO[t].label, TRAIT_INFO[t].mood]);
  factors.push(...wellbeingFactors(s));
  return factors;
}

export function mood(state: SimState, s: Survivor): number {
  const total = moodFactors(state, s).reduce((sum, [, effect]) => sum + effect, MOOD_BASE);
  return Math.max(0, Math.min(MOOD_MAX, total));
}

export function isMiserable(state: SimState, s: Survivor): boolean {
  return mood(state, s) < MISERABLE;
}

/** Everyone left in the colony remembers a death; friends of the dead take it hardest. */
export function mournDeath(state: SimState, dead: Survivor): void {
  const friends = forget(state, dead);
  const wasLeader = state.leaderId === dead.id;
  if (wasLeader) state.leaderId = null;
  for (const s of state.survivors) {
    addThought(state, s, friends.has(s.id) ? 'lostFriend' : 'sawDeath');
    steel(s, friends.has(s.id) ? -FRIEND_DEATH_RESOLVE : -DEATH_RESOLVE);
    if (friends.has(s.id)) earnTrait(state, s, 'haunted', `lost ${dead.name}`);
    if (wasLeader) addThought(state, s, 'lostLeader');
  }
}
