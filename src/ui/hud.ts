// Top bar (speed, Build, Priorities), status line, room checklist, wanderers, build
// bar, priority panel, and the debug panel (DOM). Reads state; changes go
// out as sim commands or view-state changes.

import { climateAt, isRadioactive } from '../sim/climate';
import { errandsOf } from '../sim/errands';
import { relation, type Settlement } from '../sim/settlements';
import { createWorldMap } from './worldmap';
import { FUEL_MAX, VEHICLE_HEALTH, vehicleOf } from '../sim/vehicles';
import { SUB } from '../sim/motion';
import { powerOn, waterOn } from '../sim/timeline';
import { STEP_UP, heirs } from '../sim/succession';
import { describeWish } from '../sim/wishes';
import { HIGH, LONELY_MOOD, LOW } from '../sim/wellbeing';
import { BREAKING, STEELED, describeResolve, isBreaking, isSteeled } from '../sim/resolve';
import { hungerOf, isFallen } from '../sim/colonies';
import { describeTasks } from '../sim/talk';
import { storyLines } from '../sim/story';
import { describeOrder } from '../sim/orders';
import { controlled } from '../sim/control';
import type { Command } from '../sim/commands';
import type { Point } from '../sim/grid';
import { checkInvariants } from '../sim/invariants';
import { explainIdle, getJob, type Job } from '../sim/jobs';
import { JOB_TYPES, type JobType, type Priority } from '../sim/jobTypes';
import { HUNGRY, SATIETY_MAX } from '../sim/food';
import { growTicks, isRipe } from '../sim/farming';
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
import { describeClothing } from '../sim/clothing';
import { FRIENDLY, GIFT_FOOD, HOSTILE } from '../sim/settlements';
import { createAlertList, createOverview } from './overview';
import { RECRUIT_DAYS } from '../sim/prisoners';
import { DAY_LENGTH } from '../sim/time';
import { MAX_CRAFT_TARGET, ORDERABLE, RECIPES, craftStock, type CraftKind } from '../sim/crafting';
import { createMinimap } from './minimap';
import { activity, createCharacterPanel, createStatusStrip, moodTitle } from './card';
import { createBag } from './bag';
import { WORLD_SIZE, WORLD_SIZES } from '../sim/worldgen';
import { isMiserable, mood, moodFactors } from '../sim/mood';
import { PURPOSE_NAMES } from '../render/draw';

/** What the HUD asks of the app. */
export interface GameFiles {
  /** Open the menu over the game (menu.ts). */
  openMenu(): void;
  /** Everyone aboard the helicopter: a new game+ (escape.ts). */
  newGamePlus(): void;
}

export interface Hud {
  update(state: SimState, fps: number, perf?: Perf): void;
  dispose(): void;
}

