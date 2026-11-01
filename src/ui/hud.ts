// Top bar (speed, Build, Priorities), status line, room checklist, wanderers, build
// bar, priority panel, and the debug panel (DOM). Reads state; changes go
// out as sim commands or view-state changes.

import type { Command } from '../sim/commands';
import type { Point } from '../sim/grid';
import { checkInvariants } from '../sim/invariants';
import { explainIdle, getJob, type Job } from '../sim/jobs';
import { JOB_TYPES, type JobType, type Priority } from '../sim/jobTypes';
import { HUNGRY, SATIETY_MAX } from '../sim/food';
import { GROW_TICKS, isRipe } from '../sim/farming';
import { HEALTH_MAX, isCritical, isHurt } from '../sim/health';
import { TICKS_PER_HOUR, clockText, dayOf, isNight } from '../sim/time';
import { patienceLeft, spareBeds } from '../sim/wanderers';
import { REST_MAX, TIRED } from '../sim/rest';
import { designationOf, designationStatus, evaluateRoom, getRoomMap, roomAt } from '../sim/rooms';
import type { SimState } from '../sim/state';
import type { Survivor } from '../sim/survivors';
import { chooseCategory, chooseTool, toggleBuild } from './buildMode';
import { SPEEDS } from './loop';
import { CATEGORIES, getTool, toolsIn, type CategoryId, type ToolId } from './tools';
import type { ViewState } from './view';
import { seasonOf } from '../sim/seasons';
import { TRAIT_INFO, describeSkills } from '../sim/skills';
import { INFECTION_TURN } from '../sim/infection';
import { describeRelations } from '../sim/relationships';
import { MAX_CRAFT_TARGET, ORDERABLE, RECIPES, craftStock, type CraftKind } from '../sim/crafting';
import { AUTO_SLOT, SLOTS, slotLabel } from './saves';
import { createMinimap } from './minimap';
import { WORLD_SIZE, WORLD_SIZES } from '../sim/worldgen';
import { isMiserable, mood, moodFactors } from '../sim/mood';
import { PURPOSE_NAMES } from '../render/draw';

/** The Game menu's actions (app.ts does the work). */
export interface GameFiles {
  save(slot: string): void;
  load(slot: string): void;
  exportSave(): void;
  importFile(): void;
  reportBug(): void;
  newWorld(size: number): void;
}

export interface Hud {
  update(state: SimState, fps: number): void;
  dispose(): void;
}

const JOB_LABELS: Record<JobType, string> = { guard: 'Guard', doctor: 'Doc', farm: 'Farm', build: 'Build', haul: 'Haul', craft: 'Craft', cook: 'Cook', scout: 'Scout', scavenge: 'Scav', clean: 'Clean' };
const DEBUG_JOB_LINES = 12;
/** Most recent events shown in the feed under the status line. */
const FEED_LINES = 4;

/** Click cycles 1 → 2 → 3 → 4 → off → 1; right-click goes the other way. */
export function cyclePriority(p: Priority, backwards: boolean): Priority {
  const order: Priority[] = [1, 2, 3, 4, 0];
  const i = order.indexOf(p);
  return order[(i + (backwards ? order.length - 1 : 1)) % order.length]!;
}

