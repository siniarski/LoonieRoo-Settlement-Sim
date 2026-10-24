// Mouse and keyboard -> view changes and sim commands.
//
//   Left drag      paint with the current tool
//   Right/middle   drag to pan
//   Wheel          zoom at the cursor
//   WASD / arrows  pan
//   1 2 3          wall, door, clear
//   Space          pause / resume
//   `              toggle debug panel
//   Home           recentre

import { centredOn, panByScreen, screenToTile, zoomAt, type Viewport } from '../render/camera';
import type { Command } from '../sim/commands';
import type { Point } from '../sim/grid';
import { TOOLS, getTool, tilesOnLine } from './tools';
import type { ViewState } from './view';

/** Screen pixels per second for keyboard panning. */
const KEY_PAN_PX_PER_SEC = 600;

const PAN_KEYS: Record<string, Point> = {
  w: { x: 0, y: -1 },
  a: { x: -1, y: 0 },
  s: { x: 0, y: 1 },
  d: { x: 1, y: 0 },
  ArrowUp: { x: 0, y: -1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowDown: { x: 0, y: 1 },
  ArrowRight: { x: 1, y: 0 },
};

export interface InputContext {
  canvas: HTMLCanvasElement;
  view: ViewState;
  viewport(): Viewport;
  mapSize(): { width: number; height: number };
  command(cmd: Command): void;
}

export interface Input {
  /** Applies held-key panning. Call once per frame. */
  update(frameMs: number): void;
  dispose(): void;
}

export function attachInput(ctx: InputContext): Input {
  const { canvas, view } = ctx;
  const held = new Set<string>();
  let painting: Point | null = null;
  let panning: Point | null = null;

  const screenPoint = (e: MouseEvent): Point => {
    const rect = canvas.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  };
  const tileAt = (p: Point) => screenToTile(view.camera, ctx.viewport(), p.x, p.y);

  const paint = (tile: Point) => {
    const { width, height } = ctx.mapSize();
    if (tile.x < 0 || tile.y < 0 || tile.x >= width || tile.y >= height) return;
    ctx.command(getTool(view.tool).command(tile.x, tile.y));
  };

  const onPointerDown = (e: PointerEvent) => {
    try {
      // Keeps drags tracking outside the canvas. Throws for synthetic events
      // (e.g. Playwright) whose pointerId isn't active; that's fine to ignore.
      canvas.setPointerCapture(e.pointerId);
    } catch {
      /* no capture */
    }
    const p = screenPoint(e);
    if (e.button === 0) {
      painting = tileAt(p);
      paint(painting);
    } else if (e.button === 1 || e.button === 2) {
      panning = p;
    }
  };

  const onPointerMove = (e: PointerEvent) => {
    const p = screenPoint(e);
    const tile = tileAt(p);
    view.hover = tile;
    if (painting && (tile.x !== painting.x || tile.y !== painting.y)) {
      // Skip the first point: it was painted on the previous event.
      tilesOnLine(painting, tile).slice(1).forEach(paint);
      painting = tile;
    }
    if (panning) {
      view.camera = panByScreen(view.camera, p.x - panning.x, p.y - panning.y);
      panning = p;
      view.hover = tileAt(p);
    }
  };

  const onPointerUp = (e: PointerEvent) => {
    if (canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId);
    painting = null;
    panning = null;
  };

  const onPointerLeave = () => {
    if (!painting && !panning) view.hover = null;
  };

  const onWheel = (e: WheelEvent) => {
    e.preventDefault();
    const p = screenPoint(e);
    view.camera = zoomAt(view.camera, ctx.viewport(), p.x, p.y, Math.exp(-e.deltaY * 0.0015));
    view.hover = tileAt(p);
  };

  const onContextMenu = (e: Event) => e.preventDefault();

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.target instanceof HTMLInputElement) return;
    const key = e.key.length === 1 ? e.key.toLowerCase() : e.key;
    const tool = TOOLS.find((t) => t.key === key);
    if (tool) {
      view.tool = tool.id;
    } else if (key === ' ') {
      view.paused = !view.paused;
      e.preventDefault();
    } else if (key === '`') {
      view.showDebug = !view.showDebug;
    } else if (key === 'Home') {
      const { width, height } = ctx.mapSize();
      view.camera = centredOn(width, height, view.camera.zoom);
    } else if (key in PAN_KEYS) {
      held.add(key);
      e.preventDefault();
    }
  };

  const onKeyUp = (e: KeyboardEvent) => {
    held.delete(e.key.length === 1 ? e.key.toLowerCase() : e.key);
  };

  const onBlur = () => held.clear();

  canvas.addEventListener('pointerdown', onPointerDown);
  canvas.addEventListener('pointermove', onPointerMove);
  canvas.addEventListener('pointerup', onPointerUp);
  canvas.addEventListener('pointercancel', onPointerUp);
  canvas.addEventListener('pointerleave', onPointerLeave);
  canvas.addEventListener('wheel', onWheel, { passive: false });
  canvas.addEventListener('contextmenu', onContextMenu);
  window.addEventListener('keydown', onKeyDown);
  window.addEventListener('keyup', onKeyUp);
  window.addEventListener('blur', onBlur);

  return {
    update(frameMs) {
      if (held.size === 0) return;
      let dx = 0;
      let dy = 0;
      for (const key of held) {
        const dir = PAN_KEYS[key];
        if (dir) {
          dx += dir.x;
          dy += dir.y;
        }
      }
      const distance = (KEY_PAN_PX_PER_SEC * frameMs) / 1000;
      // panByScreen moves the world with the drag; keys move the camera, so negate.
      view.camera = panByScreen(view.camera, -dx * distance, -dy * distance);
    },
    dispose() {
      canvas.removeEventListener('pointerdown', onPointerDown);
      canvas.removeEventListener('pointermove', onPointerMove);
      canvas.removeEventListener('pointerup', onPointerUp);
      canvas.removeEventListener('pointercancel', onPointerUp);
      canvas.removeEventListener('pointerleave', onPointerLeave);
      canvas.removeEventListener('wheel', onWheel);
      canvas.removeEventListener('contextmenu', onContextMenu);
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('keyup', onKeyUp);
      window.removeEventListener('blur', onBlur);
    },
  };
}