const JOB_LABELS: Record<JobType, string> = { guard: 'Guard', firefight: 'Fire', doctor: 'Doc', farm: 'Farm', build: 'Build', haul: 'Haul', craft: 'Craft', research: 'Lab', cook: 'Cook', scout: 'Scout', scavenge: 'Scav', hunt: 'Hunt', clean: 'Clean' };
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
  gameButton.textContent = '☰ Menu';
  gameButton.title = 'Saves, loading, a new game (Esc)';
  gameButton.addEventListener('click', () => files.openMenu());
  bar.append(gameButton);
  const overviewButton = el('button', 'hud-button');
  overviewButton.textContent = 'O Overview';
  overviewButton.addEventListener('click', () => (view.showOverview = !view.showOverview));
  bar.append(overviewButton);

  const info = el('div', 'hud-info');
  const status = el('div', 'hud-status');
  const roomPanel = el('pre', 'hud-room');
  const toast = el('div', 'hud-toast');
  const controlLine = el('div', 'hud-control');
  const feed = el('div', 'hud-feed');
  const wanderers = createWandererPanel(command);
  const crafting = createCraftPanel(command);
  const expeditions = createExpeditionPanel(view, command);
  const traders = createTraderPanel(command);
  const neighbours = createSettlementPanel(command);
  const prisoners = createPrisonerPanel(command);
  const story = createStoryPanel(view, command);
  const succession = createSuccessionPanel(view, command);
  const helicopter = createHelicopterPanel(command, files);
  const alertList = createAlertList(view);
  const overview = createOverview(view, command);
  info.append(status, succession.root, helicopter.root, controlLine, alertList.root, toast, story.root, roomPanel, wanderers.root, traders.root, crafting.root, expeditions.root, neighbours.root, prisoners.root, feed);

  const buildBar = createBuildBar(view);
  const priorities = createPriorityPanel(view, command);
  const debug = el('pre', 'hud-debug');
  const minimap = createMinimap(view);
  const worldMap = createWorldMap(view, command);
  // Pause-to-plan: a tinted frame around the view plus a banner, so it's obvious nothing is ticking.
  const pausedFrame = el('div', 'hud-paused-frame');
  const pausedBanner = el('div', 'hud-paused-banner');
  pausedBanner.textContent = 'PAUSED · plan freely · Space to resume';
  const conditions = createStatusStrip(view);
  const card = createCharacterPanel(view);
  const bag = createBag(command);
  // Bottom left: the priority table (P) stacked over the character panel.
  const bottomLeft = el('div', 'hud-bottom-left');
  bottomLeft.append(priorities.root, card.root);
  root.append(pausedFrame, bar, info, bottomLeft, conditions.root, ...bag.roots, debug, overview.root, minimap.root, buildBar.root, pausedBanner, worldMap.root);

  // Only touch the DOM when text actually changes.
  let lastStatus = '';
  let lastRoom = '';
  let lastDebug = '';
  let lastClock = '';
  let lastFeed = '';

  return {
    update(state, fps, perf) {
      const weather = state.weather === 'clear' ? '' : `, ${state.weather}`;
      const clockLine = `Day ${dayOf(state.tick)} · ${clockText(state.tick)} ${isNight(state.tick) ? '☾' : '☀'} · ${seasonOf(state.tick)}${weather}`;
      if (clockLine !== lastClock) clock.textContent = lastClock = clockLine;
      clock.classList.toggle('night', isNight(state.tick));

      const feedText = state.events
        .slice(-FEED_LINES)
        .map((e) => `Day ${dayOf(e.tick)} ${clockText(e.tick)}  ${e.message}`)
        .join('\n');
      feed.hidden = feedText === '';
      const you = controlled(state);
      const controlText = you
        ? view.showKeys
          ? 'WASD walk (in a car: W go, S brake, A/D steer) · Shift run · C sneak · E get in or out of a car · click swing · right-click/E use · R work · V follow me · H hold · L loot (mouse) · T talk · 1-9 use from bag · M map · F let go · K hide'
          : 'K: keys'
        : view.selectedId
          ? 'F: take control of the selected survivor'
          : '';
      controlLine.hidden = controlText === '';
      controlLine.classList.toggle('keys', you !== undefined && you !== null);
      if (controlLine.textContent !== controlText) controlLine.textContent = controlText;
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
      overviewButton.classList.toggle('active', view.showOverview);
      alertList.update(state);
      overview.update(state);
      minimap.update(state);
      worldMap.update(state);

      const text = statusText(state, view);
      if (text !== lastStatus) status.textContent = lastStatus = text;

      const room = roomText(state, view.hover);
      roomPanel.hidden = room === '';
      if (room !== lastRoom) roomPanel.textContent = lastRoom = room;

      priorities.update(state);
      conditions.update(state);
      card.update(state);
      bag.update(state);
      wanderers.update(state);
      crafting.update(state);
      expeditions.update(state);
      traders.update(state);
      neighbours.update(state);
      prisoners.update(state);
      story.update(state);
      succession.update(state);
      helicopter.update(state);

      debug.hidden = !view.showDebug;
      if (view.showDebug) {
        const lines = debugText(state, view, fps, perf);
        if (lines !== lastDebug) debug.textContent = lastDebug = lines;
      }
    },
    dispose() {
      root.innerHTML = '';
    },
  };
}