export function createHud(root: HTMLElement, view: ViewState, command: (cmd: Command) => void, files: GameFiles): Hud {
  root.innerHTML = '';

  const bar = el('div', 'hud-bar');
  const clock = el('span', 'hud-clock');
  bar.append(clock, el('span', 'hud-sep'));
  const pauseButton = el('button', 'hud-button');
  pauseButton.addEventListener('click', () => (view.paused = !view.paused));
  bar.append(pauseButton);
  const speedButtons = SPEEDS.map((speed) => {
    const button = el('button', 'hud-button');
    button.textContent = `${speed}x`;
    button.addEventListener('click', () => {
      view.speed = speed;
      view.paused = false;
    });
    bar.append(button);
    return { speed, button };
  });

  bar.append(el('span', 'hud-sep'));
  const buildButton = el('button', 'hud-button');
  buildButton.textContent = 'B Build';
  buildButton.addEventListener('click', () => toggleBuild(view));
  bar.append(buildButton);
  const prioritiesButton = el('button', 'hud-button');
  prioritiesButton.textContent = 'P Priorities';
  prioritiesButton.addEventListener('click', () => (view.showPriorities = !view.showPriorities));
  bar.append(prioritiesButton);
  const gameButton = el('button', 'hud-button');
  gameButton.textContent = '≡ Game';
  gameButton.addEventListener('click', () => (view.showGame = !view.showGame));
  bar.append(gameButton);

  const info = el('div', 'hud-info');
  const status = el('div', 'hud-status');
  const roomPanel = el('pre', 'hud-room');
  const toast = el('div', 'hud-toast');
  const feed = el('div', 'hud-feed');
  const wanderers = createWandererPanel(command);
  const crafting = createCraftPanel(command);
  const expeditions = createExpeditionPanel(view, command);
  info.append(status, toast, roomPanel, wanderers.root, crafting.root, expeditions.root, feed);

  const buildBar = createBuildBar(view);
  const priorities = createPriorityPanel(view, command);
  const debug = el('pre', 'hud-debug');
  const game = createGamePanel(view, files);
  const minimap = createMinimap(view);
  // Pause-to-plan: a tinted frame around the view plus a banner, so it's obvious nothing is ticking.
  const pausedFrame = el('div', 'hud-paused-frame');
  const pausedBanner = el('div', 'hud-paused-banner');
  pausedBanner.textContent = 'PAUSED · plan freely · Space to resume';
  root.append(pausedFrame, bar, info, priorities.root, debug, game.root, minimap.root, buildBar.root, pausedBanner);

  // Only touch the DOM when text actually changes.
  let lastStatus = '';
  let lastRoom = '';
  let lastDebug = '';
  let lastClock = '';
  let lastFeed = '';

  return {
    update(state, fps) {
      const weather = state.weather === 'clear' ? '' : `, ${state.weather}`;
      const clockLine = `Day ${dayOf(state.tick)} · ${clockText(state.tick)} ${isNight(state.tick) ? '☾' : '☀'} · ${seasonOf(state.tick)}${weather}`;
      if (clockLine !== lastClock) clock.textContent = lastClock = clockLine;
      clock.classList.toggle('night', isNight(state.tick));

      const feedText = state.events
        .slice(-FEED_LINES)
        .map((e) => `Day ${dayOf(e.tick)} ${clockText(e.tick)}  ${e.message}`)
        .join('\n');
      feed.hidden = feedText === '';
      const toastText = view.toast && performance.now() < view.toast.until ? view.toast.text : '';
      toast.hidden = toastText === '';
      if (toast.textContent !== toastText) toast.textContent = toastText;
      if (feedText !== lastFeed) feed.textContent = lastFeed = feedText;

      pauseButton.textContent = view.paused ? '▶ Resume' : '❚❚ Pause';
      pauseButton.classList.toggle('active', view.paused);
      pausedFrame.hidden = !view.paused;
      pausedBanner.hidden = !view.paused;
      // Lift the banner above the build bar when both are showing.
      pausedBanner.classList.toggle('above-build', view.buildOpen);
      buildButton.classList.toggle('active', view.buildOpen);
      buildBar.update();
      speedButtons.forEach(({ speed, button }) =>
        button.classList.toggle('active', !view.paused && speed === view.speed),
      );
      prioritiesButton.classList.toggle('active', view.showPriorities);
      gameButton.classList.toggle('active', view.showGame);
      game.update();
      minimap.update(state);

      const text = statusText(state, view);
      if (text !== lastStatus) status.textContent = lastStatus = text;

      const room = roomText(state, view.hover);
      roomPanel.hidden = room === '';
      if (room !== lastRoom) roomPanel.textContent = lastRoom = room;

      priorities.update(state);
      wanderers.update(state);
      crafting.update(state);
      expeditions.update(state);

      debug.hidden = !view.showDebug;
      if (view.showDebug) {
        const lines = debugText(state, view, fps);
        if (lines !== lastDebug) debug.textContent = lastDebug = lines;
      }
    },
    dispose() {
      root.innerHTML = '';
    },
  };
}

