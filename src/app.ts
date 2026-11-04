// Wires sim, renderer, input, and HUD together and runs the frame loop.
// createApp returns a dispose() so hot reload can tear it down cleanly.

import { createMenu, type MenuActions } from './ui/menu';
import { newGamePlus } from './sim/escape';
import { installGameHooks, type GameHandle } from './debug/hooks';
import { centredOn, worldToScreen, type Viewport } from './render/camera';
import { draw } from './render/draw';
import { applyCommand } from './sim/commands';
import { planFurnish } from './sim/planner';
import type { Point } from './sim/grid';
import { loadFile, toBugReport, toSaveFile, type LoadResult } from './sim/savefile';
import { cloneState, type SimState } from './sim/state';
import { DAY_LENGTH, dayOf, setDayScale } from './sim/time';
import { createWorld } from './sim/worldgen';
import { AUTO_SLOT, downloadText, fileStamp, loadFromSlot, pickTextFile, saveToSlot } from './ui/saves';
import { survivorAt } from './sim/survivors';
import { furnitureAt } from './sim/furniture';
import { controlled } from './sim/control';
import { worldPos } from './render/interp';
import { step } from './sim/tick';
import { createHud, type GameFiles } from './ui/hud';
import { attachInput } from './ui/input';
import { MS_PER_TICK, ticksForFrame } from './ui/loop';
import { isCategoryId, isToolId } from './ui/tools';
import type { ViewState } from './ui/view';

/** View-side helpers for agents and Playwright (screen coords are CSS px, page-relative). */
export interface ViewHooks {
  getView(): ViewState;
  tileToScreen(x: number, y: number): Point;
}

declare global {
  interface Window {
    view: ViewHooks;
  }
}

/** How long planner messages stay up. */
const TOAST_MS = 6000;

export interface App {
  handle: GameHandle;
  view: ViewState;
  dispose(): void;
}

