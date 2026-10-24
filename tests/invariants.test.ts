// Stuck-state invariant (CLAUDE.md): throw random orders and random wall
// edits at survivors and check that nobody ever stands in a wall, and nobody
// holds a goal without moving towards it. Then replay the session and check
// the whole state matches byte for byte.

import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand, type Command } from '../src/sim/commands';
import { isWalkable } from '../src/sim/grid';
import { replay } from '../src/sim/replay';
import { randomInt, seedRng } from '../src/sim/rng';
import { loadScenario } from '../src/sim/scenario';
import { cloneState } from '../src/sim/state';
import { tick } from '../src/sim/tick';

const MAP = [
  '....................',
  '.######.....######..',
  '.#....#.....#....#..',
  '.#.s..D.....D..s.#..',
  '.#....#.....#....#..',
  '.######.....###D##..',
  '..........s.........',
  '....................',
];

/** A survivor may hold a goal without a move only on the tick it arrives or gives up. */
const MAX_TICKS_WITH_GOAL_BUT_STILL = 1;

describe('stuck-state invariant', () => {
  for (const seed of [1, 2, 3, 4, 5]) {
    it(`holds under random orders and wall edits (seed ${seed})`, () => {
      const initial = loadScenario({ seed, map: MAP });
      const state = cloneState(initial);
      const driver = { rng: seedRng(seed * 7919) }; // Player input, separate from sim RNG.
      const stillWithGoal = new Map<string, number>();
      const notesSeen = new Set<string>();
      const { width, height } = state.grid;

      for (let t = 0; t < 3000; t++) {
        const roll = randomInt(driver, 0, 100);
        const x = randomInt(driver, 0, width);
        const y = randomInt(driver, 0, height);
        let cmd: Command | null = null;
        if (roll < 6) {
          const s = state.survivors[randomInt(driver, 0, state.survivors.length)]!;
          cmd = { type: 'moveSurvivor', survivorId: s.id, x, y };
        } else if (roll < 10) {
          cmd = { type: 'placeWall', x, y };
        } else if (roll < 12) {
          cmd = { type: 'clearTile', x, y };
        } else if (roll < 13) {
          cmd = { type: 'placeDoor', x, y };
        }
        if (cmd) applyCommand(state, cmd);

        tick(state);

        for (const s of state.survivors) {
          if (s.note) notesSeen.add(s.note.split(' ')[0]!);
          expect(isWalkable(state.grid, s.pos.x, s.pos.y), `${s.name} in a wall at tick ${state.tick}\n${dumpText(state)}`).toBe(true);
          if (s.move) {
            expect(isWalkable(state.grid, s.move.to.x, s.move.to.y), `${s.name} stepping into a wall\n${dumpText(state)}`).toBe(true);
          }
          const still = s.goal && !s.move ? (stillWithGoal.get(s.id) ?? 0) + 1 : 0;
          stillWithGoal.set(s.id, still);
          expect(still, `${s.name} stuck with goal ${JSON.stringify(s.goal)}: ${s.note}\n${dumpText(state)}`).toBeLessThanOrEqual(
            MAX_TICKS_WITH_GOAL_BUT_STILL,
          );
        }
      }

      // Guard against a vacuous run: every outcome must actually have happened.
      expect([...notesSeen].sort()).toEqual(expect.arrayContaining(['arrived', 'no', 'walking', 'walls']));

      const replayed = replay(initial, state.commandLog, state.tick);
      expect(JSON.stringify(replayed)).toBe(JSON.stringify(state));
    });
  }
});
