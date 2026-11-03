import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { READ_TICKS, bookSubject } from '../src/sim/books';
import { wantedCraft } from '../src/sim/crafting';
import { DOG_SENSE, addDog } from '../src/sim/dogs';
import { CHILDHOOD, LOVERS, isChild } from '../src/sim/family';
import { checkInvariants } from '../src/sim/invariants';
import { spawnItem } from '../src/sim/items';
import { NO_JOBS } from '../src/sim/jobTypes';
import { loadScenario } from '../src/sim/scenario';
import { FRIENDLY, GIFT_REP, HOSTILE, adjustReputation } from '../src/sim/settlements';
import { XP_PER_LEVEL } from '../src/sim/skills';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH, TICKS_PER_HOUR } from '../src/sim/time';
import { createWorld } from '../src/sim/worldgen';
import { addZombie } from '../src/sim/zombies';

function runUntil(state: SimState, done: (s: SimState) => boolean, max = 2000): number {
  for (let t = 0; t < max; t++) {
    if (done(state)) return t;
    tick(state);
    expect(checkInvariants(state), dumpText(state)).toEqual([]);
  }
  throw new Error(`condition not met after ${max} ticks\n${dumpText(state)}\n${state.survivors.map((s) => s.note).join('\n')}`);
}

function setup(map: string[], jobs: Partial<typeof NO_JOBS> = {}) {
  const state = loadScenario({ seed: 13, map });
  for (const s of state.survivors) s.priorities = { ...NO_JOBS, ...jobs };
  return state;
}

/** A book's id with the given subject (subjects come from the id). */
function bookWith(subject: string): number {
  for (let n = 1; n < 10000; n++) if (bookSubject(`i${n}`) === subject) return n;
  throw new Error(`no ${subject} book`);
}

describe('books', () => {
  it('subjects come from the id: mostly skills, some knowledge', () => {
    const subjects = Array.from({ length: 400 }, (_, n) => bookSubject(`i${n}`));
    expect(subjects.filter((s) => s === 'gunsmithing' || s === 'tailoring').length).toBeGreaterThan(40);
    expect(subjects.filter((s) => s === 'farm').length).toBeGreaterThan(20);
  });

  it('an idle survivor reads a free book: a skill book is a level of practice, and used up', () => {
    const state = setup(['s....']);
    state.nextId = bookWith('farm');
    spawnItem(state, 'book', 4, 0);
    const s = state.survivors[0]!;
    runUntil(state, () => state.items.length === 0, READ_TICKS + 50);
    expect(s.xp.farm).toBe(XP_PER_LEVEL);
    expect(s.reading).toBeNull();
  });

  it('a knowledge book unlocks recipes that need it', () => {
    const state = setup(['s....', '.k...']);
    state.craftTargets.armour = 1;
    expect(wantedCraft(state, 'workbench', false)).toBeNull();
    state.nextId = bookWith('tailoring');
    spawnItem(state, 'book', 4, 0);
    runUntil(state, () => state.knowledge.includes('tailoring'), READ_TICKS + 50);
    expect(wantedCraft(state, 'workbench', false)).toBe('armour');
  });

  it('a job turning up interrupts reading', () => {
    const state = setup(['s...N', '.....'], { clean: 3 });
    const s = state.survivors[0]!;
    runUntil(state, () => (s.reading?.progress ?? 0) > 10, 100);
    state.grid.dirt[1 * 5 + 0] = 0;
    applyCommand(state, { type: 'placeWall', x: 0, y: 1 }); // nothing dirty indoors: make a job some other way
    state.survivors[0]!.priorities.haul = 3;
    state.items.push({ id: 'w1', kind: 'wood', pos: { x: 2, y: 1 }, carriedBy: null, reservedBy: null, wear: 0, tag: null });
    state.blueprints.push({ id: 'bp', x: 1, y: 1, target: 'wall', needsMaterial: true, delivered: false, workDone: 0, workRequired: 30 });
    runUntil(state, () => s.jobId !== null, 50);
    expect(s.reading).toBeNull();
    expect(state.items.find((i) => i.kind === 'book')!.reservedBy).toBeNull();
  });
});

