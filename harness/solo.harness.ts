// Harness (not part of npm test): a scripted solo player (direct control) on a preset, for balance passes.
// Run: npm run harness -- solo   (env: see the top of the file; output goes to harness/out/solo.txt)
// Up to DAYS days (25) or BUDGET_MS (300000) per world, for each of SEEDS (1,2,3); PRESET (apocalypse); TRACE=1 logs every 150 ticks.
// The player searches crates by day, eats, drinks (taps while the water's on), sleeps in the hut, fights
// what's next to it, flees when hurt, treats itself and amputates; sneaks when the dead are about but not on
// it, and forages when hungry with no food about. It does not yet barricade, hunt or drive.
import { mkdirSync, writeFileSync } from 'node:fs';
import { it } from 'vitest';
import { applyCommand, type Command } from '../src/sim/commands';
import { controlled } from '../src/sim/control';
import { PRESETS } from '../src/sim/difficulty';
import { HUNGRY } from '../src/sim/food';
import { HEALTH_MAX } from '../src/sim/health';
import { findPathToAny, openBetween } from '../src/sim/pathfinding';
import { EDGE_DOOR, edgeBetween, edgeIsOpen, getTile, isOpen, neighbours4, type Point } from '../src/sim/grid';
import { SUB, SWING_REACH, centre, distance2, inReach, tileOf } from '../src/sim/motion';
import { TIRED } from '../src/sim/rest';
import type { SimState } from '../src/sim/state';
import type { Survivor } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, dayOf, isNight } from '../src/sim/time';
import { THIRSTY } from '../src/sim/water';
import { createWorld } from '../src/sim/worldgen';
import { remediesFor } from '../src/sim/medicine';
import { canAmputate } from '../src/sim/wounds';
import { forageTime } from '../src/sim/forage';
import { canForage } from '../src/sim/handiwork';
import { GROUNDS, isWalkable } from '../src/sim/grid';
import { isIndoors } from '../src/sim/rooms';
import { nearestTile } from '../src/sim/space';
import { isTap } from '../src/sim/timeline';

const cmd = (s: SimState, c: Command) => applyCommand(s, c);
const out: string[] = [];
const OUT = process.env.OUT ?? 'harness/out/solo.txt';
mkdirSync('harness/out', { recursive: true });
const flush = () => writeFileSync(OUT, out.join('\n') + '\n');
const BUDGET_MS = Number(process.env.BUDGET_MS ?? 300000);
const SETTINGS = PRESETS[(process.env.PRESET ?? 'apocalypse') as keyof typeof PRESETS].settings;
const dist = (a: Point, b: Point) => Math.max(Math.abs(a.x - b.x), Math.abs(a.y - b.y));

interface Bot {
  goal: string;
  path: Point[];
  pathFor: string;
  searched: number;
  ate: number;
  drank: number;
  slept: number;
  fled: number;
  foraged: number;
  sneaked: number;
  treated: number;
  amputated: number;
}

/**
 * Walk a path to a tile next to (or onto) a target, and use it on arrival. You move freely
 * (motion.ts): steer toward the centre of the next tile on the path, opening a closed door
 * that's next in the way (a colonist's path goes through doors; you have to open them).
 */
function goUse(state: SimState, s: Survivor, bot: Bot, key: string, target: Point, onto = false): void {
  const c = state.control!;
  const stop = () => {
    if (c.dx !== 0 || c.dy !== 0) cmd(state, { type: 'steer', dx: 0, dy: 0 });
  };
  const at = s.pos;
  const here = centre(at);
  const settled = Math.abs(s.at.x - here.x) <= SUB / 4 && Math.abs(s.at.y - here.y) <= SUB / 4;
  if (onto ? at.x === target.x && at.y === target.y && settled : Math.abs(at.x - target.x) + Math.abs(at.y - target.y) <= 1) {
    stop();
    if (!c.busy) cmd(state, { type: 'use', x: target.x, y: target.y });
    return;
  }
  if (bot.pathFor !== key || bot.path.length === 0 || state.tick % 40 === 0) {
    bot.path = findPathToAny(state.grid, at, onto ? [target] : neighbours4(state.grid, target.x, target.y)) ?? [];
    bot.pathFor = key;
  }
  const i = bot.path.findIndex((p) => p.x === at.x && p.y === at.y);
  if (i >= 0) bot.path.splice(0, i + 1);
  const next = bot.path[0];
  if (!next) return stop();
  const edge = edgeBetween(state.grid, at.x, at.y, next.x, next.y);
  const shutEdgeDoor = edge >= 0 && state.grid.edges[edge] === EDGE_DOOR && !edgeIsOpen(state.grid, edge);
  if (shutEdgeDoor || (getTile(state.grid, next.x, next.y) === 'door' && !isOpen(state.grid, next.x, next.y))) {
    stop();
    cmd(state, { type: 'use', x: next.x, y: next.y });
    return;
  }
  const goal = centre(next);
  const dx = Math.abs(goal.x - s.at.x) > SUB / 10 ? Math.sign(goal.x - s.at.x) : 0;
  const dy = Math.abs(goal.y - s.at.y) > SUB / 10 ? Math.sign(goal.y - s.at.y) : 0;
  if (c.dx !== dx || c.dy !== dy) cmd(state, { type: 'steer', dx, dy });
}