/** Behind the wheel: speed, fuel, and how the car is holding up. */
function drivingText(state: SimState): string {
  const you = controlled(state);
  const car = you ? vehicleOf(state, you) : undefined;
  if (!car) return '';
  const tiles = Math.round((Math.abs(car.speed) * 10) / SUB); // tiles a second at 10 ticks a second
  return `driving ${car.speed < 0 ? 'backwards ' : ''}${tiles} tiles/s · fuel ${Math.round((car.fuel * 100) / FUEL_MAX)}% · car ${Math.round((car.health * 100) / VEHICLE_HEALTH)}%`;
}

function statusText(state: SimState, view: ViewState): string {
  const hover = view.hover;
  const { width, height, tiles } = state.grid;
  const tile =
    hover && hover.x >= 0 && hover.y >= 0 && hover.x < width && hover.y < height ? tiles[hover.y * width + hover.x] : undefined;
  const selected = state.survivors.find((s) => s.id === view.selectedId);
  const wood = state.items.filter((i) => i.kind === 'wood').length;
  const food = state.items.filter((i) => i.kind === 'food' || i.kind === 'meal').length;
  const mode = view.tool ? `building: ${getTool(view.tool).label}` : view.buildOpen ? 'build: pick a tool' : '';
  const you = state.control?.survivorId === selected?.id;
  const place = hover && tile
    ? state.grid.explored[hover.y * width + hover.x] === 1
      ? `(${hover.x}, ${hover.y}) ${tile}${furnitureText(state, hover)}${state.world ? ` · ${climateAt(state, hover.x, hover.y)}${isRadioactive(state, hover.x, hover.y) ? ' · RADIOACTIVE' : ''}` : ''}`
      : `(${hover.x}, ${hover.y}) unexplored`
    : '';
  // Short, one line: what you're doing, what's under the mouse, the stores. (The tick is in the debug panel.)
  return [
    mode,
    selected && !you ? `${selected.name}: right-click to move` : '',
    place,
    drivingText(state),
    `wood ${wood} · food ${food} · jobs ${state.jobs.length}${state.timeline ? ` · water ${waterOn(state) ? 'on' : 'off'} · power ${powerOn(state) ? 'on' : 'off'}` : ''}`,
  ].filter((x) => x !== '').join('  ·  ');
}