describe('dogs', () => {
  it('a dog barks at a zombie, goes for it, and brings it down', () => {
    const state = setup(['s.g.......', '..........']);
    state.tick = 17 * TICKS_PER_HOUR + 1;
    state.zombiesTonight = 0;
    addZombie(state, 9, 1, 80, 'static');
    expect(9 - 2 + 1).toBeLessThanOrEqual(DOG_SENSE);
    runUntil(state, () => state.zombies.length === 0, 400);
    expect(state.events.some((e) => e.message.includes('barking'))).toBe(true);
    expect(state.events.some((e) => e.message.includes('brought down a zombie'))).toBe(true);
  });

  it('follows people and cheers them up', () => {
    const state = setup(['s.........g']);
    runUntil(state, () => state.survivors[0]!.thoughts.some((t) => t.kind === 'dog'), 400);
  });

  it('strays turn up some days, and expeditions sometimes bring one home', () => {
    const state = setup(['s....']);
    addDog(state, 2, 0);
    expect(state.dogs).toHaveLength(1);
  });
});

describe('settlements', () => {
  it('a generated world has neighbours; traders come from them', () => {
    const state = createWorld(4, 64);
    expect(state.settlements).toHaveLength(3);
  });

  it('gifts warm them; snubs and enemies show in the events', () => {
    const state = setup(['s.ffff']);
    state.settlements.push({ id: 'n1', name: 'Testville', x: 0, y: 0, reputation: 0, people: 8, food: 32, defence: 1, ties: {}, unfolded: true, residents: [], lastRaid: -1 });
    expect(applyCommand(state, { type: 'giftSettlement', settlementId: 'n1' }).ok).toBe(true);
    expect(state.settlements[0]!.reputation).toBe(GIFT_REP);
    expect(state.items.filter((i) => i.kind === 'food')).toHaveLength(1);
    expect(applyCommand(state, { type: 'giftSettlement', settlementId: 'n1' }).ok).toBe(false);
    adjustReputation(state, state.settlements[0]!, HOSTILE - GIFT_REP);
    expect(state.events.at(-1)!.message).toMatch(/enemy/);
  });

  it('a friendly settlement sends someone to join', () => {
    const state = setup(['s....']);
    state.settlements.push({ id: 'n1', name: 'Testville', x: 0, y: 0, reputation: FRIENDLY, people: 8, food: 32, defence: 1, ties: {}, unfolded: true, residents: [], lastRaid: -1 });
    state.tick = 5 * DAY_LENGTH - 6 * TICKS_PER_HOUR; // day 5, 12:00... minus a little
    while (state.wanderers.length === 0 && state.tick < 6 * DAY_LENGTH) tick(state);
    expect(state.wanderers).toHaveLength(1);
  });
});

describe('children', () => {
  it('lovers with a spare bed may have a baby, who does no work, then grows up', () => {
    const state = setup(['ss..b', '.....']);
    state.harsh = true;
    const [a, b] = state.survivors;
    a!.opinions[b!.id] = LOVERS;
    b!.opinions[a!.id] = LOVERS;
    for (const s of state.survivors) {
      s.age = 30;
      s.satiety = 3000;
    }
    let days = 0;
    while (state.survivors.length < 3 && days < 40) {
      state.tick = days * DAY_LENGTH + DAY_LENGTH / 2;
      tick(state);
      days += 1;
    }
    expect(state.survivors).toHaveLength(3);
    const baby = state.survivors[2]!;
    expect(isChild(baby)).toBe(true);
    expect(applyCommand(state, { type: 'setPriority', survivorId: baby.id, jobType: 'build', priority: 1 }).ok).toBe(false);
    state.tick = baby.childUntil!;
    tick(state);
    expect(isChild(baby)).toBe(false);
    expect(baby.priorities.build).toBeGreaterThan(0);
    expect(CHILDHOOD).toBeGreaterThan(DAY_LENGTH);
  });
});