function statusText(state: SimState, view: ViewState): string {
  const hover = view.hover;
  const { width, height, tiles } = state.grid;
  const tile =
    hover && hover.x >= 0 && hover.y >= 0 && hover.x < width && hover.y < height ? tiles[hover.y * width + hover.x] : undefined;
  const selected = state.survivors.find((s) => s.id === view.selectedId);
  const wood = state.items.filter((i) => i.kind === 'wood').length;
  const food = state.items.filter((i) => i.kind === 'food' || i.kind === 'meal').length;
  const mode = view.tool ? `building: ${getTool(view.tool).label}` : view.buildOpen ? 'build: pick a tool' : 'select';
  return [
    mode,
    selected ? `${selected.name} selected (right-click to move, click a bed to give it to them)` : 'nobody selected',
    hover && tile ? (state.grid.explored[hover.y * width + hover.x] === 1 ? `(${hover.x}, ${hover.y}) ${tile}${furnitureText(state, hover)}` : `(${hover.x}, ${hover.y}) unexplored`) : '—',
    `tick ${state.tick}`,
    `jobs ${state.jobs.length}`,
    `wood ${wood}`,
    `food ${food}`,
    ...(state.zombies.length > 0 ? [`ZOMBIES ${state.zombies.length}`] : []),
  ].join('  ·  ');
}

/** " · crate (3 left, marked)" etc. for the hovered tile's furniture, or ''. */
function furnitureText(state: SimState, p: Point): string {
  const plot = state.farm.find((x) => x.x === p.x && x.y === p.y);
  if (plot) {
    return ` · farm (${!plot.planted ? 'empty, needs sowing' : isRipe(plot) ? 'ripe' : `growing ${Math.floor((100 * plot.growth) / GROW_TICKS)}%`})`;
  }
  const f = state.furniture.find((x) => x.x === p.x && x.y === p.y);
  if (!f) return '';
  if (f.kind === 'bed') {
    const owner = f.owner ? state.survivors.find((s) => s.id === f.owner)?.name : undefined;
    return ` · ${owner ? `${owner}'s bed` : 'bed'}${f.occupant ? ' (in use)' : ''}`;
  }
  if (f.kind === 'table') return ' · table';
  if (f.kind === 'trap') return ` · spike trap (${f.loot} left)`;
  if (f.kind === 'tree') return ` · tree (${f.loot} wood${f.scavenge ? ', marked for chopping' : ''})`;
  if (f.kind === 'workbench') return ` · workbench${f.stored > 0 ? ` (${f.stored} wood in)` : ''}`;
  if (f.kind === 'stove') return ` · stove${f.stored > 0 ? ` (${f.stored} food in)` : ''}`;
  return f.loot === 0 ? ' · crate (empty)' : ` · crate (${f.loot} left${f.scavenge ? ', marked' : ''})`;
}

// --- Build bar --------------------------------------------------------------

/** Tools whose action isn't obvious from "click or drag to place". */
const TOOL_HINTS: Partial<Record<ToolId, string>> = {
  scavenge: 'Drag a box over a ruin or trees to mark crates and trees',
  explore: 'Drag a box into the dark: a scout goes and looks (up to 64 tiles a side)',
  farm: 'Drag a box over open ground to lay out plots',
  furnish: 'Click a room: the planner adds what it needs',
  remove: 'Drag over walls, doors, or furniture to take down',
  cancel: 'Drag to cancel plans, unmark crates and trees, or clear room purposes',
};

