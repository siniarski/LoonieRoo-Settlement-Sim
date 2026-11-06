import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { loadFile, runLengthDecode, runLengthEncode, toBugReport, toSaveFile } from '../src/sim/savefile';
import { createWorld } from '../src/sim/worldgen';
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
  it('round-trip the whole state exactly, but for the command log, which a save leaves behind', () => {
    const { state } = played();
    expect(state.commandLog.length).toBeGreaterThan(0);
    const loaded = loadFile(toSaveFile(state));
    expect(loaded.ok && loaded.kind).toBe('save');
    if (!loaded.ok) return;
    expect(loaded.state.commandLog).toEqual([]);
    expect(JSON.stringify({ ...loaded.state, commandLog: [] })).toBe(JSON.stringify({ ...state, commandLog: [] }));
    // ...and play on identically from there.
    step(loaded.state, 300);
    step(state, 300);
    expect(JSON.stringify({ ...loaded.state, commandLog: [] })).toBe(JSON.stringify({ ...state, commandLog: [] }));
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

describe('compact saves', () => {
  it('run-length encoding round-trips, and a world save is far smaller than the raw state', () => {
    expect(runLengthDecode(runLengthEncode(['a', 'a', 'b', 'a']))).toEqual(['a', 'a', 'b', 'a']);
    expect(runLengthEncode([0, 0, 0, 1])).toEqual([0, 3, 1, 1]);
    const world = createWorld(5, 256);
    const text = toSaveFile(world);
    expect(text.length).toBeLessThan(JSON.stringify(world).length / 3);
    const loaded = loadFile(text);
    expect(loaded.ok && JSON.stringify(loaded.state)).toBe(JSON.stringify(world));
  });

  it('still loads a save written without the encoding', () => {
    const { state } = played();
    const plain = JSON.stringify({ format: 'loonieroo-save', schemaVersion: SCHEMA_VERSION, state });
    const loaded = loadFile(plain);
    expect(loaded.ok && JSON.stringify(loaded.state)).toBe(JSON.stringify(state));
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

describe('save files: furniture as rows', () => {
  it('a generated world full of furniture round-trips exactly, and much smaller than as objects', async () => {
    const { createWorld, ensureGeneratedAround, CHUNK } = await import('../src/sim/worldgen');
    const state = createWorld(5, 128);
    for (let y = 0; y < 128; y += CHUNK) for (let x = 0; x < 128; x += CHUNK) ensureGeneratedAround(state, { x, y });
    state.furniture[0]!.scavenge = true; // a non-default in the middle of a row
    const text = toSaveFile(state);
    const loaded = loadFile(text);
    expect(loaded.ok).toBe(true);
    if (!loaded.ok) return;
    expect(JSON.stringify({ ...loaded.state, commandLog: [] })).toBe(JSON.stringify({ ...state, commandLog: [] }));
    expect(text.length).toBeLessThan(JSON.stringify(state.furniture).length);
  }, 20000);
});
