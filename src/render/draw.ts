// Top-down canvas view. Reads sim state only; never mutates it.

import { isRadioactive } from '../sim/climate';
import { isHostile } from '../sim/settlements';
import { worldPos } from './interp';
import { GROUNDS, type Point, type Tile } from '../sim/grid';
import { structureHealth } from '../sim/breach';
import { darkness } from '../sim/time';
import type { RoomPurpose } from '../sim/rooms';
import type { SimState } from '../sim/state';
import type { ToolId } from '../ui/tools';
import type { Wanderer } from '../sim/wanderers';
import { LAMP_LIGHT, litLamps } from '../sim/power';
import { tileSize, visibleTiles, worldToScreen, type Camera, type Viewport } from './camera';
import { COLOURS, FURNITURE_GLYPHS } from './palette';
import { drawRoomTints, drawRoomOutlines, drawRoomLabels, drawPlot, drawCrate, drawBed, drawWorkbench, drawTree, drawTrap, drawStove, drawTable, drawDamage, drawDirt, drawItem, drawFire, drawDog, drawGlyph, drawBlueprint } from './drawThings';
import { drawJobLink, drawHerd, drawWanderer, drawZombie, drawTask, drawSurvivor, drawPath, drawGoal } from './drawPeople';
export { FURNITURE_GLYPHS, GLYPHS } from './palette';

export const PURPOSE_NAMES: Record<RoomPurpose, string> = { housing: 'Housing', storage: 'Storage', armory: 'Armory', messhall: 'Mess hall', recroom: 'Rec room', infirmary: 'Infirmary', barracks: 'Barracks', workshop: 'Workshop', graveyard: 'Graveyard', greenhouse: 'Greenhouse', cell: 'Cells' };

/** How dark full night gets (overlay opacity). */
const NIGHT_MAX_ALPHA = 0.55;

/** Below this tile size, grid lines are just noise. */
export const MIN_GRID_LINE_PX = 10;
/** Below this tile size, survivor names are unreadable. */
export const MIN_LABEL_PX = 18;

export interface DrawOptions {
  hover: Point | null;
  /** Armed build tool, or null in select mode. */
  tool: ToolId | null;
  /** Fraction of the next tick elapsed, for interpolating movement. */
  alpha: number;
  selectedId: string | null;
  /** Debug overlay: path previews for every survivor. */
  showDebug: boolean;
  /** Box being dragged out by an area tool, if any. */
  areaDrag: { from: Point; to: Point } | null;
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
  /** On screen and explored: everything else is skipped (big maps, fog of war). */
  const shown = (x: number, y: number) =>
    x >= range.x0 && x <= range.x1 && y >= range.y0 && y <= range.y1 && grid.explored[y * grid.width + x] === 1;

  for (let y = range.y0; y <= range.y1; y++) {
    for (let x = range.x0; x <= range.x1; x++) {
      const i = y * grid.width + x;
      const tile = grid.tiles[i];
      if (tile === undefined) continue;
      if (grid.explored[i] !== 1) {
        const r = tileRect(camera, viewport, x, y);
        ctx.fillStyle = COLOURS.fog;
        ctx.fillRect(r.x, r.y, r.w, r.h);
      } else drawTile(ctx, camera, viewport, x, y, tile, grid.ground[i]!, size, tile === 'door' && grid.locked.includes(i));
      if (state.settings.winter && isRadioactive(state, x, y)) {
        const r = tileRect(camera, viewport, x, y);
        ctx.fillStyle = COLOURS.radiation;
        ctx.fillRect(r.x, r.y, r.w, r.h);
      }
    }
  }

  drawRoomTints(ctx, camera, viewport, state, range);

  for (let y = range.y0; y <= range.y1; y++) {
    for (let x = range.x0; x <= range.x1; x++) {
      const damage = grid.damage[y * grid.width + x]!;
      const tile = grid.tiles[y * grid.width + x];
      if (damage > 0 && (tile === 'wall' || tile === 'door')) drawDamage(ctx, camera, viewport, x, y, damage / structureHealth(grid, y * grid.width + x));
    }
  }

  for (let y = range.y0; y <= range.y1; y++) {
    for (let x = range.x0; x <= range.x1; x++) {
      if (grid.dirt[y * grid.width + x] === 1) drawDirt(ctx, camera, viewport, x, y, size);
    }
  }

  if (size >= MIN_GRID_LINE_PX) drawGridLines(ctx, camera, viewport, range);
  drawMapEdge(ctx, camera, viewport, grid.width, grid.height);
  if (options.showDebug) drawRoomOutlines(ctx, camera, viewport, state, range);