function createBuildBar(view: ViewState) {
  const root = el('div', 'hud-build');
  const tabs = el('div', 'build-tabs');
  const tools = el('div', 'build-tools');
  const hint = el('div', 'build-hint');
  root.append(tabs, tools, hint);

  const tabButtons = CATEGORIES.map((category) => {
    const button = el('button', 'build-tab');
    button.textContent = category.label;
    button.addEventListener('click', () => chooseCategory(view, category.id));
    tabs.append(button);
    return { id: category.id, button };
  });

  let shownCategory: CategoryId | null = null;
  let toolButtons: { id: ToolId; button: HTMLButtonElement }[] = [];
  const showTools = (category: CategoryId) => {
    tools.innerHTML = '';
    toolButtons = toolsIn(category).map((tool, i) => {
      const button = el('button', 'hud-button build-tool');
      button.textContent = `${i + 1} ${tool.label}`;
      button.title = tool.hasInstant ? 'Hold Shift while placing to build instantly' : '';
      button.addEventListener('click', () => chooseTool(view, tool.id, tool.category));
      tools.append(button);
      return { id: tool.id, button };
    });
    shownCategory = category;
  };

  let lastHint = '';
  return {
    root,
    update() {
      root.hidden = !view.buildOpen;
      if (!view.buildOpen) return;
      if (shownCategory !== view.buildCategory) showTools(view.buildCategory);
      tabButtons.forEach(({ id, button }) => button.classList.toggle('active', id === view.buildCategory));
      toolButtons.forEach(({ id, button }) => button.classList.toggle('active', id === view.tool));
      const tool = view.tool ? getTool(view.tool) : null;
      const text = tool
        ? `${TOOL_HINTS[tool.id] ?? (tool.paints ? 'Click or drag to place' : 'Click inside a room')}${tool.hasInstant ? ' · Shift: instant' : ''} · Right-click: put down · Esc`
        : 'Number keys: pick a tool · Tab / Q E: category · Esc or B: close';
      if (text !== lastHint) hint.textContent = lastHint = text;
    },
  };
}

/** Requirements checklist for the room (or outdoor room designation) under the cursor. */
function roomText(state: SimState, hover: Point | null): string {
  if (!hover) return '';
  const room = roomAt(state, hover.x, hover.y);
  const designation = room
    ? designationOf(state, room)
    : state.roomDesignations.find((d) => d.x === hover.x && d.y === hover.y);
  if (!room && !designation) return '';
  const header = room
    ? `Room · ${room.tiles.length} tiles · ${room.doors.length} door${room.doors.length === 1 ? '' : 's'}`
    : 'Not a room (open to the outside)';
  if (!designation) return `${header}\nno purpose · assign one with a Rooms tool (B)`;
  const status = evaluateRoom(state, room, designation.purpose);
  return [
    header,
    `${PURPOSE_NAMES[designation.purpose]} · ${status.complete ? 'complete' : 'INCOMPLETE'}`,
    ...status.requirements.map((r) => ` ${r.met ? '✓' : '✗'} ${r.label}`),
  ].join('\n');
}

// --- Priority panel ---------------------------------------------------------

