// Hordes: zombies moving together, on one flow field.
//
// Not every zombie is in one. Sleepers and the odd wanderer stay loners and think for themselves
// (zombies.ts plan). A horde is:
//   made     by a night's wave (a group of HORDE_MIN or more arriving together: zombies.ts
//            spawnGroup), or a herd breaking into real zombies near your people (herds.ts).
//   goals    hunting: the survivors it's after (a night wave knows where everyone is; others go
//            for whoever any member sees or hears). Roaming: a heading, somewhere off across the map.
//   moves    on one flow field (flowField): the cost to reach a goal from every tile in a box round
//            the horde, worked out backwards from the goals. Walking a tile costs STEP; a wall, door,
//            window or barricade in the way costs the time it takes to batter down (its health at
//            BATTER_RATE). So a walled-in colony draws a horde to its weakest point, and the whole
//            horde piles up there. Each member steps to its cheapest neighbour; battering is done as
//            it always was (zombies.ts). Goals outside the box pull through its edge (each edge tile
//            seeded with its distance to the nearest goal).
//   refresh  every REFRESH_EVERY ticks: members gone are dropped, goals and the box worked out
//            again. The field depends only on what's stored here (goals, box) and on the walls and
//            open doors now, so it's derived data, cached, never saved, and a reloaded game moves
//            exactly as the one that was saved.
// Every SOCIAL_EVERY ticks hordes meet and part (in list order, the same way every time):
//   joining  two hordes within JOIN_RADIUS of each other that both roam, or both hunt, become one
//            (the bigger keeps its goals). An awake loner (static or herd, after nobody) within
//            LONER_JOIN of a horde falls in with it. So hordes snowball as they travel.
//   noise    (noise.ts) a roaming horde mostly in earshot turns towards a noise; if only some of it
//            is, those split off as their own horde (or loners, if too few).
//   strays   a member more than STRAY_RADIUS from the horde's middle drops out, a loner.
//   shedding a big horde (SHED_MIN or more) has a small chance each hour (SHED_CHANCE, a hash roll)
//            of a small part (SHED_SHARE_MIN..MAX, the ones furthest back) breaking away, heading off
//            to one side. So even a huge herd leaves stragglers across the map.
//   dissolve under HORDE_MIN members, the rest are loners.
//   far off  a roaming horde with nobody of yours within DEMOTE_RADIUS becomes a herd marker again
//            (herds.ts), its zombies counted into it, until it comes near someone and breaks up again.

import { STRUCTURE_HEALTH, edgeHealth, isStructure } from './breach';
import { logEvent } from './events';
import { EDGE_DOOR, edgeBetween, getTile, inBounds, type Point } from './grid';
import { hashRoll, idNumber } from './hash';
import { MATERIALIZE_RADIUS } from './herds';
import { findById } from './lookup';
import { DIRS, crossable, isPassable } from './pathfinding';
import { manhattan } from './space';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { TICKS_PER_HOUR } from './time';
import { ZOMBIE_ATTACK_EVERY, ZOMBIE_DAMAGE, ZOMBIE_TICKS_PER_TILE, noticesSurvivor, removeZombie, type Zombie } from './zombies';

export interface Horde {
  id: string;
  /** Zombie ids, in the order they joined. */
  members: string[];
  mode: 'hunt' | 'roam';
  /** Hunting: survivors' tiles at the last refresh, and who they are. Roaming: [heading]. */
  goals: Point[];
  goalIds: string[];
  /** Roaming: where it's going. */
  heading: Point | null;
  /** The flow field's box (inclusive), from the last refresh. */
  box: { x0: number; y0: number; x1: number; y1: number };
  refreshTick: number;
  /** When the goals were last taken afresh (GOAL_EVERY). Missing in older saves. */
  goalTick?: number;
  /** A night's wave: knows where everyone is. */
  night: boolean;
}