export function createApp(canvas: HTMLCanvasElement, hudRoot: HTMLElement, state: SimState, saved?: Partial<ViewState>): App {
  const ctx2d = canvas.getContext('2d');
  if (!ctx2d) throw new Error('2D canvas not supported');

  const handle: GameHandle = { state, initial: cloneState(state) };
  setDayScale(state.settings.dayScale);
  const view: ViewState = {
    camera: centredOn(state.grid.width, state.grid.height),
    buildOpen: false,
    buildCategory: 'structure',
    tool: null,
    selectedId: null,
    areaDrag: null,
    toast: null,
    hover: null,
    speed: 1,
    paused: false,
    showDebug: false,
    showPriorities: true,
    menu: null,
    inGame: true,
    menuCustomOpen: false,
    showMinimap: true,
    showOverview: false,
    // Spread over the defaults so view state saved by older code gains new fields.
    ...saved,
  };
  // View state saved by older code may name tools/categories that no longer exist.
  if (!isToolId(view.tool) || !view.buildOpen) view.tool = null;
  if (!isCategoryId(view.buildCategory)) view.buildCategory = 'structure';
  /** Drop the selection if that survivor no longer exists (e.g. after loading a scenario). */
  const ensureSelection = () => {
    if (view.selectedId && !handle.state.survivors.some((s) => s.id === view.selectedId)) view.selectedId = null;
  };

  let viewport: Viewport = { width: 0, height: 0 };
  const resize = () => {
    const dpr = window.devicePixelRatio || 1;
    viewport = { width: canvas.clientWidth, height: canvas.clientHeight };
    canvas.width = Math.round(viewport.width * dpr);
    canvas.height = Math.round(viewport.height * dpr);
    ctx2d.setTransform(dpr, 0, 0, dpr, 0, 0);
  };
  const observer = new ResizeObserver(resize);
  observer.observe(canvas);
  resize();

  installGameHooks(handle, {
    onLoad: () => (view.camera = centredOn(handle.state.grid.width, handle.state.grid.height, view.camera.zoom)),
  });
  window.view = {
    getView: () => structuredClone(view),
    tileToScreen: (x, y) => {
      const p = worldToScreen(view.camera, viewport, x + 0.5, y + 0.5);
      const rect = canvas.getBoundingClientRect();
      return { x: p.x + rect.left, y: p.y + rect.top };
    },
  };

  const input = attachInput({
    canvas,
    view,
    viewport: () => viewport,
    mapSize: () => handle.state.grid,
    survivorAt: (tile) => survivorAt(handle.state, tile.x, tile.y)?.id ?? null,
    bedAt: (tile) => {
      const f = furnitureAt(handle.state, tile.x, tile.y);
      return f?.kind === 'bed' ? f.id : null;
    },
    command: (cmd) => {
      const result = applyCommand(handle.state, cmd);
      if (cmd.type === 'takeControl' && result.ok) view.selectedId = cmd.survivorId ?? view.selectedId;
      return result.ok;
    },
    control: () => handle.state.control,
    controlledPos: () => controlled(handle.state)?.pos ?? null,
    toggleMenu: () => toggleMenu(),
    furnish: (tile) => {
      // The planner only proposes; its commands go through the normal command path.
      const plan = planFurnish(handle.state, tile.x, tile.y);
      for (const cmd of plan.commands) applyCommand(handle.state, cmd);
      toast(plan.notes.join(' '));
    },
  });
  const toast = (text: string) => (view.toast = { text, until: performance.now() + TOAST_MS });
  /** Replace the running game (a save, an import, a bug report) and start the bug-report log afresh. Returns whether it did. */
  const replaceState = (result: LoadResult, from: string): boolean => {
    if (!result.ok) {
      toast(`Couldn't load ${from}: ${result.error}`);
      return false;
    }
    handle.state = result.state;
    handle.initial = cloneState(result.state);
    setDayScale(handle.state.settings.dayScale); // the clock reads right while paused
    view.camera = centredOn(handle.state.grid.width, handle.state.grid.height, view.camera.zoom);
    view.paused = true; // look before you play
    lastAutosaveDawn = dawns(handle.state.tick);
    toast(`Loaded ${from}${result.kind === 'bug' ? ' (bug report, replayed)' : ''}. Paused: Space to play.`);
    view.selectedId = handle.state.control?.survivorId ?? null;
    return true;
  };
  /** Into the game, off the menu. */
  const play = () => {
    view.menu = null;
    view.inGame = true;
  };
  let pausedUnderMenu = false;
  const toggleMenu = () => {
    if (view.menu === null) {
      pausedUnderMenu = view.paused;
      view.paused = true;
      view.menu = 'main';
    } else if (view.inGame) {
      view.menu = null;
      view.paused = pausedUnderMenu;
    }
  };
  const menuActions: MenuActions = {
    resume: toggleMenu,
    newGame: (size, settings, seed) => {
      const world = createWorld(seed ?? Math.floor(Math.random() * 1e9), size, settings);
      if (!replaceState({ ok: true, state: world, kind: 'save' }, `a new ${size}×${size} world`)) return;
      play();
      if (settings.start === 'solo') toast('Alone. WASD to walk, click to swing, right-click or E to use. Space to play.');
    },
    load: (slot) => {
      if (replaceState(loadFromSlot(slot), slot === AUTO_SLOT ? 'the autosave' : `slot ${slot}`)) play();
    },
    importFile: () => {
      void pickTextFile().then((text) => {
        if (text !== null && replaceState(loadFile(text), 'the file')) play();
      });
    },
    save: (slot) => toast(saveToSlot(slot, handle.state) ?? `Saved to slot ${slot}.`),
    exportSave: () => downloadText(`loonieroo-${fileStamp(handle.state)}.json`, toSaveFile(handle.state)),
    reportBug: () => {
      const note = window.prompt('What went wrong? (optional)') ?? '';
      downloadText(`loonieroo-bug-${fileStamp(handle.state)}.json`, toBugReport(handle.initial, handle.state, note));
      toast('Bug report downloaded: it replays the game from where you started to now.');
    },
  };
  const files: GameFiles = {
    openMenu: toggleMenu,
    newGamePlus: () => {
      if (!window.confirm('Everyone flies out: start again in a new world, alone, with what you carry?')) return;
      replaceState({ ok: true, state: newGamePlus(handle.state, Math.floor(Math.random() * 1e9)), kind: 'save' }, 'a new world (new game+)');
      view.selectedId = handle.state.control?.survivorId ?? null;
    },
  };
  const hud = createHud(hudRoot, view, (cmd) => applyCommand(handle.state, cmd), files);
  const menu = createMenu(hudRoot.parentElement ?? document.body, view, menuActions);
  // Autosave at every dawn (06:00), when the night's zombies have gone.
  const dawns = (tick: number) => Math.floor(tick / DAY_LENGTH); // tick 0 is 06:00
  let lastAutosaveDawn = dawns(handle.state.tick);

  let accumulatorMs = 0;
  let last = performance.now();
  let fps = 0;
  /** Smoothed costs for the debug panel (ms): one sim tick, the frame's ticks, and drawing. */
  const perf = { tickMs: 0, simMs: 0, drawMs: 0, worstTickMs: 0 };
  const reported = new Set<string>();
  let frameId = requestAnimationFrame(function frame(now) {
    // Schedule first, so one bad frame (often a half-finished edit during
    // hot reload) can't kill the loop. Each distinct error is logged once.
    frameId = requestAnimationFrame(frame);
    const frameMs = now - last;
    last = now;
    fps = fps * 0.9 + (1000 / Math.max(frameMs, 1)) * 0.1;
    try {
      const result = ticksForFrame(accumulatorMs, frameMs, view.speed, view.paused);
      accumulatorMs = result.accumulatorMs;
      const simStart = performance.now();
      step(handle.state, result.ticks);
      const simMs = performance.now() - simStart;
      perf.simMs = perf.simMs * 0.9 + simMs * 0.1;
      if (result.ticks > 0) {
        const per = simMs / result.ticks;
        perf.tickMs = perf.tickMs * 0.9 + per * 0.1;
        perf.worstTickMs = Math.max(perf.worstTickMs * 0.999, per);
      }
      if (dawns(handle.state.tick) !== lastAutosaveDawn) {
        lastAutosaveDawn = dawns(handle.state.tick);
        if (saveToSlot(AUTO_SLOT, handle.state) === null) toast(`Autosaved at dawn, day ${dayOf(handle.state.tick)}.`);
      }

      input.update(frameMs);
      ensureSelection();
      const alpha = Math.min(1, accumulatorMs / MS_PER_TICK);
      // The camera follows whoever you're steering.
      const you = controlled(handle.state);
      if (you) view.camera = { ...view.camera, ...worldPos(you, alpha) };
      const drawStart = performance.now();
      draw(ctx2d, handle.state, view.camera, viewport, {
        hover: view.hover,
        tool: view.tool,
        alpha,
        selectedId: view.selectedId,
        showDebug: view.showDebug,
        areaDrag: view.areaDrag,
      });
      perf.drawMs = perf.drawMs * 0.9 + (performance.now() - drawStart) * 0.1;
      hud.update(handle.state, fps, perf);
      menu.update();
    } catch (err) {
      const key = String(err);
      if (!reported.has(key)) {
        reported.add(key);
        console.error('[frame]', err);
      }
    }
  });

  return {
    handle,
    view,
    dispose() {
      cancelAnimationFrame(frameId);
      observer.disconnect();
      input.dispose();
      hud.dispose();
      menu.dispose();
    },
  };
}
