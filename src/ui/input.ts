// Mouse and keyboard -> view changes and sim commands.
//
// Select mode (default):
//   Left click     select a survivor (click empty ground to deselect)
//   Right click    move the selected survivor there
// Build mode (B opens the build bar):
//   1-9            arm a tool from the open category; Tab/E next category, Shift+Tab/Q previous
//   Left drag      paint with the armed tool (designations for survivors to work on);
//                  area tools (Scavenge) drag out a box instead, applied on release
//   Shift + drag   wall/door/bed/remove happen instantly (sandbox/debug)
//   Right click    put the tool down
//   Esc            put the tool down, then close build mode, then deselect
// Direct control (F takes over the selected survivor, F again hands them back):
//   WASD/arrows  walk (8 ways) · Left click  swing at / shoot towards a tile
//   Right click  use a tile next to you (pick up, wear, eat, search, drink, sleep) · E  use what's in front,
//                else underfoot (on a bed, or bare floor: sleep)
//   V  shout "follow me" to everyone near · H  shout "hold here" (each where they stand) · T  talk to whoever's nearest
//   L  shout "loot that room" (the one under the mouse)
// Always:
//   Right/middle drag  pan · Wheel  zoom at the cursor · WASD/arrows  pan
//   P priorities · M minimap · Space pause · ` debug panel · Home recentre

import { centredOn, panByScreen, screenToTile, zoomAt, type Viewport } from '../render/camera';
import type { Command } from '../sim/commands';
import type { Control } from '../sim/control';
import type { Point } from '../sim/grid';
import { disarm, handleBuildKey } from './buildMode';
import { getTool, tilesInBox, tilesOnLine } from './tools';
import type { ViewState } from './view';

