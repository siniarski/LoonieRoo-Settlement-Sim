// Scavenging runs: neighbours' people out in the world, looking for food.
//
// At dawn a settlement that's short of food (or now and then one that isn't) with RUN_MIN_HOME
// people to spare may send RUN_PARTY of them to a ruin RUN_MIN..RUN_MAX tiles off (by hash). The
// party is a marker, like a caravan (caravans.ts): out a tile every RUN_STEP ticks, searching
// for RUN_SEARCH ticks, and back. Home again, what they found goes into the stores (RUN_FIND a
// head) and they're counted back in.
//   the dead   each hour there's a chance they're caught (more at the ruin than on the road):
//              one is lost. Lose them all and they lie where they fell, bodies and what they'd
//              found, marked on your map ("dead scavengers").
//   meeting    if one of yours comes within RUN_MEET tiles, they stop to talk: their settlement
//              thinks a little better of you, and they tell you where they're going (a place on
//              your map).
// Rolls are hashes of the run's id and the tick, not the sim RNG.

import { isFallen, hungerOf } from './colonies';
import { leaveCorpse } from './corpses';
import { logEvent } from './events';
import { inBounds, isWalkable, type Point } from './grid';
import { hashRoll, idNumber } from './hash';
import { spawnItem } from './items';
import { adjustReputation, getSettlement, isHostile } from './settlements';
import { manhattan, nearestTile } from './space';
import type { SimState } from './state';
import { DAY_LENGTH, TICKS_PER_HOUR, dayOf, onClock } from './time';

export interface ScavRun {
  id: string;
  /** The settlement they're from (and go back to). */
  from: string;
  x: number;
  y: number;
  home: Point;
  ruin: Point;
  people: number;
  phase: 'out' | 'search' | 'back';
  /** Searching until this tick. */
  untilTick: number;
  nextStepTick: number;
  /** Food found so far. */
  food: number;
  /** Has one of yours met them? */
  met: boolean;
}

export const RUN_PARTY = 3;
export const RUN_MIN_HOME = 5;
export const RUN_MIN = 25;
export const RUN_MAX = 60;
export const RUN_STEP = 6;
export let RUN_SEARCH = 2 * TICKS_PER_HOUR;
onClock(() => (RUN_SEARCH = 2 * TICKS_PER_HOUR));
export const RUN_FIND = 2;
/** Chance a dawn that a settlement short of food sends a run (a well-fed one, a quarter of that). */
export const RUN_CHANCE = 0.4;
/** Chance an hour of being caught: on the road, and at the ruin. */
export const ROAD_DANGER = 0.02;
export const RUIN_DANGER = 0.08;
export const RUN_MEET = 3;
export const DEAD_LABEL = 'dead scavengers';

export function runsOf(state: SimState): ScavRun[] {
  return state.runs ?? [];
}

/** Every tick: setting out at dawn, moving, searching, trouble, meeting yours, coming home. */
export function updateRuns(state: SimState): void {
  if (state.tick > 0 && state.tick % DAY_LENGTH === 0) setOut(state);
  const list = runsOf(state);
  if (list.length === 0) return;
  for (const r of [...list]) {
    if (state.tick % TICKS_PER_HOUR === 0 && caught(state, r)) continue;
    if (!r.met) meet(state, r);
    if (r.phase === 'search') {
      if (state.tick >= r.untilTick) {
        r.food += r.people * RUN_FIND;
        r.phase = 'back';
      }
      continue;
    }
    if (state.tick < r.nextStepTick) continue;
    r.nextStepTick = state.tick + RUN_STEP;
    const goal = r.phase === 'out' ? r.ruin : r.home;
    r.x += Math.sign(goal.x - r.x);
    r.y += Math.sign(goal.y - r.y);
    if (r.x !== goal.x || r.y !== goal.y) continue;
    if (r.phase === 'out') {
      r.phase = 'search';
      r.untilTick = state.tick + RUN_SEARCH;
    } else home(state, r);
  }
}

