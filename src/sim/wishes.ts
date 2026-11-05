// Wishes: small everyday wants (like Sims 3's), one at a time per survivor.
//
// Now and then (a hash of who and the hour, never the sim RNG) someone without a wish
// picks one that fits how they are: a hot meal if there's a stove, a real bed after a
// night on the floor, a coat when it turns cold, to see someone they like, a grave to
// visit, a quiet day after a death. Each is checked against things the sim already
// tracks (mostly thoughts gained since the wish began), and lasts WISH_TICKS.
//   Met: a small lift (mood, resolve).
//   Promised (you chose to see it done) and met: a bigger lift, and they think better of you.
//   Promised and not met in time: let down (mood, resolve, and their opinion of you).
// Nobody under your control has wishes of their own.

import { logEvent } from './events';
import { ofKind } from './furniture';
import { hashRoll, idNumber } from './hash';
import { THOUGHTS, addThought, type ThoughtKind } from './mood';
import { steel } from './resolve';
import { seasonOf } from './seasons';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { DAY_LENGTH, TICKS_PER_HOUR, onClock } from './time';
import { controlled } from './control';

export type WishKind = 'hotMeal' | 'bed' | 'coat' | 'weapon' | 'see' | 'grave' | 'quiet' | 'kill' | 'company' | 'comfort';

export interface Wish {
  kind: WishKind;
  /** The survivor they want to see ('see'), else null. */
  targetId: string | null;
  /** Tick it began: what counts must happen after this. */
  since: number;
  until: number;
  promised: boolean;
}

export let WISH_TICKS = DAY_LENGTH;
onClock(() => (WISH_TICKS = DAY_LENGTH));
/** Each hour, the chance someone without a wish gets one. */
export const WISH_CHANCE = 0.2;
export const WISH_RESOLVE = 2;
export const PROMISE_RESOLVE = 8;
export const BROKEN_RESOLVE = 5;
export const PROMISE_OPINION = 10;
/** Within this many tiles (Chebyshev), both awake, counts as seeing someone. */
const SEE_RANGE = 2;

/** Did they gain this thought since the wish began (that tick included)? (A thought's start is its end less its length.) */
function gotThought(s: Survivor, kind: ThoughtKind, since: number): boolean {
  return s.thoughts.some((t) => t.kind === kind && t.until - THOUGHTS[kind].duration >= since);
}

const near = (a: Survivor, b: Survivor) => Math.max(Math.abs(a.pos.x - b.pos.x), Math.abs(a.pos.y - b.pos.y)) <= SEE_RANGE;

/** The wishes that fit this survivor right now, in a fixed order. */
function candidates(state: SimState, s: Survivor): { kind: WishKind; targetId: string | null }[] {
  const out: { kind: WishKind; targetId: string | null }[] = [];
  const has = (kind: ThoughtKind) => s.thoughts.some((t) => t.kind === kind);
  if ((ofKind(state, 'stove').length > 0)) out.push({ kind: 'hotMeal', targetId: null });
  if (has('sleptOnFloor') || s.comfort < 50) out.push({ kind: 'bed', targetId: null });
  if (!s.body && (seasonOf(state.tick) === 'autumn' || seasonOf(state.tick) === 'winter' || state.settings.winter)) out.push({ kind: 'coat', targetId: null });
  if (!s.weapon && s.priorities.guard > 0) out.push({ kind: 'weapon', targetId: null });
  const friend = state.survivors
    .filter((o) => o !== s && (s.opinions[o.id] ?? 0) >= 10)
    .sort((a, b) => (s.opinions[b.id] ?? 0) - (s.opinions[a.id] ?? 0) || a.id.localeCompare(b.id))[0];
  const you = controlled(state);
  const wanted = you && you !== s && (s.opinions[you.id] ?? 0) >= 0 ? you : friend;
  if (wanted && !near(s, wanted)) out.push({ kind: 'see', targetId: wanted.id });
  if ((has('lostFriend') || has('sawDeath')) && state.roomDesignations.some((d) => d.purpose === 'graveyard')) out.push({ kind: 'grave', targetId: null });
  if (has('sawDeath') || has('attacked')) out.push({ kind: 'quiet', targetId: null });
  if (s.kills >= 3 || s.traits.includes('hardened')) out.push({ kind: 'kill', targetId: null });
  if (s.social < 40) out.push({ kind: 'company', targetId: null });
  if (s.comfort < 40) out.push({ kind: 'comfort', targetId: null });
  return out;
}

