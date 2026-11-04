// Difficulty: a few named presets over sandbox settings, chosen when a world is made.
//
// Every setting has a "normal" value at which the game plays exactly as it did
// before settings existed (hand-made maps and older tests use NORMAL).
//   start        a group of three in a hut, or one person alone, under your control
//   zombies      percent of the usual zombies: each night's, and those asleep in ruins
//   weather      mild, normal, harsh, extreme: how often weather events come, and how hard the cold bites
//   progression  how long since the outbreak: weeks, months, a year, years. Later means
//                less left in the crates, worn-out finds, and more fallen-in walls
//   winter       nuclear winter: crops grow at half speed, and every night is cold outdoors
//   dayScale     day length, 1-3 times the usual (time.ts)
//   map          mainland, an island, an island chain, or a fenced quarantine zone (worldgen.ts)

import { GARMENT_LIFE, isGarment } from './clothing';
import { spawnItem, type Item, type ItemKind } from './items';
import type { SimState } from './state';
import { WEAPON_KILLS } from './weapons';

export type Start = 'group' | 'solo';
export const WEATHER_LEVELS = ['mild', 'normal', 'harsh', 'extreme'] as const;
export const PROGRESSION_LEVELS = ['weeks', 'months', 'a year', 'years'] as const;

export interface Settings {
  start: Start;
  /** Percent of the usual zombies, 25-300. */
  zombies: number;
  /** Index into WEATHER_LEVELS. */
  weather: number;
  /** Index into PROGRESSION_LEVELS. */
  progression: number;
  /** Nuclear winter. */
  winter: boolean;
  /** Day length: 1 (8 minutes at 1x), 2 or 3 times as long (time.ts). */
  dayScale: number;
  /** The shape of the land (worldgen.ts). */
  map: MapType;
}

export type MapType = 'mainland' | 'island' | 'islands' | 'quarantine';
export const MAP_TYPES: readonly (readonly [MapType, string])[] = [
  ['mainland', 'Mainland'],
  ['island', 'An island'],
  ['islands', 'An island chain'],
  ['quarantine', 'A fenced quarantine zone'],
];

export const NORMAL: Settings = { start: 'group', zombies: 100, weather: 1, progression: 1, winter: false, dayScale: 1, map: 'mainland' };

export const PRESETS = {
  settler: { label: 'Settler: a group, a gentle start', settings: { start: 'group', zombies: 60, weather: 0, progression: 0, winter: false, dayScale: 1, map: 'mainland' } },
  survivor: { label: 'Survivor: the usual game', settings: NORMAL },
  apocalypse: { label: 'Apocalypse: alone, a year on', settings: { start: 'solo', zombies: 150, weather: 2, progression: 2, winter: false, dayScale: 2, map: 'mainland' } },
  winter: { label: 'Nuclear winter: alone, years on, no summer', settings: { start: 'solo', zombies: 125, weather: 3, progression: 3, winter: true, dayScale: 2, map: 'islands' } },
} as const satisfies Record<string, { label: string; settings: Settings }>;
export type PresetId = keyof typeof PRESETS;

/** Weather events (blizzards, heatwaves, storms) come this many times as often. */
const EVENT_SCALE = [0.5, 1, 1.5, 2];
/** Ticks between each point of cold damage. */
const COLD_EVERY = [20, 10, 7, 5];
/** Crates hold this share of the usual loot (at least one). */
const LOOT_SCALE = [1.5, 1, 0.6, 0.35];
/** Found weapons and clothing are this worn already (a share of their life). */
const FOUND_WEAR = [0, 0, 0.3, 0.5];
/** Extra share of ruin walls that have fallen in. */
const RUIN_DECAY = [0, 0, 0.1, 0.2];

export function zombieScale(state: SimState, count: number): number {
  return Math.round((count * state.settings.zombies) / 100);
}

export function eventChance(state: SimState, base: number): number {
  return Math.min(0.6, base * EVENT_SCALE[state.settings.weather]!);
}

export function coldEvery(state: SimState): number {
  return COLD_EVERY[state.settings.weather]!;
}

export function crateLoot(state: SimState, loot: number): number {
  return Math.max(1, Math.round(loot * LOOT_SCALE[state.settings.progression]!));
}

export function ruinDecay(state: SimState): number {
  return RUIN_DECAY[state.settings.progression]!;
}

/** Something found while scavenging: weapons and clothing come worn, the later it is since the outbreak. */
export function spawnFind(state: SimState, kind: ItemKind, x: number, y: number): Item {
  const item = spawnItem(state, kind, x, y);
  const life = kind === 'weapon' ? WEAPON_KILLS : isGarment(kind) ? GARMENT_LIFE[kind] : 0;
  item.wear = Math.floor(life * FOUND_WEAR[state.settings.progression]!);
  return item;
}

export function isSettings(v: unknown): v is Settings {
  if (typeof v !== 'object' || v === null) return false;
  const s = v as Record<string, unknown>;
  const level = (n: unknown, max: number) => Number.isInteger(n) && (n as number) >= 0 && (n as number) < max;
  return (
    (s.start === 'group' || s.start === 'solo') &&
    Number.isInteger(s.zombies) && (s.zombies as number) >= 25 && (s.zombies as number) <= 300 &&
    level(s.weather, WEATHER_LEVELS.length) &&
    level(s.progression, PROGRESSION_LEVELS.length) &&
    typeof s.winter === 'boolean' &&
    (s.dayScale === 1 || s.dayScale === 2 || s.dayScale === 3) &&
    MAP_TYPES.some(([m]) => m === s.map)
  );
}
