import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { MAX_NOTES, NOTE_MAX, TOWN_NAMES, notesOf, townNames } from '../src/sim/mapnotes';
import { loadScenario } from '../src/sim/scenario';
import { TOWNS } from '../src/sim/story';
import { createWorld } from '../src/sim/worldgen';

describe('the map', () => {
  it('pins notes by command, tidies them, and takes them off', () => {
    const state = loadScenario({ seed: 1, map: ['.....', '.....'] });
    expect(applyCommand(state, { type: 'addMapNote', x: 3, y: 1, text: '  food\u0007 here  ' }).ok).toBe(true);
    expect(notesOf(state)).toEqual([expect.objectContaining({ x: 3, y: 1, text: 'food here' })]);
    expect(applyCommand(state, { type: 'addMapNote', x: 9, y: 1, text: 'off the map' }).ok).toBe(false);
    expect(applyCommand(state, { type: 'addMapNote', x: 1, y: 1, text: '   ' }).ok).toBe(false);
    applyCommand(state, { type: 'addMapNote', x: 0, y: 0, text: 'x'.repeat(NOTE_MAX + 10) });
    expect(notesOf(state)[1]!.text.length).toBe(NOTE_MAX);
    // It's saved with the game.
    expect(JSON.parse(JSON.stringify(state)).mapNotes).toHaveLength(2);
    expect(applyCommand(state, { type: 'removeMapNote', id: notesOf(state)[0]!.id }).ok).toBe(true);
    expect(notesOf(state).map((n) => n.text)).toEqual(['x'.repeat(NOTE_MAX)]);
    expect(applyCommand(state, { type: 'removeMapNote', id: 'nope' }).ok).toBe(false);
  });

  it('holds only so many', () => {
    const state = loadScenario({ seed: 1, map: ['.'] });
    for (let i = 0; i < MAX_NOTES; i++) applyCommand(state, { type: 'addMapNote', x: 0, y: 0, text: `n${i}` });
    expect(applyCommand(state, { type: 'addMapNote', x: 0, y: 0, text: 'one more' }).ok).toBe(false);
  });

  it("names a generated world's towns, each differently, from the towns in people's stories first", () => {
    const state = createWorld(5, 256);
    const towns = townNames(state);
    expect(towns.length).toBeGreaterThan(0);
    expect(new Set(towns.map((t) => t.name)).size).toBe(Math.min(towns.length, TOWN_NAMES.length));
    expect(TOWN_NAMES.slice(0, TOWNS.length)).toEqual([...TOWNS]);
    expect(townNames(createWorld(5, 256))).toEqual(towns); // derived: the same every time
    expect(townNames(loadScenario({ seed: 1, map: ['.'] }))).toEqual([]);
  }, 20000);
});
