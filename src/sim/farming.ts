// Farming: a renewable food source.
//
// The player designates farm plots on floor tiles. A plot cycles:
//   empty --(sow job)--> growing --(GROW_TICKS)--> ripe --(harvest job)--> empty, plus one food
// Sowing and harvesting are both the `farm` job type (jobs.ts). Harvested
// food lands on the plot and is hauled to storage like any other item.

import { regrowTrees } from './furniture';
import { getTile, type Point } from './grid';
import type { SimState } from './state';

/** Ticks from sowing to ripe: half a day (12 in-game hours, 2 min at 1x). */
export const GROW_TICKS = 2400;
/** Ticks of work to sow or to harvest a plot. */
export const FARM_WORK = 20;

export interface FarmPlot {
  id: string;
  x: number;
  y: number;
  planted: boolean;
  /** Ticks grown since sowing, 0..GROW_TICKS. */
  growth: number;
}

export function plotAt(state: SimState, x: number, y: number): FarmPlot | undefined {
  return state.farm.find((p) => p.x === x && p.y === y);
}

export function getPlot(state: SimState, id: string): FarmPlot | undefined {
  return state.farm.find((p) => p.id === id);
}

export function isRipe(plot: FarmPlot): boolean {
  return plot.planted && plot.growth >= GROW_TICKS;
}

/** Does this plot need a survivor right now (to sow, or to harvest)? */
export function needsWork(plot: FarmPlot): boolean {
  return !plot.planted || isRipe(plot);
}

export function addPlot(state: SimState, x: number, y: number): FarmPlot {
  const plot: FarmPlot = { id: `p${state.nextId++}`, x, y, planted: false, growth: 0 };
  state.farm.push(plot);
  return plot;
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

/** Crops grow every tick; plots whose tile stopped being floor (wall or door built) are lost. */
export function updateFarm(state: SimState): void {
  regrowTrees(state);
  for (const plot of [...state.farm]) {
    if (getTile(state.grid, plot.x, plot.y) !== 'floor') {
      removePlot(state, plot);
      continue;
    }
    if (plot.planted && plot.growth < GROW_TICKS) plot.growth += 1;
  }
}

export function plotPoint(plot: FarmPlot): Point {
  return { x: plot.x, y: plot.y };
}
