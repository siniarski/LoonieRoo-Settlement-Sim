// Thirst: a third need, in a harsh game only (state.harsh).
//
// Hydration drains a point a tick. Below THIRSTY a survivor drops their job
// and walks to the nearest place to drink: next to a well, or next to a rain
// collector with water in it (both clean), or failing those, next to open
// water (a river or lake), which now and then makes them sick. Drinking
// takes DRINK_TICKS and fills them up. At 0 they lose health.
//
// Rain collectors fill while it rains or snows. While the mains are on, kitchen counters,
// toilets and baths are taps (timeline.ts).

import { scorching } from './climate';
import { TANK_WATER, isTap, waterOn } from './timeline';
import { slowTick } from './time';
import { SICK_TICKS } from './corpses';
import { isWet } from './seasons';
import { furnitureAt, ofKinds } from './furniture';
import { getTile, neighbours4, type Point } from './grid';
import { hashRoll, idNumber } from './hash';
import { logEvent } from './events';
import { findPathToAny } from './pathfinding';
import { releaseSurvivor } from './jobs';
import { cancelTreatment } from './needs';
import { cancelArming } from './weapons';
import { cancelDressing } from './clothing';
import { nearestTile, samePoint } from './space';
import type { SimState } from './state';
import { walk, type Survivor } from './survivors';

export const HYDRATION_MAX = 2400;
export const THIRSTY = 800;
export const DRINK_TICKS = 20;
/** Dehydrated (hydration 0): lose 1 health every this many ticks. */
export const DEHYDRATION_DAMAGE_EVERY = 2;
export const DRINK_RETRY_TICKS = 50;
/** Chance a drink of river water makes someone sick. */
export const DIRTY_WATER_SICK = 0.05;
export const THIRSTY_MOOD = -10;
/** Water a rain collector holds, and how often it gains one in rain or snow. */
export const COLLECTOR_MAX = 20;
export const COLLECTOR_FILL_EVERY = 50;

/** tank: what's left in a toilet's tank or a bath after the mains go off (timeline.ts TANK_WATER). */
export type WaterSource = 'well' | 'collector' | 'tap' | 'tank' | 'river';

/** Water standing in a fitting with the mains off. */
function isTank(state: SimState, f: { kind: string; look: string | null; loot: number }): boolean {
  return f.kind === 'decor' && f.look !== null && f.look in TANK_WATER && f.loot > 0 && !waterOn(state);
}

/** A drink taken from a source: collectors and tanks run down. */
export function drankFrom(state: SimState, source: WaterSource, x: number, y: number): void {
  if (source === 'collector' || source === 'tank') furnitureAt(state, x, y)!.loot -= 1;
}

export interface Drink {
  /** Where they'll stand to drink. */
  x: number;
  y: number;
  source: WaterSource;
  /** The source tile (the well, collector, or water). */
  from: Point;
  progress: number;
}

export function isThirsty(s: Survivor): boolean {
  return s.hydration < THIRSTY;
}

/** Drains hydration (harsh games only); dehydration hurts. Every tick, every survivor. */
export function updateHydration(state: SimState, s: Survivor): void {
  if (!state.harsh) return;
  if (slowTick(state.tick)) s.hydration = Math.max(0, s.hydration - (state.weather === 'heatwave' || scorching(state, s.pos.x, s.pos.y) ? 2 : 1));
  if (s.hydration === 0 && state.tick % DEHYDRATION_DAMAGE_EVERY === 0) {
    s.health = Math.max(0, s.health - 1);
    s.lastHurtBy = 'thirst';
  }
}

/** Rain collectors fill in the wet. */
export function updateCollectors(state: SimState): void {
  if (!isWet(state.weather) || state.tick % COLLECTOR_FILL_EVERY !== 0) return;
  for (const f of state.furniture) if (f.kind === 'collector' && f.loot < COLLECTOR_MAX) f.loot += 1;
}

/** What's drinkable from tile (x, y): a well, a collector with water, or open water. */
export function sourceAt(state: SimState, x: number, y: number): WaterSource | null {
  const f = furnitureAt(state, x, y);
  if (f?.kind === 'well') return 'well';
  if (f?.kind === 'collector' && f.loot > 0) return 'collector';
  if (f && isTap(state, f)) return 'tap';
  if (f && isTank(state, f)) return 'tank';
  return getTile(state.grid, x, y) === 'water' ? 'river' : null;
}

/** How far (tiles searched) anyone looks for water: about a 100-tile walk. */
export const WATER_SEARCH = 20000;

