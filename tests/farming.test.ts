import { describe, expect, it } from 'vitest';
import { NO_JOBS } from '../src/sim/jobTypes';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { FARM_WORK, GROW_TICKS, isRipe } from '../src/sim/farming';
import { checkInvariants } from '../src/sim/invariants';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH } from '../src/sim/time';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 4000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

/** Farmers only (and haulers when asked), so other work doesn't interfere. */
function setup(map: string[], haul = false) {
  const state = loadScenario({ seed: 2, map });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, farm: 3, ...(haul ? { haul: 3 as const } : {}) };
  return state;
}

describe('farming', () => {
  it('plots go on bare floor only; Cancel removes them', () => {
    const state = setup(['.#c.']);
    const farm = (x: number) => applyCommand(state, { type: 'designateFarm', x, y: 0 });
    expect(farm(0)).toEqual({ ok: true, changed: true });
    expect(farm(0)).toEqual({ ok: true, changed: false });
    expect(farm(1)).toEqual({ ok: true, changed: false }); // wall
    expect(farm(2)).toEqual({ ok: true, changed: false }); // crate
    expect(dumpText(state)).toBe('~#c.');
    applyCommand(state, { type: 'cancelDesignation', x: 0, y: 0 });
    expect(state.farm).toEqual([]);
  });

  it('a plot is sown, grows, is harvested into food, and gets sown again', () => {
    const state = setup(['s..~']);
    const plot = state.farm[0]!;
    runUntil(state, () => plot.planted);
    expect(state.survivors[0]!.note).toBe('sowed (3, 0)');
    const sownAt = state.tick;
    runUntil(state, () => isRipe(plot));
    expect(state.tick - sownAt).toBe(GROW_TICKS);
    runUntil(state, () => state.items.length === 1);
    expect(state.items[0]).toMatchObject({ kind: 'food' });
    expect(state.survivors[0]!.note).toBe('harvested food at (3, 0)');
    // By now the farmer is hungry, so they may eat the fresh harvest first.
    runUntil(state, () => plot.planted, FARM_WORK + 100);
  });

  it('crops only grow once sown', () => {
    const state = loadScenario({ seed: 2, map: ['~'] });
    for (let i = 0; i < 100; i++) tick(state);
    expect(state.farm[0]).toMatchObject({ planted: false, growth: 0 });
  });

  it('building on a plot destroys it', () => {
    const state = setup(['s.~~']);
    applyCommand(state, { type: 'placeWall', x: 3, y: 0 });
    tick(state);
    expect(state.farm.map((p) => p.x)).toEqual([2]);
  });

  it('two farmers never tend the same plot', () => {
    const state = setup(['s..~..s']);
    for (let i = 0; i < 40; i++) {
      tick(state);
      expect(state.survivors.filter((s) => s.jobId).length).toBeLessThanOrEqual(1);
    }
  });

  it('three survivors with 12 plots live through three days on what they grow', () => {
    // Walled all round: no outdoor edge, so no zombies; a big room, so plenty of floor. Three
    // cans bridge the first harvest (hungry and sleeping rough, they'd be miserable and slow).
    const inner = (row: string) => `#${row}#`;
    const map = [
      '#'.repeat(16),
      inner('..............'),
      inner('..~~~~~~......'),
      inner('..~~~~~~......'),
      inner('..............'),
      inner('...s..s..s....'),
      inner('...........fff'),
      '#'.repeat(16),
    ];
    const state = setup(map);
    for (let i = 0; i < 3 * DAY_LENGTH; i++) tick(state);
    expect(state.events.filter((e) => e.message.includes('died'))).toEqual([]);
    expect(state.survivors).toHaveLength(3);
  });
});
