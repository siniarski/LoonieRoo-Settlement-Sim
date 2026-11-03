import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
import { RAIDER_HEALTH, TRADER_FIRST_DAY, TRADER_HOUR } from '../src/sim/visitors';
import { addZombie } from '../src/sim/zombies';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 3000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}`);
}

const at = (day: number, hour: number) => (day - 1) * DAY_LENGTH + (hour - 6) * TICKS_PER_HOUR;

function setup(map: string[]) {
  const state = loadScenario({ seed: 3, map });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS };
  return state;
}

describe('traders', () => {
  it(`come on day ${TRADER_FIRST_DAY} at noon with three offers, trade, and leave at dusk`, () => {
    const state = setup(['s.ffffffff', 'wwwwwwww..']);
    state.tick = at(TRADER_FIRST_DAY, TRADER_HOUR);
    tick(state);
    expect(state.traders).toHaveLength(1);
    const t = state.traders[0]!;
    expect(t.offers).toHaveLength(3);
    // Take an offer we can afford; check the swap.
    const i = t.offers.findIndex((o) => o.want === 'food' || o.want === 'wood');
    const o = t.offers[i]!;
    const before = state.items.filter((it) => it.kind === o.give).length;
    const want = state.items.filter((it) => it.kind === o.want).length;
    expect(applyCommand(state, { type: 'trade', traderId: t.id, offer: i }).ok).toBe(true);
    expect(state.items.filter((it) => it.kind === o.want).length).toBe(want - o.wantCount + (o.give === o.want ? o.giveCount : 0));
    expect(state.items.filter((it) => it.kind === o.give).length).toBe(before + o.giveCount - (o.give === o.want ? o.wantCount : 0));
    runUntil(state, () => state.traders.length === 0, DAY_LENGTH);
  });

  it("refuses a trade you can't pay for", () => {
    const state = setup(['s.........']);
    state.traders.push({ id: 't1', x: 5, y: 0, leavesTick: 1e9, offers: [{ give: 'weapon', giveCount: 1, want: 'food', wantCount: 4 }], from: null, traded: false });
    expect(applyCommand(state, { type: 'trade', traderId: 't1', offer: 0 }).ok).toBe(false);
  });
});

describe('raiders and locks', () => {
  // A hut with one door; a survivor inside; a raider outside, at night.
  const HUT = ['r..........', '...#####...', '...#.s.#...', '...##D##...', '...........'];

  function night(locked: boolean) {
    const state = setup(HUT.map((row) => row.replace('r', '.')));
    state.tick = at(1, 23) + 1;
    if (locked) applyCommand(state, { type: 'toggleLock', x: 5, y: 3 });
    addZombie(state, 0, 0, RAIDER_HEALTH, 'raider');
    return state;
  }

  it('raiders walk in through an unlocked door', () => {
    const state = night(false);
    runUntil(state, () => state.survivors[0]!.health < 1000, 400);
    expect(state.grid.tiles[3 * 11 + 5]).toBe('door'); // walked through, no damage needed
    expect(state.grid.damage[3 * 11 + 5]).toBe(0);
    expect(state.survivors[0]!.infection).toBeNull(); // raiders don't infect
  });

  it('a locked door must be broken down', () => {
    const state = night(true);
    runUntil(state, () => state.grid.damage[3 * 11 + 5]! > 0, 400);
    expect(state.survivors[0]!.health).toBe(1000);
  });

  it('raiders leave at dawn; only doors can be locked', () => {
    const state = night(false);
    runUntil(state, () => state.zombies.length === 0, DAY_LENGTH);
    expect(applyCommand(state, { type: 'toggleLock', x: 0, y: 0 }).ok).toBe(false);
  });
});
