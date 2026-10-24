import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import type { Priority } from '../src/sim/jobTypes';
import { createState } from '../src/sim/state';
import { cyclePriority } from '../src/ui/hud';
import { TOOLS, getTool, tilesOnLine } from '../src/ui/tools';

const designate = { selectedId: null, instant: false };
const instant = { selectedId: null, instant: true };

describe('tools', () => {
  it('wall, door, and remove designate jobs by default', () => {
    const state = createState({ seed: 0, width: 3, height: 1 });
    applyCommand(state, getTool('wall').command(0, 0, designate)!);
    applyCommand(state, getTool('door').command(1, 0, designate)!);
    expect(dumpText(state)).toBe('+d.');
    applyCommand(state, getTool('cancel').command(0, 0, designate)!);
    expect(dumpText(state)).toBe('.d.');
  });

  it('Shift makes wall, door, and remove instant', () => {
    const state = createState({ seed: 0, width: 3, height: 1 });
    applyCommand(state, getTool('wall').command(0, 0, instant)!);
    applyCommand(state, getTool('door').command(1, 0, instant)!);
    expect(dumpText(state)).toBe('#D.');
    applyCommand(state, getTool('remove').command(0, 0, instant)!);
    expect(dumpText(state)).toBe('.D.');
    applyCommand(state, getTool('remove').command(1, 0, designate)!);
    expect(dumpText(state)).toBe('.x.');
  });

  it('move needs a selected survivor', () => {
    const move = getTool('move');
    expect(move.paints).toBe(false);
    expect(move.command(2, 3, designate)).toBeNull();
    expect(move.command(2, 3, { selectedId: 's1', instant: false })).toEqual({
      type: 'moveSurvivor',
      survivorId: 's1',
      x: 2,
      y: 3,
    });
  });

  it('has unique ids and keys', () => {
    expect(new Set(TOOLS.map((t) => t.id)).size).toBe(TOOLS.length);
    expect(new Set(TOOLS.map((t) => t.key)).size).toBe(TOOLS.length);
    expect(getTool('door').label).toBe('Door');
  });
});

describe('priority cycling', () => {
  it('goes 1 → 2 → 3 → 4 → off → 1, and back with right-click', () => {
    const forward: Priority[] = [];
    let p: Priority = 1;
    for (let i = 0; i < 5; i++) forward.push((p = cyclePriority(p, false)));
    expect(forward).toEqual([2, 3, 4, 0, 1]);
    expect(cyclePriority(1, true)).toBe(0);
    expect(cyclePriority(0, true)).toBe(4);
  });
});

describe('tilesOnLine', () => {
  it('returns a single tile for a zero-length line', () => {
    expect(tilesOnLine({ x: 2, y: 3 }, { x: 2, y: 3 })).toEqual([{ x: 2, y: 3 }]);
  });

  it('covers horizontal and vertical lines inclusively', () => {
    expect(tilesOnLine({ x: 0, y: 0 }, { x: 3, y: 0 })).toHaveLength(4);
    expect(tilesOnLine({ x: 1, y: 4 }, { x: 1, y: 1 })).toEqual([
      { x: 1, y: 4 },
      { x: 1, y: 3 },
      { x: 1, y: 2 },
      { x: 1, y: 1 },
    ]);
  });

  it('has no gaps on diagonals and steep lines', () => {
    for (const [a, b] of [
      [{ x: 0, y: 0 }, { x: 5, y: 2 }],
      [{ x: 4, y: 0 }, { x: 0, y: 7 }],
      [{ x: -3, y: 2 }, { x: 3, y: -2 }],
    ] as const) {
      const line = tilesOnLine(a, b);
      expect(line[0]).toEqual(a);
      expect(line.at(-1)).toEqual(b);
      for (let i = 1; i < line.length; i++) {
        const step = Math.max(Math.abs(line[i]!.x - line[i - 1]!.x), Math.abs(line[i]!.y - line[i - 1]!.y));
        expect(step).toBe(1);
      }
    }
  });
});