/** Save slots, the autosave, file export/import, and bug reports. Rebuilt each time it opens (or a slot changes). */
function createGamePanel(view: ViewState, files: GameFiles) {
  const root = el('div', 'hud-game');
  let last = '';
  const button = (label: string, action: () => void) => {
    const b = el('button', 'hud-button');
    b.textContent = label;
    b.addEventListener('click', () => {
      action();
      last = ''; // slot labels may have changed
    });
    return b;
  };
  const row = (label: string, ...controls: HTMLElement[]) => {
    const r = el('div', 'hud-game-row');
    const text = el('span', 'hud-game-label');
    text.textContent = label;
    r.append(text, ...controls);
    return r;
  };
  return {
    root,
    update() {
      root.hidden = !view.showGame;
      if (!view.showGame) {
        last = '';
        return;
      }
      const labels = [...SLOTS, AUTO_SLOT].map((s) => slotLabel(s) ?? '');
      const key = labels.join('|');
      if (key === last) return;
      last = key;
      root.replaceChildren();
      const title = el('div', 'hud-game-title');
      title.textContent = 'Game';
      root.append(title);
      for (const slot of SLOTS) {
        const label = slotLabel(slot);
        const load = button('Load', () => files.load(slot));
        load.disabled = label === null;
        root.append(row(`Slot ${slot}: ${label ?? 'empty'}`, button('Save', () => files.save(slot)), load));
      }
      const auto = slotLabel(AUTO_SLOT);
      const loadAuto = button('Load', () => files.load(AUTO_SLOT));
      loadAuto.disabled = auto === null;
      root.append(row(`Autosave (each dawn): ${auto ?? 'none yet'}`, loadAuto));
      root.append(
        row('Files:', button('Export save', files.exportSave), button('Import…', files.importFile), button('Report bug', files.reportBug)),
      );
      const size = el('select', 'hud-select');
      for (const n of WORLD_SIZES) {
        const option = el('option', '');
        option.value = String(n);
        option.textContent = `${n}×${n}`;
        option.selected = n === WORLD_SIZE;
        size.append(option);
      }
      root.append(row('A fresh start somewhere else:', size, button('New world', () => files.newWorld(Number(size.value)))));
    },
  };
}

/** Known ruins to send the selected survivor to, and who's away. Shown once there's a point of interest or an expedition out. */
function createExpeditionPanel(view: ViewState, command: (cmd: Command) => void) {
  const root = el('div', 'hud-craft');
  let last = '';
  return {
    root,
    update(state: SimState) {
      const selected = state.survivors.find((s) => s.id === view.selectedId);
      const away = state.expeditions.map((x) => ({ x, hours: Math.ceil((x.returnTick - state.tick) / TICKS_PER_HOUR) }));
      const key = JSON.stringify([selected?.id ?? null, state.pointsOfInterest.map((p) => p.id), away.map((a) => [a.x.id, a.hours])]);
      if (key === last) return;
      last = key;
      root.hidden = state.pointsOfInterest.length === 0 && away.length === 0;
      root.replaceChildren();
      const title = el('div', 'hud-wanderers-title');
      title.textContent = 'Expeditions';
      root.append(title);
      for (const p of state.pointsOfInterest.slice(0, 5)) {
        const row = el('div', 'hud-wanderer');
        const label = el('span', 'hud-wanderer-label');
        label.textContent = `${p.label} at (${p.x}, ${p.y})`;
        const send = el('button', 'hud-button');
        send.textContent = selected ? `Send ${selected.name}` : 'Select someone to send';
        send.disabled = !selected;
        send.addEventListener('click', () => {
          if (selected) command({ type: 'sendExpedition', survivorIds: [selected.id], poiId: p.id });
        });
        row.append(label, send);
        root.append(row);
      }
      for (const { x, hours } of away) {
        const row = el('div', 'hud-wanderer-label');
        row.textContent = `${x.members.map((s) => s.name).join(', ')} → ${x.target.label} (${x.target.x}, ${x.target.y}), back in about ${hours}h`;
        root.append(row);
      }
    },
  };
}

