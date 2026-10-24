// Top-down canvas view. Reads sim state only; never mutates it.

import type { Blueprint, BuildTarget } from '../sim/designations';
import type { Point, Tile } from '../sim/grid';
import { getJob } from '../sim/jobs';
import { REST_MAX, TIRED } from '../sim/rest';
import { evaluateRoom, getRoomMap, labelAt, roomAt, type Room, type RoomPurpose } from '../sim/rooms';
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
    bed: '#c7a6ff',
    housing: '#6ea8ff',
    storage: '#e6be6e',
  } satisfies Record<ToolId, string>,
  selectHover: '#e8ece6',
  survivor: '#f2e6c9',
  survivorOutline: '#1a1d1b',
  survivorAsleep: '#8a8fa8',
  rest: '#7fb2ff',
  restLow: '#ff8a7a',
  selected: '#8cf0a4',
  path: 'rgba(140, 240, 164, 0.7)',
  goal: '#8cf0a4',
  label: '#f2e6c9',
  dirt: 'rgba(120, 92, 56, 0.85)',
  wood: '#b07a3f',
  woodGrain: '#7a4f24',
  blueprint: {
    wall: 'rgba(110, 180, 255, 0.35)',
    door: 'rgba(255, 190, 110, 0.35)',
    bed: 'rgba(199, 166, 255, 0.30)',
    floor: 'rgba(255, 110, 110, 0.0)',
  } satisfies Record<BuildTarget, string>,
  blueprintEdge: { wall: '#6eb4ff', door: '#ffbe6e', bed: '#c7a6ff', floor: '#ff6e6e' } satisfies Record<BuildTarget, string>,
  progress: '#8cf0a4',
  jobLine: 'rgba(255, 214, 102, 0.8)',
  bedFrame: '#5b4a7a',
  bedSheet: '#9c8cc4',
  bedPillow: '#e9e3f5',
  room: { housing: 'rgba(110, 168, 255, 0.10)', storage: 'rgba(230, 190, 110, 0.10)' } satisfies Record<RoomPurpose, string>,
  roomLabel: { housing: '#9cc4ff', storage: '#f0d39a' } satisfies Record<RoomPurpose, string>,
  incomplete: '#ff8a7a',
  roomOutline: 'rgba(255, 255, 255, 0.35)',
  labelBg: 'rgba(13, 15, 14, 0.75)',
};

const PURPOSE_NAMES: Record<RoomPurpose, string> = { housing: 'Housing', storage: 'Storage' };

/** Below this tile size, grid lines are just noise. */
const MIN_GRID_LINE_PX = 10;
/** Below this tile size, survivor names are unreadable. */
const MIN_LABEL_PX = 18;

export interface DrawOptions {
  hover: Point | null;
  /** Armed build tool, or null in select mode. */
  tool: ToolId | null;
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

  drawRoomTints(ctx, camera, viewport, state, range);

  for (let y = range.y0; y <= range.y1; y++) {
    for (let x = range.x0; x <= range.x1; x++) {
      if (grid.dirt[y * grid.width + x] === 1) drawDirt(ctx, camera, viewport, x, y, size);
    }
  }

  if (size >= MIN_GRID_LINE_PX) drawGridLines(ctx, camera, viewport, range);
  drawMapEdge(ctx, camera, viewport, grid.width, grid.height);
  if (options.showDebug) drawRoomOutlines(ctx, camera, viewport, state, range);

  for (const f of state.furniture) drawBed(ctx, camera, viewport, f.x, f.y);
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
  drawRoomLabels(ctx, camera, viewport, state, size);
}

type TileRange = { x0: number; y0: number; x1: number; y1: number };

/** Soft tint over designated rooms, by purpose. */
function drawRoomTints(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, state: SimState, range: TileRange): void {
  if (state.roomDesignations.length === 0) return;
  const { labels } = getRoomMap(state);
  const purposeByLabel = new Map<number, RoomPurpose>();
  for (const d of state.roomDesignations) {
    const label = labelAt(state, d.x, d.y);
    if (label >= 0) purposeByLabel.set(label, d.purpose);
  }
  const { width } = state.grid;
  for (let y = range.y0; y <= range.y1; y++) {
    for (let x = range.x0; x <= range.x1; x++) {
      const purpose = purposeByLabel.get(labels[y * width + x]!);
      if (!purpose) continue;
      const r = tileRect(camera, viewport, x, y);
      ctx.fillStyle = COLOURS.room[purpose];
      ctx.fillRect(r.x, r.y, r.w, r.h);
    }
  }
}

/** Debug: outline every room (designated or not) where it meets something else. */
function drawRoomOutlines(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, state: SimState, range: TileRange): void {
  const { labels } = getRoomMap(state);
  const { width, height } = state.grid;
  const at = (x: number, y: number) => (x < 0 || y < 0 || x >= width || y >= height ? -99 : labels[y * width + x]!);
  ctx.strokeStyle = COLOURS.roomOutline;
  ctx.lineWidth = 1;
  ctx.setLineDash([2, 3]);
  ctx.beginPath();
  for (let y = range.y0; y <= range.y1; y++) {
    for (let x = range.x0; x <= range.x1; x++) {
      const label = at(x, y);
      if (label < 0) continue;
      const r = tileRect(camera, viewport, x, y);
      const inset = 2.5;
      if (at(x, y - 1) !== label) (ctx.moveTo(r.x, r.y + inset), ctx.lineTo(r.x + r.w, r.y + inset));
      if (at(x, y + 1) !== label) (ctx.moveTo(r.x, r.y + r.h - inset), ctx.lineTo(r.x + r.w, r.y + r.h - inset));
      if (at(x - 1, y) !== label) (ctx.moveTo(r.x + inset, r.y), ctx.lineTo(r.x + inset, r.y + r.h));
      if (at(x + 1, y) !== label) (ctx.moveTo(r.x + r.w - inset, r.y), ctx.lineTo(r.x + r.w - inset, r.y + r.h));
    }
  }
  ctx.stroke();
  ctx.setLineDash([]);
}

