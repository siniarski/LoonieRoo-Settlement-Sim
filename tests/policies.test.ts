import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { SATIETY_MAX } from '../src/sim/food';
import { NO_JOBS } from '../src/sim/jobTypes';
import { chooseJob, reconcileJobs } from '../src/sim/jobs';
import { mood } from '../src/sim/mood';
import { killSurvivor, wantsSleep } from '../src/sim/needs';
import { LEADER_MIN, chooseLeader } from '../src/sim/policies';
import { loadScenario } from '../src/sim/scenario';
import { hasTrait } from '../src/sim/skills';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
import { AMPUTATE_WINDOW, BLEED_EVERY, amputate, updateWounds } from '../src/sim/wounds';

describe('policies', () => {
  it('rationing makes food last longer, at a cost in mood', () => {
    const state = loadScenario({ seed: 1, map: ['s.'] });
    const s = state.survivors[0]!;
    s.priorities = { ...NO_JOBS };
    const before = mood(state, s);
    expect(applyCommand(state, { type: 'setPolicy', policy: 'rationing', on: true }).ok).toBe(true);
    expect(mood(state, s)).toBeLessThan(before);
    s.satiety = SATIETY_MAX;
    for (let i = 0; i < 400; i++) tick(state);
    expect(SATIETY_MAX - s.satiety).toBe(300);
  });

  it('curfew: no outdoor work at night but guarding', () => {
    const state = loadScenario({ seed: 1, map: ['s.c'] });
    const s = state.survivors[0]!;
    s.priorities = { ...NO_JOBS, scavenge: 3 };
    applyCommand(state, { type: 'designateScavenge', x: 2, y: 0 });
    reconcileJobs(state);
    state.tick = 17 * TICKS_PER_HOUR;
    state.zombiesTonight = 0;
    expect(chooseJob(state, s)).not.toBeNull();
    applyCommand(state, { type: 'setPolicy', policy: 'curfew', on: true });
    expect(chooseJob(state, s)).toBeNull();
  });

  it('quarantine sends the infected to bed', () => {
    const state = loadScenario({ seed: 1, map: ['s.'] });
    const s = state.survivors[0]!;
    s.infection = 10;
    expect(wantsSleep(state, s)).toBe(false);
    applyCommand(state, { type: 'setPolicy', policy: 'quarantine', on: true });
    expect(wantsSleep(state, s)).toBe(true);
  });
});

describe('the leader', () => {
  it('is whoever the others think best of, and their death hits everyone', () => {
    const state = loadScenario({ seed: 1, map: ['sss'] });
    const [a, b, c] = state.survivors;
    expect(chooseLeader(state)).toBeNull();
    b!.opinions[a!.id] = LEADER_MIN + 10;
    c!.opinions[a!.id] = LEADER_MIN + 10;
    expect(chooseLeader(state)).toBe(a);
    state.tick = DAY_LENGTH;
    tick(state);
    expect(state.leaderId).toBe(a!.id);
    killSurvivor(state, a!, 'testing');
    expect(b!.thoughts.some((t) => t.kind === 'lostLeader')).toBe(true);
    expect(state.leaderId).toBeNull();
  });
});

describe('wounds and amputation', () => {
  it('a bleeding wound costs health until bandaged', () => {
    const state = loadScenario({ seed: 1, map: ['s'] });
    const s = state.survivors[0]!;
    s.bleeding = true;
    s.bleedUntil = 1e9;
    const h = s.health;
    for (let t = 0; t < BLEED_EVERY * 4; t++) {
      state.tick = t;
      updateWounds(state, s);
    }
    expect(s.health).toBe(h - 4);
  });

  it('an amputation in time cures the infection, for good and at a price', () => {
    const state = loadScenario({ seed: 1, map: ['s'] });
    const s = state.survivors[0]!;
    s.infection = 10;
    s.bitten = 'arm';
    expect(amputate(state, s)).toBeNull();
    expect(s.infection).toBeNull();
    expect(hasTrait(s, 'lostArm')).toBe(true);
    s.infection = AMPUTATE_WINDOW + 1;
    s.bitten = 'leg';
    expect(amputate(state, s)).toMatch(/too late/);
  });
});