/** Met? (For 'quiet', met means the time ran out with nothing bad happening.) */
function met(state: SimState, s: Survivor, w: Wish): boolean {
  switch (w.kind) {
    case 'hotMeal':
      return gotThought(s, 'hotMeal', w.since);
    case 'bed':
      return gotThought(s, 'sleptInBed', w.since);
    case 'coat':
      return s.body !== null;
    case 'weapon':
      return s.weapon !== null;
    case 'see': {
      const o = state.survivors.find((x) => x.id === w.targetId);
      return o !== undefined && !o.sleep?.asleep && !s.sleep?.asleep && near(s, o);
    }
    case 'grave':
      return gotThought(s, 'paidRespects', w.since);
    case 'quiet':
      return state.tick >= w.until && !gotThought(s, 'sawDeath', w.since) && !gotThought(s, 'attacked', w.since) && !gotThought(s, 'lostFriend', w.since);
    case 'kill':
      return gotThought(s, 'bloodUp', w.since);
    case 'company':
      return s.social >= 60;
    case 'comfort':
      return s.comfort >= 60;
  }
}

export function describeWish(state: SimState, w: Wish): string {
  const who = w.targetId ? state.survivors.find((x) => x.id === w.targetId) : undefined;
  return {
    hotMeal: 'a hot meal',
    bed: 'a night in a real bed',
    coat: 'a coat',
    weapon: 'a weapon',
    see: who && state.control?.survivorId === who.id ? 'a word with you' : `to see ${who?.name ?? 'someone'}`,
    grave: 'to visit the graves',
    quiet: 'a quiet day (no deaths, no attacks)',
    kill: 'to put one of them down',
    company: 'some company',
    comfort: 'somewhere decent to sleep',
  }[w.kind];
}

/** Every 10 ticks: check wishes; on the hour, maybe make new ones. */
export function updateWishes(state: SimState): void {
  if (state.tick % 10 !== 0) return;
  for (const s of state.survivors) {
    if (state.control?.survivorId === s.id) {
      s.wish = null;
      continue;
    }
    const w = s.wish;
    if (w) {
      if (met(state, s, w)) grant(state, s, w);
      else if (state.tick >= w.until) expire(state, s, w);
      continue;
    }
    if (state.tick % TICKS_PER_HOUR !== 0 || s.childUntil !== null) continue;
    if (hashRoll(idNumber(s.id), state.tick, 0x3a5) >= WISH_CHANCE) continue;
    const options = candidates(state, s);
    const pick = options[Math.floor(hashRoll(idNumber(s.id), state.tick, 0x3a6) * options.length)];
    if (pick) s.wish = { ...pick, since: state.tick, until: state.tick + WISH_TICKS, promised: false };
  }
}

function grant(state: SimState, s: Survivor, w: Wish): void {
  s.wish = null;
  addThought(state, s, w.promised ? 'promiseKept' : 'wishMet');
  steel(s, w.promised ? PROMISE_RESOLVE : WISH_RESOLVE);
  const you = controlled(state);
  if (w.promised && you && you !== s) s.opinions[you.id] = Math.min(100, (s.opinions[you.id] ?? 0) + PROMISE_OPINION);
  if (w.promised) logEvent(state, `A promise kept: ${s.name} got ${describeWish(state, w)}`);
}

function expire(state: SimState, s: Survivor, w: Wish): void {
  s.wish = null;
  if (!w.promised) return;
  addThought(state, s, 'promiseBroken');
  steel(s, -BROKEN_RESOLVE);
  const you = controlled(state);
  if (you && you !== s) s.opinions[you.id] = Math.max(-100, (s.opinions[you.id] ?? 0) - PROMISE_OPINION);
  logEvent(state, `A promise broken: ${s.name} never got ${describeWish(state, w)}`);
}

/** You promise to see someone's wish done. Returns an error, or null. */
export function promise(state: SimState, s: Survivor): string | null {
  if (!s.wish) return `${s.name} doesn't want anything just now`;
  if (s.wish.promised) return 'already promised';
  s.wish.promised = true;
  logEvent(state, `You promised ${s.name} ${describeWish(state, s.wish)}`);
  return null;
}
