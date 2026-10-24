// The fixed-timestep tick. Speed controls change how many ticks run per
// frame, never what a tick means.

import type { SimState } from './state';
import { updateSurvivors } from './survivors';

export const TICKS_PER_SECOND = 10;

export function tick(state: SimState): void {
  updateSurvivors(state);
  state.tick += 1;
}

export function step(state: SimState, ticks: number): void {
  for (let i = 0; i < ticks; i++) tick(state);
}
