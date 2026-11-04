// Talking to survivors, and the tasks that come out of their stories.
//
// Each talk (no more than once every TALK_EVERY ticks per person) tells you the
// next line of their story (story.ts) and warms you both to each other. Once
// they've told you enough they ask for something, one thing at a time:
//   home  find their old place: there may be something left (a crate)
//   bury  bring their dead to a graveyard (a corpse lies at the spot)
//   pet   find their dog, still waiting at the house
//   burn  burn down the place where their regret happened (set a fire there)
//   radio a missing relative: with a powered radio, call round at noon. They may answer
//         (and come to join you) or nobody does
//   medicine  get medicine to a friend at a neighbouring settlement: carry one to within
//         MEDICINE_REACH tiles of it
// Along the way: people from the same town recognise each other (a warm surprise), and
// once someone has told you everything, the next talk they tell the others what they did:
// most think a little less of them, the cheerful think a little more, and they feel lighter.
// The spot is generated out in the world, 20-45 tiles away. Doing it lifts
// their mood for days and their opinion of whoever is under your control.

import { steel } from './resolve';
import { addDog, getDogById } from './dogs';
import { logEvent } from './events';
import { addFurniture, furnitureAt } from './furniture';
import { getTile, inBounds, isWalkable, type Point } from './grid';
import { hashRoll, idNumber } from './hash';
import { getItem, itemAt, removeItem, spawnItem } from './items';
import { addThought, type ThoughtKind } from './mood';
import { completeRooms, labelAt } from './rooms';
import { nearestTile } from './space';
import type { SimState } from './state';
import { REGRETS, TOWNS, storyLines } from './story';
import { hasTrait } from './skills';
import { hasRadio } from './radio';
import { adjustReputation } from './settlements';
import { addWanderer, frontierSpot } from './wanderers';
import type { Survivor } from './survivors';
import { TICKS_PER_HOUR, tickOfDay } from './time';
import { ensureGeneratedAround } from './worldgen';
import { controlled } from './control';

export type TaskKind = 'home' | 'bury' | 'pet' | 'burn' | 'radio' | 'medicine';

export interface Task {
  id: string;
  /** Who asked. */
  giverId: string;
  kind: TaskKind;
  x: number;
  y: number;
  /** The corpse (bury), dog (pet) or settlement (medicine) the task is about. */
  targetId: string | null;
}

export const TALK_EVERY = 2 * TICKS_PER_HOUR;
/** Talking needs you within this many tiles. */
export const TALK_RANGE = 2;
export const TALK_OPINION = 5;
export const TASK_OPINION = 25;
const TASK_MIN = 20;
const TASK_SPREAD = 25;
/** Lines told before each task is asked for. */
const TOLD_BEFORE: Record<TaskKind, number> = { home: 2, bury: 2, radio: 2, pet: 3, medicine: 3, burn: 4 };
const TASK_ORDER: readonly TaskKind[] = ['home', 'bury', 'radio', 'pet', 'medicine', 'burn'];
const REWARD: Record<TaskKind, ThoughtKind> = { home: 'wentHome', bury: 'laidToRest', pet: 'petFound', burn: 'burnedThePast', radio: 'reunited', medicine: 'wentHome' };
/** Chance a missing relative answers the radio. */
export const ANSWER_CHANCE = 0.5;
export const MEDICINE_REACH = 6;
export const KNEW_OPINION = 20;
export const CONFESS_OPINION = -10;
/** Resolve from a request seen through, a relative found, a confession; lost to a silent radio. */
export const TASK_RESOLVE = 10;
export const REUNITED_RESOLVE = 15;
export const CONFESS_RESOLVE = 5;
export const SILENCE_RESOLVE = 10;

