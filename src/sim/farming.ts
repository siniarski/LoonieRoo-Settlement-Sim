// Farming: a renewable food source.
//
// The player designates farm plots on floor tiles, each with a crop. A plot cycles:
//   empty --(sow job)--> growing --(its crop's grow time)--> ripe --(harvest job)--> empty, plus the yield
// Sowing and harvesting are both the `farm` job type (jobs.ts). Harvested
// food lands on the plot and is hauled to storage like any other item.
//
// Crops: potatoes (quick, one food), wheat (slow, three food), herbs (slow,
// one medicine). Outdoors they grow as the season allows (seasons.ts); in a
// complete greenhouse they grow at full speed all year, winter included.
//
// Chicken coops (furniture) lay a food every EGG_EVERY ticks, half as often in winter.

import { slowTick } from './time';
import { cropGrows, seasonOf } from './seasons';
import { regrowTrees, ofKind } from './furniture';
import { getTile, type Point } from './grid';
import { spawnItem, type ItemKind } from './items';
import { completeRooms, labelAt } from './rooms';
import type { SimState } from './state';

export type Crop = 'potato' | 'wheat' | 'herb';
export const CROPS: Record<Crop, { grow: number; yield: number; item: ItemKind; label: string }> = {
  potato: { grow: 2400, yield: 1, item: 'food', label: 'Potatoes' },
  wheat: { grow: 4800, yield: 3, item: 'food', label: 'Wheat' },
  herb: { grow: 4800, yield: 1, item: 'medicine', label: 'Herbs' },
};

/** Ticks from sowing to ripe for potatoes: half a day (12 in-game hours, 2 min at 1x). */
export const GROW_TICKS = CROPS.potato.grow;
/** Ticks of work to sow or to harvest a plot. */
export const FARM_WORK = 20;
/** A coop lays a food this often (twice as long in winter). */
export const EGG_EVERY = 1200;

export interface FarmPlot {
  id: string;
  x: number;
  y: number;
  crop: Crop;
  planted: boolean;
  /** Ticks grown since sowing, 0..its crop's grow time. */
  growth: number;
}

export function plotAt(state: SimState, x: number, y: number): FarmPlot | undefined {
  return state.farm.find((p) => p.x === x && p.y === y);
}

export function getPlot(state: SimState, id: string): FarmPlot | undefined {
  return state.farm.find((p) => p.id === id);
}

export function growTicks(plot: FarmPlot): number {
  return CROPS[plot.crop].grow;
}

export function isRipe(plot: FarmPlot): boolean {
  return plot.planted && plot.growth >= growTicks(plot);
}

/** Does this plot need a survivor right now (to sow, or to harvest)? */
export function needsWork(plot: FarmPlot): boolean {
  return !plot.planted || isRipe(plot);
}

export function addPlot(state: SimState, x: number, y: number, crop: Crop = 'potato'): FarmPlot {
  const plot: FarmPlot = { id: `p${state.nextId++}`, x, y, crop, planted: false, growth: 0 };
  state.farm.push(plot);
  return plot;
}

export function isCrop(c: unknown): c is Crop {
  return c === 'potato' || c === 'wheat' || c === 'herb';
}

export function removePlot(state: SimState, plot: FarmPlot): void {
  const i = state.farm.indexOf(plot);
  if (i >= 0) state.farm.splice(i, 1);
}

/** Removes the plot on a tile (if any), e.g. when a wall or door is built there. */
export function clearPlot(state: SimState, p: Point): void {
  const plot = plotAt(state, p.x, p.y);
  if (plot) removePlot(state, plot);
}

/** The harvest: the crop's yield on the plot. */
export function harvest(state: SimState, plot: FarmPlot): void {
  const crop = CROPS[plot.crop];
  for (let n = 0; n < crop.yield; n++) spawnItem(state, crop.item, plot.x, plot.y);
  plot.planted = false;
  plot.growth = 0;
}

/** Crops grow every tick (as the season allows, or always in a greenhouse); plots whose tile stopped being floor are lost. Coops lay. */
export function updateFarm(state: SimState): void {
  if (!slowTick(state.tick)) return;
  regrowTrees(state);
  const outdoors = cropGrows(state); // seasons and rain (seasons.ts)
  const greenhouses = state.farm.length > 0 ? completeRooms(state, 'greenhouse') : new Set<number>();
  for (const plot of [...state.farm]) {
    if (getTile(state.grid, plot.x, plot.y) !== 'floor') {
      removePlot(state, plot);
      continue;
    }
    const growth = greenhouses.has(labelAt(state, plot.x, plot.y)) ? 1 : outdoors;
    if (plot.planted && plot.growth < growTicks(plot)) plot.growth = Math.min(growTicks(plot), plot.growth + growth);
  }
  const every = seasonOf(state.tick) === 'winter' ? 2 * EGG_EVERY : EGG_EVERY;
  if (state.tick > 0 && state.tick % every === 0) for (const f of ofKind(state, 'coop')) spawnItem(state, 'food', f.x, f.y);
}

export function plotPoint(plot: FarmPlot): Point {
  return { x: plot.x, y: plot.y };
}
