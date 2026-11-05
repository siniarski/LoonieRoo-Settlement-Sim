// The world winding down: the mains, and things that happen out there.
//
//   the mains    a generated world starts with the water and the power still on. Each goes off
//                for good on a day rolled from the seed (rollTimeline), sooner the further on
//                the world is (settings.progression: weeks, months, a year, years; a year on,
//                they're off from the start). Hand-made maps have no mains.
//     water on   kitchen counters, toilets and baths are taps: clean water (water.ts).
//     power on   iceboxes keep food like fridges (power.ts spoilage), and indoors is lit at
//                night (inLight: guards hit harder, you see as far as by day: sight.ts).
//   happenings   while the world is still winding down (until a few days after the power goes):
//     helicopter on a rolled day a helicopter comes over, hangs over you (the one you control,
//                else the first survivor) for HELI_HOVER ticks, then flies off, thundering
//                (noise 'helicopter') every HELI_NOISE_EVERY ticks: it drags the dead after it.
//     alarms     smash a window into a building while the power's on and its alarm may go off
//                (ALARM_CHANCE), ringing (noise 'alarm') for ALARM_TICKS.
//     far off    now and then, on the hour, gunshots or a scream somewhere FAR_MIN..FAR_MAX
//                tiles away: real noise, which moves the dead out there.
// Rolls use hash.ts (seed and tick), not the sim RNG.

import { logEvent } from './events';
import type { Furniture } from './furniture';
import { inBounds, type Point } from './grid';
import { hashRoll } from './hash';
import { makeNoise } from './noise';
import { OUTDOORS, labelAt } from './rooms';
import type { SimState } from './state';
import { DAY_LENGTH, TICKS_PER_HOUR, dayOf, isNight, onClock, tickOfDay } from './time';

export interface Timeline {
  /** The day the taps run dry; 1 or less: already off when the world began. */
  waterOffDay: number;
  /** The day the power goes. */
  powerOffDay: number;
  /** The day the helicopter comes over; 0 for never. */
  helicopterDay: number;
  /** The helicopter, while it's here. */
  heli: { x: number; y: number; leaveTick: number; exit: Point } | null;
  /** Alarms ringing. */
  alarms: { x: number; y: number; untilTick: number }[];
}

/** [earliest, latest] day the water goes, the power goes, and the helicopter comes, by progression. */
const WINDOWS: readonly { water: [number, number]; power: [number, number]; heli: [number, number] }[] = [
  { water: [8, 14], power: [10, 18], heli: [3, 6] }, // weeks
  { water: [3, 7], power: [4, 9], heli: [2, 5] }, // months
  { water: [0, 0], power: [0, 0], heli: [0, 0] }, // a year
  { water: [0, 0], power: [0, 0], heli: [0, 0] }, // years
];

export const HELI_HOVER = 300;
export const HELI_NOISE_EVERY = 30;
/** Ticks per tile the helicopter flies. */
export const HELI_STEP = 2;
export const ALARM_CHANCE = 0.5;
export let ALARM_TICKS = TICKS_PER_HOUR;
onClock(() => (ALARM_TICKS = TICKS_PER_HOUR));
/** Chance, each hour while the world winds down, of something heard far off. */
export const FAR_CHANCE = 0.06;
export const FAR_MIN = 25;
export const FAR_MAX = 45;
/** Happenings go on until this many days after the power goes. */
export const WIND_DOWN_DAYS = 3;
/** Looks that are taps while the water's on. */
export const TAP_LOOKS: readonly string[] = ['counter', 'toilet', 'bath'];

/** Sets the days the mains go off and the helicopter comes (generated worlds). */
export function rollTimeline(state: SimState, seed: number): void {
  const w = WINDOWS[Math.max(0, Math.min(WINDOWS.length - 1, state.settings.progression))]!;
  const roll = ([lo, hi]: [number, number], key: number) => (hi === 0 ? 0 : lo + Math.floor(hashRoll(seed, key) * (hi - lo + 1)));
  state.timeline = {
    waterOffDay: roll(w.water, 0x7a7e),
    powerOffDay: roll(w.power, 0x9077),
    helicopterDay: roll(w.heli, 0x4e11),
    heli: null,
    alarms: [],
  };
}

export function waterOn(state: SimState): boolean {
  return state.timeline != null && dayOf(state.tick) < state.timeline.waterOffDay;
}

export function powerOn(state: SimState): boolean {
  return state.timeline != null && dayOf(state.tick) < state.timeline.powerOffDay;
}

/** Is this a tap with water coming out of it? */
export function isTap(state: SimState, f: Furniture): boolean {
  return f.look !== null && TAP_LOOKS.includes(f.look) && waterOn(state);
}

/** Does this keep food cold (an icebox with the power on)? */
export function isMainsFridge(state: SimState, f: Furniture): boolean {
  return f.look === 'icebox' && powerOn(state);
}