/** Talk to a survivor. Returns null if it went ahead, or why not. */
export function talk(state: SimState, s: Survivor): string | null {
  const you = controlled(state);
  if (you === s) return "that's you";
  if (you && Math.max(Math.abs(you.pos.x - s.pos.x), Math.abs(you.pos.y - s.pos.y)) > TALK_RANGE) return `${s.name} is too far away to talk`;
  if (s.sleep?.asleep) return `${s.name} is asleep`;
  if (state.tick < s.story.nextTalk) return `${s.name} doesn't want to talk again yet`;
  s.story.nextTalk = state.tick + TALK_EVERY;
  if (you) {
    warm(s, you, TALK_OPINION);
    warm(you, s, TALK_OPINION);
  }
  const lines = storyLines(s.story);
  if (s.story.told < lines.length) {
    logEvent(state, `${s.name}: "${lines[s.story.told]}"`);
    s.story.told += 1;
    if (s.story.told === 1) recognise(state, s);
  } else if (!s.story.confessed) confess(state, s);
  else logEvent(state, `${s.name} sits with you a while. There's nothing more to say.`);
  if (!state.tasks.some((t) => t.giverId === s.id)) {
    const kind = TASK_ORDER.find((k) => !s.story.asked.includes(k) && s.story.told >= TOLD_BEFORE[k] && wants(state, s, k));
    if (kind) offerTask(state, s, kind);
  }
  return null;
}

/** Whether this task fits their story (and the world: medicine needs a neighbour). */
function wants(state: SimState, s: Survivor, k: TaskKind): boolean {
  if (k === 'bury') return s.story.lost.dead;
  if (k === 'radio') return !s.story.lost.dead;
  if (k === 'pet') return s.story.pet !== null;
  if (k === 'medicine') return friendFor(state, s) !== undefined;
  return true;
}

/** The neighbouring settlement their friend is at: the nearest one still standing. */
function friendFor(state: SimState, s: Survivor) {
  return state.settlements
    .filter((n) => n.people > 0)
    .sort((a, b) => Math.abs(a.x - s.pos.x) + Math.abs(a.y - s.pos.y) - (Math.abs(b.x - s.pos.x) + Math.abs(b.y - s.pos.y)))[0];
}

/** Having said where they're from: anyone here from the same town who has said so too knew them. */
function recognise(state: SimState, s: Survivor): void {
  for (const o of state.survivors) {
    if (o === s || o.story.told === 0 || o.story.town !== s.story.town || s.story.knew.includes(o.id)) continue;
    s.story.knew.push(o.id);
    o.story.knew.push(s.id);
    warm(s, o, KNEW_OPINION);
    warm(o, s, KNEW_OPINION);
    addThought(state, s, 'oldFriend');
    addThought(state, o, 'oldFriend');
    logEvent(state, `${s.name} and ${o.name} knew each other, back in ${TOWNS[s.story.town]}`);
  }
}

/** Everything told: they tell the others what they did. */
function confess(state: SimState, s: Survivor): void {
  s.story.confessed = true;
  addThought(state, s, 'confessed');
  steel(s, CONFESS_RESOLVE);
  for (const o of state.survivors) if (o !== s) warm(o, s, hasTrait(o, 'cheerful') ? -CONFESS_OPINION / 2 : CONFESS_OPINION);
  logEvent(state, `${s.name} told everyone: "${REGRETS[s.story.regret]}." Some look at them differently now.`);
}

function warm(of: Survivor, about: Survivor, by: number): void {
  of.opinions[about.id] = Math.max(-100, Math.min(100, (of.opinions[about.id] ?? 0) + by));
}

