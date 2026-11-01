// Relationships: what survivors think of each other, -100..100.
//
// Time spent close together (within NEAR tiles, both awake) warms them to
// each other, a little every BOND_EVERY ticks. A breakdown sours things: the
// survivor lashes out at whoever is nearest, and if that one already
// disliked them enough, it comes to blows. A friend's death hurts far more
// than a stranger's (mood.ts).

import { logEvent } from './events';
import { everyone } from './expeditions';
import { manhattan } from './space';
import type { SimState } from './state';
import type { Survivor } from './survivors';

export const BOND_EVERY = 200;
export const BOND = 2;
export const NEAR = 3;
export const FRIEND = 30;
export const RIVAL = -30;
/** A survivor this disliked by the one they lash out at gets hit back. */
export const FIGHT_BELOW = -40;
export const LASH_OUT = 25;
export const FIGHT_DAMAGE = 60;

export function opinion(of: Survivor, about: Survivor): number {
  return of.opinions[about.id] ?? 0;
}

function adjust(of: Survivor, about: Survivor, by: number): void {
  of.opinions[about.id] = Math.max(-100, Math.min(100, opinion(of, about) + by));
}

/** Every BOND_EVERY ticks: survivors near each other, both awake, warm to each other. */
export function updateRelationships(state: SimState): void {
  if (state.tick % BOND_EVERY !== 0) return;
  const awake = state.survivors.filter((s) => !s.sleep?.asleep);
  for (let i = 0; i < awake.length; i++) {
    for (let j = i + 1; j < awake.length; j++) {
      const a = awake[i]!;
      const b = awake[j]!;
      if (manhattan(a.pos, b.pos) > NEAR) continue;
      adjust(a, b, BOND);
      adjust(b, a, BOND);
    }
  }
}

/** A breakdown begins: lash out at the nearest awake survivor; a rival hits back. */
export function lashOut(state: SimState, s: Survivor): void {
  const target = state.survivors
    .filter((o) => o !== s && !o.sleep?.asleep && manhattan(o.pos, s.pos) <= 5)
    .sort((a, b) => manhattan(a.pos, s.pos) - manhattan(b.pos, s.pos))[0];
  if (!target) return;
  const fight = opinion(target, s) <= FIGHT_BELOW;
  adjust(s, target, -LASH_OUT);
  adjust(target, s, -LASH_OUT);
  if (!fight) {
    logEvent(state, `${s.name} lashed out at ${target.name}`);
    return;
  }
  for (const who of [s, target]) {
    who.health = Math.max(1, who.health - FIGHT_DAMAGE);
    who.lastHurtBy = 'a fight';
    who.hurtTick = state.tick;
  }
  logEvent(state, `${s.name} and ${target.name} came to blows`);
}

/** Someone died: forget them. Returns the ids of those who counted them a friend. */
export function forget(state: SimState, dead: Survivor): Set<string> {
  const friends = new Set<string>();
  for (const s of everyone(state)) {
    if (opinion(s, dead) >= FRIEND) friends.add(s.id);
    delete s.opinions[dead.id];
  }
  return friends;
}

/** "friends: Bea; rivals: Cal" for panels. */
export function describeRelations(state: SimState, s: Survivor): string {
  const names = (pick: (v: number) => boolean) =>
    state.survivors.filter((o) => o !== s && pick(opinion(s, o))).map((o) => o.name);
  const friends = names((v) => v >= FRIEND);
  const rivals = names((v) => v <= RIVAL);
  return [friends.length ? `friends: ${friends.join(', ')}` : '', rivals.length ? `rivals: ${rivals.join(', ')}` : ''].filter(Boolean).join('; ');
}
