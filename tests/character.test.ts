import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { PROFESSIONS, characterProblem, cleanName, pointsLeft, type Character } from '../src/sim/character';
import { PRESETS } from '../src/sim/difficulty';
import { loadScenario } from '../src/sim/scenario';
import { RUN_HEARD, WALK_HEARD, heardFrom } from '../src/sim/sight';
import { skillLevel } from '../src/sim/skills';
import { STORY_JOBS } from '../src/sim/story';
import { createWorld } from '../src/sim/worldgen';

const job = (name: string) => STORY_JOBS.indexOf(name);
const you = (c: Partial<Character> = {}): Character => ({ name: 'Rook', job: job('a student'), traits: [], ...c });

describe('character creation', () => {
  it('every past is a profession', () => {
    for (const j of STORY_JOBS) expect(PROFESSIONS[j], j).toBeDefined();
  });

  it('traits cost points, bad ones give them back, and you cannot overspend', () => {
    expect(pointsLeft(you())).toBe(8);
    expect(pointsLeft(you({ traits: ['tough'] }))).toBe(4);
    expect(characterProblem(you({ traits: ['tough', 'quick', 'graceful'] }))).toBe('not enough points');
    expect(characterProblem(you({ traits: ['tough', 'quick', 'graceful', 'lazy'] }))).toBeNull(); // 8 - 11 + 3
    expect(characterProblem(you({ job: job('a soldier'), traits: ['tough'] }))).toBe('not enough points');
    expect(characterProblem(you({ traits: ['graceful', 'clumsy'] }))).toMatch(/both/);
    expect(characterProblem(you({ traits: ['hardened'] }))).toMatch(/can't choose/); // earned, not chosen
    expect(characterProblem(you({ name: '  ' }))).toBe('needs a name');
    expect(characterProblem(you({ job: 99 }))).toBe('no such profession');
    expect(cleanName('  Mary-Jo  <script>  ')).toBe('Mary-Jo script');
  });

  it('you start as who you made: name, past, skills, traits and kit in your bag', () => {
    const state = createWorld(4, 64, { ...PRESETS.apocalypse.settings }, you({ name: 'Rook', job: job('a nurse'), traits: ['graceful', 'gloomy'] }));
    const s = state.survivors[0]!;
    expect(state.control?.survivorId).toBe(s.id);
    expect(s.name).toBe('Rook');
    expect(STORY_JOBS[s.story.job]).toBe('a nurse');
    expect(s.traits).toEqual(['graceful', 'gloomy']);
    expect(skillLevel(s, 'doctor')).toBe(3);
    const bag = s.pack.map((id) => state.items.find((i) => i.id === id)!.kind);
    expect(bag).toEqual(expect.arrayContaining(['medicine', 'bandage']));
  }, 20000);

  it('a character that breaks the rules is ignored', () => {
    const state = createWorld(4, 64, { ...PRESETS.apocalypse.settings }, you({ traits: ['tough', 'quick', 'graceful'] }));
    expect(state.survivors[0]!.name).not.toBe('Rook');
  }, 20000);

  it('graceful people are heard half as far, clumsy ones half as far again', () => {
    const heard = (traits: Character['traits']) => {
      const state = loadScenario({ seed: 1, map: ['s....'] });
      const s = state.survivors[0]!;
      s.traits = traits;
      applyCommand(state, { type: 'takeControl', survivorId: s.id });
      applyCommand(state, { type: 'steer', dx: 1, dy: 0, run: true });
      return heardFrom(state, s);
    };
    expect(heard([])).toBe(RUN_HEARD);
    expect(heard(['graceful'])).toBe(RUN_HEARD / 2);
    expect(heard(['clumsy'])).toBe((RUN_HEARD * 3) / 2);
    expect(WALK_HEARD).toBeGreaterThan(0);
  });
});