  for (const plot of state.farm) drawPlot(ctx, camera, viewport, plot);
  for (const f of state.furniture) {
    if (!shown(f.x, f.y)) continue;
    if (f.kind === 'bed') drawBed(ctx, camera, viewport, f.x, f.y);
    else if (f.kind === 'table') drawTable(ctx, camera, viewport, f.x, f.y);
    else if (f.kind === 'workbench') drawWorkbench(ctx, camera, viewport, f, size);
    else if (f.kind === 'stove') drawStove(ctx, camera, viewport, f);
    else if (f.kind === 'tree') drawTree(ctx, camera, viewport, f, size);
    else if (f.kind === 'trap') drawTrap(ctx, camera, viewport, f);
    else if (FURNITURE_GLYPHS[f.kind]) drawGlyph(ctx, camera, viewport, f.x + 0.5, f.y + 0.5, size * 1.7, FURNITURE_GLYPHS[f.kind]!);
    else drawCrate(ctx, camera, viewport, f, size);
  }
  for (const bp of state.blueprints) drawBlueprint(ctx, camera, viewport, bp, size);
  for (const f of state.fires) if (shown(f.x, f.y)) drawFire(ctx, camera, viewport, f.x, f.y, state.tick);
  // Lamplight: a warm pool round each lit lamp.
  for (const l of litLamps(state)) {
    if (!shown(l.x, l.y)) continue;
    const c = worldToScreen(camera, viewport, l.x + 0.5, l.y + 0.5);
    const glow = ctx.createRadialGradient(c.x, c.y, 0, c.x, c.y, size * (LAMP_LIGHT + 0.5));
    glow.addColorStop(0, 'rgba(255, 220, 140, 0.30)');
    glow.addColorStop(1, 'rgba(255, 220, 140, 0)');
    ctx.fillStyle = glow;
    ctx.fillRect(c.x - size * (LAMP_LIGHT + 1), c.y - size * (LAMP_LIGHT + 1), size * (2 * LAMP_LIGHT + 2), size * (2 * LAMP_LIGHT + 2));
  }
  for (const item of state.items) {
    if (item.pos && shown(item.pos.x, item.pos.y)) drawItem(ctx, camera, viewport, item.kind, item.pos.x + 0.5, item.pos.y + 0.5, size);
  }

  // Weather: a cool wash for rain, a pale one for snow.
  if (state.weather !== 'clear') {
    ctx.fillStyle = {
      rain: 'rgba(90, 120, 160, 0.12)',
      storm: 'rgba(60, 70, 110, 0.22)',
      snow: 'rgba(230, 236, 245, 0.10)',
      blizzard: 'rgba(235, 240, 250, 0.28)',
      heatwave: 'rgba(255, 170, 80, 0.12)',
      clear: 'rgba(0, 0, 0, 0)',
    }[state.weather];
    ctx.fillRect(0, 0, viewport.width, viewport.height);
  }

  // Night falls over the world, but not over survivors, previews, or labels, so play stays readable.
  const dark = darkness(state.tick);
  if (dark > 0) {
    ctx.fillStyle = COLOURS.night;
    ctx.globalAlpha = dark * NIGHT_MAX_ALPHA;
    ctx.fillRect(0, 0, viewport.width, viewport.height);
    ctx.globalAlpha = 1;
  }

  if (options.areaDrag && options.tool) drawAreaBox(ctx, camera, viewport, options.areaDrag, COLOURS.tools[options.tool]);
  else if (options.hover) drawHover(ctx, camera, viewport, options.hover, options.tool, grid.width, grid.height);

  for (const s of state.survivors) {
    if (options.showDebug) {
      drawPath(ctx, camera, viewport, s, options.alpha);
      drawJobLink(ctx, camera, viewport, state, s, options.alpha);
    }
    if (s.id === options.selectedId && s.goal) drawGoal(ctx, camera, viewport, s.goal, size);
  }
  for (const t of state.tasks) drawTask(ctx, camera, viewport, t, size);
  for (const a of state.exploreAreas) drawAreaBox(ctx, camera, viewport, { from: { x: a.x0, y: a.y0 }, to: { x: a.x1, y: a.y1 } }, COLOURS.tools.explore);
  for (const h of state.herds) {
    const onScreen = h.x >= range.x0 && h.x <= range.x1 && h.y >= range.y0 && h.y <= range.y1;
    if (onScreen && (h.heard || shown(h.x, h.y))) drawHerd(ctx, camera, viewport, h, size);
  }
  for (const w of state.wanderers) if (shown(w.x, w.y)) drawWanderer(ctx, camera, viewport, w, size);
  // Neighbours' people in their compounds: a wanderer's look, marked with their settlement's first letter.
  for (const r of state.residents) {
    if (!shown(r.pos.x, r.pos.y)) continue;
    const n = state.settlements.find((x) => x.id === r.settlementId);
    const at = worldPos(r, options.alpha);
    drawWanderer(ctx, camera, viewport, { id: r.id, name: r.name, x: at.x - 0.5, y: at.y - 0.5 } as Wanderer, size, n?.name[0] ?? '?', n !== undefined && isHostile(n));
  }
  for (const t of state.traders) if (shown(t.x, t.y)) drawWanderer(ctx, camera, viewport, { id: t.id, name: 'Trader', x: t.x, y: t.y, leavesTick: t.leavesTick, traits: [] }, size, '$');
  for (const z of state.zombies) if (shown(z.pos.x, z.pos.y)) drawZombie(ctx, camera, viewport, state, z, options, size);
  for (const a of state.animals) {
    if (!shown(a.pos.x, a.pos.y)) continue;
    const t = a.move ? Math.min(1, (a.move.progress + options.alpha) / a.move.duration) : 0;
    const p = worldToScreen(camera, viewport, (a.move ? a.move.from.x + (a.move.to.x - a.move.from.x) * t : a.pos.x) + 0.5, (a.move ? a.move.from.y + (a.move.to.y - a.move.from.y) * t : a.pos.y) + 0.5);
    const r = Math.max(2, size * 0.24);
    ctx.fillStyle = '#b07a46';
    ctx.fillRect(p.x - r * 1.2, p.y - r * 0.5, r * 2.2, r);
    ctx.strokeStyle = a.hunted ? '#ff5a4a' : '#e8d0a0';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(p.x + r * 0.8, p.y - r * 0.5);
    ctx.lineTo(p.x + r * 1.1, p.y - r * 1.3);
    ctx.stroke();
  }
  for (const q of state.prisoners) {
    if (!shown(q.x, q.y)) continue;
    const p = worldToScreen(camera, viewport, q.x + 0.5, q.y + 0.5);
    ctx.fillStyle = '#e0a080';
    ctx.strokeStyle = '#3a2a20';
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.arc(p.x, p.y, Math.max(3, size * 0.3), 0, Math.PI * 2);
    ctx.fill();
    ctx.stroke();
  }
  for (const d of state.dogs) if (shown(d.pos.x, d.pos.y)) drawDog(ctx, camera, viewport, d, options.alpha, size);
  for (const s of state.survivors) drawSurvivor(ctx, camera, viewport, state, s, options, size);
  drawRoomLabels(ctx, camera, viewport, state, size);
}

