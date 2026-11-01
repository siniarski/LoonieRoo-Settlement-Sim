import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { checkInvariants } from '../src/sim/invariants';
import { NO_JOBS } from '../src/sim/jobTypes';
import { MOOD_BASE, mood } from '../src/sim/mood';
import { loadScenario } from '../src/sim/scenario';
import { MAX_LEVEL, XP_PER_LEVEL, rollTraits, skillLevel } from '../src/sim/skills';
import type { SimState } from '../src/sim/state';
import { TICKS_PER_TILE } from '../src/sim/survivors';
import { tick } from '../src/sim/tick';
import { TICKS_PER_HOUR } from '../src/sim/time';
import { ZOMBIE_DAMAGE } from '../src/sim/zombies';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}`);
}

/** Ticks for one survivor to remove a wall, with a given build xp and traits. */
function removeWall(xp: number, traits: string[] = []) {
  const state = loadScenario({ seed: 1, map: ['s#'] });
  const s = state.survivors[0]!;
  s.priorities = { ...NO_JOBS, build: 3 };
  s.xp = { build: xp };
  s.traits = traits as never;
  applyCommand(state, { type: 'designateRemove', x: 1, y: 0 });
  return { ticks: runUntil(state, (st) => st.grid.tiles[1] === 'floor'), s };
}

describe('skills', () => {
  it('work is practice: xp builds up in the job done', () => {
    const { s } = removeWall(0);
    expect(s.xp.build).toBeGreaterThan(15);
    expect(skillLevel(s, 'build')).toBe(0);
  });

  it('each level makes the work quicker, up to half as fast again', () => {
    const novice = removeWall(0).ticks;
    const master = removeWall(MAX_LEVEL * XP_PER_LEVEL).ticks;
    expect(master).toBeLessThan(novice);
    expect(master).toBeGreaterThanOrEqual(Math.floor((novice * 2) / 3) - 2);
  });
});

describe('traits', () => {
  it('are rolled from the id: the same arrival always brings the same, most bring one', () => {
    expect(rollTraits('v42')).toEqual(rollTraits('v42'));
    const ids = Array.from({ length: 200 }, (_, i) => `v${i}`);
    const withOne = ids.filter((id) => rollTraits(id).length === 1).length;
    expect(withOne).toBeGreaterThan(120);
    expect(withOne).toBeLessThan(180);
  });

  it('lazy works slower; cheerful and gloomy move mood', () => {
    expect(removeWall(0, ['lazy']).ticks).toBeGreaterThan(removeWall(0).ticks);
    const state = loadScenario({ seed: 1, map: ['s.s'] });
    state.survivors[0]!.traits = ['cheerful'];
    state.survivors[1]!.traits = ['gloomy'];
    expect(state.survivors.map((s) => mood(state, s))).toEqual([MOOD_BASE + 10, MOOD_BASE - 10]);
  });

  it('quick walks a tile in fewer ticks; tough takes less from a zombie', () => {
    const state = loadScenario({ seed: 1, map: ['s.....', '......'] });
    const s = state.survivors[0]!;
    s.priorities = { ...NO_JOBS };
    s.traits = ['quick'];
    applyCommand(state, { type: 'moveSurvivor', survivorId: s.id, x: 5, y: 0 });
    tick(state);
    expect(s.move?.duration).toBe(TICKS_PER_TILE - 1);

    const fight = loadScenario({ seed: 1, map: ['zs'] });
    fight.tick = 17 * TICKS_PER_HOUR + 1;
    fight.survivors[0]!.priorities = { ...NO_JOBS };
    fight.survivors[0]!.traits = ['tough'];
    runUntil(fight, () => fight.survivors[0]!.health < 1000, 50);
    expect(1000 - fight.survivors[0]!.health).toBe(Math.round((ZOMBIE_DAMAGE * 3) / 4));
  });

  it("a wanderer's traits show before you decide, and come with them when taken in", () => {
    const state = loadScenario({ seed: 1, map: ['v.s'] });
    const w = state.wanderers[0]!;
    w.traits = ['quick'];
    applyCommand(state, { type: 'acceptWanderer', wandererId: w.id });
    expect(state.survivors.at(-1)!.traits).toEqual(['quick']);
  });
});
