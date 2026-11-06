// Handiwork: what the survivor you control does with their own hands (control.ts), so
// going solo doesn't mean handing over to the colony to get a door nailed shut.
//
//   work (R) the tile in front:
//     damaged wall, door or barricade  repair it (no materials), until it's whole
//     door, carrying wood              barricade it (still yours to walk through; tougher)
//     wall or barricade, carrying wood reinforce it (2.5x the battering, as with scrap)
//     bare floor, carrying wood        put up a wall
//   forage (E underfoot, outdoors on grass, by day, spring to autumn): a chance of food,
//     once a day per tile
//   hunt: swing at a deer next to you (or shoot one); a hit may bring it down for venison
// Building takes BUILD_WORK ticks (as a colonist would); moving stops it.

import { completeBlueprint, BUILD_WORK, type Blueprint } from './designations';
import { furnitureAt } from './furniture';
import { GROUNDS, getTile, tileIndex, type Point } from './grid';
import { hashRoll, idNumber } from './hash';
import { getItem, itemAt, removeItem, spawnItem } from './items';
import { REPAIR_PER_TICK, repairStructure, structureDamage, isStructure } from './breach';
import { isReinforced } from './defences';
import { MEAT, provoke } from './animals';
import { unpack } from './inventory';
import { isIndoors } from './rooms';
import { seasonOf } from './seasons';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { dayOf, isNight } from './time';
import { logEvent } from './events';

export const FORAGE_TICKS = 60;
export const FORAGE_CHANCE = 0.35;
/** A swing (or shot) at a deer next to you brings it down this often. */
export const HUNT_CHANCE = 0.5;

type Build = 'wall' | 'barricade' | 'reinforce';

/** Wood in your hands, else in your bag (picked-up wood goes in the bag when it fits). */
function carryingWood(state: SimState, s: Survivor) {
  const item = s.carrying ? getItem(state, s.carrying) : undefined;
  if (item?.kind === 'wood') return item;
  for (const id of s.pack ?? []) {
    const packed = getItem(state, id);
    if (packed?.kind === 'wood') return packed;
  }
  return undefined;
}

/** What working this tile would do, or why not. */
function plan(state: SimState, s: Survivor, p: Point): { what: 'repair' } | { what: 'build'; target: Build } | string {
  const tile = getTile(state.grid, p.x, p.y);
  if (isStructure(tile) && structureDamage(state, p.x, p.y) > 0) return { what: 'repair' };
  const wood = carryingWood(state, s);
  if (tile === 'door') return wood ? { what: 'build', target: 'barricade' } : 'you need wood to barricade a door';
  if (tile === 'wall' || tile === 'barricade') {
    if (isReinforced(state, p.x, p.y)) return 'already as strong as you can make it';
    return wood ? { what: 'build', target: 'reinforce' } : 'you need wood to reinforce it';
  }
  if (tile !== 'floor') return 'nothing to work on there';
  if (!wood) return 'you need wood to put up a wall';
  if (furnitureAt(state, p.x, p.y) || itemAt(state, p.x, p.y)) return 'something is in the way';
  if (state.survivors.some((o) => o.pos.x === p.x && o.pos.y === p.y) || state.zombies.some((z) => z.pos.x === p.x && z.pos.y === p.y)) return 'someone is in the way';
  return { what: 'build', target: 'wall' };
}

/** Start working a tile next to you. Returns the busy action, or why not. */
export function startWork(state: SimState, s: Survivor, p: Point): { what: 'build' | 'repair'; targetId: string; duration: number } | string {
  if (p.x === s.pos.x && p.y === s.pos.y) return "you can't build where you stand";
  const job = plan(state, s, p);
  if (typeof job === 'string') return job;
  if (job.what === 'repair') return { what: 'repair', targetId: `${p.x},${p.y}`, duration: Math.ceil(structureDamage(state, p.x, p.y) / REPAIR_PER_TICK) };
  return { what: 'build', targetId: `${p.x},${p.y},${job.target}`, duration: BUILD_WORK[job.target] };
}

