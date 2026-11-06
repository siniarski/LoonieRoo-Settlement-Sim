import { describe, expect, it } from 'vitest';
import { caravansOf, sendCaravan } from '../src/sim/caravans';
import { ERRAND_REP, LAPSED_REP, errandsOf, maybeAsk } from '../src/sim/errands';
import { centre } from '../src/sim/motion';
import { loadScenario } from '../src/sim/scenario';
import { ALLY, FEUD, adjustReputation, newSettlement, relation, tieSettlements } from '../src/sim/settlements';
import { tick } from '../src/sim/tick';
import { DAY_LENGTH } from '../src/sim/time';
import { removeZombie } from '../src/sim/zombies';

/** An open field with you in the middle and two neighbours, 30 tiles apart. */
function world() {
  const row = '.'.repeat(80);
  const state = loadScenario({ seed: 1, map: Array.from({ length: 60 }, (_, y) => (y === 30 ? row.slice(0, 40) + 's' + row.slice(41) : row)) });
  const s = state.survivors[0]!;
  for (const k of Object.keys(s.priorities)) s.priorities[k as 'guard'] = 0;
  const a = newSettlement(state, 'Ashby', 10, 10, 8, 40, 2);
  const b = newSettlement(state, 'Brook', 40, 10, 8, 0, 2);
  return { state, s, a, b };
}
const days = (state: ReturnType<typeof loadScenario>, n: number) => {
  for (let i = 0; i < n * DAY_LENGTH; i++) tick(state);
};

describe('caravans', () => {
  it('travel the road a tile at a time and deliver the food; the two warm to each other', () => {
    const { state, a, b } = world();
    state.settings.zombies = 0; // no ambushes on this road
    const c = sendCaravan(state, a.id, b.id, { x: b.x, y: b.y }, 12)!;
    expect(caravansOf(state)).toEqual([c]);
    for (let i = 0; i < 20; i++) tick(state);
    expect(c.x).toBeGreaterThan(a.x);
    expect(c.x).toBeLessThan(b.x);
    for (let i = 0; i < 400 && caravansOf(state).length > 0; i++) tick(state);
    expect(caravansOf(state)).toEqual([]);
    expect(b.food).toBe(12);
    expect(a.ties[b.id]).toBeGreaterThan(0);
  });

  it('can be caught by the dead on the way, and lost, leaving what was left in the road', () => {
    const { state, a, s } = world();
    state.settings.zombies = 100000; // every hour, an ambush
    sendCaravan(state, a.id, 'you', s.pos, 4, ['medicine'])!.people = 1; // one person: the first ambush finishes it
    let lost = false;
    for (let i = 0; i < 2000 && caravansOf(state).length > 0; i++) {
      tick(state);
      lost ||= state.events.some((e) => e.message.includes('never made it'));
    }
    expect(caravansOf(state)).toEqual([]);
    expect(lost).toBe(true);
  });

  it('bring you the goods when they come to you', () => {
    const { state, a, s } = world();
    state.settings.zombies = 0;
    sendCaravan(state, a.id, 'you', s.pos, 2, ['medicine', 'ammo']);
    for (let i = 0; i < 1000 && caravansOf(state).length > 0; i++) tick(state);
    expect(state.items.filter((i) => i.kind === 'medicine' && i.pos)).toHaveLength(1);
    expect(state.items.filter((i) => i.kind === 'food' && i.pos).length).toBeGreaterThanOrEqual(2);
  });

  it('a starving settlement is sent food by caravan, not in an instant', () => {
    const { state, a, b } = world();
    state.settings.zombies = 0;
    state.settings.winter = true; // little grows: an empty store means starving by dawn
    a.food = 200; // well fed, with plenty to spare
    let sent = false;
    for (let d = 0; d < 8 && !sent; d++) {
      b.food = 0;
      days(state, 1);
      sent = caravansOf(state).some((c) => c.from === a.id && c.to === b.id) || state.events.some((e) => e.message.includes('caravan of food'));
    }
    expect(sent).toBe(true);
  });
});

describe('alliances and feuds', () => {
  it('ties make allies or enemies, and how you are seen spills over', () => {
    const { state, a, b } = world();
    const c = newSettlement(state, 'Cray', 60, 40, 5, 10, 1);
    tieSettlements(a, b, ALLY);
    tieSettlements(a, c, FEUD);
    expect(relation(a, b)).toBe('allies');
    expect(relation(c, a)).toBe('feud');
    const [rb, rc] = [b.reputation, c.reputation];
    adjustReputation(state, a, 20);
    expect(b.reputation).toBe(rb + 10); // an ally feels half of it
    expect(c.reputation).toBe(rc - 5); // their enemy a quarter, the other way
  });

  it('feuding neighbours raid each other now and then, fed or not', () => {
    const { state, a, b } = world();
    b.food = 40;
    tieSettlements(a, b, -80);
    let raided = false;
    for (let d = 0; d < 30 && !raided; d++) {
      days(state, 1);
      raided = state.events.some((e) => e.message.includes('their old enemy'));
    }
    expect(raided).toBe(true);
  }, 30000);
});

describe('errands', () => {
  /** Asks until a neighbour wants this kind of errand. */
  function ask(kind: 'nest' | 'missing') {
    for (let salt = 0; salt < 200; salt++) {
      const w = world();
      w.state.settings.zombies = 0;
      if (maybeAsk(w.state, w.a, salt) && errandsOf(w.state)[0]!.kind === kind) return w;
    }
    throw new Error(`nobody asked for ${kind}`);
  }

  it('clear a nest of the dead near them: they think much more of you, and send thanks by caravan', () => {
    const { state, a } = ask('nest');
    const e = errandsOf(state)[0]!;
    expect(e.zombieIds.length).toBeGreaterThan(0);
    const before = a.reputation;
    for (const id of e.zombieIds) removeZombie(state, state.zombies.find((z) => z.id === id)!);
    tick(state);
    expect(errandsOf(state)).toEqual([]);
    expect(a.reputation).toBe(before + ERRAND_REP);
    expect(caravansOf(state).some((c) => c.from === a.id && c.to === 'you')).toBe(true);
  });

  it('find their missing one by going there', () => {
    const { state, s, a } = ask('missing');
    const e = errandsOf(state)[0]!;
    const people = a.people;
    s.pos = { x: e.x, y: e.y };
    s.at = centre(s.pos);
    tick(state);
    expect(errandsOf(state)).toEqual([]);
    expect(a.people).toBe(people + 1);
  });

  it('left undone, it lapses and they think a little less of you; one at a time', () => {
    const { state, a } = ask('missing');
    expect(maybeAsk(state, a, 999)).toBeNull(); // already asked
    const before = a.reputation;
    state.tick = errandsOf(state)[0]!.until;
    tick(state);
    expect(errandsOf(state)).toEqual([]);
    expect(a.reputation).toBe(before + LAPSED_REP);
  });
});
