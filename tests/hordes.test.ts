import { describe, expect, it } from 'vitest';
import { formHorde, getHorde, hordesOf, membersOf, SHED_MIN, STRAY_RADIUS, demoteRadius } from '../src/sim/hordes';
import { makeNoise } from '../src/sim/noise';
import { centre } from '../src/sim/motion';
import { loadFile, toSaveFile } from '../src/sim/savefile';
import { loadScenario } from '../src/sim/scenario';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR, isNight } from '../src/sim/time';
import { addZombie, type Zombie } from '../src/sim/zombies';

const field = (w = 60, h = 40) => Array.from({ length: h }, () => '.'.repeat(w));
const quiet = (state: ReturnType<typeof loadScenario>) => {
  for (const s of state.survivors) for (const k of Object.keys(s.priorities)) s.priorities[k as 'guard'] = 0;
};
const noon = (state: ReturnType<typeof loadScenario>) => (state.tick = Math.floor(DAY_LENGTH / 2));
/** A survivor standing still out of their notice (past AGGRO_RADIUS) but near enough that hordes stay real. */
const watcher = (state: ReturnType<typeof loadScenario>, x: number, y: number) => {
  quiet(state);
  const s = state.survivors[0]!;
  s.pos = { x, y };
  s.at = centre(s.pos);
};
const withSurvivor = (map: string[]) => map.map((r, y) => (y === 0 ? 's' + r.slice(1) : r));
function crowd(state: ReturnType<typeof loadScenario>, x: number, y: number, n: number, kind: Zombie['kind'] = 'herd'): Zombie[] {
  const out: Zombie[] = [];
  for (let i = 0; i < n; i++) out.push(addZombie(state, x + (i % 5), y + Math.floor(i / 5), 200, kind));
  return out;
}