/** Purpose and status, centred at the top of each designated room. Incomplete rooms name their first unmet requirement. */
function drawRoomLabels(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, state: SimState, size: number): void {
  ctx.font = `${Math.round(Math.max(10, Math.min(13, size * 0.5)))}px ui-monospace, Consolas, monospace`;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  for (const d of state.roomDesignations) {
    const room = roomAt(state, d.x, d.y);
    const status = evaluateRoom(state, room, d.purpose);
    const missing = status.requirements.find((r) => !r.met);
    const detail = missing
      ? `⚠ needs: ${missing.label}`
      : d.purpose === 'housing'
        ? `✓ ${status.beds} bed${status.beds === 1 ? '' : 's'}${bedsInUse(state, room) > 0 ? `, ${bedsInUse(state, room)} in use` : ''}`
        : `✓ ${status.size} tiles`;
    const text = `${PURPOSE_NAMES[d.purpose]}  ${detail}`;
    const anchor = room ? { x: (room.minX + room.maxX + 1) / 2, y: room.minY + 0.15 } : { x: d.x + 0.5, y: d.y + 0.15 };
    const p = worldToScreen(camera, viewport, anchor.x, anchor.y);
    const w = ctx.measureText(text).width + 8;
    const h = Math.round(Math.max(10, Math.min(13, size * 0.5))) + 4;
    ctx.fillStyle = COLOURS.labelBg;
    ctx.fillRect(p.x - w / 2, p.y, w, h);
    ctx.fillStyle = missing ? COLOURS.incomplete : COLOURS.roomLabel[d.purpose];
    ctx.fillText(text, p.x, p.y + 2);
  }
}

function bedsInUse(state: SimState, room: Room | null): number {
  if (!room) return 0;
  return state.furniture.filter((f) => f.kind === 'bed' && f.occupant !== null && labelAt(state, f.x, f.y) === room.index).length;
}

function drawBed(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, x: number, y: number): void {
  const r = tileRect(camera, viewport, x, y);
  const pad = Math.max(1, Math.round(r.w * 0.12));
  ctx.fillStyle = COLOURS.bedFrame;
  ctx.fillRect(r.x + pad, r.y + pad, r.w - pad * 2, r.h - pad * 2);
  ctx.fillStyle = COLOURS.bedSheet;
  ctx.fillRect(r.x + pad + 1, r.y + pad + Math.round(r.h * 0.3), r.w - pad * 2 - 2, r.h - pad * 2 - Math.round(r.h * 0.3) - 1);
  ctx.fillStyle = COLOURS.bedPillow;
  ctx.fillRect(r.x + pad + 2, r.y + pad + 2, r.w - pad * 2 - 4, Math.max(2, Math.round(r.h * 0.2)));
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
  const asleep = s.sleep?.asleep === true;
  ctx.fillStyle = asleep ? COLOURS.survivorAsleep : COLOURS.survivor;
  ctx.strokeStyle = COLOURS.survivorOutline;
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.arc(p.x, p.y, radius, 0, Math.PI * 2);
  ctx.fill();
  ctx.stroke();
  if (s.carrying) drawWood(ctx, camera, viewport, world.x + 0.3, world.y + 0.25, size * 0.6);

  // Rest bar under the survivor once it matters: below half, or while sleeping.
  if (asleep || s.rest < REST_MAX / 2) {
    const w = Math.max(10, size * 0.8);
    const h = Math.max(2, Math.round(size * 0.1));
    const y = p.y + radius + 3;
    ctx.fillStyle = 'rgba(0, 0, 0, 0.65)';
    ctx.fillRect(p.x - w / 2, y, w, h);
    ctx.fillStyle = s.rest < TIRED ? COLOURS.restLow : COLOURS.rest;
    ctx.fillRect(p.x - w / 2, y, (w * s.rest) / REST_MAX, h);
  }

  if (size >= MIN_LABEL_PX) {
    ctx.fillStyle = COLOURS.label;
    ctx.font = `${Math.round(Math.min(14, size * 0.45))}px ui-monospace, Consolas, monospace`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.fillText(asleep ? `${s.name} z` : s.name, p.x, p.y - radius - 3);
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
  tool: ToolId | null,
  w: number,
  h: number,
): void {
  if (hover.x < 0 || hover.y < 0 || hover.x >= w || hover.y >= h) return;
  const r = tileRect(camera, viewport, hover.x, hover.y);
  // Select mode gets a faint outline; an armed tool previews boldly in its colour.
  const colour = tool ? COLOURS.tools[tool] : COLOURS.selectHover;
  ctx.globalAlpha = tool ? 0.25 : 0.06;
  ctx.fillStyle = colour;
  ctx.fillRect(r.x, r.y, r.w, r.h);
  ctx.globalAlpha = tool ? 1 : 0.5;
  ctx.strokeStyle = colour;
  ctx.lineWidth = tool ? 2 : 1;
  ctx.strokeRect(r.x + 1, r.y + 1, r.w - 2, r.h - 2);
  ctx.globalAlpha = 1;
}