/** " · crate (3 left, marked)" etc. for the hovered tile's furniture, or ''. */
function furnitureText(state: SimState, p: Point): string {
  const plot = state.farm.find((x) => x.x === p.x && x.y === p.y);
  if (plot) {
    return ` · ${plot.crop} (${!plot.planted ? 'empty, needs sowing' : isRipe(plot) ? 'ripe' : `growing ${Math.floor((100 * plot.growth) / growTicks(plot))}%`})`;
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
  if (f.kind === 'workbench') return ` · workbench${f.stored > 0 ? ` (${f.stored} ${f.storedKind ?? 'wood'} in)` : ''}`;
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
      button.textContent = `${(i + 1) % 10} ${tool.label}`;
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


/** The helicopter is down: who goes? */
function createHelicopterPanel(command: (cmd: Command) => void, files: GameFiles) {
  const root = el('div', 'hud-wanderers');
  root.hidden = true;
  let last = '';
  return {
    root,
    update(state: SimState) {
      const key = state.endgame.helicopter ? `${state.survivors.length}|${state.control?.survivorId ?? ''}` : '';
      if (key === last) return;
      last = key;
      root.hidden = !state.endgame.helicopter;
      root.replaceChildren();
      if (!state.endgame.helicopter) return;
      const title = el('div', 'hud-wanderers-title');
      title.textContent = `The helicopter is down until dusk. ${state.endgame.escaped > 0 ? `(${state.endgame.escaped} flown out so far.) ` : ''}Who goes?`;
      root.append(title);
      const button = (label: string, about: string, act: () => void) => {
        const b = el('button', 'hud-button');
        b.textContent = label;
        b.title = about;
        b.addEventListener('click', act);
        root.append(b);
      };
      button('Everyone (a new start elsewhere)', 'A new game+: a fresh world with the same settings, and you in it alone, with what you carry, your skills and your story.', () => files.newGamePlus());
      if (state.control && state.survivors.length > 1) button('The others; I stay', 'Everyone but you flies out. You stay, and can call again for anyone else you find.', () => command({ type: 'escape', who: 'others' }));
      button('Wave it off', 'Nobody goes. You can call again.', () => command({ type: 'escape', who: 'none' }));
    },
  };
}

/** When the one you controlled dies: who carries on? Pauses the game the first time it shows. */
function createSuccessionPanel(view: ViewState, command: (cmd: Command) => void) {
  const root = el('div', 'hud-wanderers');
  root.hidden = true;
  let last = '';
  return {
    root,
    update(state: SimState) {
      const s = state.succession;
      const key = s ? JSON.stringify([s, heirs(state).map((h) => [h.id, h.resolve])]) : '';
      if (key === last) return;
      if (s && !last) view.paused = true;
      last = key;
      root.hidden = !s;
      root.replaceChildren();
      if (!s) return;
      const title = el('div', 'hud-wanderers-title');
      title.textContent = `${s.name} died of ${s.cause}. Who carries on?`;
      root.append(title);
      const choose = (label: string, choice: string, about: string) => {
        const b = el('button', 'hud-button');
        b.textContent = label;
        b.title = about;
        b.addEventListener('click', () => {
          command({ type: 'succeed', choice });
          view.selectedId = state.control?.survivorId ?? view.selectedId;
        });
        root.append(b);
      };
      const ready = heirs(state);
      for (const h of ready) choose(`${h.name} (resolve ${h.resolve})`, h.id, `${h.name} takes over: you play as them from here.`);
      if (ready.length === 0 && state.survivors.length > 0) {
        const none = el('div', 'hud-story-line');
        none.textContent = `Nobody left has the resolve to step up (${STEP_UP}+).`;
        root.append(none);
      }
      choose('Start anew, far from here', 'anew', 'A new survivor, alone, somewhere else in this world, with a weapon and a bandage.');
      if (state.survivors.length > 0) choose('Just watch the colony', 'none', 'Nobody under your control; the colony runs itself.');
    },
  };
}

/** The selected survivor's story so far, what they've asked of you, and a Talk button. */
function createStoryPanel(view: ViewState, command: (cmd: Command) => void) {
  const root = el('div', 'hud-wanderers');
  let last = '';
  return {
    root,
    update(state: SimState) {
      const s = state.survivors.find((x) => x.id === view.selectedId && x.id !== state.control?.survivorId);
      const told = s ? storyLines(s.story).slice(0, s.story.told) : [];
      const asks = s ? describeTasks(state, s) : [];
      const wish = s?.wish ? `${describeWish(state, s.wish)}|${s.wish.promised}|${Math.ceil((s.wish.until - state.tick) / TICKS_PER_HOUR)}` : '';
      const key = JSON.stringify([s?.id ?? null, told.length, asks, wish]);
      if (key === last) return;
      last = key;
      root.hidden = !s;
      root.replaceChildren();
      if (!s) return;
      const title = el('div', 'hud-wanderers-title');
      title.textContent = `${s.name}'s story`;
      root.append(title);
      for (const line of told.length > 0 ? told.map((l) => `"${l}"`) : ["You don't know much about them yet."]) {
        const p = el('div', 'hud-story-line');
        p.textContent = line;
        root.append(p);
      }
      for (const ask of asks) {
        const p = el('div', 'hud-story-ask');
        p.textContent = `${s.name} ${ask}`;
        root.append(p);
      }
      if (s.wish) {
        const row = el('div', 'hud-wanderer');
        const label = el('span', s.wish.promised ? 'hud-story-ask' : 'hud-wanderer-label');
        const hours = Math.ceil((s.wish.until - state.tick) / TICKS_PER_HOUR);
        label.textContent = `Wants ${describeWish(state, s.wish)} (${hours}h left${s.wish.promised ? ', promised' : ''})`;
        row.append(label);
        if (!s.wish.promised) {
          const promiseButton = el('button', 'hud-button');
          promiseButton.textContent = 'Promise';
          promiseButton.title = 'See it done for a bigger lift, and they think better of you. Let it run out and they are let down.';
          promiseButton.addEventListener('click', () => command({ type: 'promiseWish', survivorId: s.id }));
          row.append(promiseButton);
        }
        root.append(row);
      }
      const talkButton = el('button', 'hud-button');
      talkButton.textContent = `Talk to ${s.name}`;
      talkButton.title = 'Once every couple of hours. While you control someone, they need to be next to them (or press T).';
      talkButton.addEventListener('click', () => command({ type: 'talk', survivorId: s.id }));
      root.append(talkButton);
    },
  };
}

/** Prisoners in the cells: talk them round, or let them go. */
function createPrisonerPanel(command: (cmd: Command) => void) {
  const root = el('div', 'hud-wanderers');
  let last = '';
  return {
    root,
    update(state: SimState) {
      const rows = state.prisoners.map((p) => ({ p, ready: state.tick - p.since >= RECRUIT_DAYS * DAY_LENGTH }));
      const key = JSON.stringify(rows.map((r) => [r.p.id, r.ready]));
      if (key === last) return;
      last = key;
      root.hidden = rows.length === 0;
      root.replaceChildren();
      const title = el('div', 'hud-wanderers-title');
      title.textContent = 'Prisoners (each eats a food a day)';
      root.append(title);
      for (const { p, ready } of rows) {
        const row = el('div', 'hud-wanderer');
        const label = el('span', 'hud-wanderer-label');
        label.textContent = `${p.name} at (${p.x}, ${p.y})`;
        const recruit = el('button', 'hud-button');
        recruit.textContent = ready ? 'Recruit' : 'Recruit (not yet)';
        recruit.disabled = !ready;
        recruit.addEventListener('click', () => command({ type: 'recruitPrisoner', prisonerId: p.id }));
        const release = el('button', 'hud-button');
        release.textContent = 'Let go';
        release.addEventListener('click', () => command({ type: 'releasePrisoner', prisonerId: p.id }));
        row.append(label, recruit, release);
        root.append(row);
      }
    },
  };
}

/** Neighbouring settlements: what they think of you, and a gift button. */
/** " · allies: X · feuding: Y" for a settlement, or ''. */
function standing(state: SimState, n: Settlement): string {
  const others = state.settlements.filter((o) => o !== n && !isFallen(o));
  const allies = others.filter((o) => relation(n, o) === 'allies').map((o) => o.name);
  const feuds = others.filter((o) => relation(n, o) === 'feud').map((o) => o.name);
  return `${allies.length ? ` · allies: ${allies.join(', ')}` : ''}${feuds.length ? ` · feuding: ${feuds.join(', ')}` : ''}`;
}

function createSettlementPanel(command: (cmd: Command) => void) {
  const root = el('div', 'hud-wanderers');
  let last = '';
  return {
    root,
    update(state: SimState) {
      const key = JSON.stringify([state.settlements, state.requests, state.items.length, state.errands, dayOf(state.tick)]);
      if (key === last) return;
      last = key;
      root.hidden = state.settlements.length === 0;
      root.replaceChildren();
      const title = el('div', 'hud-wanderers-title');
      title.textContent = 'Neighbours';
      root.append(title);
      const food = state.items.filter((i) => i.kind === 'food' && i.pos !== null && i.reservedBy === null).length;
      for (const n of state.settlements) {
        const row = el('div', 'hud-wanderer');
        const label = el('span', 'hud-wanderer-label');
        const mood = n.reputation <= HOSTILE ? 'hostile' : n.reputation >= FRIENDLY ? 'friendly' : 'wary';
        label.textContent = isFallen(n)
          ? `${n.name} (${n.x}, ${n.y}): fallen, nobody left`
          : `${n.name} (${n.x}, ${n.y}): ${mood} ${n.reputation} · ${n.people} people, ${hungerOf(n)}, walls ${n.defence}/5${standing(state, n)}`;
        const gift = el('button', 'hud-button');
        gift.textContent = `Gift ${GIFT_FOOD} food`;
        gift.disabled = food < GIFT_FOOD;
        gift.addEventListener('click', () => command({ type: 'giftSettlement', settlementId: n.id }));
        const raid = el('button', 'hud-button');
        raid.textContent = 'Raid';
        raid.title = 'Send everyone armed to take their food. They will count you an enemy, win or lose.';
        raid.addEventListener('click', () => {
          if (window.confirm(`Raid ${n.name}? They will count you an enemy.`)) command({ type: 'raidSettlement', settlementId: n.id });
        });
        if (isFallen(n)) row.append(label);
        else row.append(label, gift, raid);
        root.append(row);
      }
      for (const e of errandsOf(state)) {
        const n = state.settlements.find((s) => s.id === e.settlementId);
        const days = Math.max(0, Math.ceil((e.until - state.tick) / DAY_LENGTH));
        const row = el('div', 'hud-wanderer');
        const text = el('span', 'hud-wanderer-label');
        text.textContent = `${n?.name ?? '?'} asks: ${e.kind === 'nest' ? 'clear the dead' : `find ${e.who}`} near (${e.x}, ${e.y}), ${days} day${days === 1 ? '' : 's'} left (on your map)`;
        row.append(text);
        root.append(row);
      }
      for (const r of state.requests) {
        const n = state.settlements.find((s) => s.id === r.settlementId);
        const have = state.items.filter((i) => i.kind === r.kind && i.pos !== null && i.reservedBy === null).length;
        const row = el('div', 'hud-wanderer');
        const label = el('span', 'hud-wanderer-label');
        label.textContent = `${n?.name ?? '?'} asks for ${r.count} ${r.kind} (you have ${have})`;
        const send = el('button', 'hud-button');
        send.textContent = 'Send';
        send.disabled = have < r.count;
        send.addEventListener('click', () => command({ type: 'fulfilRequest', requestId: r.id }));
        row.append(label, send);
        root.append(row);
      }
    },
  };
}

/** A trader's offers, each with a Trade button. */
function createTraderPanel(command: (cmd: Command) => void) {
  const root = el('div', 'hud-wanderers');
  let last = '';
  return {
    root,
    update(state: SimState) {
      const key = JSON.stringify([state.traders, state.items.length]);
      if (key === last) return;
      last = key;
      root.hidden = state.traders.length === 0;
      root.replaceChildren();
      for (const t of state.traders) {
        const title = el('div', 'hud-wanderers-title');
        const from = state.settlements.find((n) => n.id === t.from);
        title.textContent = `Trader${from ? ` from ${from.name}` : ''} at (${t.x}, ${t.y})`;
        root.append(title);
        t.offers.forEach((o, i) => {
          const row = el('div', 'hud-wanderer');
          const label = el('span', 'hud-wanderer-label');
          const have = state.items.filter((it) => it.kind === o.want && it.pos !== null && it.reservedBy === null).length;
          label.textContent = `${o.giveCount} ${o.give} for ${o.wantCount} ${o.want} (you have ${have})`;
          const go = el('button', 'hud-button');
          go.textContent = 'Trade';
          go.disabled = have < o.wantCount;
          go.addEventListener('click', () => command({ type: 'trade', traderId: t.id, offer: i }));
          row.append(label, go);
          root.append(row);
        });
      }
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
        const hold = el('button', 'hud-button');
        hold.textContent = 'Outpost';
        hold.title = 'Send them to stay there: supplies every dawn, at some risk';
        hold.disabled = !selected;
        hold.addEventListener('click', () => {
          if (selected) command({ type: 'sendExpedition', survivorIds: [selected.id], poiId: p.id, outpost: true });
        });
        row.append(label, send, hold);
        root.append(row);
      }
      for (const { x, hours } of away) {
        const row = el('div', 'hud-wanderer');
        const label = el('span', 'hud-wanderer-label');
        label.textContent = `${x.members.map((s) => s.name).join(', ')} → ${x.target.label} (${x.target.x}, ${x.target.y})${x.outpost ? ', holding it as an outpost' : `, back in about ${hours}h`}`;
        row.append(label);
        if (x.outpost) {
          const recall = el('button', 'hud-button');
          recall.textContent = 'Recall';
          recall.addEventListener('click', () => command({ type: 'recallOutpost', expeditionId: x.id }));
          row.append(recall);
        }
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
    for (const label of ['Survivor', 'Mood', 'Resolve', 'Social', 'Comfort', 'Shift', 'Order', ...JOB_TYPES.map((t) => JOB_LABELS[t]), 'Doing']) {
      const th = el('th', '');
      th.textContent = label;
      head.append(th);
    }
    table.append(head);

    for (const s of state.survivors) {
      const row = el('tr', s.id === view.selectedId ? 'selected' : '');
      const name = el('td', 'prio-name');
      name.textContent = s.name;
      name.title = [`Select · ${describeSkills(s)}`, describeClothing(state, s), describeRelations(state, s)].filter(Boolean).join(' · ');
      name.addEventListener('click', () => (view.selectedId = s.id));
      row.append(name);

      const m = mood(state, s);
      const moodCell = el('td', `prio-mood${isMiserable(state, s) ? ' low' : m >= 60 ? ' high' : ''}`);
      moodCell.textContent = String(m);
      moodCell.title = moodTitle(state, s);
      row.append(moodCell);

      const resolveCell = el('td', `prio-mood${isBreaking(s) ? ' low' : isSteeled(s) ? ' high' : ''}`);
      resolveCell.textContent = String(s.resolve);
      resolveCell.title = `${s.name} is ${describeResolve(s)}. Resolve drifts with mood each dawn (loneliness and poor sleep get in that way); requests seen through and promises kept lift it, deaths and broken promises cut it. Under ${BREAKING} they may walk away; at ${STEELED} or more they don't break down.`;
      row.append(resolveCell);

      for (const [value, about] of [
        [s.social, `Social: rises while near someone awake, and with every talk; sinks alone. Under ${LOW}: lonely (${LONELY_MOOD} mood).`],
        [s.comfort, `Comfort: set on waking by where they slept (a bed, indoors, a finished bedroom, warm, clean). Under ${LOW}: uncomfortable; over ${HIGH}: comfortable.`],
      ] as const) {
        const cell = el('td', `prio-mood${value < LOW ? ' low' : value > HIGH ? ' high' : ''}`);
        cell.textContent = String(value);
        cell.title = about;
        row.append(cell);
      }

      const shiftCell = el('td', '');
      const shift = el('button', 'prio-cell');
      shift.textContent = s.shift === 'night' ? '☾' : '☀';
      shift.title = `${s.name} works the ${s.shift} shift (sleeps ${s.shift === 'night' ? 'by day' : 'at night'}). Click to switch.`;
      shift.addEventListener('click', () => command({ type: 'setShift', survivorId: s.id, shift: s.shift === 'night' ? 'day' : 'night' }));
      shiftCell.append(shift);
      row.append(shiftCell);

      const orderCell = el('td', '');
      const order = el('button', 'prio-cell');
      order.textContent = !s.order ? '⌂' : s.order.kind === 'follow' ? '→' : s.order.kind === 'loot' ? '⚲' : '■';
      order.title = `${s.name} is ${describeOrder(s.order)}. Click: settle (colony jobs) → follow you → hold where they stand.`;
      const next = !s.order ? 'follow' : s.order.kind === 'follow' ? 'hold' : 'settle';
      order.addEventListener('click', () => command({ type: 'setOrder', survivorId: s.id, order: next }));
      orderCell.append(order);
      row.append(orderCell);

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
      const key = JSON.stringify([view.selectedId, state.survivors.map((s) => [s.id, s.name, s.resolve, s.social, s.comfort, s.shift, s.order, s.priorities, activity(state, s), moodTitle(state, s)])]);
      if (key === lastKey) return;
      lastKey = key;
      rebuild(state);
    },
  };
}

// --- Debug panel ------------------------------------------------------------

/** Frame costs from app.ts, smoothed (ms). */
export interface Perf {
  tickMs: number;
  simMs: number;
  drawMs: number;
  worstTickMs: number;
}

function debugText(state: SimState, view: ViewState, fps: number, perf?: Perf): string {
  const problems = checkInvariants(state);
  const open = state.jobs.filter((j) => !j.reservedBy).length;
  return [
    `fps            ${fps.toFixed(0)}`,
    ...(perf ? [`tick ms        ${perf.tickMs.toFixed(2)} (worst lately ${perf.worstTickMs.toFixed(1)})`, `sim ms/frame   ${perf.simMs.toFixed(2)}`, `draw ms/frame  ${perf.drawMs.toFixed(2)}`, `zombies/people ${state.zombies.length}/${state.survivors.length + state.residents.length}`] : []),
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
    `    ${moodTitle(state, s)}  ·  ${describeSkills(s)}  ·  wearing ${describeClothing(state, s) || 'nothing much'}`,
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
