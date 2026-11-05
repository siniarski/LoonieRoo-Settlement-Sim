// The fixed-timestep tick. Speed controls change how many ticks run per
// frame, never what a tick means.

import { closeBehind } from './doors';
import { updateVehicles } from './vehicles';
import { updateTimeline } from './timeline';
import { syncStep, type Body } from './motion';
import { updateHelicopter } from './escape';
import { updateRadiation } from './climate';
import { updateBody } from './body';
import { setDayScale } from './time';
import { forgetForaging } from './handiwork';
import { DAY_LENGTH } from './time';
import { updateWishes } from './wishes';
import { updateSocial } from './wellbeing';
import { dawnResolve } from './resolve';
import { updateResidents } from './residents';
import { updateColonies } from './colonies';
import { updateTasks } from './talk';
import { keepOrder } from './orders';
import { updateFarm } from './farming';
import { interruptForFight, interruptScouting, reconcileJobs, updateWork } from './jobs';
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
import { updateDressing } from './clothing';
import { updateBreakdown, updateLeisure } from './leisure';
import { reconcileExploreAreas, reconcilePointsOfInterest, updateFog } from './fog';
import { updateHerds } from './herds';
import { updateWeather } from './seasons';
import { updateRelationships } from './relationships';
import { updateExpeditions } from './expeditions';
import { updateVisitors } from './visitors';
import { forgetThoughts } from './mood';
import { ZOMBIE_HEALTH, addZombie, updateZombies } from './zombies';
import { updateInfection } from './infection';
import { updateFires } from './fire';
import { updateCorpses } from './corpses';
import { updateCollectors, updateDrink, updateHydration } from './water';
import { updateHeaters, updatePower, updateSpoilage } from './power';
import { updateAnimals } from './animals';
import { updateDirector } from './director';
import { updatePrisoners } from './prisoners';
import { updateRadio } from './radio';
import { updateEndgame } from './endgame';
import { updateLeader } from './policies';
import { updateDefences } from './defences';
import { updateOutposts } from './outposts';
import { updateWounds } from './wounds';
import { countWatch } from './towers';
import { stopReading, updateReading } from './books';
import { updateDogs } from './dogs';
import { updateSettlements } from './settlements';
import { updateFamily } from './family';
import { isControlled, reconcileControl, updateControlled } from './control';

export const TICKS_PER_SECOND = 10;

export function tick(state: SimState): void {
  setDayScale(state.settings.dayScale);
  for (const e of movers(state)) e.was = { x: e.at.x, y: e.at.y };
  // Zombies first (spawn, dawn retreat, move, attack), so jobs see this tick's
  // zombies and survivors react to this tick's hits.
  updateZombies(state);
  updateFires(state);
  updateCorpses(state);
  updateCollectors(state);
  updatePower(state);
  updateHeaters(state);
  updateAnimals(state);
  updateDirector(state);
  updatePrisoners(state);
  updateRadio(state);
  updateEndgame(state);
  updateHelicopter(state);
  updateLeader(state);
  updateDefences(state);
  updateOutposts(state);
  updateSpoilage(state);
  updateDogs(state);
  updateTasks(state);
  updateSettlements(state);
  updateColonies(state);
  updateResidents(state);
  updateFamily(state);
  updateHerds(state);
  updateWeather(state);
  updateTimeline(state);
  updateVehicles(state);
  updateRelationships(state);
  updateSocial(state);
  if (state.tick % DAY_LENGTH === 0) forgetForaging(state);
  updateWishes(state);
  updateExpeditions(state);
  updateVisitors(state);
  updateFarm(state);
  updateWanderers(state);
  reconcileExploreAreas(state);
  reconcilePointsOfInterest(state);
  for (const s of state.survivors) {
    if (s.sleep && s.hurtTick === state.tick) wake(state, s, 'woken by a zombie!');
  }
  wakeLightSleepers(state);
  for (const s of dawnResolve(state)) killSurvivor(state, s, 'walked away', true);
  reconcileJobs(state);
  reconcileNeeds(state);
  const dying: { s: Survivor; cause: string }[] = [];
  for (const s of state.survivors) {
    if (isControlled(state, s)) updateControlled(state, s);
    else behave(state, s);
    updateRest(state, s);
    updateSatiety(state, s);
    updateWounds(state, s);
    updateHydration(state, s);
    updateBody(state, s);
    updateRadiation(state, s);
    countWatch(state, s);
    forgetThoughts(state, s);
    const cause = updateHealth(state, s) ?? updateInfection(state, s);
    if (cause) dying.push({ s, cause });
  }
  // Deaths after the loop, so the survivor array isn't spliced while iterating it.
  for (const { s, cause } of dying) {
    killSurvivor(state, s, cause);
    // The infection's victims get up again.
    if (cause === 'the infection') addZombie(state, s.pos.x, s.pos.y, ZOMBIE_HEALTH, 'static');
  }
  // Work finished this tick may have covered a room's anchor tile or removed a bed
  // someone sleeps in; tidy up now so state is consistent between ticks (both also
  // run at the start, for commands applied in between).
  reconcileRoomDesignations(state);
  reconcileNeeds(state);
  reconcileWanderers(state);
  reconcileControl(state);
  // Doors held open for someone passing swing shut once they're through (doors.ts).
  closeBehind(state);
  // Precise positions follow tile steps (free movers keep their own: motion.ts).
  for (const e of movers(state)) if (!isFree(state, e)) syncStep(e);
  // Everyone looks round where they now stand.
  updateFog(state);
  state.tick += 1;
}

/** What a survivor does on their own: needs first, then leisure, then work. */
function behave(state: SimState, s: Survivor): void {
  updateBreakdown(state, s);
  interruptForNeeds(state, s);
  interruptScouting(state, s);
  interruptForFight(state, s);
  updateMovement(state, s);
  // Standing still: eat, then treat wounds, then pick up a weapon, then sleep, then relax or work
  // (not mid-breakdown). A need taking over ends any rec-room visit.
  if (s.move || s.goal) return;
  const needs = updateMeal(state, s) || updateDrink(state, s) || updateTreatment(state, s) || updateArming(state, s) || updateDressing(state, s) || updateSleep(state, s);
  if (needs) {
    s.leisure = null;
    stopReading(state, s, s.note ?? '');
  } else if (!s.breakdown && !keepOrder(state, s) && !updateLeisure(state, s) && !updateReading(state, s)) updateWork(state, s);
}

/** Everything with a precise position (motion.ts). */
function movers(state: SimState): Body[] {
  return [...state.survivors, ...state.zombies, ...state.dogs, ...state.animals, ...state.residents];
}

/** Moving under its own steering, not tile steps (motion.ts). */
function isFree(state: SimState, e: Body): boolean {
  const id = (e as { id?: string }).id ?? '';
  return id.startsWith('z') || (state.control !== null && id === state.control.survivorId);
}

export function step(state: SimState, ticks: number): void {
  for (let i = 0; i < ticks; i++) tick(state);
}