function setOut(state: SimState): void {
  const day = dayOf(state.tick);
  const { width, height } = state.grid;
  for (const n of state.settlements) {
    if (isFallen(n) || n.people < RUN_MIN_HOME || runsOf(state).some((r) => r.from === n.id)) continue;
    const roll = (k: number) => hashRoll(idNumber(n.id), day, 0x5ca, k);
    const chance = hungerOf(n) === 'fed' ? RUN_CHANCE / 4 : RUN_CHANCE;
    if (roll(0) >= chance) continue;
    const angle = roll(1) * Math.PI * 2;
    const dist = RUN_MIN + Math.floor(roll(2) * (RUN_MAX - RUN_MIN));
    const ruin = { x: Math.max(1, Math.min(width - 2, Math.round(n.x + Math.cos(angle) * dist))), y: Math.max(1, Math.min(height - 2, Math.round(n.y + Math.sin(angle) * dist))) };
    const people = Math.min(RUN_PARTY, n.people - RUN_MIN_HOME + RUN_PARTY);
    n.people -= people;
    state.runs ??= [];
    state.runs.push({ id: `sr${day}x${idNumber(n.id)}`, from: n.id, x: n.x, y: n.y, home: { x: n.x, y: n.y }, ruin, people, phase: 'out', untilTick: 0, nextStepTick: state.tick + RUN_STEP, food: 0, met: false });
    if (!isHostile(n)) logEvent(state, `${people} from ${n.name} went out scavenging towards (${ruin.x}, ${ruin.y})`);
  }
}

/** The dead catch up with them: one lost; all of them, and they lie where they fell. Returns true if the run is over. */
function caught(state: SimState, r: ScavRun): boolean {
  const danger = (r.phase === 'search' ? RUIN_DANGER : ROAD_DANGER) * (1 + dayOf(state.tick) / 20) * state.settings.zombies / 100;
  if (hashRoll(idNumber(r.id), state.tick, 0xdea) >= danger) return false;
  r.people -= 1;
  const n = getSettlement(state, r.from);
  const heard = n && !isHostile(n);
  // A body where they fell, if the ground there is real yet.
  const spot = inBounds(state.grid, r.x, r.y) ? nearestTile(state.grid, r, (x, y) => isWalkable(state.grid, x, y), 60) : null;
  if (spot) leaveCorpse(state, spot, `a scavenger from ${n?.name ?? 'somewhere'}`);
  if (r.people > 0) {
    if (heard) logEvent(state, `One of ${n.name}'s scavengers was killed near (${r.x}, ${r.y})`);
    return false;
  }
  // All of them: what they'd found lies with them, and it's on your map.
  if (spot) for (let k = 0; k < Math.min(6, r.food); k++) spawnItem(state, 'food', spot.x, spot.y);
  const at = spot ?? { x: r.x, y: r.y };
  state.pointsOfInterest.push({ id: `p${state.nextId++}`, x: at.x, y: at.y, label: DEAD_LABEL });
  if (heard) logEvent(state, `${n.name}'s scavengers never came home: the dead got them near (${at.x}, ${at.y})`);
  state.runs!.splice(state.runs!.indexOf(r), 1);
  return true;
}

/** One of yours crosses their path: a few words, and they say where they're going. */
function meet(state: SimState, r: ScavRun): void {
  const you = state.survivors.find((s) => manhattan(s.pos, r) <= RUN_MEET);
  if (!you) return;
  r.met = true;
  const n = getSettlement(state, r.from);
  if (!n || isHostile(n)) return;
  adjustReputation(state, n, 3);
  if (!state.pointsOfInterest.some((p) => manhattan(p, r.ruin) <= 5)) state.pointsOfInterest.push({ id: `p${state.nextId++}`, x: r.ruin.x, y: r.ruin.y, label: `${n.name}'s scavenging spot` });
  logEvent(state, `${you.name} met ${r.people} scavengers from ${n.name}, ${r.phase === 'back' ? 'on their way home' : `on their way to a ruin at (${r.ruin.x}, ${r.ruin.y})`}`);
}

function home(state: SimState, r: ScavRun): void {
  state.runs!.splice(state.runs!.indexOf(r), 1);
  const n = getSettlement(state, r.from);
  if (!n) return;
  n.people += r.people;
  n.food += r.food;
  if (!isHostile(n)) logEvent(state, `${n.name}'s scavengers came home with ${r.food} food`);
}
