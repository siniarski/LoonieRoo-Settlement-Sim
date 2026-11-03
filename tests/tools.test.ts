import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import type { Priority } from '../src/sim/jobTypes';
import { createState } from '../src/sim/state';
import { handleBuildKey } from '../src/ui/buildMode';
import { cyclePriority } from '../src/ui/hud';
import { CATEGORIES, TOOLS, getTool, stepCategory, tilesInBox, tilesOnLine, toolForKey, toolsIn } from '../src/ui/tools';
import type { ViewState } from '../src/ui/view';

const designate = { instant: false };
const instant = { instant: true };

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

  it('bed designates (or places instantly), housing/storage designate the room under the click', () => {
    expect(getTool('bed').command(1, 2, designate)).toEqual({ type: 'designateBuild', x: 1, y: 2, structure: 'bed' });
    expect(getTool('bed').command(1, 2, instant)).toEqual({ type: 'placeBed', x: 1, y: 2 });
    expect(getTool('housing').command(1, 2, designate)).toEqual({ type: 'designateRoom', x: 1, y: 2, purpose: 'housing' });
    expect(getTool('storage').paints).toBe(false);
  });

  it('has unique ids, and every tool sits in a category', () => {
    expect(new Set(TOOLS.map((t) => t.id)).size).toBe(TOOLS.length);
    const categorised = CATEGORIES.flatMap((c) => toolsIn(c.id));
    expect(categorised).toHaveLength(TOOLS.length);
    for (const c of CATEGORIES) expect(toolsIn(c.id).length, c.id).toBeLessThanOrEqual(10); // number keys 1-9, then 0
    expect(toolForKey('rooms', '0')?.id).toBe(toolsIn('rooms')[9]?.id);
  });

  it('Furnish hands clicks to the planner instead of issuing a command itself', () => {
    const furnish = getTool('furnish');
    expect(furnish.furnish).toBe(true);
    expect(furnish.category).toBe('orders') // (Rooms ran out of number keys);
    expect(furnish.command(3, 3, designate)).toBeNull();
  });

  it('number keys pick tools within the open category', () => {
    expect(toolForKey('structure', '1')?.id).toBe('wall');
    expect(toolForKey('structure', '2')?.id).toBe('door');
    expect(toolForKey('structure', '3')?.id).toBe('barricade');
    expect(toolForKey('structure', '4')?.id).toBe('trap');
    expect(toolForKey('structure', '5')?.id).toBe('tower');
    expect(toolForKey('structure', '6')).toBeUndefined();
    expect(toolForKey('orders', '1')?.id).toBe('furnish');
    expect(toolForKey('orders', '2')?.id).toBe('scavenge');
    expect(toolForKey('orders', '4')?.id).toBe('cancel');
    expect(toolForKey('rooms', 'x')).toBeUndefined();
  });

  it('area tools cover every tile in the dragged box, whichever way it was dragged', () => {
    expect(getTool('scavenge').area).toBe(true);
    const box = tilesInBox({ x: 3, y: 2 }, { x: 1, y: 3 });
    expect(box).toEqual([
      { x: 1, y: 2 },
      { x: 2, y: 2 },
      { x: 3, y: 2 },
      { x: 1, y: 3 },
      { x: 2, y: 3 },
      { x: 3, y: 3 },
    ]);
    expect(tilesInBox({ x: 5, y: 5 }, { x: 5, y: 5 })).toEqual([{ x: 5, y: 5 }]);
  });

  it('categories wrap in both directions', () => {
    expect(stepCategory('structure', 1)).toBe('furniture');
    expect(stepCategory('orders', 1)).toBe('structure');
    expect(stepCategory('structure', -1)).toBe('orders');
  });
});

describe('build mode keys', () => {
  const fresh = (): Pick<ViewState, 'buildOpen' | 'buildCategory' | 'tool' | 'selectedId'> => ({
    buildOpen: false,
    buildCategory: 'structure',
    tool: null,
    selectedId: 's1',
  });

  it('B opens and closes build mode; closing drops the tool', () => {
    const view = fresh();
    expect(handleBuildKey(view, '1', false)).toBe(false); // number keys do nothing outside build mode
    handleBuildKey(view, 'b', false);
    expect(view.buildOpen).toBe(true);
    handleBuildKey(view, '2', false);
    expect(view.tool).toBe('door');
    handleBuildKey(view, 'b', false);
    expect(view).toMatchObject({ buildOpen: false, tool: null });
  });

  it('Tab / Shift+Tab / Q / E switch category and put the tool down', () => {
    const view = fresh();
    handleBuildKey(view, 'b', false);
    handleBuildKey(view, '1', false);
    handleBuildKey(view, 'Tab', false);
    expect(view).toMatchObject({ buildCategory: 'furniture', tool: null });
    handleBuildKey(view, 'Tab', true);
    expect(view.buildCategory).toBe('structure');
    handleBuildKey(view, 'q', false);
    expect(view.buildCategory).toBe('orders');
    handleBuildKey(view, 'e', false);
    expect(view.buildCategory).toBe('structure');
  });

  it('pressing the armed tool again puts it down', () => {
    const view = fresh();
    handleBuildKey(view, 'b', false);
    handleBuildKey(view, '1', false);
    handleBuildKey(view, '1', false);
    expect(view.tool).toBeNull();
  });

  it('Escape steps back: tool, then build mode, then selection', () => {
    const view = fresh();
    handleBuildKey(view, 'b', false);
    handleBuildKey(view, '1', false);
    handleBuildKey(view, 'Escape', false);
    expect(view).toMatchObject({ buildOpen: true, tool: null, selectedId: 's1' });
    handleBuildKey(view, 'Escape', false);
    expect(view).toMatchObject({ buildOpen: false, selectedId: 's1' });
    handleBuildKey(view, 'Escape', false);
    expect(view.selectedId).toBeNull();
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
