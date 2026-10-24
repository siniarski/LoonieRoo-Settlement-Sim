// Top-down canvas view. Reads sim state only; never mutates it.

import type { Blueprint } from '../sim/designations';
import type { Point, Tile } from '../sim/grid';
import { getJob } from '../sim/jobs';
import type { SimState } from '../sim/state';
import type { Survivor } from '../sim/survivors';
import type { ToolId } from '../ui/tools';
import { tileSize, visibleTiles, worldToScreen, type Camera, type Viewport } from './camera';
import { survivorWorldPos } from './interp';

const COLOURS = {
  background: '#0d0f0e',
  mapEdge: '#3a3f3b',
  gridLine: 'rgba(255, 255, 255, 0.05)',
  tiles: {
    floor: '#262b27',
    wall: '#8b919c',
    door: '#a8713a',
  } satisfies Record<Tile, string>,
  wallTop: '#b2b8c2',
  doorFrame: '#5a3c1e',
  tools: {
    wall: '#9fd3ff',
    door: '#ffc27a',
    remove: '#ff7a7a',
    cancel: '#c9c9c9',
    move: '#8cf0a4',
  } satisfies Record<ToolId, string>,
  survivor: '#f2e6c9',
  survivorOutline: '#1a1d1b',
  selected: '#8cf0a4',
  path: 'rgba(140, 240, 164, 0.7)',
  goal: '#8cf0a4',
  label: '#f2e6c9',
  dirt: 'rgba(120, 92, 56, 0.85)',
  wood: '#b07a3f',
  woodGrain: '#7a4f24',
  blueprint: { wall: 'rgba(110, 180, 255, 0.35)', door: 'rgba(255, 190, 110, 0.35)', floor: 'rgba(255, 110, 110, 0.0)' },
  blueprintEdge: { wall: '#6eb4ff', door: '#ffbe6e', floor: '#ff6e6e' },
  progress: '#8cf0a4',
  jobLine: 'rgba(255, 214, 102, 0.8)',
};

/** Below this tile size, grid lines are just noise. */
const MIN_GRID_LINE_PX = 10;
/** Below this tile size, survivor names are unreadable. */
const MIN_LABEL_PX = 18;

export interface DrawOptions {
  hover: Point | null;
  tool: ToolId;
  /** Fraction of the next tick elapsed, for interpolating movement. */
  alpha: number;
  selectedId: string | null;
  /** Debug overlay: path previews for every survivor. */
  showDebug: boolean;
}

export function draw(
  ctx: CanvasRenderingContext2D,
  state: SimState,
  camera: Camera,
  viewport: Viewport,
  options: DrawOptions,
): void {
  const { grid } = state;
  ctx.fillStyle = COLOURS.background;
  ctx.fillRect(0, 0, viewport.width, viewport.height);

  const size = tileSize(camera);
  const range = visibleTiles(camera, viewport, grid.width, grid.height);

  for (let y = range.y0; y <= range.y1; y++) {
    for (let x = range.x0; x <= range.x1; x++) {
      const tile = grid.tiles[y * grid.width + x];
      if (tile !== undefined) drawTile(ctx, camera, viewport, x, y, tile, size);
    }
  }

  for (let y = range.y0; y <= range.y1; y++) {
    for (let x = range.x0; x <= range.x1; x++) {
      if (grid.dirt[y * grid.width + x] === 1) drawDirt(ctx, camera, viewport, x, y, size);
    }
  }

  if (size >= MIN_GRID_LINE_PX) drawGridLines(ctx, camera, viewport, range);
  drawMapEdge(ctx, camera, viewport, grid.width, grid.height);

  for (const bp of state.blueprints) drawBlueprint(ctx, camera, viewport, bp, size);
  for (const item of state.items) if (item.pos) drawWood(ctx, camera, viewport, item.pos.x + 0.5, item.pos.y + 0.5, size);

  if (options.hover) drawHover(ctx, camera, viewport, options.hover, options.tool, grid.width, grid.height);

  for (const s of state.survivors) {
    if (options.showDebug) {
      drawPath(ctx, camera, viewport, s, options.alpha);
      drawJobLink(ctx, camera, viewport, state, s, options.alpha);
    }
    if (s.id === options.selectedId && s.goal) drawGoal(ctx, camera, viewport, s.goal, size);
  }
  for (const s of state.survivors) drawSurvivor(ctx, camera, viewport, s, options, size);
}

function drawDirt(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, x: number, y: number, size: number): void {
  const r = tileRect(camera, viewport, x, y);
  ctx.fillStyle = COLOURS.dirt;
  // A fixed speckle pattern per tile, so it doesn't shimmer.
  const dot = Math.max(1, Math.round(size * 0.1));
  for (const [fx, fy] of [
    [0.2, 0.3],
    [0.55, 0.2],
    [0.75, 0.6],
    [0.35, 0.7],
    [0.6, 0.85],
  ] as const) {
    ctx.fillRect(r.x + Math.round(r.w * fx), r.y + Math.round(r.h * fy), dot, dot);
  }
}