/** Stock targets for the workbenches: shown once there is a workbench (or a plan for one). */
function createCraftPanel(command: (cmd: Command) => void) {
  const root = el('div', 'hud-craft');
  let last = '';
  return {
    root,
    update(state: SimState) {
      const benches = state.furniture.filter((f) => f.kind === 'workbench').length;
      const planned = state.blueprints.some((b) => b.target === 'workbench');
      const rows = ORDERABLE.map((k) => ({ k: k as Exclude<CraftKind, 'meal'>, have: craftStock(state, k), want: state.craftTargets[k as Exclude<CraftKind, 'meal'>] }));
      const key = `${benches}|${planned}|${JSON.stringify(rows)}`;
      if (key === last) return;
      last = key;
      root.hidden = benches === 0 && !planned;
      root.replaceChildren();
      const title = el('div', 'hud-wanderers-title');
      title.textContent = benches === 0 ? 'Workbench orders (once the bench is built)' : `Workbench orders · ${benches} bench${benches === 1 ? '' : 'es'}`;
      root.append(title);
      for (const { k, have, want } of rows) {
        const row = el('div', 'hud-wanderer');
        const label = el('span', 'hud-wanderer-label');
        label.textContent = `${RECIPES[k].label} (${RECIPES[k].count} ${RECIPES[k].input}${RECIPES[k].workshopOnly ? ', workshop' : ''}): have ${have}, keep`;
        const set = (count: number) => command({ type: 'setCraftTarget', kind: k, count });
        const less = el('button', 'hud-button');
        less.textContent = '−';
        less.disabled = want === 0;
        less.addEventListener('click', () => set(want - 1));
        const value = el('span', 'hud-craft-value');
        value.textContent = String(want);
        const more = el('button', 'hud-button');
        more.textContent = '+';
        more.disabled = want >= MAX_CRAFT_TARGET;
        more.addEventListener('click', () => set(want + 1));
        row.append(label, less, value, more);
        root.append(row);
      }
    },
  };
}

/** People waiting at the edge, each with Take in / Turn away. Rebuilt only when the list or the times change. */
function createWandererPanel(command: (cmd: Command) => void) {
  const root = el('div', 'hud-wanderers');
  let last = '';
  return {
    root,
    update(state: SimState) {
      const spare = spareBeds(state);
      const rows = state.wanderers.map((w) => ({ w, left: Math.ceil(patienceLeft(state, w) / (TICKS_PER_HOUR / 6)) * 10 }));
      const key = `${spare}|${rows.map((r) => `${r.w.id}:${r.left}`).join(',')}`;
      if (key === last) return;
      last = key;
      root.hidden = rows.length === 0;
      root.replaceChildren();
      const title = el('div', 'hud-wanderers-title');
      title.textContent = `Wanderers waiting · ${spare > 0 ? `${spare} spare bed${spare === 1 ? '' : 's'}` : 'no spare beds'}`;
      root.append(title);
      for (const { w, left } of rows) {
        const row = el('div', 'hud-wanderer');
        const label = el('span', 'hud-wanderer-label');
        label.textContent = `${w.name}${w.traits.length ? ` (${w.traits.map((t) => TRAIT_INFO[t].label).join(', ')})` : ''} at (${w.x}, ${w.y}) · leaves in ${Math.floor(left / 60)}h ${String(left % 60).padStart(2, '0')}m`;
        const accept = el('button', 'hud-button');
        accept.textContent = 'Take in';
        accept.addEventListener('click', () => command({ type: 'acceptWanderer', wandererId: w.id }));
        const dismiss = el('button', 'hud-button');
        dismiss.textContent = 'Turn away';
        dismiss.addEventListener('click', () => command({ type: 'dismissWanderer', wandererId: w.id }));
        row.append(label, accept, dismiss);
        root.append(row);
      }
    },
  };
}