describe('hordes', () => {
  it('a night wave comes as one horde and goes for the weakest way in: the door, not the wall', () => {
    // You in a walled box (tile walls), its only door on the far (east) side; the dead come from the west.
    const map = field(40, 20).map((r, y) => {
      if (y < 6 || y > 12) return r;
      const row = r.split('');
      for (let x = 18; x <= 24; x++) if (y === 6 || y === 12 || x === 18 || x === 24) row[x] = 'W';
      if (y === 9) row[24] = 'D';
      if (y === 9) row[21] = 's';
      return row.join('');
    }).map((r) => r.replace(/W/g, '#'));
    const state = loadScenario({ seed: 1, map });
    quiet(state);
    while (!isNight(state.tick)) state.tick += 10; // night arrivals walk off at dawn
    const wave = crowd(state, 2, 8, 5, 'night');
    const h = formHorde(state, wave, null, true)!;
    expect(h).not.toBeNull();
    expect(new Set(wave.map((z) => z.hordeId))).toEqual(new Set([h.id]));
    expect(h.mode).toBe('hunt');
    let doorHit = false;
    let wallHit = false;
    for (let t = 0; t < 1500 && !doorHit; t++) {
      tick(state);
      if (state.grid.damage[9 * 40 + 24]! > 0) doorHit = true;
      for (let y = 6; y <= 12; y++) if (state.grid.damage[y * 40 + 18]! > 0) wallHit = true;
    }
    expect(doorHit).toBe(true);
    expect(wallHit).toBe(false); // they went round to the door rather than through the west wall
  });

  it('two hordes that meet become one; a loner nearby falls in', () => {
    const state = loadScenario({ seed: 1, map: withSurvivor(field()) });
    noon(state);
    watcher(state, 38, 25);
    const a = formHorde(state, crowd(state, 10, 10, 4), { x: 59, y: 10 }, false)!;
    const b = formHorde(state, crowd(state, 14, 12, 3), { x: 59, y: 10 }, false)!;
    const loner = addZombie(state, 12, 15, 200, 'static');
    for (let t = 0; t < 60; t++) tick(state);
    expect(hordesOf(state)).toHaveLength(1);
    const one = hordesOf(state)[0]!;
    expect(one.id).toBe(a.id); // the bigger kept going
    expect(getHorde(state, b.id)).toBeUndefined();
    expect(loner.hordeId).toBe(one.id);
    expect(membersOf(state, one)).toHaveLength(8);
  });

  it('one left far behind drops out, a loner', () => {
    const state = loadScenario({ seed: 1, map: withSurvivor(field(80, 20)) });
    noon(state);
    watcher(state, 60, 18);
    const zs = crowd(state, 50, 5, 4);
    const h = formHorde(state, zs, { x: 79, y: 5 }, false)!;
    const lag = zs[0]!;
    lag.pos = { x: 50 - STRAY_RADIUS - 3, y: 5 };
    lag.at = centre(lag.pos);
    lag.stuckUntil = 10_000; // in a pit: it can't keep up
    for (let t = 0; t < 60; t++) tick(state);
    expect(lag.hordeId ?? null).toBeNull();
    expect(membersOf(state, h)).toHaveLength(3);
  });

  it('a big horde now and then loses a small part off its back, which heads off on its own', () => {
    const state = loadScenario({ seed: 3, map: withSurvivor(field(120, 120)) });
    noon(state);
    watcher(state, 42, 75);
    const big = formHorde(state, crowd(state, 40, 40, 30), { x: 42, y: 42 }, false)!;
    expect(membersOf(state, big).length).toBeGreaterThanOrEqual(SHED_MIN);
    let shed = false;
    for (let t = 0; t < 24 * TICKS_PER_HOUR && !shed; t++) {
      tick(state);
      shed = state.events.some((e) => e.message.includes('broke away'));
    }
    expect(shed).toBe(true);
    const left = membersOf(state, big).length;
    expect(left).toBeLessThan(30);
    expect(left).toBeGreaterThan(30 * 0.8); // a small part, not half
  });

  it('a roaming horde with nobody near becomes a herd marker again, and the count holds', () => {
    const state = loadScenario({ seed: 1, map: withSurvivor(field(200, 20)) });
    noon(state);
    const s = state.survivors[0];
    if (s) {
      s.pos = { x: 1, y: 1 };
      s.at = centre(s.pos);
    }
    quiet(state);
    formHorde(state, crowd(state, 1 + demoteRadius() + 20, 5, 6), { x: 199, y: 5 }, false);
    const herdsBefore = state.herds.length;
    for (let t = 0; t < 60; t++) tick(state);
    expect(hordesOf(state)).toHaveLength(0);
    expect(state.zombies).toHaveLength(0);
    expect(state.herds).toHaveLength(herdsBefore + 1);
    expect(state.herds.at(-1)!.size).toBe(6);
  });

  it('a noise in earshot of part of a roaming horde draws that part off', () => {
    const state = loadScenario({ seed: 1, map: field(120, 40) });
    noon(state);
    state.survivors.length = 0;
    const zs = [...crowd(state, 10, 10, 5), ...crowd(state, 30, 10, 5)];
    const h = formHorde(state, zs, { x: 119, y: 10 }, false)!;
    makeNoise(state, { x: 12, y: 30 }, 'fighting'); // radius 6: reaches nobody
    expect(hordesOf(state)).toHaveLength(1);
    makeNoise(state, { x: 5, y: 25 }, 'gunfire'); // radius 30: the five at x 10..14 (24 at most), not the five at 30..34 (39 at least)
    expect(hordesOf(state)).toHaveLength(2);
    expect(membersOf(state, h)).toHaveLength(5);
  });

  it('a game saved mid-horde plays on exactly as the one that was saved', () => {
    const state = loadScenario({ seed: 2, map: field(60, 30).map((r, y) => (y === 15 ? r.slice(0, 50) + 's' + r.slice(51) : r)) });
    quiet(state);
    formHorde(state, crowd(state, 3, 3, 8, 'night'), null, true);
    for (let t = 0; t < 40; t++) tick(state);
    const loaded = loadFile(toSaveFile(state));
    expect(loaded.ok).toBe(true);
    if (!loaded.ok) return;
    const copy = loaded.state;
    for (let t = 0; t < 200; t++) {
      tick(state);
      tick(copy);
    }
    expect(JSON.stringify({ ...copy, commandLog: [] })).toBe(JSON.stringify({ ...state, commandLog: [] }));
  });
});

describe('hordes on the move', () => {
  it('a night wave crosses open ground to its prey and attacks', () => {
    const map = Array.from({ length: 20 }, () => '.'.repeat(60)).map((r, y) => (y === 10 ? r.slice(0, 50) + 's' + r.slice(51) : r));
    const state = loadScenario({ seed: 1, map });
    quiet(state);
    while (!isNight(state.tick)) state.tick += 10;
    const wave = crowd(state, 2, 10, 5, 'night');
    formHorde(state, wave, null, true);
    for (let t = 0; t < 600 && state.survivors[0]!.health === 1000; t++) tick(state);
    expect(Math.min(...wave.map((z) => Math.abs(z.pos.x - 50)))).toBeLessThanOrEqual(2);
    expect(state.survivors[0]!.health).toBeLessThan(1000);
  });
});
