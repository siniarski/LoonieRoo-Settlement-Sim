// The main menu: what you see on starting, and (Escape, or the Menu button) over a paused game.
//   main  Continue (the newest save) or Resume, New game, Load, and in a game: Save, Report a bug
//   new   the four presets, a Customise section (the sandbox settings, map size, a seed), Next
//   character  who you are (sim/character.ts): a name, a profession, traits for points; Start
//   load  the save slots, the autosave, and a file
// Plain DOM over the canvas; it only calls the actions app.ts hands it.

import type { Arena } from '../sim/arena';
import { MAP_TYPES, NORMAL, PRESETS, PROGRESSION_LEVELS, WEATHER_LEVELS, type MapType, type PresetId, type Settings } from '../sim/difficulty';
import { WORLD_SIZE, WORLD_SIZES } from '../sim/worldgen';
import { CHOOSABLE, OPPOSED, PROFESSIONS, TRAIT_COST, characterProblem, pointsLeft, type Character } from '../sim/character';
import { TRAIT_INFO } from '../sim/skills';
import { STORY_JOBS } from '../sim/story';
import { AUTO_SLOT, SLOTS, slotLabel, slotTime } from './saves';
import type { ViewState } from './view';

export interface MenuActions {
  /** Back to the game under the menu. */
  resume(): void;
  newGame(size: number, settings: Settings, seed: number | null, you: Character): void;
  /** A debug arena: a small solo world with zombies near you from the start (arena.ts). */
  arena(kind: Arena): void;
  load(slot: string): void;
  importFile(): void;
  save(slot: string): void;
  exportSave(): void;
  reportBug(): void;
}

export interface Menu {
  update(): void;
  dispose(): void;
}