function createPriorityPanel(view: ViewState, command: (cmd: Command) => void) {
  const root = el('div', 'hud-priorities');
  const table = el('table', 'prio-table');
  root.append(table);
  let lastKey = '';

  const rebuild = (state: SimState) => {
    table.innerHTML = '';
    const head = el('tr', '');
    for (const label of ['Survivor', 'Mood', ...JOB_TYPES.map((t) => JOB_LABELS[t]), 'Doing']) {
      const th = el('th', '');
      th.textContent = label;
      head.append(th);
    }
    table.append(head);

    for (const s of state.survivors) {
      const row = el('tr', s.id === view.selectedId ? 'selected' : '');
      const name = el('td', 'prio-name');
      name.textContent = s.name;
      name.title = [`Select · ${describeSkills(s)}`, describeRelations(state, s)].filter(Boolean).join(' · ');
      name.addEventListener('click', () => (view.selectedId = s.id));
      row.append(name);

      const m = mood(state, s);
      const moodCell = el('td', `prio-mood${isMiserable(state, s) ? ' low' : m >= 60 ? ' high' : ''}`);
      moodCell.textContent = String(m);
      moodCell.title = moodTitle(state, s);
      row.append(moodCell);

      for (const type of JOB_TYPES) {
        const td = el('td', '');
        const button = el('button', `prio-cell p${s.priorities[type]}`);
        button.textContent = s.priorities[type] === 0 ? '–' : String(s.priorities[type]);
        button.title = `${JOB_LABELS[type]} priority for ${s.name}. Click: higher number, right-click: back. – is off.`;
        const set = (backwards: boolean) =>
          command({ type: 'setPriority', survivorId: s.id, jobType: type, priority: cyclePriority(s.priorities[type], backwards) });
        button.addEventListener('click', () => set(false));
        button.addEventListener('contextmenu', (e) => {
          e.preventDefault();
          set(true);
        });
        td.append(button);
        row.append(td);
      }

      const doing = el('td', 'prio-doing');
      doing.textContent = activity(state, s);
      row.append(doing);
      table.append(row);
    }
  };

  return {
    root,
    update(state: SimState) {
      // Build mode takes the bottom of the screen: the panel steps aside until it closes.
      const shown = view.showPriorities && !view.buildOpen;
      root.hidden = !shown;
      if (!shown) return;
      // Rebuild only when something shown in the table changed.
      const key = JSON.stringify([view.selectedId, state.survivors.map((s) => [s.id, s.name, s.priorities, activity(state, s), moodTitle(state, s)])]);
      if (key === lastKey) return;
      lastKey = key;
      rebuild(state);
    },
  };
}

/** "Mood 55: ate at a table +10, hurt -10" (the tooltip, and what the debug panel shows). */
function moodTitle(state: SimState, s: Survivor): string {
  const factors = moodFactors(state, s).map(([label, effect]) => `${label} ${effect > 0 ? '+' : ''}${effect}`);
  return `Mood ${mood(state, s)}${isMiserable(state, s) ? ' (miserable: works slower)' : ''}${factors.length ? `: ${factors.join(', ')}` : ''}`;
}

function activity(state: SimState, s: Survivor): string {
  if (s.meal) return s.meal.progress > 0 ? (s.meal.seat ? 'eating at a table' : 'eating') : s.carrying ? 'taking food to a table' : 'going to eat';
  if (s.treatment) return s.treatment.progress > 0 ? 'treating themselves' : 'going for a remedy';
  if (s.arming) return 'going for a weapon';
  if (s.breakdown) return 'having a breakdown';
  if (s.leisure) return s.leisure.progress > 0 ? 'relaxing' : 'going to relax';
  if (s.sleep && isHurt(s.health)) return s.sleep.asleep ? 'resting (hurt)' : 'going to rest (hurt)';
  if (s.sleep) return s.sleep.asleep ? `asleep (${s.sleep.bedId ? 'bed' : 'floor'})` : 'going to bed';
  const job = s.jobId ? getJob(state, s.jobId) : undefined;
  if (job) return `${job.type} (${job.x}, ${job.y})`;
  if (s.goal) return 'walking (ordered)';
  return 'idle';
}

// --- Debug panel ------------------------------------------------------------