/** Screen pixels per second for keyboard panning. */
const KEY_PAN_PX_PER_SEC = 600;
/** A right-button press that moves less than this is a click, not a pan. */
const CLICK_SLOP_PX = 4;

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
  /** Id of the survivor on (or stepping onto) a tile, if any. */
  survivorAt(tile: Point): string | null;
  /** Id of the bed on a tile, if any. */
  bedAt(tile: Point): string | null;
  /** Applies a command; true if it was accepted. */
  command(cmd: Command): boolean;
  /** The direct-control state, if the player is steering someone. */
  control(): Control | null;
  /** Where the controlled survivor stands. */
  controlledPos(): Point | null;
  /** Ask the auto-planner to furnish the room at this tile. */
  furnish(tile: Point): void;
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
  /** Shift state captured when the drag started, so a drag doesn't change mode halfway. */
  let paintInstant = false;
  let panning: Point | null = null;
  /** Right button: where it went down and how far it has travelled, to tell click from pan. */
  let rightPress: { start: Point; travelled: number } | null = null;

  const screenPoint = (e: MouseEvent): Point => {
    const rect = canvas.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  };
  const tileAt = (p: Point) => screenToTile(view.camera, ctx.viewport(), p.x, p.y);
  const onMap = (tile: Point) => {
    const { width, height } = ctx.mapSize();
    return tile.x >= 0 && tile.y >= 0 && tile.x < width && tile.y < height;
  };

  const apply = (tile: Point) => {
    if (!view.tool || !onMap(tile)) return;
    const tool = getTool(view.tool);
    if (tool.furnish) ctx.furnish(tile);
    else {
      const cmd = tool.command(tile.x, tile.y, { instant: paintInstant });
      if (cmd) ctx.command(cmd);
    }
  };

  const onLeftDown = (tile: Point, shift: boolean) => {
    if (view.tool && getTool(view.tool).area) {
      view.areaDrag = { from: tile, to: tile };
      return;
    }
    if (view.tool) {
      paintInstant = shift;
      apply(tile);
      if (getTool(view.tool).paints) painting = tile;
      return;
    }
    if (ctx.control()) {
      ctx.command({ type: 'swing', x: tile.x, y: tile.y });
      return;
    }
    // Select mode: pick a survivor; with someone picked, a click on a bed gives it to them;
    // anywhere else clears the selection.
    const who = ctx.survivorAt(tile);
    const bed = who ? null : ctx.bedAt(tile);
    if (!who && bed && view.selectedId) ctx.command({ type: 'assignBed', bedId: bed, survivorId: view.selectedId });
    else view.selectedId = who;
  };

  const onRightClick = (tile: Point) => {
    if (view.tool) {
      disarm(view);
      return;
    }
    if (ctx.control()) {
      ctx.command({ type: 'use', x: tile.x, y: tile.y });
      return;
    }
    if (view.selectedId && onMap(tile)) {
      ctx.command({ type: 'moveSurvivor', survivorId: view.selectedId, x: tile.x, y: tile.y });
    }
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
      onLeftDown(tileAt(p), e.shiftKey);
    } else if (e.button === 1 || e.button === 2) {
      panning = p;
      if (e.button === 2) rightPress = { start: p, travelled: 0 };
    }
  };

  const onPointerMove = (e: PointerEvent) => {
    const p = screenPoint(e);
    const tile = tileAt(p);
    view.hover = tile;
    if (view.areaDrag) view.areaDrag.to = tile;
    if (painting && (tile.x !== painting.x || tile.y !== painting.y)) {
      // Skip the first point: it was painted on the previous event.
      tilesOnLine(painting, tile).slice(1).forEach(apply);
      painting = tile;
    }
    if (panning) {
      if (rightPress) rightPress.travelled += Math.hypot(p.x - panning.x, p.y - panning.y);
      view.camera = panByScreen(view.camera, p.x - panning.x, p.y - panning.y);
      panning = p;
      view.hover = tileAt(p);
    }
  };

  const onPointerUp = (e: PointerEvent) => {
    if (canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId);
    if (e.button === 2 && rightPress && rightPress.travelled < CLICK_SLOP_PX) onRightClick(tileAt(rightPress.start));
    if (e.button === 0 && view.areaDrag) {
      const { from, to } = view.areaDrag;
      view.areaDrag = null;
      const tool = view.tool ? getTool(view.tool) : null;
      if (tool?.boxCommand) ctx.command(tool.boxCommand(from, to));
      else tilesInBox(from, to).forEach(apply);
    }
    painting = null;
    panning = null;
    rightPress = null;
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
    if (handleBuildKey(view, key, e.shiftKey)) {
      if (key === 'Tab') e.preventDefault(); // don't move browser focus
      if (!view.tool) painting = null;
      return;
    }
    if (key === ' ') {
      view.paused = !view.paused;
      e.preventDefault();
    } else if (key === '`') {
      view.showDebug = !view.showDebug;
    } else if (key === 'p') {
      view.showPriorities = !view.showPriorities;
    } else if (key === 'm') {
      view.showMinimap = !view.showMinimap;
    } else if (key === 'o') {
      view.showOverview = !view.showOverview;
    } else if (key === 'f') {
      if (ctx.control()) ctx.command({ type: 'takeControl', survivorId: null });
      else if (view.selectedId) ctx.command({ type: 'takeControl', survivorId: view.selectedId });
    } else if (key === 'l' && ctx.control() && view.hover) {
      ctx.command({ type: 'shout', order: 'loot', x: view.hover.x, y: view.hover.y });
    } else if (key === 't' && ctx.control()) {
      ctx.command({ type: 'talk' });
    } else if ((key === 'v' || key === 'h') && ctx.control()) {
      ctx.command({ type: 'shout', order: key === 'v' ? 'follow' : 'hold' });
    } else if (key === 'e' && ctx.control()) {
      const c = ctx.control()!;
      const at = ctx.controlledPos()!;
      // The tile in front, else the one underfoot.
      const front = { type: 'use', x: at.x + c.facing.x, y: at.y + c.facing.y } as const;
      if (!ctx.command(front)) ctx.command({ type: 'use', x: at.x, y: at.y });
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
      let dx = 0;
      let dy = 0;
      for (const key of held) {
        const dir = PAN_KEYS[key];
        if (dir) {
          dx += dir.x;
          dy += dir.y;
        }
      }
      // Under direct control the keys walk, and the sim only hears about it when they change.
      const c = ctx.control();
      if (c) {
        dx = Math.sign(dx);
        dy = Math.sign(dy);
        if (c.dx !== dx || c.dy !== dy) ctx.command({ type: 'steer', dx, dy });
        return;
      }
      if (held.size === 0) return;
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
