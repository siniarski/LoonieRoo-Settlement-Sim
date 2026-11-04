// Escape, and a new game+. When the helicopter is down (endgame.ts), you choose:
//   everyone   all aboard: a new game+ in a fresh world (same settings), you alone with what
//              you carried, your skills, traits, story and resolve (newGamePlus)
//   others     send everyone else; you stay, and can call another helicopter later for
//              whoever else you find
//   none       wave it off
// Left waiting past nightfall, it goes without anyone.

import { logEvent } from './events';
import { getItem, type Item } from './items';
import { killSurvivor } from './needs';
import { cloneState, type SimState } from './state';
import type { Survivor } from './survivors';
import { isNight } from './time';
import { createWorld, WORLD_SIZE } from './worldgen';

/** Choose who goes ('others' or 'none'; 'everyone' is a new game: newGamePlus). Returns an error, or null. */
export function escape(state: SimState, who: 'others' | 'none'): string | null {
  const eg = state.endgame;
  if (!eg.helicopter) return 'no helicopter is waiting';
  eg.helicopter = false;
  eg.evacuation = null;
  if (who === 'none') {
    logEvent(state, 'You waved the helicopter off. It lifts away into the grey.');
    return null;
  }
  const you = state.control?.survivorId;
  const going = state.survivors.filter((s) => s.id !== you);
  if (going.length === 0) return null;
  for (const s of going) killSurvivor(state, s, 'escaped', true);
  eg.escaped += going.length;
  logEvent(state, `${going.map((s) => s.name).join(', ')} flew out. You stayed. Call again for anyone else you find.`);
  return null;
}

/** The helicopter waits until nightfall, then goes empty. */
export function updateHelicopter(state: SimState): void {
  const eg = state.endgame;
  if (!eg.helicopter || !isNight(state.tick)) return;
  eg.helicopter = false;
  eg.evacuation = null;
  logEvent(state, 'Nobody came to the helicopter. It left at dusk.');
}

/** What a survivor wears and carries: the item ids in their slots. */
function carried(s: Survivor): string[] {
  return [s.weapon, s.body, s.feet, s.cart, s.tool, s.carrying, ...s.cartLoad].filter((id): id is string => id !== null);
}

/**
 * Everyone aboard: a fresh world (same settings, solo start, a new seed), and you in it with
 * what you carried. Pure: makes a new state from the old one.
 */
export function newGamePlus(old: SimState, seed: number): SimState {
  const you = old.survivors.find((s) => s.id === old.control?.survivorId) ?? old.survivors[0];
  const state = createWorld(seed, old.world?.size ?? WORLD_SIZE, { ...old.settings, start: 'solo' });
  if (!you) return state;
  const fresh = state.survivors[0]!;
  // Drop the new start's kit: you bring your own.
  state.items = state.items.filter((i) => i.carriedBy !== fresh.id);
  // Your gear, under new ids.
  const ids = new Map<string, string>();
  for (const id of carried(you)) {
    const item = getItem(old, id);
    if (!item) continue;
    const copy: Item = { ...structuredCloneItem(item), id: `i${state.nextId++}`, pos: null, carriedBy: fresh.id, reservedBy: null };
    ids.set(id, copy.id);
    state.items.push(copy);
  }
  const map = (id: string | null) => (id ? (ids.get(id) ?? null) : null);
  // You, in the new body slot: who you were, what you know, what you carry. Not where you were, or who you knew.
  const me = cloneState(old).survivors.find((s) => s.id === you.id)!;
  Object.assign(fresh, {
    name: me.name,
    traits: me.traits,
    xp: me.xp,
    kills: me.kills,
    watchHours: me.watchHours,
    age: me.age,
    resolve: me.resolve,
    story: { ...me.story, knew: [], nextTalk: 0 },
    health: me.health,
    satiety: me.satiety,
    hydration: me.hydration,
    rest: me.rest,
    infection: me.infection,
    bitten: me.bitten,
    wound: me.wound,
    weapon: map(me.weapon),
    body: map(me.body),
    feet: map(me.feet),
    cart: map(me.cart),
    tool: map(me.tool),
    carrying: map(me.carrying),
    cartLoad: me.cartLoad.map(map).filter((id): id is string => id !== null),
  });
  state.endgame.escaped = old.endgame.escaped + old.survivors.length - 1;
  logEvent(state, `${fresh.name} came down somewhere new, with what they could carry. ${old.survivors.length - 1} more flew out with them, and went their own way.`);
  return state;
}

function structuredCloneItem(item: Item): Item {
  return JSON.parse(JSON.stringify(item)) as Item;
}
