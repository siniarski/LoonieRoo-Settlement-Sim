import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { replay } from '../src/sim/replay';
import { random } from '../src/sim/rng';
import { cloneState, createState } from '../src/sim/state';
import { step } from '../src/sim/tick';

describe('replay', () => {
  it('reproduces a live session exactly from initial state + command log', () => {
    const initial = createState({ seed: 1234, width: 8, height: 6 });
    const live = cloneState(initial);

    // A pseudo-random play session driven by the sim's own RNG.
    for (let i = 0; i < 40; i++) {
      const x = Math.floor(random(live) * 8);
      const y = Math.floor(random(live) * 6);
      const type = random(live) < 0.7 ? 'placeWall' : 'placeDoor';
      applyCommand(live, { type, x, y });
      step(live, 1 + Math.floor(random(live) * 3));
    }

    const replayed = replay(initial, live.commandLog, live.tick);

    // RNG calls above were made by the "player", not the sim, so rng differs;
    // everything the sim owns must match.
    expect(dumpText(replayed)).toBe(dumpText(live));
    expect(replayed.grid).toEqual(live.grid);
    expect(replayed.tick).toBe(live.tick);
    expect(replayed.walkabilityVersion).toBe(live.walkabilityVersion);
    expect(replayed.commandLog).toEqual(live.commandLog);
  });

  it('gives identical full state for two replays of the same log', () => {
    const initial = createState({ seed: 7, width: 4, height: 4 });
    const log = [
      { tick: 0, command: { type: 'placeWall' as const, x: 1, y: 1 } },
      { tick: 2, command: { type: 'placeDoor' as const, x: 2, y: 1 } },
      { tick: 2, command: { type: 'clearTile' as const, x: 1, y: 1 } },
    ];
    expect(JSON.stringify(replay(initial, log, 20))).toBe(JSON.stringify(replay(initial, log, 20)));
  });

  it('does not mutate the initial state', () => {
    const initial = createState({ seed: 7, width: 3, height: 3 });
    const before = JSON.stringify(initial);
    replay(initial, [{ tick: 1, command: { type: 'placeWall', x: 0, y: 0 } }], 5);
    expect(JSON.stringify(initial)).toBe(before);
  });

  it('applies commands logged at the final tick', () => {
    const initial = createState({ seed: 0, width: 2, height: 1 });
    const state = replay(initial, [{ tick: 3, command: { type: 'placeWall', x: 0, y: 0 } }], 3);
    expect(dumpText(state)).toBe('#.');
  });

  it('rejects unsorted logs and rejected commands', () => {
    const initial = createState({ seed: 0, width: 2, height: 1 });
    expect(() =>
      replay(
        initial,
        [
          { tick: 2, command: { type: 'placeWall', x: 0, y: 0 } },
          { tick: 1, command: { type: 'placeWall', x: 1, y: 0 } },
        ],
        5,
      ),
    ).toThrow(/unsorted/);
    expect(() => replay(initial, [{ tick: 0, command: { type: 'placeWall', x: 9, y: 0 } }], 5)).toThrow(/rejected/);
  });
});
