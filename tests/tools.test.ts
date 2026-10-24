import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { createState } from '../src/sim/state';
import { TOOLS, getTool, tilesOnLine } from '../src/ui/tools';

describe('tools', () => {
  it('each tool maps to a command the sim accepts', () => {
    const state = createState({ seed: 0, width: 3, height: 1 });
    TOOLS.forEach((tool, x) => {
      expect(applyCommand(state, tool.command(x, 0)).ok).toBe(true);
    });
    expect(dumpText(state)).toBe('#D.');
  });

  it('has unique ids and keys', () => {
    expect(new Set(TOOLS.map((t) => t.id)).size).toBe(TOOLS.length);
    expect(new Set(TOOLS.map((t) => t.key)).size).toBe(TOOLS.length);
    expect(getTool('door').label).toBe('Door');
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
