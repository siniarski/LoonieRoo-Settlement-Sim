import { describe, expect, it } from 'vitest';
import { PRESETS } from '../src/sim/difficulty';
import { loadScenario } from '../src/sim/scenario';
import { SIGHT_RADIUS, sightRadius } from '../src/sim/sight';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, dayOf, isNight, tickOfDay } from '../src/sim/time';
import { ALARM_TICKS, TANK_WATER, isMainsFridge, maybeAlarm, mainsLit, powerOn, waterOn, type Timeline } from '../src/sim/timeline';
import { drankFrom, sourceAt } from '../src/sim/water';
import { CHUNK, createWorld, ensureGeneratedAround } from '../src/sim/worldgen';

const timeline = (t: Partial<Timeline> = {}): Timeline => ({ waterOffDay: 5, powerOffDay: 6, helicopterDay: 0, heli: null, alarms: [], ...t });
/** The first tick of day `d`. */
const startOf = (d: number) => {
  let t = (d - 1) * DAY_LENGTH;
  while (dayOf(t) < d) t += 1;
  while (t > 0 && dayOf(t - 1) === d) t -= 1;
  return t;
};

describe('the mains', () => {
  it('stay on for a while in a fresh world, are off from the start a year on, and missing on hand-made maps', () => {
    const fresh = createWorld(3, 64, { ...PRESETS.settler.settings });
    const t = fresh.timeline!;
    expect(t.waterOffDay).toBeGreaterThanOrEqual(8);
    expect(t.waterOffDay).toBeLessThanOrEqual(14);
    expect(t.powerOffDay).toBeGreaterThanOrEqual(10);
    expect(waterOn(fresh) && powerOn(fresh)).toBe(true);
    fresh.tick = startOf(t.waterOffDay);
    expect(waterOn(fresh)).toBe(false);
    const late = createWorld(3, 64, { ...PRESETS.apocalypse.settings });
    expect(waterOn(late) || powerOn(late)).toBe(false);
    expect(late.timeline!.helicopterDay).toBe(0);
    expect(waterOn(loadScenario({ seed: 1, map: ['...'] }))).toBe(false);
  }, 20000);

  it('kitchen counters, toilets and baths run while the water is on; iceboxes keep cold while the power is', () => {
    const state = createWorld(3, 128, { ...PRESETS.settler.settings });
    for (let y = 0; y < 128; y += CHUNK) for (let x = 0; x < 128; x += CHUNK) ensureGeneratedAround(state, { x, y });
    const tap = state.furniture.find((f) => f.look === 'sink' || f.look === 'counter');
    const toilet = state.furniture.find((f) => f.look === 'toilet');
    const icebox = state.furniture.find((f) => f.look === 'icebox');
    expect(tap && icebox && toilet).toBeTruthy();
    expect(sourceAt(state, tap!.x, tap!.y)).toBe('tap');
    expect(isMainsFridge(state, icebox!)).toBe(true);
    state.tick = startOf(state.timeline!.powerOffDay);
    expect(sourceAt(state, tap!.x, tap!.y)).toBeNull();
    expect(isMainsFridge(state, icebox!)).toBe(false);
    // With the mains off a toilet's tank still holds a drink or two, then it's dry.
    expect(sourceAt(state, toilet!.x, toilet!.y)).toBe('tank');
    for (let k = 0; k < TANK_WATER.toilet!; k++) drankFrom(state, 'tank', toilet!.x, toilet!.y);
    expect(sourceAt(state, toilet!.x, toilet!.y)).toBeNull();
  }, 20000);

  it('without the power, a house fridge rots a find a day', () => {
    const state = createWorld(3, 128, { ...PRESETS.settler.settings });
    for (let y = 0; y < 128; y += CHUNK) for (let x = 0; x < 128; x += CHUNK) ensureGeneratedAround(state, { x, y });
    const icebox = state.furniture.find((f) => f.look === 'icebox' && f.loot > 0)!;
    icebox.loot = 3;
    state.tick = startOf(state.timeline!.powerOffDay + 1);
    tick(state);
    expect(icebox.loot).toBe(2);
  }, 20000);

  it('with the power on, indoors is lit at night and you see as far as by day', () => {
    const state = loadScenario({ seed: 1, map: ['#####', '#.s.#', '#...#', '#####', '.....'] });
    state.timeline = timeline();
    while (!isNight(state.tick)) state.tick += 10;
    expect(mainsLit(state, { x: 2, y: 1 })).toBe(true);
    expect(mainsLit(state, { x: 2, y: 4 })).toBe(false); // outdoors
    expect(sightRadius(state, state.survivors[0]!)).toBe(SIGHT_RADIUS);
    state.timeline.powerOffDay = 1;
    expect(mainsLit(state, { x: 2, y: 1 })).toBe(false);
    expect(sightRadius(state, state.survivors[0]!)).toBeLessThan(SIGHT_RADIUS);
  });

  it('says so the morning they go off', () => {
    const state = loadScenario({ seed: 1, map: ['s..'] });
    state.timeline = timeline({ waterOffDay: 2, powerOffDay: 2 });
    state.survivors[0]!.priorities = { ...state.survivors[0]!.priorities, guard: 0 };
    state.tick = startOf(2) - 1;
    tick(state);
    tick(state);
    expect(state.events.some((e) => e.message.includes('taps ran dry'))).toBe(true);
    expect(state.events.some((e) => e.message.includes('lights went out'))).toBe(true);
  });
});

describe('happenings', () => {
  it('a helicopter comes over on its day, hangs over you, draws the dead, and goes', () => {
    const state = loadScenario({ seed: 1, map: Array.from({ length: 40 }, () => '.'.repeat(40)).map((r, y) => (y === 20 ? r.slice(0, 20) + 's' + r.slice(21) : r)) });
    for (const k of Object.keys(state.survivors[0]!.priorities)) state.survivors[0]!.priorities[k as 'guard'] = 0;
    state.timeline = timeline({ helicopterDay: 1 });
    state.dormant = [{ id: 'd1', x: 5, y: 5 }];
    while (tickOfDay(state.tick) !== Math.floor(DAY_LENGTH * 0.6)) state.tick += 1;
    tick(state);
    expect(state.timeline.heli).not.toBeNull();
    let over = false;
    for (let i = 0; i < 2000 && state.timeline.heli; i++) {
      tick(state);
      const h = state.timeline.heli as Timeline['heli'];
      if (h && h.x === 20 && h.y === 20) over = true;
    }
    expect(over).toBe(true);
    expect(state.timeline.heli).toBeNull();
    expect(state.dormant).toEqual([]); // the noise woke it
    expect(state.events.some((e) => e.message.includes('helicopter is gone'))).toBe(true);
  });

  it('an alarm may go off when a window is smashed with the power on, and rings for a while', () => {
    const state = loadScenario({ seed: 1, map: ['s....'] });
    state.timeline = timeline();
    for (let key = 0; key < 20 && state.timeline.alarms.length === 0; key++) maybeAlarm(state, { x: 3, y: 0 }, key);
    expect(state.timeline.alarms.length).toBe(1);
    for (let i = 0; i <= ALARM_TICKS; i++) tick(state);
    expect(state.timeline.alarms).toEqual([]);
    state.timeline.powerOffDay = 1;
    for (let key = 0; key < 20; key++) maybeAlarm(state, { x: 3, y: 0 }, key);
    expect(state.timeline.alarms).toEqual([]);
  });
});
