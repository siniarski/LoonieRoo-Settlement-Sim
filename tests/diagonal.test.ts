import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { loadScenario } from '../src/sim/scenario';
import type { SimState } from '../src/sim/state';
import { tick } from '../src/sim/tick';

/** Ticks for the survivor to walk to (x, y), and every tile they stood on. */
function walkTo(map: string[], x: number, y: number): { ticks: number; tiles: string[] } {
  const state: SimState = loadScenario({ seed: 1, map });
  const s = state.survivors[0]!;
  for (const k of Object.keys(s.priorities)) s.priorities[k as keyof typeof s.priorities] = 0;
  applyCommand(state, { type: 'moveSurvivor', survivorId: s.id, x, y });
  const tiles: string[] = [];
  let ticks = 0;
  while ((s.pos.x !== x || s.pos.y !== y || s.move) && ticks < 400) {
    tick(state);
    ticks++;
    tiles.push(`${s.pos.x},${s.pos.y}`);
  }
  return { ticks, tiles };
}

describe('colonists walk diagonally', () => {
  it('across open ground they cut corners: a diagonal is quicker than going round', () => {
    const open = walkTo(['s....', '.....', '.....', '.....', '.....'], 4, 4);
    const diag = 4 * Math.round((4 * 1414) / 1000);
    expect(open.ticks).toBeLessThan(8 * 4); // eight four-way steps would take 32
    expect(open.ticks).toBeLessThanOrEqual(diag + 2);
  });

  it("never cut a wall's corner", () => {
    // The wall at (1, 1) sits on the corner: they must go round it, not between.
    const { tiles } = walkTo(['s....', '.#...', '.....'], 2, 2);
    for (const t of tiles) expect(t).not.toBe('1,1');
    const steps = tiles.filter((t, i) => t !== tiles[i - 1]);
    for (let i = 1; i < steps.length; i++) {
      const [ax, ay] = steps[i - 1]!.split(',').map(Number) as [number, number];
      const [bx, by] = steps[i]!.split(',').map(Number) as [number, number];
      // A diagonal step past (1, 1) would go from (0, 1) to (1, 2) or (1, 0) to (2, 1).
      const past = (ax === 0 && ay === 1 && bx === 1 && by === 2) || (ax === 1 && ay === 0 && bx === 2 && by === 1);
      expect(past).toBe(false);
    }
  });
});
