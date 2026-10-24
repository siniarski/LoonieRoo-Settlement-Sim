import { describe, expect, it } from 'vitest';
import {
  MAX_ZOOM,
  MIN_ZOOM,
  TILE_PX,
  centredOn,
  panByScreen,
  screenToTile,
  screenToWorld,
  visibleTiles,
  worldToScreen,
  zoomAt,
} from '../src/render/camera';

const viewport = { width: 800, height: 600 };

describe('camera', () => {
  it('puts the camera position at the viewport centre', () => {
    const camera = { x: 10, y: 5, zoom: 1 };
    expect(worldToScreen(camera, viewport, 10, 5)).toEqual({ x: 400, y: 300 });
    expect(worldToScreen(camera, viewport, 11, 5)).toEqual({ x: 400 + TILE_PX, y: 300 });
  });

  it('round-trips world and screen coordinates', () => {
    const camera = { x: 3.25, y: -2, zoom: 1.7 };
    const screen = worldToScreen(camera, viewport, 12.5, 7.75);
    const world = screenToWorld(camera, viewport, screen.x, screen.y);
    expect(world.x).toBeCloseTo(12.5);
    expect(world.y).toBeCloseTo(7.75);
  });

  it('floors to the tile under a screen point', () => {
    const camera = { x: 0, y: 0, zoom: 1 };
    expect(screenToTile(camera, viewport, 400, 300)).toEqual({ x: 0, y: 0 });
    expect(screenToTile(camera, viewport, 399, 299)).toEqual({ x: -1, y: -1 });
    expect(screenToTile(camera, viewport, 400 + TILE_PX * 2 + 1, 300)).toEqual({ x: 2, y: 0 });
  });

  it('centres on a map', () => {
    expect(centredOn(40, 20)).toEqual({ x: 20, y: 10, zoom: 1 });
  });

  it('pans so the world follows the drag', () => {
    const camera = { x: 10, y: 10, zoom: 2 };
    const before = worldToScreen(camera, viewport, 4, 4);
    const after = worldToScreen(panByScreen(camera, 30, -12), viewport, 4, 4);
    expect(after.x - before.x).toBeCloseTo(30);
    expect(after.y - before.y).toBeCloseTo(-12);
  });

  it('zooms around the cursor, keeping the point under it fixed', () => {
    const camera = { x: 10, y: 10, zoom: 1 };
    const anchor = screenToWorld(camera, viewport, 123, 456);
    const zoomed = zoomAt(camera, viewport, 123, 456, 2);
    expect(zoomed.zoom).toBe(2);
    const again = screenToWorld(zoomed, viewport, 123, 456);
    expect(again.x).toBeCloseTo(anchor.x);
    expect(again.y).toBeCloseTo(anchor.y);
  });

  it('clamps zoom', () => {
    const camera = { x: 0, y: 0, zoom: 1 };
    expect(zoomAt(camera, viewport, 0, 0, 100).zoom).toBe(MAX_ZOOM);
    expect(zoomAt(camera, viewport, 0, 0, 0.001).zoom).toBe(MIN_ZOOM);
  });

  it('lists visible tiles clamped to the map', () => {
    const camera = centredOn(10, 10);
    expect(visibleTiles(camera, viewport, 10, 10)).toEqual({ x0: 0, y0: 0, x1: 9, y1: 9 });
    const zoomedIn = { x: 5, y: 5, zoom: 4 };
    const range = visibleTiles(zoomedIn, { width: 96, height: 96 }, 10, 10);
    // 96px viewport at 96px/tile shows world 4.5..5.5, i.e. tiles 4 and 5.
    expect(range).toEqual({ x0: 4, y0: 4, x1: 5, y1: 5 });
  });
});
