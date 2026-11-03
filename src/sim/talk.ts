// Talking to survivors, and the tasks that come out of their stories.
//
// Each talk (no more than once every TALK_EVERY ticks per person) tells you the
// next line of their story (story.ts) and warms you both to each other. Once
// they've told you enough they ask for something, one thing at a time:
//   home  find their old place: there may be something left (a crate)
//   bury  bring their dead to a graveyard (a corpse lies at the spot)
//   pet   find their dog, still waiting at the house
//   burn  burn down the place where their regret happened (set a fire there)
// The spot is generated out in the world, 20-45 tiles away. Doing it lifts
// their mood for days and their opinion of whoever is under your control.

import { addDog, getDogById } from './dogs';
import { logEvent } from './events';
import { addFurniture, furnitureAt } from './furniture';
import { getTile, inBounds, isWalkable, type Point } from './grid';
import { hashRoll, idNumber } from './hash';
import { getItem, itemAt, spawnItem } from './items';
import { addThought, type ThoughtKind } from './mood';
import { completeRooms, labelAt } from './rooms';
import { nearestTile } from './space';
import type { SimState } from './state';
import { storyLines } from './story';
import type { Survivor } from './survivors';
import { TICKS_PER_HOUR } from './time';
import { ensureGeneratedAround } from './worldgen';
import { controlled } from './control';

export type TaskKind = 'home' | 'bury' | 'pet' | 'burn';

export interface Task {
  id: string;
  /** Who asked. */
  giverId: string;
  kind: TaskKind;
  x: number;
  y: number;
  /** The corpse (bury) or dog (pet) the task is about. */
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
const TOLD_BEFORE: Record<TaskKind, number> = { home: 2, bury: 2, pet: 3, burn: 4 };
const TASK_ORDER: readonly TaskKind[] = ['home', 'bury', 'pet', 'burn'];
const REWARD: Record<TaskKind, ThoughtKind> = { home: 'wentHome', bury: 'laidToRest', pet: 'petFound', burn: 'burnedThePast' };

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
  } else logEvent(state, `${s.name} sits with you a while. There's nothing more to say.`);
  if (!state.tasks.some((t) => t.giverId === s.id)) {
    const kind = TASK_ORDER.find((k) => !s.story.asked.includes(k) && s.story.told >= TOLD_BEFORE[k] && (k !== 'bury' || s.story.lost.dead) && (k !== 'pet' || s.story.pet));
    if (kind) offerTask(state, s, kind);
  }
  return null;
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
  } else {
    addFurniture(state, 'crate', spot.x, spot.y).loot = 0;
    logEvent(state, `${s.name} asks: "Burn it down. The place at ${where}. I can't do it myself."`);
  }
  state.tasks.push(task);
}

/** A free floor tile out in the world for a task, generating the land there if need be. */
function taskSpot(state: SimState, s: Survivor, kind: TaskKind): Point | null {
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
  }
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
  const you = controlled(state);
  if (you && you !== giver) warm(giver, you, TASK_OPINION);
  const done = {
    home: `${giver.name}'s old place has been seen to`,
    bury: `${giver.story.lost.name} has a grave now. ${giver.name} can rest a little`,
    pet: `${giver.story.pet} has been found`,
    burn: `The place ${giver.name} couldn't stop seeing is ash`,
  }[task.kind];
  logEvent(state, `${done}.`);
}

/** Open tasks given by a survivor, as text. */
export function describeTasks(state: SimState, s: Survivor): string[] {
  return state.tasks
    .filter((t) => t.giverId === s.id)
    .map((t) => {
      const what = { home: 'see their old place', bury: `bury ${s.story.lost.name}`, pet: `find ${s.story.pet}`, burn: 'burn down the place' }[t.kind];
      return `asks you to ${what} at (${t.x}, ${t.y})`;
    });
}
