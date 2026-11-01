import { describe, expect, it } from 'vitest';
import { NO_JOBS } from '../src/sim/jobTypes';
import { killSurvivor } from '../src/sim/needs';
import { BOND, BOND_EVERY, FIGHT_DAMAGE, FRIEND, lashOut, opinion } from '../src/sim/relationships';
import { loadScenario } from '../src/sim/scenario';
import { tick } from '../src/sim/tick';

function setup(map: string[]) {
  const state = loadScenario({ seed: 2, map });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS };
  return state;
}

describe('relationships', () => {
  it('survivors who spend time near each other warm to each other; distant ones don\'t', () => {
    const state = setup(['s.s.........s']);
    const [a, b, far] = state.survivors;
    for (let i = 0; i < 3 * BOND_EVERY; i++) tick(state);
    expect(opinion(a!, b!)).toBe(3 * BOND);
    expect(opinion(b!, a!)).toBe(3 * BOND);
    expect(opinion(a!, far!)).toBe(0);
  });

  it("a friend's death hurts more than a stranger's", () => {
    const state = setup(['s.s.s']);
    const [dead, friend, stranger] = state.survivors;
    friend!.opinions[dead!.id] = FRIEND;
    killSurvivor(state, dead!, 'a test');
    expect(friend!.thoughts.map((t) => t.kind)).toEqual(['lostFriend']);
    expect(stranger!.thoughts.map((t) => t.kind)).toEqual(['sawDeath']);
    expect(friend!.opinions).toEqual({});
  });

  it('a breakdown lashes out at the nearest; a rival hits back', () => {
    const state = setup(['s.s.....']);
    const [a, b] = state.survivors;
    lashOut(state, a!);
    expect(opinion(b!, a!)).toBeLessThan(0);
    expect(a!.health).toBe(1000);
    b!.opinions[a!.id] = -60;
    lashOut(state, a!);
    expect([a!.health, b!.health]).toEqual([1000 - FIGHT_DAMAGE, 1000 - FIGHT_DAMAGE]);
    expect(state.events.at(-1)!.message).toBe(`${a!.name} and ${b!.name} came to blows`);
  });
});