/**
 * Nearest tile to stand on and drink from: clean water first, the river only if there's none.
 * The clean sources are looked up once, not tile by tile (profiling: on a 256 map, a thirsty
 * crowd flooding the whole map with furniture lookups took 145 ms in one tick).
 */
export function findWater(state: SimState, from: Point): Drink | null {
  const { grid } = state;
  const clean = new Map<number, WaterSource>();
  const tapsOn = waterOn(state);
  // (By kind, in list order as before: wells and collectors, and with the water on the fittings that can be taps.)
  // (With the water off, decor too: what's left in toilets and baths.)
  for (const f of ofKinds(state, tapsOn ? ['well', 'collector', 'crate', 'decor'] : ['well', 'collector', 'decor'])) {
    if (f.kind === 'well') clean.set(f.y * grid.width + f.x, 'well');
    else if (f.kind === 'collector' && f.loot > 0) clean.set(f.y * grid.width + f.x, 'collector');
    else if (tapsOn && isTap(state, f)) clean.set(f.y * grid.width + f.x, 'tap');
    else if (isTank(state, f)) clean.set(f.y * grid.width + f.x, 'tank');
  }
  const passes: ((x: number, y: number) => WaterSource | undefined)[] = [
    (x, y) => clean.get(y * grid.width + x),
    (x, y) => (getTile(grid, x, y) === 'water' && !clean.has(y * grid.width + x) ? 'river' : undefined),
  ];
  for (const [i, sourceOf] of passes.entries()) {
    if (i === 0 && clean.size === 0) continue;
    let found: Drink | null = null;
    nearestTile(
      grid,
      from,
      (x, y) => {
        for (const n of neighbours4(grid, x, y)) {
          const source = sourceOf(n.x, n.y);
          if (!source) continue;
          found = { x, y, source, from: n, progress: 0 };
          return true;
        }
        return false;
      },
      WATER_SEARCH,
    );
    if (found) return found;
  }
  return null;
}

/** Interrupt work for a drink (before movement), if there's water to go to. */
export function thirstInterrupts(state: SimState, s: Survivor): boolean {
  return state.harsh && isThirsty(s) && state.tick >= s.nextDrinkTick && !s.drink;
}

/**
 * Runs when standing still, after eating and before treatment. Returns true if
 * drinking handled this tick.
 */
export function updateDrink(state: SimState, s: Survivor): boolean {
  if (s.sleep || !state.harsh) return false;
  if (!s.drink && !startDrink(state, s)) return false;
  const d = s.drink!;
  if (!samePoint(s.pos, d)) {
    const path = findPathToAny(state.grid, s.pos, [d]);
    if (!path || path.length === 0) {
      cancelDrink(s, "can't reach water");
      s.nextDrinkTick = state.tick + DRINK_RETRY_TICKS;
      return false;
    }
    walk(s, path, state.walkabilityVersion);
    return true;
  }
  const source = sourceAt(state, d.from.x, d.from.y);
  if (source !== d.source) {
    cancelDrink(s, 'the water ran dry');
    return false;
  }
  d.progress += 1;
  if (d.progress === 1) s.note = `drinking from ${d.source === 'river' ? 'the river' : `a ${d.source}`}`;
  if (d.progress < DRINK_TICKS) return true;
  s.hydration = HYDRATION_MAX;
  s.drink = null;
  drankFrom(state, d.source, d.from.x, d.from.y);
  if (d.source === 'river' && s.sick === null && hashRoll(idNumber(s.id), state.tick, 7) < DIRTY_WATER_SICK) {
    s.sick = SICK_TICKS / 2;
    logEvent(state, `${s.name} got sick from river water (wells and rain collectors are clean)`);
  }
  s.note = 'had a drink';
  return true;
}

function startDrink(state: SimState, s: Survivor): boolean {
  if (!isThirsty(s) || state.tick < s.nextDrinkTick) return false;
  const d = findWater(state, s.pos);
  if (!d) {
    s.nextDrinkTick = state.tick + DRINK_RETRY_TICKS;
    return false;
  }
  if (s.jobId) releaseSurvivor(state, s, 'thirsty');
  cancelTreatment(state, s, 'thirsty');
  cancelArming(state, s, 'thirsty');
  cancelDressing(state, s, 'thirsty');
  s.drink = d;
  s.note = `going for a drink at (${d.x}, ${d.y})`;
  return true;
}

export function cancelDrink(s: Survivor, note: string): void {
  if (!s.drink) return;
  s.drink = null;
  s.goal = null;
  s.path = [];
  s.pathVersion = -1;
  s.note = note;
}
