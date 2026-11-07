// Harness (not part of npm test): a scripted colony player on generated worlds, for balance passes.
// Run: npm run harness -- colony   (env: see the top of the file; output goes to harness/out/colony.txt)
// Up to DAYS days (25) or BUDGET_MS of wall clock (300000) per world, for each of SEEDS (1,2,3).
// The player walls in the camp, builds rooms and defences, sets priorities, amputates and plants herbs.
import { mkdirSync, writeFileSync } from 'node:fs';
import { it } from 'vitest';
import { Session } from 'node:inspector';
import { applyCommand, type Command } from '../src/sim/commands';
import { foodDays } from '../src/sim/alerts';
import { threatScale } from '../src/sim/director';
import { dayOf } from '../src/sim/time';
import { canAmputate } from '../src/sim/wounds';
import { mood, moodFactors } from '../src/sim/mood';
import { seasonOf } from '../src/sim/seasons';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH } from '../src/sim/time';
import { createWorld } from '../src/sim/worldgen';

const cmd = (s: SimState, c: Command) => applyCommand(s, c);
const out: string[] = [];
const OUT = process.env.OUT ?? 'harness/out/colony.txt';
mkdirSync('harness/out', { recursive: true });
const flush = () => writeFileSync(OUT, out.join('\n') + '\n');
const BUDGET_MS = Number(process.env.BUDGET_MS ?? 300000);

/** The mood factors dragging the colony down most, summed over everyone. */
function worstFactors(state: SimState): string {
  const sum = new Map<string, number>();
  for (const s of state.survivors) for (const [label, v] of moodFactors(state, s)) if (v < 0) sum.set(label, (sum.get(label) ?? 0) + v);
  return [...sum.entries()].sort((a, b) => a[1] - b[1]).slice(0, 3).map(([l, v]) => `${l} ${v}`).join(', ');
}

