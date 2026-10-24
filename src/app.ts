// Wires sim, renderer, input, and HUD together and runs the frame loop.
// createApp returns a dispose() so hot reload can tear it down cleanly.

import { installGameHooks, type GameHandle } from './debug/hooks';
import { centredOn, worldToScreen, type Viewport } from './render/camera';
import { draw } from './render/draw';
import { applyCommand } from './sim/commands';
import type { Point } from './sim/grid';
import type { SimState } from './sim/state';
import { survivorAt } from './sim/survivors';
import { step } from './sim/tick';
import { createHud } from './ui/hud';
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

export interface App {
  handle: GameHandle;
  view: ViewState;
  dispose(): void;
}

export function createApp(canvas: HTMLCanvasElement, hudRoot: HTMLElement, state: SimState, saved?: ViewState): App {
  const ctx2d = canvas.getContext('2d');
  if (!ctx2d) throw new Error('2D canvas not supported');

  const handle: GameHandle = { state };
  const view: ViewState = {
    camera: centredOn(state.grid.width, state.grid.height),
    buildOpen: false,
    buildCategory: 'structure',
    tool: null,
    selectedId: null,
    areaDrag: null,
    hover: null,
    speed: 1,
    paused: false,
    showDebug: false,
    showPriorities: true,
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
  });
  const hud = createHud(hudRoot, view, (cmd) => applyCommand(handle.state, cmd));

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
