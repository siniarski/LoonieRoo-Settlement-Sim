// Top bar (speed, Build, Priorities), status line, room checklist, build
// bar, priority panel, and the debug panel (DOM). Reads state; changes go
// out as sim commands or view-state changes.

import type { Command } from '../sim/commands';
import type { Point } from '../sim/grid';
import { checkInvariants } from '../sim/invariants';
import { explainIdle, getJob, type Job } from '../sim/jobs';
import { JOB_TYPES, type JobType, type Priority } from '../sim/jobTypes';
import { HUNGRY, SATIETY_MAX } from '../sim/food';
import { REST_MAX, TIRED } from '../sim/rest';
import { designationOf, designationStatus, evaluateRoom, getRoomMap, roomAt } from '../sim/rooms';
import type { SimState } from '../sim/state';
import type { Survivor } from '../sim/survivors';
import { chooseCategory, chooseTool, toggleBuild } from './buildMode';
import { SPEEDS } from './loop';
import { CATEGORIES, getTool, toolsIn, type CategoryId, type ToolId } from './tools';
import type { ViewState } from './view';

export interface Hud {
  update(state: SimState, fps: number): void;
  dispose(): void;
}

const JOB_LABELS: Record<JobType, string> = { build: 'Build', haul: 'Haul', scavenge: 'Scav', clean: 'Clean' };
const DEBUG_JOB_LINES = 12;

/** Click cycles 1 → 2 → 3 → 4 → off → 1; right-click goes the other way. */
export function cyclePriority(p: Priority, backwards: boolean): Priority {
  const order: Priority[] = [1, 2, 3, 4, 0];
  const i = order.indexOf(p);
  return order[(i + (backwards ? order.length - 1 : 1)) % order.length]!;
}

export function createHud(root: HTMLElement, view: ViewState, command: (cmd: Command) => void): Hud {
  root.innerHTML = '';

  const bar = el('div', 'hud-bar');
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

  const info = el('div', 'hud-info');
  const status = el('div', 'hud-status');
  const roomPanel = el('pre', 'hud-room');
  info.append(status, roomPanel);

  const buildBar = createBuildBar(view);
  const priorities = createPriorityPanel(view, command);
  const debug = el('pre', 'hud-debug');
  // Pause-to-plan: a tinted frame around the view plus a banner, so it's obvious nothing is ticking.
  const pausedFrame = el('div', 'hud-paused-frame');
  const pausedBanner = el('div', 'hud-paused-banner');
  pausedBanner.textContent = 'PAUSED · plan freely · Space to resume';
  root.append(pausedFrame, bar, info, priorities.root, debug, buildBar.root, pausedBanner);

  // Only touch the DOM when text actually changes.
  let lastStatus = '';
  let lastRoom = '';
  let lastDebug = '';

  return {
    update(state, fps) {
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

      const text = statusText(state, view);
      if (text !== lastStatus) status.textContent = lastStatus = text;

      const room = roomText(state, view.hover);
      roomPanel.hidden = room === '';
      if (room !== lastRoom) roomPanel.textContent = lastRoom = room;

      priorities.update(state);

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
  const food = state.items.filter((i) => i.kind === 'food').length;
  const mode = view.tool ? `building: ${getTool(view.tool).label}` : view.buildOpen ? 'build: pick a tool' : 'select';
  return [
    mode,
    selected ? `${selected.name} selected (right-click to move)` : 'nobody selected',
    hover && tile ? `(${hover.x}, ${hover.y}) ${tile}${furnitureText(state, hover)}` : '—',
    `tick ${state.tick}`,
    `jobs ${state.jobs.length}`,
    `wood ${wood}`,
    `food ${food}`,
  ].join('  ·  ');
}

/** " · crate (3 left, marked)" etc. for the hovered tile's furniture, or ''. */
function furnitureText(state: SimState, p: Point): string {
  const f = state.furniture.find((x) => x.x === p.x && x.y === p.y);
  if (!f) return '';
  if (f.kind === 'bed') return ` · bed${f.occupant ? ' (in use)' : ''}`;
  return f.loot === 0 ? ' · crate (empty)' : ` · crate (${f.loot} left${f.scavenge ? ', marked' : ''})`;
}

// --- Build bar --------------------------------------------------------------

/** Tools whose action isn't obvious from "click or drag to place". */
const TOOL_HINTS: Partial<Record<ToolId, string>> = {
  scavenge: 'Drag a box over a ruin to mark its crates',
  remove: 'Drag over walls, doors, or furniture to take down',
  cancel: 'Drag to cancel plans, unmark crates, or clear room purposes',
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
  if (!designation) return `${header}\nno purpose · 7 Housing or 8 Storage to assign`;
  const status = evaluateRoom(state, room, designation.purpose);
  return [
    header,
    `${designation.purpose === 'housing' ? 'Housing' : 'Storage'} · ${status.complete ? 'complete' : 'INCOMPLETE'}`,
    ...status.requirements.map((r) => ` ${r.met ? '✓' : '✗'} ${r.label}`),
  ].join('\n');
}

// --- Priority panel ---------------------------------------------------------

function createPriorityPanel(view: ViewState, command: (cmd: Command) => void) {
  const root = el('div', 'hud-priorities');
  const table = el('table', 'prio-table');
  root.append(table);
  let lastKey = '';

  const rebuild = (state: SimState) => {
    table.innerHTML = '';
    const head = el('tr', '');
    for (const label of ['Survivor', ...JOB_TYPES.map((t) => JOB_LABELS[t]), 'Doing']) {
      const th = el('th', '');
      th.textContent = label;
      head.append(th);
    }
    table.append(head);

    for (const s of state.survivors) {
      const row = el('tr', s.id === view.selectedId ? 'selected' : '');
      const name = el('td', 'prio-name');
      name.textContent = s.name;
      name.title = 'Select';
      name.addEventListener('click', () => (view.selectedId = s.id));
      row.append(name);

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
      root.hidden = !view.showPriorities;
      if (!view.showPriorities) return;
      // Rebuild only when something shown in the table changed.
      const key = JSON.stringify([view.selectedId, state.survivors.map((s) => [s.id, s.name, s.priorities, activity(state, s)])]);
      if (key === lastKey) return;
      lastKey = key;
      rebuild(state);
    },
  };
}

function activity(state: SimState, s: Survivor): string {
  if (s.meal) return s.meal.progress > 0 ? 'eating' : 'going to eat';
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
    `    rest ${Math.round((100 * s.rest) / REST_MAX)}%${s.rest < TIRED ? ' TIRED' : ''}  ·  food ${Math.round((100 * s.satiety) / SATIETY_MAX)}%${s.satiety < HUNGRY ? ' HUNGRY' : ''}  ·  ${activity(state, s)}`,
    `    job ${job ? `${job.id} ${job.type} ${at(job)}` : s.sleep || s.meal ? `none: ${activity(state, s)}` : `none: ${explainIdle(state, s)}`}  ·  prio b${p.build} h${p.haul} s${p.scavenge} c${p.clean}`,
    `    goal ${s.goal ? `${at(s.goal)}, ${remaining} tiles left` : '—'}  ·  ${cache}`,
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