function offerTask(state: SimState, s: Survivor, kind: TaskKind): void {
  const spot = taskSpot(state, s, kind);
  if (!spot) return;
  s.story.asked.push(kind);
  const task: Task = { id: `q${state.nextId++}`, giverId: s.id, kind, x: spot.x, y: spot.y, targetId: null };
  const { lost, pet } = s.story;
  const where = `(${spot.x}, ${spot.y})`;
  if (kind === 'home') {
    addFurniture(state, 'crate', spot.x, spot.y).loot = 3;
    logEvent(state, `${s.name} asks: "Go and see my old place, ${where}. There might be something left."`);
  } else if (kind === 'bury') {
    const body = spawnItem(state, 'corpse', spot.x, spot.y);
    body.tag = lost.name;
    body.wear = state.tick;
    task.targetId = body.id;
    logEvent(state, `${s.name} asks: "Bring ${lost.name} home, ${where}, and give them a grave."`);
  } else if (kind === 'pet') {
    const dog = addDog(state, spot.x, spot.y);
    dog.name = pet!;
    dog.waiting = true;
    task.targetId = dog.id;
    logEvent(state, `${s.name} asks: "Find ${pet}. If ${pet}'s anywhere, it's at the house, ${where}."`);
  } else if (kind === 'radio') {
    logEvent(state, `${s.name} asks: "Get a radio working. If ${lost.name} is out there, they'll be listening at noon."`);
  } else if (kind === 'medicine') {
    const friend = friendFor(state, s)!;
    task.targetId = friend.id;
    logEvent(state, `${s.name} asks: "A friend of mine is at ${friend.name}, ${where}, and sick. Take them some medicine."`);
  } else {
    addFurniture(state, 'crate', spot.x, spot.y).loot = 0;
    logEvent(state, `${s.name} asks: "Burn it down. The place at ${where}. I can't do it myself."`);
  }
  state.tasks.push(task);
}

/** A free floor tile out in the world for a task, generating the land there if need be. */
function taskSpot(state: SimState, s: Survivor, kind: TaskKind): Point | null {
  // Some aren't out in the world: the radio is at home, the friend at their settlement.
  if (kind === 'radio') return { ...s.pos };
  if (kind === 'medicine') {
    const friend = friendFor(state, s);
    return friend ? { x: friend.x, y: friend.y } : null;
  }
  const n = idNumber(s.id);
  const k = TASK_ORDER.indexOf(kind);
  const angle = hashRoll(n, k, 0x7a5) * Math.PI * 2;
  const dist = TASK_MIN + hashRoll(n, k, 0x7a6) * TASK_SPREAD;
  const { width, height } = state.grid;
  const p = {
    x: Math.max(1, Math.min(width - 2, Math.round(s.pos.x + Math.cos(angle) * dist))),
    y: Math.max(1, Math.min(height - 2, Math.round(s.pos.y + Math.sin(angle) * dist))),
  };
  ensureGeneratedAround(state, p);
  return nearestTile(state.grid, p, (x, y) => inBounds(state.grid, x, y) && getTile(state.grid, x, y) === 'floor' && isWalkable(state.grid, x, y) && !furnitureAt(state, x, y) && !itemAt(state, x, y));
}

const near = (a: Point, b: Point, r: number) => Math.max(Math.abs(a.x - b.x), Math.abs(a.y - b.y)) <= r;

/** Checks tasks for being done (or no longer doable). Every 10 ticks. */
export function updateTasks(state: SimState): void {
  if (state.tick % 10 !== 0 || state.tasks.length === 0) return;
  const graves = state.tasks.some((t) => t.kind === 'bury') ? completeRooms(state, 'graveyard') : new Set<number>();
  for (const task of [...state.tasks]) {
    const giver = state.survivors.find((s) => s.id === task.giverId);
    if (!giver) {
      drop(state, task);
      continue;
    }
    const outcome = check(state, task, giver, graves);
    if (outcome === null) continue;
    drop(state, task);
    if (outcome) reward(state, task, giver);
  }
}

