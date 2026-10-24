// The fixed-timestep tick. Speed controls change how many ticks run per
// frame, never what a tick means.

import type { SimState } from './state';

export const TICKS_PER_SECOND = 10;

export function tick(state: SimState): void {
  // Milestone 1: nothing moves yet. Survivors, jobs, and needs hook in here.
  state.tick += 1;
}

export function step(state: SimState, ticks: number): void {
  for (let i = 0; i < ticks; i++) tick(state);
}