/** The newest save (a slot or the autosave), or null if there are none. */
export function newestSave(): string | null {
  const slots = [...SLOTS, AUTO_SLOT].filter((s) => slotLabel(s) !== null);
  slots.sort((a, b) => slotTime(b) - slotTime(a));
  return slots[0] ?? null;
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

export function createMenu(parent: HTMLElement, view: ViewState, actions: MenuActions): Menu {
  const root = el('div', 'menu');
  parent.append(root);
  let shown = '';
  /** The New game screen's choices, kept while you move between screens. */
  const pick: { preset: PresetId; settings: Settings; size: number; seed: string; you: Character } = {
    preset: 'survivor', settings: { ...NORMAL }, size: WORLD_SIZE, seed: '', you: { name: 'Ash', job: STORY_JOBS.indexOf('a student'), traits: [] },
  };

  const button = (label: string, onClick: () => void, className = 'menu-button') => {
    const b = el('button', className, label);
    b.addEventListener('click', onClick);
    return b;
  };
  const go = (screen: ViewState['menu']) => {
    view.menu = screen;
    shown = '';
  };

  const main = (panel: HTMLElement) => {
    const continueFrom = newestSave();
    if (view.inGame) panel.append(button('Resume', actions.resume, 'menu-button primary'));
    else if (continueFrom) {
      const b = button('Continue', () => actions.load(continueFrom), 'menu-button primary');
      panel.append(b, el('div', 'menu-note', slotLabel(continueFrom) ?? ''));
    }
    panel.append(button('New game', () => go('new')), button('Load', () => go('load')));
    if (view.inGame) {
      const saves = el('div', 'menu-row');
      saves.append(el('span', 'menu-label', 'Save to'));
      for (const slot of SLOTS) saves.append(button(slot, () => {
        actions.save(slot);
        shown = '';
      }, 'menu-small'));
      saves.append(button('File…', actions.exportSave, 'menu-small'));
      panel.append(saves, button('Report a bug', actions.reportBug));
    }
  };

  const newGame = (panel: HTMLElement) => {
    panel.append(el('div', 'menu-heading', 'New game'));
    const presets = el('div', 'menu-presets');
    for (const [id, p] of Object.entries(PRESETS) as [PresetId, (typeof PRESETS)[PresetId]][]) {
      const [title, about] = p.label.split(': ');
      const card = el('button', `menu-preset${pick.preset === id ? ' chosen' : ''}`);
      card.append(el('div', 'menu-preset-title', title), el('div', 'menu-preset-about', about ?? ''));
      card.addEventListener('click', () => {
        pick.preset = id;
        pick.settings = { ...p.settings };
        shown = '';
      });
      presets.append(card);
    }
    panel.append(presets);

    // Customise: every setting, starting from the preset.
    const custom = el('details', 'menu-custom');
    custom.open = view.menuCustomOpen;
    custom.addEventListener('toggle', () => (view.menuCustomOpen = custom.open));
    custom.append(el('summary', '', 'Customise'));
    const grid = el('div', 'menu-grid');
    const choose = <T extends string | number>(label: string, options: readonly (readonly [T, string])[], value: T, set: (v: T) => void) => {
      const select = el('select', 'hud-select');
      for (const [v, text] of options) {
        const o = el('option', '', text);
        o.value = String(v);
        o.selected = v === value;
        select.append(o);
      }
      select.addEventListener('change', () => {
        const chosen = options.find(([v]) => String(v) === select.value);
        if (chosen) set(chosen[0]);
      });
      grid.append(el('label', 'menu-label', label), select);
    };
    const s = pick.settings;
    choose('Start', [['group', 'With a group of three'], ['solo', 'Alone (you control them)']] as const, s.start, (v) => (s.start = v));
    choose('Zombies', [25, 50, 60, 75, 100, 125, 150, 200, 300].map((n) => [n, `${n}%`] as const), s.zombies, (v) => (s.zombies = v));
    choose('Weather', WEATHER_LEVELS.map((w, i) => [i, w] as const), s.weather, (v) => (s.weather = v));
    choose('Since the outbreak', PROGRESSION_LEVELS.map((p, i) => [i, p] as const), s.progression, (v) => (s.progression = v));
    choose('Nuclear winter', [[0, 'No'], [1, 'Yes']] as const, s.winter ? 1 : 0, (v) => (s.winter = v === 1));
    choose('Day length', [[1, 'Normal'], [2, 'Twice as long'], [3, 'Three times as long']] as const, s.dayScale, (v) => (s.dayScale = v));
    choose('Map', MAP_TYPES, s.map, (v) => (s.map = v as MapType));
    choose('Map size', WORLD_SIZES.map((n) => [n, `${n} × ${n}`] as const), pick.size, (v) => (pick.size = v));
    const seed = el('input', 'menu-input');
    seed.placeholder = 'random';
    seed.value = pick.seed;
    seed.addEventListener('input', () => (pick.seed = seed.value));
    grid.append(el('label', 'menu-label', 'Seed'), seed);
    custom.append(grid);
    panel.append(custom);

    panel.append(button('Next: who you are', () => go('character'), 'menu-button primary'));
    const debug = el('div', 'menu-row');
    debug.append(el('span', 'menu-label', 'Debug'), button('Arena: one zombie', () => actions.arena('one'), 'menu-small'), button('Arena: a crowd', () => actions.arena('crowd'), 'menu-small'));
    panel.append(debug, button('Back', () => go('main')));
  };

  const character = (panel: HTMLElement) => {
    const you = pick.you;
    const redraw = () => (shown = '');
    panel.append(el('div', 'menu-heading', 'Who you are'));
    const grid = el('div', 'menu-grid');
    const name = el('input', 'menu-input');
    name.maxLength = 16;
    name.value = you.name;
    name.addEventListener('input', () => {
      you.name = name.value;
      start.disabled = characterProblem(you) !== null;
    });
    const job = el('select', 'hud-select');
    STORY_JOBS.forEach((j, i) => {
      const p = PROFESSIONS[j]!;
      const o = el('option', '', `${j[0]!.toUpperCase()}${j.slice(1)} (${p.points > 0 ? '+' : ''}${p.points})`);
      o.value = String(i);
      o.selected = i === you.job;
      job.append(o);
    });
    job.addEventListener('change', () => {
      you.job = Number(job.value);
      redraw();
    });
    const prof = PROFESSIONS[STORY_JOBS[you.job]!]!;
    const skills = Object.entries(prof.skills).map(([t, n]) => `${t} ${n}`).join(', ') || 'no skills';
    const kit = prof.kit.length ? prof.kit.join(', ') : 'nothing';
    grid.append(el('label', 'menu-label', 'Name'), name, el('label', 'menu-label', 'Before'), job, el('span', ''), el('span', 'menu-note', `${prof.about}: ${skills}; brings ${kit}`));
    panel.append(grid);
    const traits = el('div', 'menu-presets');
    for (const t of CHOOSABLE) {
      const cost = TRAIT_COST[t]!;
      const on = you.traits.includes(t);
      const card = el('button', `menu-preset${on ? ' chosen' : ''}`);
      card.append(el('div', 'menu-preset-title', `${TRAIT_INFO[t].label} (${cost > 0 ? '-' : '+'}${Math.abs(cost)})`), el('div', 'menu-preset-about', TRAIT_INFO[t].about));
      const clash = OPPOSED.some(([a, b]) => (a === t && you.traits.includes(b)) || (b === t && you.traits.includes(a)));
      card.disabled = !on && clash;
      card.addEventListener('click', () => {
        you.traits = on ? you.traits.filter((x) => x !== t) : [...you.traits, t];
        redraw();
      });
      traits.append(card);
    }
    const left = pointsLeft(you);
    panel.append(el('div', left < 0 ? 'menu-note menu-warn' : 'menu-note', `Traits: ${left} point${left === 1 ? '' : 's'} left${left < 0 ? ' (too many: drop one, or pick a different past)' : ''}`), traits);
    const start = button('Start', () => {
      const n = Number(pick.seed);
      actions.newGame(pick.size, { ...pick.settings }, pick.seed.trim() !== '' && Number.isInteger(n) && n > 0 ? n : null, { ...you, traits: [...you.traits] });
    }, 'menu-button primary');
    start.disabled = characterProblem(you) !== null;
    panel.append(start, button('Back', () => go('new')));
  };

  const load = (panel: HTMLElement) => {
    panel.append(el('div', 'menu-heading', 'Load'));
    for (const slot of [...SLOTS, AUTO_SLOT]) {
      const label = slotLabel(slot);
      const row = el('div', 'menu-row');
      row.append(el('span', 'menu-label', slot === AUTO_SLOT ? 'Autosave' : `Slot ${slot}`), el('span', 'menu-note', label ?? 'empty'));
      const b = button('Load', () => actions.load(slot), 'menu-small');
      b.disabled = label === null;
      row.append(b);
      panel.append(row);
    }
    panel.append(button('From a file…', actions.importFile), button('Back', () => go('main')));
  };

  return {
    update() {
      const key = `${view.menu}|${view.inGame}`;
      root.hidden = view.menu === null;
      if (view.menu === null || key === shown) return;
      shown = key;
      root.replaceChildren();
      const panel = el('div', 'menu-panel');
      panel.append(el('div', 'menu-title', 'LOONIEROO'), el('div', 'menu-subtitle', 'a settlement in the dead country'));
      if (view.menu === 'main') main(panel);
      else if (view.menu === 'new') newGame(panel);
      else if (view.menu === 'character') character(panel);
      else load(panel);
      root.append(panel);
    },
    dispose() {
      root.remove();
    },
  };
}
