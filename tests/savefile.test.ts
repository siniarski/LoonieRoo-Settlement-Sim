import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { loadFile, toBugReport, toSaveFile } from '../src/sim/savefile';
import { loadScenario } from '../src/sim/scenario';
import { cloneState, SCHEMA_VERSION } from '../src/sim/state';
import { step } from '../src/sim/tick';

const MAP = ['..........', '.####D###.', '.#..s...#.', '.########.', 'w.f...c...'];

function played() {
  const initial = loadScenario({ seed: 9, map: MAP });
  const state = cloneState(initial);
  step(state, 50);
  applyCommand(state, { type: 'designateBuild', x: 0, y: 0, structure: 'wall' });
  step(state, 200);
  applyCommand(state, { type: 'designateScavenge', x: 6, y: 4 });
  step(state, 300);
  return { initial, state };
}

describe('save files', () => {
  it('round-trip the whole state exactly', () => {
    const { state } = played();
    const loaded = loadFile(toSaveFile(state));
    expect(loaded.ok && loaded.kind).toBe('save');
    if (!loaded.ok) return;
    expect(JSON.stringify(loaded.state)).toBe(JSON.stringify(state));
    // ...and play on identically from there.
    step(loaded.state, 300);
    step(state, 300);
    expect(JSON.stringify(loaded.state)).toBe(JSON.stringify(state));
  });

  it('refuse files from another schema version, junk, and other JSON', () => {
    const { state } = played();
    const old = JSON.parse(toSaveFile(state)) as { schemaVersion: number };
    old.schemaVersion = SCHEMA_VERSION - 1;
    const result = loadFile(JSON.stringify(old));
    expect(result.ok).toBe(false);
    expect(!result.ok && result.error).toMatch(/different version/);
    expect(loadFile('not json')).toEqual({ ok: false, error: 'not a JSON file' });
    expect(loadFile('{"hello": 1}')).toEqual({ ok: false, error: 'not a LoonieRoo file' });
  });
});

describe('bug reports', () => {
  it('replay from the starting state to exactly the reported moment', () => {
    const { initial, state } = played();
    const loaded = loadFile(toBugReport(initial, state, 'Ash stood still'));
    expect(loaded.ok && loaded.kind).toBe('bug');
    if (!loaded.ok) return;
    expect(JSON.stringify(loaded.state)).toBe(JSON.stringify(state));
  });

  it('carry only the commands since the starting state (e.g. a save loaded mid-game)', () => {
    const { state: midGame } = played();
    const start = cloneState(midGame);
    applyCommand(midGame, { type: 'designateBuild', x: 9, y: 0, structure: 'door' });
    step(midGame, 100);
    const report = JSON.parse(toBugReport(start, midGame)) as { commands: unknown[] };
    expect(report.commands).toHaveLength(1);
    const loaded = loadFile(JSON.stringify(report));
    expect(loaded.ok && JSON.stringify(loaded.state)).toBe(JSON.stringify(midGame));
  });

  it('a report whose log does not replay is refused, not half-loaded', () => {
    const { initial, state } = played();
    const report = JSON.parse(toBugReport(initial, state)) as { commands: { tick: number }[] };
    report.commands.reverse(); // out of order
    const loaded = loadFile(JSON.stringify(report));
    expect(loaded.ok).toBe(false);
  });
});
