import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { CORPSE_CHECK_EVERY, CORPSE_ROT, SICK_TICKS } from '../src/sim/corpses';
import { FIRE_BURN, burnable, ignite } from '../src/sim/fire';
import { getTile } from '../src/sim/grid';
import { HEALTH_MAX } from '../src/sim/health';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { killSurvivor } from '../src/sim/needs';
import { loadScenario } from '../src/sim/scenario';
import type { RoomPurpose } from '../src/sim/rooms';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { damageZombie, addZombie } from '../src/sim/zombies';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = {}, rooms: { x: number; y: number; purpose: RoomPurpose }[] = []) {
  const state = loadScenario({ seed: 9, map, rooms });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

describe('fire', () => {
  it('burns a wall down to floor if nobody puts it out, and spreads along walls', () => {
    const state = setup(['s.......', '..####..', '........']);
    expect(burnable(state, 2, 1)).toBe(true);
    ignite(state, 2, 1);
    runUntil(state, () => getTile(state.grid, 2, 1) === 'floor', FIRE_BURN + 5);
    expect(state.events.some((e) => e.message.includes('burned down'))).toBe(true);
    // Over that long it should have caught on at least one neighbour.
    expect(state.fires.length > 0 || getTile(state.grid, 3, 1) === 'floor').toBe(true);
  });

  it('hurts survivors and zombies standing in it', () => {
    const state = setup(['s..']);
    const s = state.survivors[0]!;
    const z = addZombie(state, 2, 0, 200, 'static');
    ignite(state, 0, 0);
    ignite(state, 2, 0);
    const zh = z.health;
    tick(state);
    expect(s.health).toBeLessThan(HEALTH_MAX);
    expect(z.health).toBeLessThan(zh);
  });

  it('firefighters put a fire out', () => {
    const state = setup(['s......', '...#...'], { firefight: 3 });
    ignite(state, 3, 1);
    runUntil(state, () => state.fires.length === 0, 400);
    expect(getTile(state.grid, 3, 1)).toBe('wall');
    expect(state.survivors[0]!.note).toMatch(/^put out a fire/);
  });

  it('the Burn order sets explored ground alight, not water', () => {
    const state = setup(['s..%']);
    expect(applyCommand(state, { type: 'ignite', x: 2, y: 0 }).ok).toBe(true);
    expect(state.fires).toHaveLength(1);
    expect(applyCommand(state, { type: 'ignite', x: 3, y: 0 })).toEqual({ ok: true, changed: false });
  });
});

describe('corpses', () => {
  it('only in a harsh game: the dead stay where they fall', () => {
    const mild = setup(['ss.']);
    killSurvivor(mild, mild.survivors[0]!, 'testing');
    expect(mild.items).toEqual([]);
    const harsh = setup(['ss.']);
    harsh.harsh = true;
    killSurvivor(harsh, harsh.survivors[0]!, 'testing');
    const z = addZombie(harsh, 2, 0, 10, 'static');
    damageZombie(harsh, z, 50, harsh.survivors[0]!);
    expect(harsh.items.map((i) => [i.kind, i.tag])).toEqual([
      ['corpse', 'Ash'],
      ['corpse', null],
    ]);
  });

  it('a rotting corpse sickens those nearby; medicine cures it', () => {
    const state = setup(['s.&.', 'W...']);
    state.harsh = true;
    const s = state.survivors[0]!;
    state.items[0]!.wear = -CORPSE_ROT;
    runUntil(state, () => s.sick !== null, 200 * CORPSE_CHECK_EVERY);
    expect(s.thoughts.some((t) => t.kind === 'stench')).toBe(true);
    expect(s.sick).toBeLessThanOrEqual(SICK_TICKS);
    state.items.push({ id: 'med', kind: 'medicine', pos: { x: 3, y: 1 }, carriedBy: null, reservedBy: null, wear: 0, tag: null });
    runUntil(state, () => s.sick === null, 400);
  });

  it('haulers carry the dead to a graveyard, where they stop rotting', () => {
    const map = ['s.&.....', '###D####', '#......#', '#......#', '########'];
    const state = setup(map, { haul: 3 }, [{ x: 1, y: 2, purpose: 'graveyard' }]);
    state.harsh = true;
    runUntil(state, () => state.items[0]!.pos !== null && state.items[0]!.pos.y >= 2, 1500);
  });
});
