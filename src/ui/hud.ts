// Tool palette, speed controls, status line, priority panel, and the debug
// panel (DOM). Reads state; changes go out as sim commands.

import type { Command } from '../sim/commands';
import type { Point } from '../sim/grid';
import { checkInvariants } from '../sim/invariants';
import { explainIdle, getJob, type Job } from '../sim/jobs';
import { JOB_TYPES, type JobType, type Priority } from '../sim/jobTypes';
import { designationOf, designationStatus, evaluateRoom, getRoomMap, roomAt } from '../sim/rooms';
import type { SimState } from '../sim/state';
import type { Survivor } from '../sim/survivors';
import { SPEEDS } from './loop';
import { TOOLS } from './tools';
import type { ViewState } from './view';

export interface Hud {
  update(state: SimState, fps: number): void;
  dispose(): void;
}

const JOB_LABELS: Record<JobType, string> = { build: 'Build', haul: 'Haul', clean: 'Clean' };
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
  const toolButtons = TOOLS.map((tool) => {
    const button = el('button', 'hud-button');
    button.textContent = `${tool.key} ${tool.label}`;
    button.title = tool.paints && tool.id !== 'cancel' ? 'Hold Shift to place instantly' : '';
    button.addEventListener('click', () => (view.tool = tool.id));
    bar.append(button);
    return { id: tool.id, button };
  });

  bar.append(el('span', 'hud-sep'));

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
  const prioritiesButton = el('button', 'hud-button');
  prioritiesButton.textContent = 'P Priorities';
  prioritiesButton.addEventListener('click', () => (view.showPriorities = !view.showPriorities));
  bar.append(prioritiesButton);

  const info = el('div', 'hud-info');
  const status = el('div', 'hud-status');
  const roomPanel = el('pre', 'hud-room');
  info.append(status, roomPanel);

  const priorities = createPriorityPanel(view, command);
  const debug = el('pre', 'hud-debug');
  // Pause-to-plan: a tinted frame around the view plus a banner, so it's obvious nothing is ticking.
  const pausedFrame = el('div', 'hud-paused-frame');
  const pausedBanner = el('div', 'hud-paused-banner');
  pausedBanner.textContent = 'PAUSED · plan freely · Space to resume';
  root.append(pausedFrame, bar, info, priorities.root, debug, pausedBanner);

  // Only touch the DOM when text actually changes.
  let lastStatus = '';
  let lastRoom = '';
  let lastDebug = '';

  return {
    update(state, fps) {
      toolButtons.forEach(({ id, button }) => button.classList.toggle('active', id === view.tool));
      pauseButton.textContent = view.paused ? '▶ Resume' : '❚❚ Pause';
      pauseButton.classList.toggle('active', view.paused);
      pausedFrame.hidden = !view.paused;
      pausedBanner.hidden = !view.paused;
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
  return [
    `tick ${state.tick}`,
    hover && tile ? `(${hover.x}, ${hover.y}) ${tile}` : '—',
    selected ? `selected ${selected.name}` : 'nobody selected',
    `jobs ${state.jobs.length}`,
    `wood ${wood}`,
  ].join('  ·  ');
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
    `${selected ? '▶' : ' '} ${s.name} [${s.id}] at ${at(s.pos)}  ${moving}${s.carrying ? '  carrying wood' : ''}`,
    `    job ${job ? `${job.id} ${job.type} ${at(job)}` : `none: ${explainIdle(state, s)}`}  ·  prio b${p.build} h${p.haul} c${p.clean}`,
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