function decide(state: SimState, s: Survivor, bot: Bot, home: Point, bed: Point): void {
  const c = state.control!;
  // Mid-step only steering changes: keep on with the current goal.
  if (s.move && bot.goal && !['fight', 'flee', 'idle'].includes(bot.goal)) {
    const key = bot.pathFor;
    if (bot.path.length) {
      const at = s.move.to;
      const i = bot.path.findIndex((p) => p.x === at.x && p.y === at.y);
      if (i >= 0) bot.path.splice(0, i + 1);
      const next = bot.path[0];
      const dx = next ? Math.sign(next.x - at.x) : 0;
      const dy = next ? Math.sign(next.y - at.y) : 0;
      if (c.dx !== dx || c.dy !== dy) cmd(state, { type: 'steer', dx, dy });
    }
    void key;
    return;
  }
  if (s.move) return;
  // Where the dead really are: the tile under each one's centre (its pos can lag a tile behind: motion.ts).
  const zs = state.zombies.map((q) => ({ id: q.id, pos: tileOf(q.at), body: q })).filter((q) => dist(q.pos, s.pos) <= 6).sort((a, b) => dist(a.pos, s.pos) - dist(b.pos, s.pos));
  const z = zs[0];
  // The dead about but not on you: sneak (sight.ts), so they don't notice; otherwise walk.
  const sneak = state.zombies.some((q) => dist(q.pos, s.pos) <= 10) && !(z && dist(z.pos, s.pos) <= 1);
  if ((c.sneaking === true) !== sneak) {
    cmd(state, { type: 'sneak', on: sneak });
    if (sneak) bot.sneaked++;
  }
  // Fight what's next to you; flee when hurt.
  // In swing reach as the game measures it (centre to centre, not through a wall): a bite reaches across a tile edge.
  const close = state.zombies.filter((q) => inReach(s, q, SWING_REACH, state.grid)).sort((a, b) => distance2(a.at, s.at) - distance2(b.at, s.at))[0];
  if (close && s.health > HEALTH_MAX * 0.3) {
    const zt = tileOf(close.at);
    if (c.dx || c.dy) cmd(state, { type: 'steer', dx: 0, dy: 0 });
    cmd(state, { type: 'aim', x: Math.max(-64, Math.min(64, Math.round((close.at.x - s.at.x) / 4))) || 1, y: Math.max(-64, Math.min(64, Math.round((close.at.y - s.at.y) / 4))) });
    cmd(state, { type: 'swing', x: zt.x, y: zt.y });
    bot.goal = 'fight';
    return;
  }
  if (z && s.health <= HEALTH_MAX * 0.3) {
    bot.fled++;
    const dx = Math.sign(s.pos.x - z.pos.x) || 1;
    const dy = Math.sign(s.pos.y - z.pos.y);
    if (c.dx !== dx || c.dy !== dy) cmd(state, { type: 'steer', dx, dy });
    bot.goal = 'flee';
    return;
  }
  if (c.busy) {
    // Don't sleep through a zombie at the door.
    if (c.busy.what === 'sleep' && z && dist(z.pos, s.pos) <= 3) cmd(state, { type: 'steer', dx: 0, dy: 0 });
    return;
  }
  const items = (kind: string) => state.items.filter((i) => i.pos && i.kind === kind && !i.reservedBy).sort((a, b) => dist(a.pos!, s.pos) - dist(b.pos!, s.pos));
  // Treat yourself: the remedy you carry, else the nearest one about; an infection with none: cut it off.
  // A searched container beside you: take everything in it (inventory.ts).
  const full = state.furniture.find((f) => f.kind === 'crate' && dist(f, s.pos) <= 1 && state.items.some((i) => i.inside === f.id));
  if (full) cmd(state, { type: 'takeAll', containerId: full.id });
  const inBag = (kinds: string[]) => s.pack.map((id) => state.items.find((i) => i.id === id)).find((i) => i && kinds.includes(i.kind));
  const needs = remediesFor(s);
  // A remedy in the bag: use it.
  const bagged = needs.length > 0 ? inBag(needs) : undefined;
  if (bagged && (c.dx || c.dy ? cmd(state, { type: 'steer', dx: 0, dy: 0 }) : true) && cmd(state, { type: 'useItem', itemId: bagged.id }).ok) {
    bot.treated++;
    bot.goal = 'treat';
    return;
  }
  if (needs.length > 0) {
    const carried = s.carrying ? state.items.find((i) => i.id === s.carrying) : undefined;
    if (carried && needs.includes(carried.kind)) {
      bot.treated++;
      bot.goal = 'treat';
      cmd(state, { type: 'use', x: s.pos.x, y: s.pos.y });
      return;
    }
    const near = needs.map((k) => items(k)[0]).find((i) => i && dist(i.pos!, s.pos) < 40);
    if (near) {
      bot.goal = 'treat';
      if (dist(near.pos!, s.pos) <= 1) bot.treated++;
      return goUse(state, s, bot, `r${near.id}`, near.pos!);
    }
    if (s.infection !== null && canAmputate(s)) {
      bot.amputated++;
      cmd(state, { type: 'amputate', survivorId: s.id });
    }
  }
  // Keep a medicine about you when you find one.
  if (!s.carrying && !isNight(state.tick)) {
    const med = items('medicine')[0];
    if (med && dist(med.pos!, s.pos) < 12) {
      bot.goal = 'pocket';
      return goUse(state, s, bot, `m${med.id}`, med.pos!);
    }
  }
  if (state.harsh && s.hydration < THIRSTY) {
    const water = state.furniture
      .filter((f) => (f.kind === 'well' || (f.kind === 'collector' && f.loot > 0) || (isTap(state, f) && dist(f, s.pos) < 40)))
      .sort((a, b) => dist(a, s.pos) - dist(b, s.pos))[0];
    if (water) {
      bot.goal = 'drink';
      if (dist(water, s.pos) <= 1 && !c.busy) bot.drank++;
      return goUse(state, s, bot, `water${water.id}`, water);
    }
  }
  if (s.satiety < HUNGRY) {
    // Food in the bag first.
    const packed = inBag(['food', 'meal']);
    if (packed && (c.dx || c.dy ? cmd(state, { type: 'steer', dx: 0, dy: 0 }) : true) && cmd(state, { type: 'useItem', itemId: packed.id }).ok) {
      bot.ate++;
      bot.goal = 'eat';
      return;
    }
    const food = items('food')[0] ?? items('meal')[0];
    if (food) {
      bot.goal = 'eat';
      if (dist(food.pos!, s.pos) <= 1) bot.ate++;
      return goUse(state, s, bot, `food${food.id}`, food.pos!);
    }
    // None about: forage (handiwork.ts), by day on open grass or a field not picked over today.
    if (forageTime(state)) {
      if (canForage(state, s) === null) {
        if (c.dx || c.dy) cmd(state, { type: 'steer', dx: 0, dy: 0 });
        if (cmd(state, { type: 'use', x: s.pos.x, y: s.pos.y }).ok) bot.foraged++;
        bot.goal = 'forage';
        return;
      }
      const spot = nearestTile(state.grid, s.pos, (x, y) => {
        const g = GROUNDS[state.grid.ground[y * state.grid.width + x]!];
        return (g === 'grass' || g === 'field') && isWalkable(state.grid, x, y) && !isIndoors(state, x, y) && state.foraged[String(y * state.grid.width + x)] !== dayOf(state.tick);
      }, 2000);
      if (spot) {
        bot.goal = 'forage';
        return goUse(state, s, bot, `forage${spot.x},${spot.y}`, spot, true);
      }
    }
  }
  if (!s.weapon) {
    const w = items('weapon')[0] ?? items('gun')[0];
    if (w && dist(w.pos!, s.pos) < 30) {
      bot.goal = 'arm';
      return goUse(state, s, bot, `w${w.id}`, w.pos!);
    }
  }
  if (s.rest < TIRED || (isNight(state.tick) && s.rest < 1800)) {
    bot.goal = 'sleep';
    if (s.pos.x === bed.x && s.pos.y === bed.y) bot.slept++;
    return goUse(state, s, bot, 'bed', bed, true);
  }
  if (isNight(state.tick)) {
    bot.goal = 'home';
    return goUse(state, s, bot, 'bed', bed, true);
  }
  // Day: carry food home, else search the nearest crate with something left.
  const crate = state.furniture.filter((f) => f.kind === 'crate' && f.loot > 0 && dist(f, home) < 90).sort((a, b) => dist(a, s.pos) - dist(b, s.pos))[0];
  if (crate) {
    bot.goal = 'search';
    if (dist(crate, s.pos) <= 1) bot.searched++;
    return goUse(state, s, bot, `crate${crate.id}`, crate);
  }
  bot.goal = 'idle';
  if (c.dx || c.dy) cmd(state, { type: 'steer', dx: 0, dy: 0 });
}

