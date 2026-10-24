// Camera maths: world (tile) coordinates <-> screen (CSS pixel) coordinates.
// This is the only place the top-down projection lives; an isometric view
// would swap these functions. Pure functions, no DOM.

import type { Point } from '../sim/grid';

/** Screen pixels per tile at zoom 1. */
export const TILE_PX = 24;
export const MIN_ZOOM = 0.25;
export const MAX_ZOOM = 4;

export interface Camera {
  /** World position (in tiles) shown at the centre of the viewport. */
  x: number;
  y: number;
  zoom: number;
}

export interface Viewport {
  /** CSS pixels. */
  width: number;
  height: number;
}

export function tileSize(camera: Camera): number {
  return TILE_PX * camera.zoom;
}

export function centredOn(width: number, height: number, zoom = 1): Camera {
  return { x: width / 2, y: height / 2, zoom };
}

export function worldToScreen(camera: Camera, viewport: Viewport, wx: number, wy: number): Point {
  const size = tileSize(camera);
  return {
    x: (wx - camera.x) * size + viewport.width / 2,
    y: (wy - camera.y) * size + viewport.height / 2,
  };
}

export function screenToWorld(camera: Camera, viewport: Viewport, sx: number, sy: number): Point {
  const size = tileSize(camera);
  return {
    x: (sx - viewport.width / 2) / size + camera.x,
    y: (sy - viewport.height / 2) / size + camera.y,
  };
}

/** The integer tile under a screen point (may be out of the map's bounds). */
export function screenToTile(camera: Camera, viewport: Viewport, sx: number, sy: number): Point {
  const world = screenToWorld(camera, viewport, sx, sy);
  return { x: Math.floor(world.x), y: Math.floor(world.y) };
}

/** Moves the camera so the world follows a screen-space drag of (dx, dy). */
export function panByScreen(camera: Camera, dx: number, dy: number): Camera {
  const size = tileSize(camera);
  return { ...camera, x: camera.x - dx / size, y: camera.y - dy / size };
}

/** Zooms by `factor`, keeping the world point under (sx, sy) fixed on screen. */
export function zoomAt(camera: Camera, viewport: Viewport, sx: number, sy: number, factor: number): Camera {
  const zoom = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, camera.zoom * factor));
  const anchor = screenToWorld(camera, viewport, sx, sy);
  const size = TILE_PX * zoom;
  return {
    zoom,
    x: anchor.x - (sx - viewport.width / 2) / size,
    y: anchor.y - (sy - viewport.height / 2) / size,
  };
}

/** Inclusive range of tiles visible in the viewport, clamped to the map. */
export function visibleTiles(
  camera: Camera,
  viewport: Viewport,
  mapWidth: number,
  mapHeight: number,
): { x0: number; y0: number; x1: number; y1: number } {
  const topLeft = screenToTile(camera, viewport, 0, 0);
  const bottomRight = screenToTile(camera, viewport, viewport.width, viewport.height);
  return {
    x0: Math.max(0, topLeft.x),
    y0: Math.max(0, topLeft.y),
    x1: Math.min(mapWidth - 1, bottomRight.x),
    y1: Math.min(mapHeight - 1, bottomRight.y),
  };
}
