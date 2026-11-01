// The fixed-timestep tick. Speed controls change how many ticks run per
// frame, never what a tick means.

import { updateFarm } from './farming';
import { interruptScouting, reconcileJobs, updateWork } from './jobs';
import {
  interruptForNeeds,
  killSurvivor,
  reconcileNeeds,
  updateHealth,
  updateMeal,
  updateRest,
  updateSatiety,
  updateSleep,
  updateTreatment,
  wake,
  wakeLightSleepers,
} from './needs';
import { reconcileRoomDesignations } from './rooms';
import type { SimState } from './state';
import { updateMovement, type Survivor } from './survivors';
import { reconcileWanderers, updateWanderers } from './wanderers';
import { updateArming } from './weapons';
import { updateBreakdown, updateLeisure } from './leisure';
import { reconcileExploreAreas, reconcilePointsOfInterest, updateFog } from './fog';
import { updateHerds } from './herds';
import { updateWeather } from './seasons';
import { forgetThoughts } from './mood';
import { updateZombies } from './zombies';

export const TICKS_PER_SECOND = 10;

export function tick(state: SimState): void {
  // Zombies first (spawn, dawn retreat, move, attack), so jobs see this tick's
  // zombies and survivors react to this tick's hits.
  updateZombies(state);
  updateHerds(state);
  updateWeather(state);
  updateFarm(state);
  updateWanderers(state);
  reconcileExploreAreas(state);
  reconcilePointsOfInterest(state);
  for (const s of state.survivors) {
    if (s.sleep && s.hurtTick === state.tick) wake(state, s, 'woken by a zombie!');
  }
  wakeLightSleepers(state);
  reconcileJobs(state);
  reconcileNeeds(state);
  const dying: { s: Survivor; cause: string }[] = [];
  for (const s of state.survivors) {
    updateBreakdown(state, s);
    interruptForNeeds(state, s);
    interruptScouting(state, s);
    updateMovement(state, s);
    // Standing still: eat, then treat wounds, then pick up a weapon, then sleep, then relax or work
    // (not mid-breakdown). A need taking over ends any rec-room visit.
    if (!s.move && !s.goal) {
      const needs = updateMeal(state, s) || updateTreatment(state, s) || updateArming(state, s) || updateSleep(state, s);
      if (needs) s.leisure = null;
      else if (!s.breakdown && !updateLeisure(state, s)) updateWork(state, s);
    }
    updateRest(state, s);
    updateSatiety(s);
    forgetThoughts(state, s);
    const cause = updateHealth(state, s);
    if (cause) dying.push({ s, cause });
  }
  // Deaths after the loop, so the survivor array isn't spliced while iterating it.
  for (const { s, cause } of dying) killSurvivor(state, s, cause);
  // Work finished this tick may have covered a room's anchor tile or removed a bed
  // someone sleeps in; tidy up now so state is consistent between ticks (both also
  // run at the start, for commands applied in between).
  reconcileRoomDesignations(state);
  reconcileNeeds(state);
  reconcileWanderers(state);
  // Everyone looks round where they now stand.
  updateFog(state);
  state.tick += 1;
}

export function step(state: SimState, ticks: number): void {
  for (let i = 0; i < ticks; i++) tick(state);
}