/** A tick of repairing. Returns true when done (or no longer needed). */
export function repairTick(state: SimState, targetId: string): boolean {
  const [x, y] = targetId.split(',').map(Number) as [number, number];
  if (!isStructure(getTile(state.grid, x, y))) return true;
  repairStructure(state, x, y, REPAIR_PER_TICK);
  return structureDamage(state, x, y) === 0;
}

/** The build is done: spend the wood and make it so (if it still makes sense). Returns what happened. */
export function finishBuild(state: SimState, s: Survivor, targetId: string): string {
  const [xs, ys, target] = targetId.split(',') as [string, string, Build];
  const p = { x: Number(xs), y: Number(ys) };
  const now = plan(state, s, p);
  if (typeof now === 'string' || now.what !== 'build' || now.target !== target) return typeof now === 'string' ? now : 'it changed while you worked';
  const wood = carryingWood(state, s)!;
  if (s.carrying === wood.id) s.carrying = null;
  unpack(s, wood.id);
  removeItem(state, wood);
  const bp: Blueprint = { id: `h${state.nextId++}`, x: p.x, y: p.y, target, needsMaterial: false, delivered: true, workDone: 0, workRequired: 0 };
  completeBlueprint(state, bp);
  return target === 'wall' ? 'put up a wall' : target === 'barricade' ? 'barricaded the door' : 'reinforced it';
}

/** Foraging needs open ground, daylight and a growing season; and each tile gives once a day. */
export function canForage(state: SimState, s: Survivor): string | null {
  const { x, y } = s.pos;
  const g = GROUNDS[state.grid.ground[tileIndex(state.grid, x, y)]!];
  if (isIndoors(state, x, y) || (g !== 'grass' && g !== 'field')) return 'nothing to forage here: find open grass, or a field';
  if (isNight(state.tick)) return 'too dark to forage';
  if (seasonOf(state.tick) === 'winter') return 'nothing grows in winter';
  if (state.foraged[String(tileIndex(state.grid, x, y))] === dayOf(state.tick)) return 'already picked over today';
  return null;
}

/** Foraging done: maybe some food (less in a nuclear winter). */
export function finishForage(state: SimState, s: Survivor): string {
  const i = tileIndex(state.grid, s.pos.x, s.pos.y);
  state.foraged[String(i)] = dayOf(state.tick);
  // Old fields still throw up what was sown there: twice the chance.
  const field = GROUNDS[state.grid.ground[i]!] === 'field' ? 2 : 1;
  const chance = FORAGE_CHANCE * field * (state.settings.winter ? 0.5 : 1);
  if (hashRoll(idNumber(s.id), i, state.tick) >= chance) return 'found nothing worth eating';
  spawnItem(state, 'food', s.pos.x, s.pos.y);
  return 'found something to eat';
}

/** Forget foraged tiles from before today (at dawn). */
export function forgetForaging(state: SimState): void {
  const today = dayOf(state.tick);
  for (const k of Object.keys(state.foraged)) if (state.foraged[k] !== today) delete state.foraged[k];
}

/**
 * A swing or shot at an animal. A deer drops to a lucky blow (HUNT_CHANCE) or bolts; the rest take
 * `damage` from their health, and a wolf or bear that's hit comes for whoever hit it (its pack
 * too). Brought down: MEAT by kind where it fell. Returns what happened.
 */
export function hitAnimal(state: SimState, s: Survivor, id: string, damage: number): string {
  const a = state.animals.find((q) => q.id === id);
  if (!a) return 'it got away';
  if (a.kind === 'deer') {
    if (hashRoll(idNumber(a.id), state.tick, 0xdee) >= HUNT_CHANCE) {
      a.leavesTick = Math.min(a.leavesTick, state.tick + 50);
      return 'missed: the deer bolts';
    }
  } else {
    a.health = (a.health ?? 0) - damage;
    if (a.health > 0) {
      provoke(state, a, s);
      return `hit the ${a.kind}`;
    }
  }
  state.animals.splice(state.animals.indexOf(a), 1);
  for (let i = 0; i < MEAT[a.kind]; i++) spawnItem(state, 'food', a.pos.x, a.pos.y);
  logEvent(state, `${s.name} brought down a ${a.kind}`);
  return 'brought it down';
}