export function tileRect(camera: Camera, viewport: Viewport, x: number, y: number) {
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
  ground: number,
  size: number,
  locked = false,
): void {
  const r = tileRect(camera, viewport, x, y);
  ctx.fillStyle = tile === 'floor' || tile === 'door' ? COLOURS.ground[GROUNDS[ground] ?? 'grass'] : COLOURS.tiles[tile];
  ctx.fillRect(r.x, r.y, r.w, r.h);
  if (tile === 'barricade') {
    // Two crossed planks.
    ctx.strokeStyle = COLOURS.plankEdge;
    ctx.lineWidth = Math.max(2, r.w * 0.22);
    ctx.beginPath();
    ctx.moveTo(r.x + r.w * 0.15, r.y + r.h * 0.15);
    ctx.lineTo(r.x + r.w * 0.85, r.y + r.h * 0.85);
    ctx.moveTo(r.x + r.w * 0.85, r.y + r.h * 0.15);
    ctx.lineTo(r.x + r.w * 0.15, r.y + r.h * 0.85);
    ctx.stroke();
    ctx.strokeStyle = COLOURS.plank;
    ctx.lineWidth = Math.max(1, r.w * 0.14);
    ctx.stroke();
  }
  if (tile === 'water' && size >= 8) {
    ctx.fillStyle = COLOURS.waterRipple;
    ctx.fillRect(r.x + r.w * ((x * 7 + y * 3) % 5) * 0.15, r.y + r.h * 0.45, r.w * 0.3, Math.max(1, r.h * 0.08));
  }

  if (tile === 'wall') {
    // A lighter top edge so wall runs read as solid blocks.
    ctx.fillStyle = COLOURS.wallTop;
    ctx.fillRect(r.x, r.y, r.w, Math.max(1, Math.round(size * 0.15)));
  } else if (tile === 'door' && locked) {
    ctx.fillStyle = COLOURS.doorFrame;
    ctx.fillRect(r.x, r.y, r.w, r.h);
    ctx.fillStyle = '#c9a227';
    ctx.fillRect(r.x + r.w * 0.35, r.y + r.h * 0.4, r.w * 0.3, r.h * 0.3);
    ctx.strokeStyle = '#c9a227';
    ctx.lineWidth = Math.max(1, r.w * 0.06);
    ctx.strokeRect(r.x + r.w * 0.4, r.y + r.h * 0.22, r.w * 0.2, r.h * 0.2);
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

/** The box an area tool is dragging out, covering whole tiles. */
function drawAreaBox(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  viewport: Viewport,
  box: { from: Point; to: Point },
  colour: string,
): void {
  const a = tileRect(camera, viewport, Math.min(box.from.x, box.to.x), Math.min(box.from.y, box.to.y));
  const b = tileRect(camera, viewport, Math.max(box.from.x, box.to.x), Math.max(box.from.y, box.to.y));
  const x = a.x;
  const y = a.y;
  const w = b.x + b.w - a.x;
  const h = b.y + b.h - a.y;
  ctx.globalAlpha = 0.15;
  ctx.fillStyle = colour;
  ctx.fillRect(x, y, w, h);
  ctx.globalAlpha = 1;
  ctx.strokeStyle = colour;
  ctx.lineWidth = 2;
  ctx.setLineDash([6, 4]);
  ctx.strokeRect(x + 1, y + 1, w - 2, h - 2);
  ctx.setLineDash([]);
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
