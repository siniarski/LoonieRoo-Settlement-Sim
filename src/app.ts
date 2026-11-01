// Wires sim, renderer, input, and HUD together and runs the frame loop.
// createApp returns a dispose() so hot reload can tear it down cleanly.

import { installGameHooks, type GameHandle } from './debug/hooks';
import { centredOn, worldToScreen, type Viewport } from './render/camera';
import { draw } from './render/draw';
import { applyCommand } from './sim/commands';
import { planFurnish } from './sim/planner';
import type { Point } from './sim/grid';
import { loadFile, toBugReport, toSaveFile, type LoadResult } from './sim/savefile';
import { cloneState, type SimState } from './sim/state';
import { DAY_LENGTH, dayOf } from './sim/time';
import { createWorld } from './sim/worldgen';
import { AUTO_SLOT, downloadText, fileStamp, loadFromSlot, pickTextFile, saveToSlot } from './ui/saves';
import { survivorAt } from './sim/survivors';
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

export function createApp(canvas: HTMLCanvasElement, hudRoot: HTMLElement, state: SimState, saved?: ViewState): App {
  const ctx2d = canvas.getContext('2d');
  if (!ctx2d) throw new Error('2D canvas not supported');

  const handle: GameHandle = { state, initial: cloneState(state) };
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
    showGame: false,
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
    command: (cmd) => applyCommand(handle.state, cmd),
    furnish: (tile) => {
      // The planner only proposes; its commands go through the normal command path.
      const plan = planFurnish(handle.state, tile.x, tile.y);
      for (const cmd of plan.commands) applyCommand(handle.state, cmd);
      toast(plan.notes.join(' '));
    },
  });
  const toast = (text: string) => (view.toast = { text, until: performance.now() + TOAST_MS });
  /** Replace the running game (a save, an import, a bug report) and start the bug-report log afresh. */
  const replaceState = (result: LoadResult, from: string) => {
    if (!result.ok) return toast(`Couldn't load ${from}: ${result.error}`);
    handle.state = result.state;
    handle.initial = cloneState(result.state);
    view.camera = centredOn(handle.state.grid.width, handle.state.grid.height, view.camera.zoom);
    view.paused = true; // look before you play
    lastAutosaveDawn = dawns(handle.state.tick);
    toast(`Loaded ${from}${result.kind === 'bug' ? ' (bug report, replayed)' : ''}. Paused: Space to play.`);
  };
  const files: GameFiles = {
    save: (slot) => toast(saveToSlot(slot, handle.state) ?? `Saved to slot ${slot}.`),
    load: (slot) => replaceState(loadFromSlot(slot), slot === AUTO_SLOT ? 'the autosave' : `slot ${slot}`),
    exportSave: () => downloadText(`loonieroo-${fileStamp(handle.state)}.json`, toSaveFile(handle.state)),
    importFile: () => {
      void pickTextFile().then((text) => {
        if (text !== null) replaceState(loadFile(text), 'the file');
      });
    },
    newWorld: (size) => {
      if (!window.confirm(`Start a new ${size}×${size} world? Unsaved progress here is lost.`)) return;
      replaceState({ ok: true, state: createWorld(Math.floor(Math.random() * 1e9), size), kind: 'save' }, `a new ${size}×${size} world`);
    },
    reportBug: () => {
      const note = window.prompt('What went wrong? (optional)') ?? '';
      downloadText(`loonieroo-bug-${fileStamp(handle.state)}.json`, toBugReport(handle.initial, handle.state, note));
      toast('Bug report downloaded: it replays the game from where you started to now.');
    },
  };
  const hud = createHud(hudRoot, view, (cmd) => applyCommand(handle.state, cmd), files);
  // Autosave at every dawn (06:00), when the night's zombies have gone.
  const dawns = (tick: number) => Math.floor(tick / DAY_LENGTH); // tick 0 is 06:00
  let lastAutosaveDawn = dawns(handle.state.tick);

  let accumulatorMs = 0;
  let last = performance.now();
  let fps = 0;
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
      step(handle.state, result.ticks);
      if (dawns(handle.state.tick) !== lastAutosaveDawn) {
        lastAutosaveDawn = dawns(handle.state.tick);
        if (saveToSlot(AUTO_SLOT, handle.state) === null) toast(`Autosaved at dawn, day ${dayOf(handle.state.tick)}.`);
      }

      input.update(frameMs);
      ensureSelection();
      draw(ctx2d, handle.state, view.camera, viewport, {
        hover: view.hover,
        tool: view.tool,
        alpha: Math.min(1, accumulatorMs / MS_PER_TICK),
        selectedId: view.selectedId,
        showDebug: view.showDebug,
        areaDrag: view.areaDrag,
      });
      hud.update(handle.state, fps);
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
    },
  };
}