export const HORDE_MIN = 3;
export const REFRESH_EVERY = 15;
export const SOCIAL_EVERY = 50;
/** Box margin round the members (and goals near them), and its largest side. */
export const BOX_MARGIN = 14;
export const BOX_MAX = 160;
/** Field costs: a tile walked is STEP; battering costs STEP per BATTER_RATE health. */
export const STEP = 10;
/** Damage a zombie does in the time it takes to walk a tile. */
export function batterRate(): number {
  return (ZOMBIE_DAMAGE * ZOMBIE_TICKS_PER_TILE) / ZOMBIE_ATTACK_EVERY;
}
/** A goal (a survivor) moving no further than this from where it was leaves the field as it is. */
export const GOAL_SLACK = 3;
/** Goals that moved further are taken afresh no more often than this. */
export const GOAL_EVERY = 60;
/** Members this close to the box's edge (not the map's) make it be worked out again. */
export const BOX_SLACK = 4;
export const JOIN_RADIUS = 10;
export const LONER_JOIN = 6;
export const STRAY_RADIUS = 24;
export const SHED_MIN = 20;
export const SHED_CHANCE = 0.15;
export const SHED_SHARE_MIN = 0.05;
export const SHED_SHARE_MAX = 0.15;
/** Roaming hordes this far from all your people go back to being herd markers (past where herds break up). */
export function demoteRadius(): number {
  return MATERIALIZE_RADIUS + 20;
}

export function hordesOf(state: SimState): Horde[] {
  return state.hordes ?? [];
}

export function getHorde(state: SimState, id: string | null | undefined): Horde | undefined {
  return id ? findById(hordesOf(state), id) : undefined;
}

/** A new horde of these zombies (HORDE_MIN or more): hunting the colony (a night's wave) or roaming towards a heading. */
export function formHorde(state: SimState, members: Zombie[], heading: Point | null, night: boolean): Horde | null {
  if (members.length < HORDE_MIN) return null;
  state.hordes ??= [];
  const h: Horde = { id: `hd${state.nextId++}`, members: members.map((z) => z.id), mode: night ? 'hunt' : 'roam', goals: [], goalIds: [], heading, box: { x0: 0, y0: 0, x1: 0, y1: 0 }, refreshTick: -REFRESH_EVERY, night };
  for (const z of members) z.hordeId = h.id;
  state.hordes.push(h);
  refresh(state, h);
  return h;
}

/** The horde's members still about. */
export function membersOf(state: SimState, h: Horde): Zombie[] {
  const out: Zombie[] = [];
  for (const id of h.members) {
    const z = findById(state.zombies, id);
    if (z && z.hordeId === h.id) out.push(z);
  }
  return out;
}

/** The middle of a horde: the median each way, so a few stragglers don't drag it after them. */
function middle(zs: readonly Zombie[]): Point {
  const xs = zs.map((z) => z.pos.x).sort((a, b) => a - b);
  const ys = zs.map((z) => z.pos.y).sort((a, b) => a - b);
  const k = (zs.length - 1) >> 1;
  return { x: xs[k]!, y: ys[k]! };
}