function run(seed: number, days: number): void {
  const began = performance.now();
  const state = createWorld(seed, undefined, SETTINGS);
  const s0 = controlled(state)!;
  const bedF = state.furniture.find((f) => f.kind === 'bed')!;
  const bed = { x: bedF.x, y: bedF.y };
  const home = { ...s0.pos };
  const bot: Bot = { goal: '', path: [], pathFor: '', searched: 0, ate: 0, drank: 0, slept: 0, fled: 0, foraged: 0, sneaked: 0, treated: 0, amputated: 0 };
  const log = (why: string) => {
    const s = controlled(state);
    out.push(`  day ${dayOf(state.tick)} ${why}: ${s ? `hp ${s.health} food ${s.satiety} water ${s.hydration} rest ${s.rest} weapon ${s.weapon ? 'y' : 'n'} kills ${s.kills} goal ${bot.goal}` : 'dead'} | items food ${state.items.filter((i) => i.kind === 'food').length} zombies ${state.zombies.length} | searched ${bot.searched} ate ${bot.ate} drank ${bot.drank} slept ${bot.slept} fled ${bot.fled} foraged ${bot.foraged} sneaked ${bot.sneaked} treated ${bot.treated} amputated ${bot.amputated} infected ${s?.infection ?? '-'}`);
  };
  out.push(`seed ${seed} (${process.env.PRESET ?? 'apocalypse'})`);
  for (let t = 0; t < days * DAY_LENGTH; t++) {
    const s = controlled(state);
    if (!s) {
      const ev = state.events.filter((e) => /died|killed/.test(e.message)).slice(-2).map((e) => e.message).join(' / ');
      out.push(`  DEAD on day ${dayOf(state.tick)} at tick ${state.tick}: ${ev}`);
      log('end');
      flush();
      return;
    }
    decide(state, s, bot, home, bed);
    if (process.env.TRACE && state.tick % Number(process.env.TRACE_EVERY ?? 150) === 0) out.push(`   t${state.tick} z ${state.zombies.filter((q) => dist(tileOf(q.at), s.pos) <= 3).map((q) => `${tileOf(q.at).x},${tileOf(q.at).y}${q.windup ? "w" : ""}`).join(" ")} pos ${s.pos.x},${s.pos.y} goal ${bot.goal} sat ${s.satiety} hp ${s.health} busy ${state.control!.busy?.what ?? '-'} steer ${state.control!.dx},${state.control!.dy} path ${bot.path.length} note ${s.note}`);
    tick(state);
    if (state.tick % DAY_LENGTH === 0) log('dawn');
    if (performance.now() - began > BUDGET_MS) {
      out.push(`  out of time on day ${dayOf(state.tick)}`);
      flush();
      return;
    }
  }
  out.push(`  SURVIVED ${days} days`);
  log('end');
  flush();
}

it('solo balance', () => {
  for (const seed of (process.env.SEEDS ?? '1,2,3').split(',').map(Number)) run(seed, Number(process.env.DAYS ?? 25));
}, 3_600_000);
