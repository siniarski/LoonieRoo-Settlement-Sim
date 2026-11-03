import { describe, expect, it } from 'vitest';
import { applyCommand } from '../src/sim/commands';
import { loadScenario } from '../src/sim/scenario';
import { createState, type SimState } from '../src/sim/state';
import { makeStory, storyLines } from '../src/sim/story';
import { addSurvivor, type Survivor } from '../src/sim/survivors';
import { TALK_EVERY, talk, updateTasks, type TaskKind } from '../src/sim/talk';
import { tick } from '../src/sim/tick';
import { setTile } from '../src/sim/grid';

/** An open 80x80 field with two survivors side by side. */
function field(): { state: SimState; you: Survivor; them: Survivor } {
  const state = createState({ seed: 1, width: 80, height: 80 });
  const you = addSurvivor(state, 40, 40);
  const them = addSurvivor(state, 41, 40);
  return { state, you, them };
}

/** Talk until they ask for a task of this kind (moving time on between talks). */
function talkUntil(state: SimState, s: Survivor, kind: TaskKind): void {
  for (let i = 0; i < 8; i++) {
    expect(talk(state, s)).toBeNull();
    const task = state.tasks.find((t) => t.giverId === s.id);
    if (task?.kind === kind) return;
    if (task) state.tasks.splice(state.tasks.indexOf(task), 1); // skip past other asks
    state.tick += TALK_EVERY;
  }
  throw new Error(`${s.name} never asked for ${kind}`);
}

/** A survivor id whose story has a dead relative and a dog. */
function storyWith(dead: boolean, pet: boolean): string {
  for (let n = 1; n < 500; n++) {
    const s = makeStory(`s${n}`);
    if (s.lost.dead === dead && (s.pet !== null) === pet) return `s${n}`;
  }
  throw new Error('none');
}

describe('stories', () => {
  it('are the same for the same id, and come out a line at a talk', () => {
    expect(makeStory('s7')).toEqual(makeStory('s7'));
    const { state, them } = field();
    expect(talk(state, them)).toBeNull();
    expect(them.story.told).toBe(1);
    expect(state.events.at(-1)!.message).toContain(storyLines(them.story)[0]!);
    expect(talk(state, them)).toMatch(/yet/); // not again so soon
  });

  it('you need to be close to talk to someone while controlling', () => {
    const { state, you, them } = field();
    applyCommand(state, { type: 'takeControl', survivorId: you.id });
    them.pos = { x: 50, y: 40 };
    expect(applyCommand(state, { type: 'talk' }).ok).toBe(false);
    them.pos = { x: 41, y: 41 };
    expect(applyCommand(state, { type: 'talk' })).toMatchObject({ ok: true });
    expect(them.opinions[you.id]).toBeGreaterThan(0);
  });
});

describe('tasks', () => {
  it('home: go and see their old place', () => {
    const { state, you, them } = field();
    applyCommand(state, { type: 'takeControl', survivorId: you.id });
    talkUntil(state, them, 'home');
    const task = state.tasks[0]!;
    expect(state.furniture.some((f) => f.kind === 'crate' && f.x === task.x && f.y === task.y)).toBe(true);
    you.pos = { x: task.x + 1, y: task.y };
    state.tick = Math.ceil(state.tick / 10) * 10;
    updateTasks(state);
    expect(state.tasks).toEqual([]);
    expect(them.thoughts.some((t) => t.kind === 'wentHome')).toBe(true);
    expect(them.opinions[you.id]).toBeGreaterThanOrEqual(25);
  });

  it('bury: their dead in a graveyard', () => {
    const state = createState({ seed: 1, width: 80, height: 80 });
    state.nextId = Number(storyWith(true, false).slice(1));
    const them = addSurvivor(state, 41, 40);
    talkUntil(state, them, 'bury');
    const body = state.items.find((i) => i.kind === 'corpse')!;
    expect(body.tag).toBe(them.story.lost.name);
    // A walled graveyard by the camp, and the body carried into it.
    for (let x = 30; x <= 34; x++) for (let y = 30; y <= 34; y++) if (x === 30 || x === 34 || y === 30 || y === 34) setTile(state.grid, x, y, x === 32 && y === 34 ? 'door' : 'wall');
    state.walkabilityVersion += 1;
    expect(applyCommand(state, { type: 'designateRoom', x: 32, y: 32, purpose: 'graveyard' })).toMatchObject({ ok: true });
    body.pos = { x: 32, y: 32 };
    state.tick = Math.ceil(state.tick / 10) * 10;
    updateTasks(state);
    expect(state.tasks).toEqual([]);
    expect(them.thoughts.some((t) => t.kind === 'laidToRest')).toBe(true);
  });

  it('pet: the dog waits at the house until someone comes', () => {
    const state = createState({ seed: 1, width: 80, height: 80 });
    state.nextId = Number(storyWith(false, true).slice(1));
    const them = addSurvivor(state, 41, 40);
    talkUntil(state, them, 'pet');
    const dog = state.dogs[0]!;
    expect(dog.name).toBe(them.story.pet);
    const at = { ...dog.pos };
    for (let i = 0; i < 50; i++) tick(state);
    expect(dog.pos).toEqual(at); // still waiting
    them.pos = { x: at.x + 1, y: at.y };
    for (let i = 0; i < 20; i++) tick(state);
    expect(dog.waiting).toBe(false);
    expect(state.tasks).toEqual([]);
    expect(them.thoughts.some((t) => t.kind === 'petFound')).toBe(true);
  });

  it('burn: set the place alight', () => {
    const { state, them } = field();
    talkUntil(state, them, 'burn');
    const task = state.tasks[0]!;
    applyCommand(state, { type: 'ignite', x: task.x, y: task.y });
    expect(state.fires.length).toBeGreaterThan(0);
    state.tick = Math.ceil(state.tick / 10) * 10;
    updateTasks(state);
    expect(state.tasks).toEqual([]);
  });

  it('a task is dropped when whoever asked is gone', () => {
    const state = loadScenario({ seed: 1, map: ['s'.padEnd(60, '.'), ''.padEnd(60, '.')] });
    const them = state.survivors[0]!;
    talkUntil(state, them, 'home');
    state.survivors = [];
    state.tick = Math.ceil(state.tick / 10) * 10;
    updateTasks(state);
    expect(state.tasks).toEqual([]);
  });
});