function run(seed: number, days: number): void {
  const began = performance.now();
  const state = createWorld(seed);
  const start = { ...state.survivors[0]!.pos };
  cmd(state, { type: 'designateRoom', x: start.x - 3, y: start.y - 5, purpose: 'housing' });
  for (const [kind, count] of [['weapon', 2], ['bandage', 2], ['medicine', 2], ['coat', 3], ['boots', 1], ['cart', 1], ['hammer', 1], ['axe', 1]] as const) cmd(state, { type: 'setCraftTarget', kind, count });
  cmd(state, { type: 'designateBuild', x: start.x + 6, y: start.y - 3, structure: 'workbench' });
  // A crafter-doctor, as a player would set one.
  const maker = state.survivors[0]!;
  for (const job of ['craft', 'doctor', 'cook'] as const) cmd(state, { type: 'setPriority', survivorId: maker.id, jobType: job, priority: 1 });
  cmd(state, { type: 'designateBuild', x: start.x + 6, y: start.y - 1, structure: 'stove' });
  cmd(state, { type: 'designateBuild', x: start.x + 8, y: start.y - 1, structure: 'well' });
  cmd(state, { type: 'designateBuild', x: start.x + 8, y: start.y + 1, structure: 'collector' });
  // New in pass 4: heaters by the camp, coops, a generator and radio, wheat.
  for (const [dx, dy] of [[-6, 0], [4, 3]] as const) cmd(state, { type: 'designateBuild', x: start.x + dx, y: start.y + dy, structure: 'heater' });
  for (const dx of [10, 11]) cmd(state, { type: 'designateBuild', x: start.x + dx, y: start.y - 1, structure: 'coop' });
  cmd(state, { type: 'designateBuild', x: start.x + 10, y: start.y + 2, structure: 'generator' });
  cmd(state, { type: 'designateBuild', x: start.x + 11, y: start.y + 2, structure: 'radio' });
  // One guard on the night shift.
  const watch = state.survivors[2];
  if (watch) {
    cmd(state, { type: 'setShift', survivorId: watch.id, shift: 'night' });
    cmd(state, { type: 'setPriority', survivorId: watch.id, jobType: 'guard', priority: 1 });
  }
  // New in pass 5: quarantine on, a lab bench, a beacon, two lamps, pits by the gates.
  // (quarantine off: see pass 5)
  const R = 16;
  // A graveyard: a 6x5 walled yard with a door, south-east of the camp.
  const gx = start.x + 4;
  const gy = start.y + 6;
  for (let x = gx; x <= gx + 5; x++) for (let y = gy; y <= gy + 4; y++) {
    if (x === gx || x === gx + 5 || y === gy || y === gy + 4) cmd(state, { type: 'designateBuild', x, y, structure: x === gx + 2 && y === gy ? 'door' : 'wall' });
  }
  const dirs = [[1, 0], [0, 1], [-1, 0], [0, -1]];
  let seen = 0;
  for (let day = 1; day <= days; day++) {
    cmd(state, { type: 'designateRoom', x: gx + 2, y: gy + 2, purpose: 'graveyard' });
    // Wall the camp in from day 5 (clearing trees off the line), and hunt every deer in sight.
    if (day === 5) for (const [dx, dy] of [[0, 4], [-4, -2]] as const) cmd(state, { type: 'designateBuild', x: start.x + dx, y: start.y + dy, structure: 'lamp' });
    if (day === 10) {
      cmd(state, { type: 'designateBuild', x: start.x + 12, y: start.y + 2, structure: 'lab' });
      cmd(state, { type: 'designateBuild', x: start.x + 12, y: start.y - 1, structure: 'beacon' });
    }
    if (day >= 5) {
      for (let i = -R; i <= R; i++) {
        for (const [x, y] of [[start.x + i, start.y - R], [start.x + i, start.y + R], [start.x - R, start.y + i], [start.x + R, start.y + i]]) {
          const f = state.furniture.find((g) => g.x === x && g.y === y && g.kind === 'tree');
          if (f) cmd(state, { type: 'designateRemove', x, y });
        }
      }
      cmd(state, { type: 'planPerimeter', x0: start.x - R, y0: start.y - R, x1: start.x + R, y1: start.y + R });
      for (const [x, y] of [[start.x, start.y - R + 1], [start.x, start.y + R - 1], [start.x - R + 1, start.y], [start.x + R - 1, start.y]]) cmd(state, { type: 'designateBuild', x: x!, y: y!, structure: 'pit' });
    }
    cmd(state, { type: 'designateHunt', x0: 0, y0: 0, x1: state.grid.width - 1, y1: state.grid.height - 1, kinds: ['deer'] });
    for (let i = state.farm.length; i < 2 * state.survivors.length && i < 40; i++) cmd(state, { type: 'designateFarm', x: start.x - 8 + (i % 8), y: start.y + 6 + Math.floor(i / 8), crop: i % 8 === 7 ? 'herb' : i % 3 === 2 ? 'wheat' : 'potato' });
    const [dx, dy] = dirs[day % 4]!;
    const cx = start.x + dx! * (20 + day * 6);
    const cy = start.y + dy! * (20 + day * 6);
    cmd(state, { type: 'designateExplore', x0: cx - 10, y0: cy - 10, x1: cx + 10, y1: cy + 10 });
    for (const f of state.furniture) if ((f.kind === 'crate' || f.kind === 'tree') && !f.scavenge) cmd(state, { type: 'designateScavenge', x: f.x, y: f.y });
    // Chicken coops for the winter (farming.ts: they lay all year), one for every three people, from day 6.
    if (day >= 6) {
      const coops = state.furniture.filter((f) => f.kind === 'coop').length + state.blueprints.filter((b) => b.target === 'coop').length;
      for (let k = coops; k < Math.min(10, Math.ceil(state.survivors.length / 3)); k++) cmd(state, { type: 'designateBuild', x: start.x + 6 + (k % 5) * 2, y: start.y - 8 + Math.floor(k / 5) * 2, structure: 'coop' });
    }
    const beds = state.furniture.filter((f) => f.kind === 'bed').length;
    for (let b = beds; b < state.survivors.length; b++) cmd(state, { type: 'designateBuild', x: start.x - 10 + b, y: start.y - 10, structure: 'bed' });
    const t0 = performance.now();
    let worst = 0;
    // PROFILE_DAY=n: a V8 profile of that day's ticks, top functions to harness/out/colony-profile.txt.
    const session = process.env.PROFILE_DAY && Number(process.env.PROFILE_DAY) === day ? new Session() : null;
    if (session) {
      session.connect();
      session.post('Profiler.enable');
      session.post('Profiler.setSamplingInterval', { interval: 100 });
      session.post('Profiler.start');
    }
    for (let t = 0; t < DAY_LENGTH; t++) {
      const a = performance.now();
      tick(state);
      worst = Math.max(worst, performance.now() - a);
      // Take a trade we can afford (keeps neighbours sweet).
      for (const s of state.survivors) if (canAmputate(s)) cmd(state, { type: 'amputate', survivorId: s.id });
      for (const r of [...state.requests]) cmd(state, { type: 'fulfilRequest', requestId: r.id });
      for (const tr of state.traders) if (!tr.traded) tr.offers.forEach((_, i) => cmd(state, { type: 'trade', traderId: tr.id, offer: i }));
      for (const w of [...state.wanderers]) if (state.items.filter((i) => i.kind === 'food').length >= 3 * state.survivors.length) cmd(state, { type: 'acceptWanderer', wandererId: w.id });
      if (state.survivors.length === 0) break;
    }
    if (session) {
      session.post('Profiler.stop', (err, res) => {
        if (err || !res) return;
        const prof = res.profile;
        const byId = new Map(prof.nodes.map((n) => [n.id, n]));
        const parent = new Map<number, number>();
        for (const n of prof.nodes) for (const c of n.children ?? []) parent.set(c, n.id);
        const name = (n: (typeof prof.nodes)[number]) => `${n.callFrame.functionName || '(anon)'} ${n.callFrame.url.split('/').pop()}:${n.callFrame.lineNumber + 1}`;
        const self = new Map<string, number>();
        const total = new Map<string, number>();
        let all = 0;
        for (const id of prof.samples ?? []) {
          all++;
          const seenK = new Set<string>();
          let cur: number | undefined = id;
          let first = true;
          while (cur !== undefined) {
            const k = name(byId.get(cur)!);
            if (first) self.set(k, (self.get(k) ?? 0) + 1);
            first = false;
            if (!seenK.has(k)) {
              seenK.add(k);
              total.set(k, (total.get(k) ?? 0) + 1);
            }
            cur = parent.get(cur);
          }
        }
        const top = (m: Map<string, number>) => [...m].filter(([k]) => /\.ts:/.test(k)).sort((a, b) => b[1] - a[1]).slice(0, 25).map(([k, c]) => `${((100 * c) / all).toFixed(1).padStart(5)}%  ${k}`);
        writeFileSync('harness/out/colony-profile.txt', [`seed ${seed} day ${day}`, 'TOTAL', ...top(total), 'SELF', ...top(self)].join('\n') + '\n');
      });
      session.disconnect();
    }
    const ev = state.events.filter((e) => e.tick >= seen).map((e) => e.message);
    seen = state.tick;
    const n = (k: string) => state.items.filter((i) => i.kind === k).length;
    const moods = state.survivors.map((s) => mood(state, s));
    const count = (re: RegExp) => ev.filter((m) => re.test(m)).length;
    out.push(
      [
        `s${seed} day ${String(day).padStart(2)} ${seasonOf(state.tick - 1).slice(0, 3)} ${((performance.now() - t0) / DAY_LENGTH).toFixed(2)}ms/t worst ${worst.toFixed(0)}`,
        `alive ${state.survivors.length}${state.survivors.some((s) => s.childUntil !== null) ? ` (kids ${state.survivors.filter((s) => s.childUntil !== null).length})` : ''}`,
        `food ${n('food')}+${n('meal')} (${foodDays(state).toFixed(1)}d) wood ${n('wood')} cloth ${n('cloth')}`,
        `coats ${state.survivors.filter((s) => s.body).length} dogs ${state.dogs.length} med ${n('medicine')} band ${n('bandage')} wpn ${n('weapon')} benches ${state.furniture.filter((f) => f.kind === 'workbench').length} crafted ${count(/crafted|made medicine/)}`,
        `zombies ${state.zombies.length}`,
        `kills ${count(/killed a zombie|shot|brought down/)}`,
        `raids ${count(/^Raiders/)} fires ${count(/caught fire|firebombed/)} burned ${count(/burned down/)}`,
        `rotted ${ev.filter((m) => /rotted/.test(m)).map((m) => m.split(' ')[0]).join('+') || 0}`,
        `sick ${state.survivors.filter((s) => s.sick !== null).length} inf ${state.survivors.filter((s) => s.infection !== null).length} corpses ${n('corpse')}`,
        `mood ${moods.length ? Math.round(moods.reduce((a, b) => a + b, 0) / moods.length) : '-'}`,
        `rep ${state.settlements.map((s) => s.reputation).join('/')}`,
        `low: ${worstFactors(state)}`,
        `end ${state.endgame.won ?? '-'} cure ${state.endgame.cure} evac ${state.endgame.evacuation === null ? '-' : dayOf(state.endgame.evacuation)} sieges ${count(/SIEGE/)} bleed ${state.survivors.filter((s) => s.bleeding).length} wx ${state.weather} threat ${threatScale(state).toFixed(1)} kinds ${count(/screamer shrieked/)}scr hunted ${count(/hunted|Deer seen/)} prisoners ${state.prisoners.length} walls ${state.grid.tiles.filter((t) => t === 'wall').length}`,
        `deaths ${ev.filter((m) => m.includes(' died of ')).map((m) => m.replace(/^.* died of /, '')).join(',') || '-'}`,
      ].join(' | '),
    );
    flush();
    // LOG=path: the state (its command log included) after each day, to compare two runs (TypeScript and C#).
    if (process.env.LOG) writeFileSync(`${process.env.LOG}-s${seed}-day${day}.json`, JSON.stringify(state));
    if (state.survivors.length === 0) {
      out.push(`s${seed} wiped out on day ${day}`);
      break;
    }
    if (performance.now() - began > BUDGET_MS) {
      out.push(`s${seed} stopped at the time budget after day ${day}`);
      break;
    }
  }
  flush();
}

it('world balance', () => {
  for (const seed of (process.env.SEEDS ?? '1,2,3').split(',').map(Number)) run(seed, Number(process.env.DAYS ?? 25));
}, 3600000);