/** Members dropped, goals and the box worked out again. */
function refresh(state: SimState, h: Horde): void {
  const zs = membersOf(state, h);
  h.members = zs.map((z) => z.id);
  h.refreshTick = state.tick;
  if (zs.length === 0) return;
  // Who it's after: a night's wave, everyone; otherwise whoever any member notices.
  const prey: Survivor[] = h.night ? [...state.survivors] : state.survivors.filter((s) => zs.some((z) => noticesSurvivor(state, z, s)));
  // Goals: kept as they were while the same people are only a step or two from where they were
  // (GOAL_SLACK), so the field isn't worked out again for every shuffle.
  const wasMode = h.mode;
  const was = h.goals;
  const wasIds = h.goalIds;
  if (prey.length > 0) {
    h.mode = 'hunt';
    const goals = prey.map((s) => ({ x: s.pos.x, y: s.pos.y }));
    const ids = prey.map((s) => s.id);
    // The same people: kept while they've barely moved, or for up to GOAL_EVERY ticks whatever they did
    // (with twenty people about someone is always on the move). Members close the last stretch on
    // their target as it is (zombies.ts closeIn).
    const samePeople = wasMode === 'hunt' && ids.length === wasIds.length && ids.every((id, i) => id === wasIds[i]);
    const barely = samePeople && ids.every((_, i) => manhattan(goals[i]!, was[i]!) <= GOAL_SLACK);
    if (!samePeople || (!barely && state.tick - (h.goalTick ?? -GOAL_EVERY) >= GOAL_EVERY)) {
      h.goals = goals;
      h.goalIds = ids;
      h.goalTick = state.tick;
    }
  } else {
    if (h.mode === 'hunt' && !h.heading && h.goals.length > 0) h.heading = { ...h.goals[0]! };
    h.mode = 'roam';
    h.goals = h.heading ? [{ ...h.heading }] : [];
    h.goalIds = [];
  }
  const goalsKept = h.goals === was;
  // The box: round the members, and any goals not far from them, with a margin; no bigger than BOX_MAX.
  const mid = middle(zs);
  let x0 = mid.x;
  let y0 = mid.y;
  let x1 = mid.x;
  let y1 = mid.y;
  for (const p of [...zs.map((z) => z.pos), ...h.goals.filter((g) => manhattan(g, mid) <= BOX_MAX)]) {
    x0 = Math.min(x0, p.x);
    y0 = Math.min(y0, p.y);
    x1 = Math.max(x1, p.x);
    y1 = Math.max(y1, p.y);
  }
  const { width, height } = state.grid;
  const clampBox = (lo: number, hi: number, centre: number, size: number) => {
    lo -= BOX_MARGIN;
    hi += BOX_MARGIN;
    if (hi - lo + 1 > BOX_MAX) {
      lo = centre - (BOX_MAX >> 1);
      hi = lo + BOX_MAX - 1;
    }
    return [Math.max(0, lo), Math.min(size - 1, hi)] as const;
  };
  [x0, x1] = clampBox(x0, x1, mid.x, width);
  [y0, y1] = clampBox(y0, y1, mid.y, height);
  // The box too stays as it was while the goals did and every member is still well inside it.
  const b = h.box;
  const inside = (q: Point) =>
    (q.x - b.x0 >= BOX_SLACK || b.x0 === 0) && (b.x1 - q.x >= BOX_SLACK || b.x1 === width - 1) && (q.y - b.y0 >= BOX_SLACK || b.y0 === 0) && (b.y1 - q.y >= BOX_SLACK || b.y1 === height - 1);
  if (!goalsKept || !zs.every((z) => inside(z.pos))) h.box = { x0, y0, x1, y1 };
  for (const z of zs) {
    z.targetId = null;
    if (h.mode === 'hunt') {
      let best = Infinity;
      h.goals.forEach((g, i) => {
        const d = manhattan(g, z.pos);
        if (d < best) {
          best = d;
          z.targetId = h.goalIds[i]!;
        }
      });
    }
  }
}

// --- The flow field ---------------------------------------------------------------------

interface Field {
  key: string;
  dist: Int32Array;
  box: Horde['box'];
}
const fields = new WeakMap<Horde, Field>();
const UNREACHED = 0x7fffffff;

/**
 * Entering tile b: a step, plus battering it down if it's a wall, door or barricade (Infinity: never).
 * `asShut`: a door counts as shut whether it is or not (the field's view: doors swing open and shut
 * all the time, and the field shouldn't be worked out again for each).
 */
function enterCost(state: SimState, bx: number, by: number, asShut = false): number {
  const { grid } = state;
  const tile = getTile(grid, bx, by);
  if (tile === undefined || tile === 'water') return Infinity;
  if (isPassable(grid, bx, by, 'zombie') && !(asShut && tile === 'door')) return STEP;
  if (!isStructure(tile)) return Infinity;
  const i = by * grid.width + bx;
  return STEP + Math.ceil((STRUCTURE_HEALTH[tile] * (grid.reinforced.includes(i) ? 2.5 : 1) * STEP) / batterRate());
}

/** Crossing the border from a to b: battering it down if it's in the way, else nothing (`asShut`: an edge door counts as shut). */
function edgeCost(state: SimState, ax: number, ay: number, bx: number, by: number, asShut = false): number {
  const { grid } = state;
  const e = edgeBetween(grid, ax, ay, bx, by);
  if (e < 0 || grid.edges[e] === 0) return 0;
  if (!(asShut && grid.edges[e] === EDGE_DOOR) && crossable(grid, ax, ay, bx, by, 'zombie')) return 0;
  return Math.ceil((Math.max(1, edgeHealth(grid, e)) * STEP) / batterRate());
}

