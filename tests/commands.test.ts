import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { createState } from '../src/sim/state';
import { step } from '../src/sim/tick';

function fresh() {
  return createState({ seed: 1, width: 5, height: 3 });
}

describe('commands', () => {
  it('places walls and doors and clears tiles', () => {
    const state = fresh();
    applyCommand(state, { type: 'placeWall', x: 0, y: 1 });
    applyCommand(state, { type: 'placeWall', x: 1, y: 1 });
    applyCommand(state, { type: 'placeDoor', x: 2, y: 1 });
    applyCommand(state, { type: 'placeWall', x: 3, y: 1 });
    applyCommand(state, { type: 'clearTile', x: 0, y: 1 });
    expect(dumpText(state)).toBe(['.....', '.#D#.', '.....'].join('\n'));
  });

  it('bumps walkabilityVersion only when a tile actually changes', () => {
    const state = fresh();
    applyCommand(state, { type: 'placeWall', x: 1, y: 1 });
    expect(state.walkabilityVersion).toBe(1);

    const again = applyCommand(state, { type: 'placeWall', x: 1, y: 1 });
    expect(again).toEqual({ ok: true, changed: false });
    expect(state.walkabilityVersion).toBe(1);

    applyCommand(state, { type: 'placeDoor', x: 1, y: 1 });
    expect(state.walkabilityVersion).toBe(2);
  });

  it('rejects out-of-bounds commands without touching state', () => {
    const state = fresh();
    const result = applyCommand(state, { type: 'placeWall', x: 5, y: 0 });
    expect(result.ok).toBe(false);
    expect(state.walkabilityVersion).toBe(0);
    expect(state.commandLog).toEqual([]);
  });

  it('logs accepted commands with the tick they were applied at', () => {
    const state = fresh();
    applyCommand(state, { type: 'placeWall', x: 0, y: 0 });
    step(state, 3);
    applyCommand(state, { type: 'placeDoor', x: 1, y: 0 });
    expect(state.commandLog).toEqual([
      { tick: 0, command: { type: 'placeWall', x: 0, y: 0 } },
      { tick: 3, command: { type: 'placeDoor', x: 1, y: 0 } },
    ]);
  });

  it('logs a copy, so mutating the caller object does not rewrite history', () => {
    const state = fresh();
    const cmd = { type: 'placeWall' as const, x: 0, y: 0 };
    applyCommand(state, cmd);
    cmd.x = 4;
    expect(state.commandLog[0]?.command).toEqual({ type: 'placeWall', x: 0, y: 0 });
  });
});