function debugText(state: SimState, view: ViewState, fps: number): string {
  const problems = checkInvariants(state);
  const open = state.jobs.filter((j) => !j.reservedBy).length;
  return [
    `fps            ${fps.toFixed(0)}`,
    `tick           ${state.tick}`,
    `speed          ${view.paused ? 'paused' : `${view.speed}x`}`,
    `map            ${state.grid.width}x${state.grid.height}`,
    `walkability v  ${state.walkabilityVersion}`,
    `commands       ${state.commandLog.length}`,
    `camera         ${view.camera.x.toFixed(1)}, ${view.camera.y.toFixed(1)} @ ${view.camera.zoom.toFixed(2)}x`,
    `rng            ${state.rng}`,
    `invariants     ${problems.length === 0 ? 'ok' : `${problems.length} PROBLEM(S): ${problems[0]}`}`,
    `rooms          ${getRoomMap(state).rooms.length} enclosed, ${state.roomDesignations
      .map((d) => `${d.purpose}${designationStatus(state, d).complete ? '' : ' (incomplete)'}`)
      .join(', ') || 'none designated'}`,
    '',
    ...state.survivors.flatMap((s) => survivorLines(state, s, s.id === view.selectedId)),
    '',
    ...state.zombies.map((z) => {
      const target = state.survivors.find((s) => s.id === z.targetId);
      return `  zombie ${z.id} at (${z.pos.x}, ${z.pos.y}) hp ${z.health}  ·  ${target ? `chasing ${target.name}` : 'wandering'}`;
    }),
    ...(state.zombies.length > 0 ? [''] : []),
    `jobs ${state.jobs.length} (${open} open)`,
    ...state.jobs.slice(0, DEBUG_JOB_LINES).map((j) => jobLine(state, j)),
    ...(state.jobs.length > DEBUG_JOB_LINES ? [`  … ${state.jobs.length - DEBUG_JOB_LINES} more`] : []),
  ].join('\n');
}

/** "Why is she just standing there?" in four lines. */
function survivorLines(state: SimState, s: Survivor, selected: boolean): string[] {
  const at = (p: Point) => `(${p.x}, ${p.y})`;
  const moving = s.move ? `moving ${at(s.move.from)}→${at(s.move.to)} ${s.move.progress}/${s.move.duration}` : 'still';
  const remaining = s.path.length + (s.move ? 1 : 0);
  const cache =
    s.pathVersion === -1
      ? 'no path'
      : s.pathVersion === state.walkabilityVersion
        ? `path v${s.pathVersion} fresh`
        : `path v${s.pathVersion} STALE`;
  const job = s.jobId ? getJob(state, s.jobId) : undefined;
  const p = s.priorities;
  return [
    `${selected ? '▶' : ' '} ${s.name} [${s.id}] at ${at(s.pos)}  ${moving}${s.carrying ? `  carrying ${state.items.find((i) => i.id === s.carrying)?.kind ?? s.carrying}` : ''}`,
    `    health ${Math.round((100 * s.health) / HEALTH_MAX)}%${isCritical(s.health) ? ' CRITICAL' : isHurt(s.health) ? ' HURT' : ''}${s.infection !== null ? ` INFECTED ${Math.round((100 * s.infection) / INFECTION_TURN)}%` : ''}  ·  rest ${Math.round((100 * s.rest) / REST_MAX)}%${s.rest < TIRED ? ' TIRED' : ''}  ·  food ${Math.round((100 * s.satiety) / SATIETY_MAX)}%${s.satiety < HUNGRY ? ' HUNGRY' : ''}  ·  ${activity(state, s)}`,
    `    job ${job ? `${job.id} ${job.type} ${at(job)}` : s.sleep || s.meal ? `none: ${activity(state, s)}` : `none: ${explainIdle(state, s)}`}  ·  prio g${p.guard} b${p.build} f${p.farm} h${p.haul} s${p.scavenge} c${p.clean}`,
    `    goal ${s.goal ? `${at(s.goal)}, ${remaining} tiles left` : '—'}  ·  ${cache}`,
    `    ${moodTitle(state, s)}  ·  ${describeSkills(s)}`,
    `    ${s.note ?? ''}`,
  ];
}

function jobLine(state: SimState, j: Job): string {
  const who = j.reservedBy ? state.survivors.find((s) => s.id === j.reservedBy)?.name ?? j.reservedBy : 'open';
  const extra = j.type === 'clean' && j.progress > 0 ? ` ${j.progress}` : '';
  return `  ${j.id.padEnd(5)} ${j.type.padEnd(5)} (${j.x}, ${j.y})${extra}  ← ${who}`;
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  node.className = className;
  return node;
}