/** What it costs a zombie to go from tile a into its neighbour b (Infinity: it can't, ever). */
function stepCost(state: SimState, ax: number, ay: number, bx: number, by: number): number {
  const enter = enterCost(state, bx, by);
  return enter === Infinity ? Infinity : enter + edgeCost(state, ax, ay, bx, by);
}

/**
 * The horde's field: cost to a goal from each tile in its box, from what's stored on it and the walls
 * now, every door taken as shut. (A member's own next step sees doors as they are: hordeStep.)
 */
export function flowField(state: SimState, h: Horde): Field {
  const b = h.box;
  const key = `${h.goals.map((g) => `${g.x},${g.y}`).join(';')}|${b.x0},${b.y0},${b.x1},${b.y1}|${state.walkabilityVersion}`;
  const hit = fields.get(h);
  if (hit && hit.key === key) return hit;
  const { x0, y0, x1, y1 } = h.box;
  const w = x1 - x0 + 1;
  const ht = y1 - y0 + 1;
  const dist = new Int32Array(w * ht).fill(UNREACHED);
  // A binary heap on (cost, then local index): ties always come off in the same order.
  let hc = new Float64Array(1024);
  let hi = new Int32Array(1024);
  let size = 0;
  const less = (a: number, b: number) => hc[a]! < hc[b]! || (hc[a] === hc[b] && hi[a]! < hi[b]!);
  const swap = (a: number, b: number) => {
    const c = hc[a]!;
    hc[a] = hc[b]!;
    hc[b] = c;
    const i = hi[a]!;
    hi[a] = hi[b]!;
    hi[b] = i;
  };
  const push = (c: number, i: number) => {
    if (size === hc.length) {
      const nc = new Float64Array(size * 2);
      nc.set(hc);
      hc = nc;
      const ni = new Int32Array(size * 2);
      ni.set(hi);
      hi = ni;
    }
    hc[size] = c;
    hi[size] = i;
    let k = size++;
    while (k > 0) {
      const p = (k - 1) >> 1;
      if (!less(k, p)) break;
      swap(k, p);
      k = p;
    }
  };
  const pop = (): [number, number] => {
    const top: [number, number] = [hc[0]!, hi[0]!];
    size--;
    if (size > 0) {
      hc[0] = hc[size]!;
      hi[0] = hi[size]!;
      let k = 0;
      for (;;) {
        const l = 2 * k + 1;
        const r = l + 1;
        let m = k;
        if (l < size && less(l, m)) m = l;
        if (r < size && less(r, m)) m = r;
        if (m === k) break;
        swap(k, m);
        k = m;
      }
    }
    return top;
  };
  const inBox = (x: number, y: number) => x >= x0 && x <= x1 && y >= y0 && y <= y1;
  // Seeds: goals in the box at 0; goals outside pull through the box's edge.
  const outside = h.goals.filter((g) => !inBox(g.x, g.y));
  for (const g of h.goals) if (inBox(g.x, g.y)) push(0, (g.y - y0) * w + (g.x - x0));
  if (outside.length > 0) {
    for (let y = y0; y <= y1; y++) {
      for (let x = x0; x <= x1; x++) {
        if (x !== x0 && x !== x1 && y !== y0 && y !== y1) continue;
        if (getTile(state.grid, x, y) === 'water' || !inBounds(state.grid, x, y)) continue;
        let best = Infinity;
        for (const g of outside) best = Math.min(best, manhattan(g, { x, y }));
        push(best * STEP, (y - y0) * w + (x - x0));
      }
    }
  }
  // Entering each tile costs the same from any side, but for the edge crossed: worked out once.
  const enter = new Float64Array(w * ht).fill(-1);
  const stepIn = (ax: number, ay: number, bx: number, by: number, bi: number) => {
    if (enter[bi] === -1) enter[bi] = enterCost(state, bx, by, true);
    const base = enter[bi]!;
    if (base === Infinity) return Infinity;
    return base + edgeCost(state, ax, ay, bx, by, true);
  };
  while (size > 0) {
    const [c, i] = pop();
    if (c >= dist[i]!) continue;
    dist[i] = c;
    const bx = (i % w) + x0;
    const by = Math.floor(i / w) + y0;
    // Backwards: from each neighbour a into this tile b.
    for (const d of DIRS) {
      const ax = bx + d.x;
      const ay = by + d.y;
      if (!inBox(ax, ay)) continue;
      const j = (ay - y0) * w + (ax - x0);
      if (dist[j] !== UNREACHED) continue;
      const step = stepIn(ax, ay, bx, by, i);
      if (step === Infinity) continue;
      push(c + step, j);
    }
  }
  const field = { key, dist, box: h.box };
  fields.set(h, field);
  return field;
}