function drawWood(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, wx: number, wy: number, size: number): void {
  const p = worldToScreen(camera, viewport, wx, wy);
  const w = size * 0.6;
  const h = size * 0.16;
  ctx.fillStyle = COLOURS.wood;
  ctx.strokeStyle = COLOURS.woodGrain;
  ctx.lineWidth = 1;
  for (const dy of [-h * 1.1, 0, h * 1.1]) {
    ctx.fillRect(p.x - w / 2, p.y + dy - h / 2, w, h);
    ctx.strokeRect(p.x - w / 2 + 0.5, p.y + dy - h / 2 + 0.5, w - 1, h - 1);
  }
}

function drawBlueprint(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, bp: Blueprint, size: number): void {
  const r = tileRect(camera, viewport, bp.x, bp.y);
  const edge = COLOURS.blueprintEdge[bp.target];
  ctx.fillStyle = COLOURS.blueprint[bp.target];
  ctx.fillRect(r.x, r.y, r.w, r.h);
  ctx.strokeStyle = edge;
  ctx.lineWidth = 1.5;
  // Dashed while still waiting for material, solid once it can be built.
  ctx.setLineDash(bp.needsMaterial && !bp.delivered ? [3, 3] : []);
  ctx.strokeRect(r.x + 1.5, r.y + 1.5, r.w - 3, r.h - 3);
  ctx.setLineDash([]);
  if (bp.target === 'floor') {
    ctx.beginPath();
    ctx.moveTo(r.x + 3, r.y + 3);
    ctx.lineTo(r.x + r.w - 3, r.y + r.h - 3);
    ctx.moveTo(r.x + r.w - 3, r.y + 3);
    ctx.lineTo(r.x + 3, r.y + r.h - 3);
    ctx.stroke();
  }
  if (bp.workDone > 0) {
    const barH = Math.max(2, Math.round(size * 0.12));
    ctx.fillStyle = 'rgba(0, 0, 0, 0.6)';
    ctx.fillRect(r.x + 2, r.y + r.h - barH - 2, r.w - 4, barH);
    ctx.fillStyle = COLOURS.progress;
    ctx.fillRect(r.x + 2, r.y + r.h - barH - 2, ((r.w - 4) * bp.workDone) / bp.workRequired, barH);
  }
}

/** Debug: a line from each worker to their job's tile, so reservations are visible. */
function drawJobLink(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  viewport: Viewport,
  state: SimState,
  s: Survivor,
  alpha: number,
): void {
  const job = s.jobId ? getJob(state, s.jobId) : undefined;
  if (!job) return;
  const from = worldToScreen(camera, viewport, ...xy(survivorWorldPos(s, alpha)));
  const to = worldToScreen(camera, viewport, job.x + 0.5, job.y + 0.5);
  const r = tileRect(camera, viewport, job.x, job.y);
  ctx.strokeStyle = COLOURS.jobLine;
  ctx.lineWidth = 1.5;
  ctx.beginPath();
  ctx.moveTo(from.x, from.y);
  ctx.lineTo(to.x, to.y);
  ctx.stroke();
  ctx.strokeRect(r.x + 0.5, r.y + 0.5, r.w - 1, r.h - 1);
}

function xy(p: Point): [number, number] {
  return [p.x, p.y];
}

function drawSurvivor(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  viewport: Viewport,
  s: Survivor,
  options: DrawOptions,
  size: number,
): void {
  const world = survivorWorldPos(s, options.alpha);
  const p = worldToScreen(camera, viewport, world.x, world.y);
  const radius = Math.max(3, size * 0.32);

  if (s.id === options.selectedId) {
    ctx.strokeStyle = COLOURS.selected;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.arc(p.x, p.y, radius + 3, 0, Math.PI * 2);
    ctx.stroke();
  }
  ctx.fillStyle = COLOURS.survivor;
  ctx.strokeStyle = COLOURS.survivorOutline;
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.arc(p.x, p.y, radius, 0, Math.PI * 2);
  ctx.fill();
  ctx.stroke();
  if (s.carrying) drawWood(ctx, camera, viewport, world.x + 0.3, world.y + 0.25, size * 0.6);

  if (size >= MIN_LABEL_PX) {
    ctx.fillStyle = COLOURS.label;
    ctx.font = `${Math.round(Math.min(14, size * 0.45))}px ui-monospace, Consolas, monospace`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.fillText(s.name, p.x, p.y - radius - 3);
  }
}