/** True: done. False: can't be done any more. Null: still open. */
function check(state: SimState, task: Task, giver: Survivor, graves: Set<number>): boolean | null {
  const spot = { x: task.x, y: task.y };
  switch (task.kind) {
    case 'home': {
      const you = controlled(state);
      return near(giver.pos, spot, 2) || (you !== undefined && near(you.pos, spot, 2)) ? true : null;
    }
    case 'bury': {
      const body = task.targetId ? getItem(state, task.targetId) : undefined;
      if (!body) return fail(state, giver, `Nobody can bury ${giver.story.lost.name} now.`);
      return body.pos && graves.has(labelAt(state, body.pos.x, body.pos.y)) ? true : null;
    }
    case 'pet': {
      const dog = task.targetId ? getDogById(state, task.targetId) : undefined;
      if (!dog) return fail(state, giver, `${giver.story.pet} is gone.`);
      return dog.waiting ? null : true;
    }
    case 'burn':
      return state.fires.some((f) => near(f, spot, 1)) ? true : null;
    case 'radio':
      return tickOfDay(state.tick) === 12 * TICKS_PER_HOUR && hasRadio(state) ? callOut(state, task, giver) : null;
    case 'medicine': {
      const friend = state.settlements.find((n) => n.id === task.targetId);
      if (!friend || friend.people <= 0) return fail(state, giver, 'nobody is left there to help.');
      const bearer = state.survivors.find((s) => s.carrying && getItem(state, s.carrying)?.kind === 'medicine' && near(s.pos, spot, MEDICINE_REACH));
      if (!bearer) return null;
      const medicine = getItem(state, bearer.carrying!)!;
      bearer.carrying = null;
      removeItem(state, medicine);
      adjustReputation(state, friend, 15);
      logEvent(state, `${bearer.name} brought medicine to ${giver.name}'s friend at ${friend.name}`);
      return true;
    }
  }
}

/** Noon on the radio: the missing relative answers (and sets out to join you), or nobody does. */
function callOut(state: SimState, task: Task, giver: Survivor): boolean | null {
  const { lost } = giver.story;
  if (hashRoll(idNumber(task.id), 0xca11) >= ANSWER_CHANCE) {
    addThought(state, giver, 'lostHope');
    steel(giver, -SILENCE_RESOLVE);
    logEvent(state, `${giver.name} called for ${lost.name} on the radio until dark. Nobody answered.`);
    return false;
  }
  const spot = frontierSpot(state, new Set(state.wanderers.map((w) => `${w.x},${w.y}`)));
  if (!spot) return null; // try again tomorrow
  const w = addWanderer(state, spot.x, spot.y);
  w.name = lost.name;
  logEvent(state, `${lost.name} answered the radio! They're at (${spot.x}, ${spot.y}), waiting to be taken in.`);
  return true;
}

function fail(state: SimState, giver: Survivor, why: string): false {
  logEvent(state, `${giver.name}'s request can't be done: ${why}`);
  return false;
}

function drop(state: SimState, task: Task): void {
  state.tasks.splice(state.tasks.indexOf(task), 1);
}

function reward(state: SimState, task: Task, giver: Survivor): void {
  addThought(state, giver, REWARD[task.kind]);
  steel(giver, task.kind === 'radio' ? REUNITED_RESOLVE : TASK_RESOLVE);
  const you = controlled(state);
  if (you && you !== giver) warm(giver, you, TASK_OPINION);
  const done = {
    home: `${giver.name}'s old place has been seen to`,
    bury: `${giver.story.lost.name} has a grave now. ${giver.name} can rest a little`,
    pet: `${giver.story.pet} has been found`,
    burn: `The place ${giver.name} couldn't stop seeing is ash`,
    radio: `${giver.name} is crying, and not from grief`,
    medicine: `${giver.name}'s friend has their medicine`,
  }[task.kind];
  logEvent(state, `${done}.`);
}

/** Open tasks given by a survivor, as text. */
export function describeTasks(state: SimState, s: Survivor): string[] {
  return state.tasks
    .filter((t) => t.giverId === s.id)
    .map((t) => {
      const what = { home: 'see their old place', bury: `bury ${s.story.lost.name}`, pet: `find ${s.story.pet}`, burn: 'burn down the place', radio: `call for ${s.story.lost.name} on a powered radio (at noon)`, medicine: 'take medicine to their friend' }[t.kind];
      return `asks you to ${what} at (${t.x}, ${t.y})`;
    });
}