/** A horde member's next tile: the neighbour it costs least to go on from (ties in N, E, S, W order), or null. */
export function hordeStep(state: SimState, z: Zombie, h: Horde): Point | null {
  const f = flowField(state, h);
  const { x0, y0, x1, y1 } = f.box;
  const w = x1 - x0 + 1;
  const here = z.pos;
  if (here.x < x0 || here.x > x1 || here.y < y0 || here.y > y1) return null;
  // At a goal already: stay. Otherwise the cheapest way on (its step, as things are now, and the
  // rest of the way, from the field), ties in N, E, S, W order. (Comparing against this tile's own
  // cost and moving only on a strictly lower one left them standing: on the best route the two are equal.)
  if (f.dist[(here.y - y0) * w + (here.x - x0)] === 0) return null;
  let best: Point | null = null;
  let bestCost = Infinity;
  for (const d of DIRS) {
    const nx = here.x + d.x;
    const ny = here.y + d.y;
    if (nx < x0 || nx > x1 || ny < y0 || ny > y1) continue;
    const rest = f.dist[(ny - y0) * w + (nx - x0)]!;
    if (rest === UNREACHED) continue;
    const c = stepCost(state, here.x, here.y, nx, ny) + rest;
    if (c < bestCost) {
      bestCost = c;
      best = { x: nx, y: ny };
    }
  }
  return best;
}

// --- Meeting and parting ----------------------------------------------------------------

/** Every tick: refreshes due, and every SOCIAL_EVERY ticks hordes meet and part. */
export function updateHordes(state: SimState): void {
  const list = hordesOf(state);
  if (list.length === 0 && state.tick % SOCIAL_EVERY !== 0) return;
  for (const h of [...list]) if (state.tick - h.refreshTick >= REFRESH_EVERY) refresh(state, h);
  if (state.tick % SOCIAL_EVERY === 0) social(state);
  // Empty or too small: gone.
  for (const h of [...hordesOf(state)]) {
    const zs = membersOf(state, h);
    if (zs.length < HORDE_MIN) disband(state, h, zs);
  }
}

function disband(state: SimState, h: Horde, zs: Zombie[]): void {
  for (const z of zs) {
    z.hordeId = null;
    if (h.mode === 'roam' && h.heading) z.heading = { ...h.heading };
  }
  state.hordes!.splice(state.hordes!.indexOf(h), 1);
}

function social(state: SimState): void {
  const list = hordesOf(state);
  // Joining: the bigger takes the smaller (ties: the earlier one), if they want the same kind of thing;
  // after each merge, look again from the start.
  for (let merged = true; merged; ) {
    merged = false;
    for (let i = 0; i < list.length && !merged; i++) {
      for (let j = i + 1; j < list.length && !merged; j++) {
        const a = list[i]!;
        const b = list[j]!;
        if (a.mode !== b.mode || a.night !== b.night) continue;
        const za = membersOf(state, a);
        const zb = membersOf(state, b);
        if (za.length === 0 || zb.length === 0 || manhattan(middle(za), middle(zb)) > JOIN_RADIUS) continue;
        const [big, small, smallZs] = za.length >= zb.length ? [a, b, zb] : [b, a, za];
        for (const z of smallZs) z.hordeId = big.id;
        big.members.push(...small.members);
        list.splice(list.indexOf(small), 1);
        refresh(state, big);
        merged = true;
      }
    }
  }
  for (const h of [...list]) {
    const zs = membersOf(state, h);
    if (zs.length === 0) continue;
    const mid = middle(zs);
    // Loners in reach fall in.
    for (const z of state.zombies) {
      if (z.hordeId || (z.kind !== 'static' && z.kind !== 'herd') || z.targetId !== null || state.tick < z.stuckUntil) continue;
      if (manhattan(z.pos, mid) > LONER_JOIN) continue;
      z.hordeId = h.id;
      z.heading = null;
      h.members.push(z.id);
    }
    // Strays drop out.
    for (const z of zs) {
      if (manhattan(z.pos, mid) <= STRAY_RADIUS) continue;
      z.hordeId = null;
      if (h.heading) z.heading = { ...h.heading };
    }
    shed(state, h);
    demote(state, h);
  }
}