/** Debug path preview: from the survivor's drawn position through every remaining tile. */
function drawPath(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, s: Survivor, alpha: number): void {
  const points = [survivorWorldPos(s, alpha)];
  if (s.move) points.push({ x: s.move.to.x + 0.5, y: s.move.to.y + 0.5 });
  for (const p of s.path) points.push({ x: p.x + 0.5, y: p.y + 0.5 });
  if (points.length < 2) return;

  ctx.strokeStyle = COLOURS.path;
  ctx.lineWidth = 2;
  ctx.setLineDash([4, 4]);
  ctx.beginPath();
  points.forEach((w, i) => {
    const p = worldToScreen(camera, viewport, w.x, w.y);
    if (i === 0) ctx.moveTo(p.x, p.y);
    else ctx.lineTo(p.x, p.y);
  });
  ctx.stroke();
  ctx.setLineDash([]);
}

function drawGoal(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, goal: Point, size: number): void {
  const p = worldToScreen(camera, viewport, goal.x + 0.5, goal.y + 0.5);
  const r = Math.max(3, size * 0.25);
  ctx.strokeStyle = COLOURS.goal;
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(p.x - r, p.y - r);
  ctx.lineTo(p.x + r, p.y + r);
  ctx.moveTo(p.x + r, p.y - r);
  ctx.lineTo(p.x - r, p.y + r);
  ctx.stroke();
}

/** Integer pixel rect for a tile, so neighbouring tiles share edges without seams. */
function tileRect(camera: Camera, viewport: Viewport, x: number, y: number) {
  const a = worldToScreen(camera, viewport, x, y);
  const b = worldToScreen(camera, viewport, x + 1, y + 1);
  const x0 = Math.round(a.x);
  const y0 = Math.round(a.y);
  return { x: x0, y: y0, w: Math.round(b.x) - x0, h: Math.round(b.y) - y0 };
}

function drawTile(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  viewport: Viewport,
  x: number,
  y: number,
  tile: Tile,
  size: number,
): void {
  const r = tileRect(camera, viewport, x, y);
  ctx.fillStyle = COLOURS.tiles[tile === 'door' ? 'floor' : tile];
  ctx.fillRect(r.x, r.y, r.w, r.h);

  if (tile === 'wall') {
    // A lighter top edge so wall runs read as solid blocks.
    ctx.fillStyle = COLOURS.wallTop;
    ctx.fillRect(r.x, r.y, r.w, Math.max(1, Math.round(size * 0.15)));
  } else if (tile === 'door') {
    const inset = Math.round(size * 0.12);
    ctx.fillStyle = COLOURS.doorFrame;
    ctx.fillRect(r.x, r.y, r.w, r.h);
    ctx.fillStyle = COLOURS.tiles.door;
    ctx.fillRect(r.x + inset, r.y + inset, r.w - inset * 2, r.h - inset * 2);
  }
}

function drawGridLines(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  viewport: Viewport,
  range: { x0: number; y0: number; x1: number; y1: number },
): void {
  ctx.strokeStyle = COLOURS.gridLine;
  ctx.lineWidth = 1;
  ctx.beginPath();
  const top = worldToScreen(camera, viewport, 0, range.y0).y;
  const bottom = worldToScreen(camera, viewport, 0, range.y1 + 1).y;
  const left = worldToScreen(camera, viewport, range.x0, 0).x;
  const right = worldToScreen(camera, viewport, range.x1 + 1, 0).x;
  for (let x = range.x0; x <= range.x1 + 1; x++) {
    const sx = Math.round(worldToScreen(camera, viewport, x, 0).x) + 0.5;
    ctx.moveTo(sx, top);
    ctx.lineTo(sx, bottom);
  }
  for (let y = range.y0; y <= range.y1 + 1; y++) {
    const sy = Math.round(worldToScreen(camera, viewport, 0, y).y) + 0.5;
    ctx.moveTo(left, sy);
    ctx.lineTo(right, sy);
  }
  ctx.stroke();
}

function drawMapEdge(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, w: number, h: number): void {
  const a = worldToScreen(camera, viewport, 0, 0);
  const b = worldToScreen(camera, viewport, w, h);
  ctx.strokeStyle = COLOURS.mapEdge;
  ctx.lineWidth = 2;
  ctx.strokeRect(Math.round(a.x) - 1, Math.round(a.y) - 1, Math.round(b.x - a.x) + 2, Math.round(b.y - a.y) + 2);
}

function drawHover(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  viewport: Viewport,
  hover: Point,
  tool: ToolId,
  w: number,
  h: number,
): void {
  if (hover.x < 0 || hover.y < 0 || hover.x >= w || hover.y >= h) return;
  const r = tileRect(camera, viewport, hover.x, hover.y);
  const colour = COLOURS.tools[tool];
  ctx.globalAlpha = 0.25;
  ctx.fillStyle = colour;
  ctx.fillRect(r.x, r.y, r.w, r.h);
  ctx.globalAlpha = 1;
  ctx.strokeStyle = colour;
  ctx.lineWidth = 2;
  ctx.strokeRect(r.x + 1, r.y + 1, r.w - 2, r.h - 2);
}