/** Indoors at night with the power on: the lights are on. */
export function mainsLit(state: SimState, p: Point): boolean {
  return isNight(state.tick) && powerOn(state) && inBounds(state.grid, p.x, p.y) && labelAt(state, p.x, p.y) !== OUTDOORS;
}

/** Who the world's happenings come looking for: the one you control, else the first survivor. */
function focus(state: SimState): Point | null {
  const you = state.control ? state.survivors.find((s) => s.id === state.control!.survivorId) : undefined;
  return (you ?? state.survivors[0])?.pos ?? null;
}

/** A point `r` tiles from `p` in the direction `a` (radians), on the map. */
function off(state: SimState, p: Point, a: number, r: number): Point {
  const { width, height } = state.grid;
  return {
    x: Math.max(0, Math.min(width - 1, Math.round(p.x + Math.cos(a) * r))),
    y: Math.max(0, Math.min(height - 1, Math.round(p.y + Math.sin(a) * r))),
  };
}

/** Which way, in words, from a to b. */
export function compass(a: Point, b: Point): string {
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  const ns = Math.abs(dy) * 2 > Math.abs(dx) ? (dy < 0 ? 'north' : 'south') : '';
  const ew = Math.abs(dx) * 2 > Math.abs(dy) ? (dx < 0 ? 'west' : 'east') : '';
  return ns && ew ? `${ns}-${ew}` : ns || ew || 'nearby';
}

/** Ring an alarm (a window smashed into a building while the power's on, maybe). */
export function maybeAlarm(state: SimState, p: Point, key: number): void {
  const t = state.timeline;
  if (!t || !powerOn(state) || hashRoll(key, state.tick, 0xa1a) >= ALARM_CHANCE) return;
  t.alarms.push({ x: p.x, y: p.y, untilTick: state.tick + ALARM_TICKS });
  logEvent(state, `An alarm went off at (${p.x}, ${p.y})!`);
  makeNoise(state, p, 'alarm');
}

/** Every tick: the mains going off, the helicopter, alarms, noises far off. */
export function updateTimeline(state: SimState): void {
  const t = state.timeline;
  if (!t) return;
  const day = dayOf(state.tick);
  if (tickOfDay(state.tick) === 0 && state.tick > 0) {
    if (day === t.waterOffDay) logEvent(state, 'The taps ran dry: the water has been shut off.');
    if (day === t.powerOffDay) logEvent(state, 'The lights went out: the power is off for good.');
  }
  // Alarms ring until they run down.
  t.alarms = t.alarms.filter((a) => state.tick < a.untilTick);
  if (state.tick % 20 === 0) for (const a of t.alarms) makeNoise(state, a, 'alarm');
  updateHeli(state, t);
  // Far off, now and then.
  const winding = t.powerOffDay > 0 && day < t.powerOffDay + WIND_DOWN_DAYS;
  const you = focus(state);
  if (winding && you && state.tick % TICKS_PER_HOUR === 0 && hashRoll(state.world?.seed ?? 0, state.tick, 0xfa2) < FAR_CHANCE) {
    const a = hashRoll(state.tick, 0xfa3) * Math.PI * 2;
    const at = off(state, you, a, FAR_MIN + Math.floor(hashRoll(state.tick, 0xfa4) * (FAR_MAX - FAR_MIN + 1)));
    const shots = hashRoll(state.tick, 0xfa5) < 0.6;
    makeNoise(state, at, shots ? 'gunfire' : 'scream');
    logEvent(state, `${shots ? 'Gunshots' : 'A scream'}, far off to the ${compass(you, at)}.`);
  }
}

function updateHeli(state: SimState, t: Timeline): void {
  const you = focus(state);
  if (!t.heli) {
    // It comes in the afternoon of its day, from far off.
    if (t.helicopterDay === 0 || dayOf(state.tick) !== t.helicopterDay || !you) return;
    if (tickOfDay(state.tick) !== Math.floor(DAY_LENGTH * 0.6)) return;
    const a = hashRoll(state.world?.seed ?? 0, 0x4e12) * Math.PI * 2;
    const from = off(state, you, a, 60);
    const exit = off(state, you, a + Math.PI, 80);
    t.heli = { x: from.x, y: from.y, leaveTick: state.tick + HELI_HOVER + 60 * HELI_STEP, exit };
    logEvent(state, `A helicopter, coming in from the ${compass(you, from)}...`);
    return;
  }
  const h = t.heli;
  const leaving = state.tick >= h.leaveTick;
  const goal = leaving ? h.exit : you ?? h.exit;
  if (state.tick % HELI_STEP === 0) {
    h.x += Math.sign(goal.x - h.x);
    h.y += Math.sign(goal.y - h.y);
  }
  if (state.tick % HELI_NOISE_EVERY === 0) makeNoise(state, h, 'helicopter');
  if (leaving && h.x === h.exit.x && h.y === h.exit.y) {
    t.heli = null;
    t.helicopterDay = 0;
    logEvent(state, 'The helicopter is gone. The dead it drew are not.');
  }
}