/** A big horde, now and then, loses a small part off its back, heading off to one side. */
function shed(state: SimState, h: Horde): void {
  if (state.tick % TICKS_PER_HOUR !== 0) return;
  const zs = membersOf(state, h);
  if (zs.length < SHED_MIN) return;
  const roll = (k: number) => hashRoll(idNumber(h.id), state.tick, 0x5ed, k);
  if (roll(0) >= SHED_CHANCE) return;
  const share = SHED_SHARE_MIN + roll(1) * (SHED_SHARE_MAX - SHED_SHARE_MIN);
  const n = Math.max(1, Math.floor(zs.length * share));
  // The ones furthest from where it's going (the back of it), by id among equals.
  const goal = h.goals[0] ?? middle(zs);
  const back = [...zs].sort((a, b) => manhattan(b.pos, goal) - manhattan(a.pos, goal) || (a.id < b.id ? -1 : 1)).slice(0, n);
  const mid = middle(back);
  // Off to one side of where it was going.
  const dx = goal.x - mid.x;
  const dy = goal.y - mid.y;
  const side = roll(2) < 0.5 ? 1 : -1;
  const { width, height } = state.grid;
  const heading = { x: Math.max(0, Math.min(width - 1, mid.x - side * dy)), y: Math.max(0, Math.min(height - 1, mid.y + side * dx)) };
  for (const z of back) z.hordeId = null;
  const split = formHorde(state, back, heading, false);
  if (!split) for (const z of back) z.heading = { ...heading };
  logEvent(state, `${n} broke away from a horde of ${zs.length} near (${mid.x}, ${mid.y})`);
}

/** A roaming horde with nobody of yours near goes back to being a herd marker (herds.ts). */
function demote(state: SimState, h: Horde): void {
  if (h.mode !== 'roam' || h.night) return;
  const zs = membersOf(state, h);
  if (zs.length === 0) return;
  const mid = middle(zs);
  if (state.survivors.some((s) => manhattan(s.pos, mid) <= demoteRadius())) return;
  const { width, height } = state.grid;
  const to = h.heading ?? { x: width - 1 - mid.x, y: height - 1 - mid.y };
  for (const z of zs) removeZombie(state, z);
  state.hordes!.splice(state.hordes!.indexOf(h), 1);
  state.herds.push({ id: `h${state.nextId++}`, x: mid.x, y: mid.y, to: { x: Math.max(0, Math.min(width - 1, to.x)), y: Math.max(0, Math.min(height - 1, to.y)) }, size: zs.length, nextStepTick: state.tick, heard: false });
}

/** A noise (noise.ts): a roaming horde mostly in earshot turns to it; if only part is, that part splits off. */
export function hordesHear(state: SimState, p: Point, r: number, toward: (z: { x: number; y: number }) => Point): void {
  for (const h of [...hordesOf(state)]) {
    if (h.mode !== 'roam') continue;
    const zs = membersOf(state, h);
    const heard = zs.filter((z) => manhattan(z.pos, p) <= r);
    if (heard.length === 0) continue;
    const mid = middle(heard);
    const heading = toward(mid);
    if (heard.length * 2 > zs.length) {
      h.heading = heading;
      h.goals = [{ ...heading }];
      refresh(state, h);
      continue;
    }
    for (const z of heard) z.hordeId = null;
    if (!formHorde(state, heard, heading, false)) for (const z of heard) z.heading = { ...heading };
  }
}

