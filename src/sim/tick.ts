// The fixed-timestep tick. Speed controls change how many ticks run per
// frame, never what a tick means.

import { reconcileJobs, updateWork } from './jobs';
import { interruptForNeeds, reconcileNeeds, updateMeal, updateRest, updateSatiety, updateSleep } from './needs';
import { reconcileRoomDesignations } from './rooms';
import type { SimState } from './state';
import { updateMovement } from './survivors';

export const TICKS_PER_SECOND = 10;

export function tick(state: SimState): void {
  reconcileJobs(state);
  reconcileNeeds(state);
  for (const s of state.survivors) {
    interruptForNeeds(state, s);
    updateMovement(state, s);
    // Standing still: eat, then sleep, then work.
    if (!s.move && !s.goal && !updateMeal(state, s) && !updateSleep(state, s)) updateWork(state, s);
    updateRest(state, s);
    updateSatiety(s);
  }
  // Walls finished this tick may have covered a room's anchor tile; slide it off now
  // so state is consistent between ticks (it also runs at the start, for commands).
  reconcileRoomDesignations(state);
  state.tick += 1;
}

export function step(state: SimState, ticks: number): void {
  for (let i = 0; i < ticks; i++) tick(state);
}
