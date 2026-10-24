import { describe, expect, it } from 'vitest';
import { dumpText } from '../src/debug/text';
import twoRooms from '../src/scenarios/two-rooms.json';
import { getTile } from '../src/sim/grid';
import { loadScenario, runScenario, type Scenario } from '../src/sim/scenario';

describe('scenarios', () => {
  it('round-trips a map through load and dumpText', () => {
    const state = loadScenario(twoRooms as Scenario);
    expect(dumpText(state)).toBe(twoRooms.map.join('\n'));
  });

  it('reads tiles from the map legend', () => {
    const state = loadScenario({ seed: 0, map: ['.#', 'D.'] });
    expect(getTile(state.grid, 0, 0)).toBe('floor');
    expect(getTile(state.grid, 1, 0)).toBe('wall');
    expect(getTile(state.grid, 0, 1)).toBe('door');
  });

  it('starts at tick 0 with an empty log and version 0', () => {
    const state = loadScenario(twoRooms as Scenario);
    expect(state.tick).toBe(0);
    expect(state.commandLog).toEqual([]);
    expect(state.walkabilityVersion).toBe(0);
  });

  it('rejects ragged rows and unknown characters with a location', () => {
    expect(() => loadScenario({ seed: 0, map: ['...', '..'] })).toThrow(/row 1/);
    expect(() => loadScenario({ seed: 0, map: ['.x.'] })).toThrow(/'x' at \(1, 0\)/);
    expect(() => loadScenario({ seed: 0, map: [] })).toThrow(/empty/);
  });

  it('runs a scenario command log', () => {
    const scenario: Scenario = {
      seed: 0,
      map: ['...', '...'],
      commands: [
        { tick: 0, command: { type: 'placeWall', x: 0, y: 0 } },
        { tick: 5, command: { type: 'placeDoor', x: 2, y: 1 } },
      ],
    };
    const state = runScenario(scenario, 10);
    expect(state.tick).toBe(10);
    expect(dumpText(state)).toBe(['#..', '..D'].join('\n'));
  });
});
