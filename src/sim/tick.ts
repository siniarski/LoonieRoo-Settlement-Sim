// The fixed-timestep tick. Speed controls change how many ticks run per
// frame, never what a tick means.

import { reconcileJobs, updateWork } from './jobs';
import { interruptIfTired, reconcileSleep, updateRest, updateSleep } from './needs';
import type { SimState } from './state';
import { updateMovement } from './survivors';

export const TICKS_PER_SECOND = 10;

export function tick(state: SimState): void {
  reconcileJobs(state);
  reconcileSleep(state);
  for (const s of state.survivors) {
    interruptIfTired(state, s);
    updateMovement(state, s);
    // Standing still: sleep comes before work.
    if (!s.move && !s.goal && !updateSleep(state, s)) updateWork(state, s);
    updateRest(state, s);
  }
  state.tick += 1;
}

export function step(state: SimState, ticks: number): void {
  for (let i = 0; i < ticks; i++) tick(state);
}
