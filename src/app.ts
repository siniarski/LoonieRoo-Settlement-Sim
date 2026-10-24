// Wires sim, renderer, input, and HUD together and runs the frame loop.
// createApp returns a dispose() so hot reload can tear it down cleanly.

import { installGameHooks, type GameHandle } from './debug/hooks';
import { centredOn, worldToScreen, type Viewport } from './render/camera';
import { draw } from './render/draw';
import { applyCommand } from './sim/commands';
import type { Point } from './sim/grid';
import type { SimState } from './sim/state';
import { step } from './sim/tick';
import { createHud } from './ui/hud';
import { attachInput } from './ui/input';
import { ticksForFrame } from './ui/loop';
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
  const view: ViewState = saved ?? {
    camera: centredOn(state.grid.width, state.grid.height),
    tool: 'wall',
    hover: null,
    speed: 1,
    paused: false,
    showDebug: false,
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
    command: (cmd) => applyCommand(handle.state, cmd),
  });
  const hud = createHud(hudRoot, view);

  let accumulatorMs = 0;
  let last = performance.now();
  let fps = 0;
  let frameId = requestAnimationFrame(function frame(now) {
    const frameMs = now - last;
    last = now;
    fps = fps * 0.9 + (1000 / Math.max(frameMs, 1)) * 0.1;

    const result = ticksForFrame(accumulatorMs, frameMs, view.speed, view.paused);
    accumulatorMs = result.accumulatorMs;
    step(handle.state, result.ticks);

    input.update(frameMs);
    draw(ctx2d, handle.state, view.camera, viewport, { hover: view.hover, tool: view.tool });
    hud.update(handle.state, fps);
    frameId = requestAnimationFrame(frame);
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
