// Top-down canvas view. Reads sim state only; never mutates it.

import type { Blueprint, BuildTarget } from '../sim/designations';
import { GROW_TICKS, isRipe, type FarmPlot } from '../sim/farming';
import type { Furniture } from '../sim/furniture';
import { GROUNDS, type Ground, type Point, type Tile } from '../sim/grid';
import { getJob } from '../sim/jobs';
import { STRUCTURE_HEALTH } from '../sim/breach';
import { HUNGRY, SATIETY_MAX } from '../sim/food';
import { HEALTH_LOW, HEALTH_MAX } from '../sim/health';
import { darkness } from '../sim/time';
import type { Zombie } from '../sim/zombies';
import type { ItemKind } from '../sim/items';
import { REST_MAX, TIRED } from '../sim/rest';
import { evaluateRoom, getRoomMap, labelAt, roomAt, type Room, type RoomPurpose } from '../sim/rooms';
import type { SimState } from '../sim/state';
import type { Survivor } from '../sim/survivors';
import type { ToolId } from '../ui/tools';
import type { Wanderer } from '../sim/wanderers';
import type { Herd } from '../sim/herds';
import type { Dog } from '../sim/dogs';
import { LAMP_LIGHT, litLamps } from '../sim/power';
import { tileSize, visibleTiles, worldToScreen, type Camera, type Viewport } from './camera';
import { survivorWorldPos } from './interp';

const COLOURS = {
  fog: '#07080a',
  background: '#0d0f0e',
  mapEdge: '#3a3f3b',
  gridLine: 'rgba(255, 255, 255, 0.05)',
  tiles: {
    floor: '#262b27',
    wall: '#8b919c',
    door: '#a8713a',
    water: '#24465e',
    barricade: '#262b27',
  } satisfies Record<Tile, string>,
  plank: '#7a5a35',
  plankEdge: '#4a3420',
  spike: '#b8bec6',
  ground: {
    grass: '#262b27',
    dirt: '#2e2a24',
    road: '#36383a',
    forest: '#1f2a1f',
    bridge: '#4a3a28',
  } satisfies Record<Ground, string>,
  waterRipple: 'rgba(160, 200, 230, 0.18)',
  wallTop: '#b2b8c2',
  doorFrame: '#5a3c1e',
  tools: {
    wall: '#9fd3ff',
    door: '#ffc27a',
    remove: '#ff7a7a',
    cancel: '#c9c9c9',
    scavenge: '#ffd166',
    explore: '#9fc3ff',
    lock: '#c9a227',
    well: '#5a8cc8',
    collector: '#5a8cc8',
    generator: '#c8b450',
    lamp: '#ffe678',
    fridge: '#c8dcf0',
    farm: '#9ed36a',
    furnish: '#c7a6ff',
    bed: '#c7a6ff',
    housing: '#6ea8ff',
    storage: '#e6be6e',
    armory: '#d98c6a',
    table: '#d6aa78',
    workbench: '#be9664',
    stove: '#8a8f96',
    barricade: '#aa7846',
    trap: '#b8bec6',
    tower: '#96704a',
    messhall: '#9ee0c0',
    recroom: '#ec9fdc',
    infirmary: '#f4f1ea',
    barracks: '#c3d48c',
    workshop: '#d6b47c',
    graveyard: '#b4b4c8',
    burn: '#ff7a2e',
  } satisfies Record<ToolId, string>,
  selectHover: '#e8ece6',
  survivor: '#f2e6c9',
  survivorOutline: '#1a1d1b',
  survivorAsleep: '#8a8fa8',
  wanderer: '#9a9a8c',
  wandererMark: '#fff3c4',
  rest: '#7fb2ff',
  satiety: '#ffb35c',
  needLow: '#ff6a5c',
  health: '#7ee08a',
  night: '#0a1030',
  zombie: '#7da35a',
  zombieOutline: '#1f2a14',
  zombieEye: '#ff5a4a',
  zombieChase: 'rgba(255, 90, 74, 0.6)',
  foodCan: '#b9c2c9',
  foodLabel: '#d9534f',
  foodRim: '#6f7880',
  bandage: '#f4f1ea',
  bandageCross: '#d9534f',
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
    barricade: 'rgba(170, 120, 70, 0.30)',
    trap: 'rgba(184, 190, 198, 0.30)',
    table: 'rgba(214, 170, 120, 0.30)',
    stove: 'rgba(120, 124, 130, 0.30)',
    workbench: 'rgba(190, 150, 100, 0.30)',
    tower: 'rgba(150, 110, 70, 0.30)',
    well: 'rgba(90, 140, 200, 0.30)',
    collector: 'rgba(90, 140, 200, 0.30)',
    generator: 'rgba(200, 180, 80, 0.30)',
    lamp: 'rgba(255, 230, 120, 0.30)',
    fridge: 'rgba(200, 220, 240, 0.30)',
    floor: 'rgba(255, 110, 110, 0.0)',
  } satisfies Record<BuildTarget, string>,
  blueprintEdge: { wall: '#6eb4ff', door: '#ffbe6e', bed: '#c7a6ff', table: '#d6aa78', workbench: '#be9664', stove: '#8a8f96', barricade: '#aa7846', trap: '#b8bec6', tower: '#96704a', well: '#5a8cc8', collector: '#5a8cc8', generator: '#c8b450', lamp: '#ffe678', fridge: '#c8dcf0', floor: '#ff6e6e' } satisfies Record<BuildTarget, string>,
  progress: '#8cf0a4',
  jobLine: 'rgba(255, 214, 102, 0.8)',
  soil: '#4a3726',
  furrow: '#3a2a1c',
  sprout: '#6fbf4a',
  ripe: '#e8c45a',
  crate: '#7a5a35',
  leaves: '#3f7d3a',
  trunk: '#5a3f26',
  crateSlat: '#5a4026',
  crateEmpty: '#4a4038',
  crateMarked: '#ffd166',
  lootPip: '#ffd166',
  bedFrame: '#5b4a7a',
  bedSheet: '#9c8cc4',
  bedPillow: '#e9e3f5',
  room: {
    housing: 'rgba(110, 168, 255, 0.10)',
    storage: 'rgba(230, 190, 110, 0.10)',
    armory: 'rgba(217, 140, 106, 0.12)',
    messhall: 'rgba(158, 224, 192, 0.10)',
    recroom: 'rgba(236, 160, 220, 0.10)',
    infirmary: 'rgba(240, 240, 240, 0.10)',
    barracks: 'rgba(170, 190, 120, 0.12)',
    workshop: 'rgba(190, 150, 100, 0.12)',
    graveyard: 'rgba(120, 120, 140, 0.14)',
  } satisfies Record<RoomPurpose, string>,
  roomLabel: { housing: '#9cc4ff', storage: '#f0d39a', armory: '#f0b39a', messhall: '#b8f0d4', recroom: '#f3c4ea', infirmary: '#f4f1ea', barracks: '#d4e0a8', workshop: '#e0c89a', graveyard: '#b4b4c8' } satisfies Record<RoomPurpose, string>,
  tableTop: '#8a6440',
  tableEdge: '#5c4128',
  blade: '#cfd6dc',
  hilt: '#6b4a2e',
  incomplete: '#ff8a7a',
  roomOutline: 'rgba(255, 255, 255, 0.35)',
  labelBg: 'rgba(13, 15, 14, 0.75)',
};

export const PURPOSE_NAMES: Record<RoomPurpose, string> = { housing: 'Housing', storage: 'Storage', armory: 'Armory', messhall: 'Mess hall', recroom: 'Rec room', infirmary: 'Infirmary', barracks: 'Barracks', workshop: 'Workshop', graveyard: 'Graveyard' };

/** How dark full night gets (overlay opacity). */
const NIGHT_MAX_ALPHA = 0.55;

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
    }
  }

  drawRoomTints(ctx, camera, viewport, state, range);

  for (let y = range.y0; y <= range.y1; y++) {
    for (let x = range.x0; x <= range.x1; x++) {
      const damage = grid.damage[y * grid.width + x]!;
      const tile = grid.tiles[y * grid.width + x];
      if (damage > 0 && (tile === 'wall' || tile === 'door')) drawDamage(ctx, camera, viewport, x, y, damage / STRUCTURE_HEALTH[tile]);
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
    ctx.fillStyle = state.weather === 'rain' ? 'rgba(90, 120, 160, 0.12)' : 'rgba(230, 236, 245, 0.10)';
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
  for (const a of state.exploreAreas) drawAreaBox(ctx, camera, viewport, { from: { x: a.x0, y: a.y0 }, to: { x: a.x1, y: a.y1 } }, COLOURS.tools.explore);
  for (const h of state.herds) {
    const onScreen = h.x >= range.x0 && h.x <= range.x1 && h.y >= range.y0 && h.y <= range.y1;
    if (onScreen && (h.heard || shown(h.x, h.y))) drawHerd(ctx, camera, viewport, h, size);
  }
  for (const w of state.wanderers) if (shown(w.x, w.y)) drawWanderer(ctx, camera, viewport, w, size);
  for (const t of state.traders) if (shown(t.x, t.y)) drawWanderer(ctx, camera, viewport, { id: t.id, name: 'Trader', x: t.x, y: t.y, leavesTick: t.leavesTick, traits: [] }, size, '$');
  for (const z of state.zombies) if (shown(z.pos.x, z.pos.y)) drawZombie(ctx, camera, viewport, state, z, options, size);
  for (const d of state.dogs) if (shown(d.pos.x, d.pos.y)) drawDog(ctx, camera, viewport, d, options.alpha, size);
  for (const s of state.survivors) drawSurvivor(ctx, camera, viewport, state, s, options, size);
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
  const placed: { x0: number; y0: number; x1: number; y1: number }[] = [];
  for (const d of state.roomDesignations) {
    const room = roomAt(state, d.x, d.y);
    const status = evaluateRoom(state, room, d.purpose);
    const missing = status.requirements.find((r) => !r.met);
    const detail = missing
      ? `⚠ needs: ${missing.label}`
      : d.purpose === 'housing' || d.purpose === 'infirmary' || d.purpose === 'barracks'
        ? `✓ ${status.beds} bed${status.beds === 1 ? '' : 's'}${bedsInUse(state, room) > 0 ? `, ${bedsInUse(state, room)} in use` : ''}`
        : d.purpose === 'messhall'
          ? `✓ ${status.tables} table${status.tables === 1 ? '' : 's'}`
          : `✓ ${status.size} tiles`;
    // Too wide for the room on screen: just the name and a tick or warning sign.
    const roomPx = room ? (room.maxX - room.minX + 1) * size : Infinity;
    const full = `${PURPOSE_NAMES[d.purpose]}  ${detail}`;
    const text = ctx.measureText(full).width + 8 <= roomPx ? full : `${PURPOSE_NAMES[d.purpose]} ${missing ? '⚠' : '✓'}`;
    // On the room's top wall, so it never covers what's inside.
    const anchor = room ? { x: (room.minX + room.maxX + 1) / 2, y: room.minY - 0.85 } : { x: d.x + 0.5, y: d.y + 0.15 };
    const p = worldToScreen(camera, viewport, anchor.x, anchor.y);
    const w = ctx.measureText(text).width + 8;
    const h = Math.round(Math.max(10, Math.min(13, size * 0.5))) + 4;
    // Step down past any label already drawn where this one would go.
    const hits = (y: number) => placed.some((r) => p.x - w / 2 < r.x1 && p.x + w / 2 > r.x0 && y < r.y1 && y + h > r.y0);
    for (let tries = 0; tries < 4 && hits(p.y); tries++) p.y += h + 2;
    placed.push({ x0: p.x - w / 2, y0: p.y, x1: p.x + w / 2, y1: p.y + h });
    ctx.fillStyle = COLOURS.labelBg;
    ctx.fillRect(p.x - w / 2, p.y, w, h);
    ctx.fillStyle = missing ? COLOURS.incomplete : COLOURS.roomLabel[d.purpose];
    ctx.fillText(text, p.x, p.y + 2);
  }
}

/** Tilled soil with furrows; a sprout that grows with the crop; gold when ripe. */
function drawPlot(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, plot: FarmPlot): void {
  const r = tileRect(camera, viewport, plot.x, plot.y);
  ctx.fillStyle = COLOURS.soil;
  ctx.fillRect(r.x + 1, r.y + 1, r.w - 2, r.h - 2);
  ctx.fillStyle = COLOURS.furrow;
  for (const t of [0.3, 0.55, 0.8]) ctx.fillRect(r.x + 2, Math.round(r.y + r.h * t), r.w - 4, Math.max(1, Math.round(r.h * 0.06)));
  if (!plot.planted) return;
  const ripe = isRipe(plot);
  const grown = ripe ? 1 : plot.growth / GROW_TICKS;
  const h = Math.max(2, r.h * (0.15 + 0.55 * grown));
  const w = Math.max(2, r.w * (0.12 + 0.3 * grown));
  ctx.fillStyle = ripe ? COLOURS.ripe : COLOURS.sprout;
  ctx.fillRect(r.x + r.w / 2 - w / 2, r.y + r.h * 0.85 - h, w, h);
}

/** A slatted crate: dull when searched out, yellow dashed outline when marked, pips for finds left. */
function drawCrate(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, f: Furniture, size: number): void {
  const r = tileRect(camera, viewport, f.x, f.y);
  const pad = Math.max(1, Math.round(r.w * 0.14));
  const x = r.x + pad;
  const y = r.y + pad;
  const w = r.w - pad * 2;
  const h = r.h - pad * 2;
  ctx.fillStyle = f.loot > 0 ? COLOURS.crate : COLOURS.crateEmpty;
  ctx.fillRect(x, y, w, h);
  ctx.strokeStyle = COLOURS.crateSlat;
  ctx.lineWidth = 1;
  ctx.beginPath();
  for (const t of [1 / 3, 2 / 3]) {
    ctx.moveTo(x, Math.round(y + h * t) + 0.5);
    ctx.lineTo(x + w, Math.round(y + h * t) + 0.5);
  }
  ctx.stroke();
  ctx.strokeRect(x + 0.5, y + 0.5, w - 1, h - 1);
  if (f.scavenge) {
    ctx.strokeStyle = COLOURS.crateMarked;
    ctx.lineWidth = 1.5;
    ctx.setLineDash([3, 2]);
    ctx.strokeRect(r.x + 1.5, r.y + 1.5, r.w - 3, r.h - 3);
    ctx.setLineDash([]);
  }
  if (f.loot > 0 && size >= MIN_GRID_LINE_PX) {
    const pip = Math.max(1.5, size * 0.06);
    ctx.fillStyle = COLOURS.lootPip;
    for (let i = 0; i < f.loot; i++) {
      ctx.fillRect(x + 2 + i * (pip + 1.5), y + h - pip - 2, pip, pip);
    }
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

/** A long bench with a saw and hammer on it; pips for wood brought in. */
function drawWorkbench(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, f: Furniture, size: number): void {
  const r = tileRect(camera, viewport, f.x, f.y);
  const pad = Math.max(1, Math.round(r.w * 0.08));
  ctx.fillStyle = COLOURS.tableEdge;
  ctx.fillRect(r.x + pad, r.y + r.h * 0.25, r.w - pad * 2, r.h * 0.5);
  ctx.fillStyle = COLOURS.tableTop;
  ctx.fillRect(r.x + pad + 1, r.y + r.h * 0.25 + 1, r.w - pad * 2 - 2, r.h * 0.5 - 2);
  ctx.fillStyle = COLOURS.blade;
  ctx.fillRect(r.x + r.w * 0.2, r.y + r.h * 0.4, r.w * 0.35, Math.max(1, r.h * 0.08));
  ctx.fillStyle = COLOURS.hilt;
  ctx.fillRect(r.x + r.w * 0.65, r.y + r.h * 0.33, Math.max(1, r.w * 0.08), r.h * 0.3);
  const pip = Math.max(2, Math.round(size * 0.12));
  ctx.fillStyle = COLOURS.wood;
  for (let i = 0; i < f.stored; i++) ctx.fillRect(r.x + 2 + i * (pip + 1.5), r.y + r.h - pip - 2, pip, pip);
}

/** A round canopy on a trunk: thinner as it's chopped, a stump when bare, dashed when marked. */
function drawTree(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, f: Furniture, size: number): void {
  const p = worldToScreen(camera, viewport, f.x + 0.5, f.y + 0.5);
  ctx.fillStyle = COLOURS.trunk;
  ctx.fillRect(p.x - size * 0.07, p.y - size * 0.05, size * 0.14, size * 0.38);
  if (f.loot > 0) {
    ctx.fillStyle = COLOURS.leaves;
    ctx.beginPath();
    ctx.arc(p.x, p.y - size * 0.08, size * (0.18 + 0.08 * f.loot), 0, Math.PI * 2);
    ctx.fill();
  }
  if (f.scavenge) {
    ctx.strokeStyle = COLOURS.crateMarked;
    ctx.lineWidth = 1.5;
    ctx.setLineDash([3, 3]);
    const r = tileRect(camera, viewport, f.x, f.y);
    ctx.strokeRect(r.x + 1.5, r.y + 1.5, r.w - 3, r.h - 3);
    ctx.setLineDash([]);
  }
}

/** Spikes, one row per charge left. */
function drawTrap(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, f: Furniture): void {
  const r = tileRect(camera, viewport, f.x, f.y);
  ctx.fillStyle = COLOURS.spike;
  for (let row = 0; row < f.loot; row++) {
    const y = r.y + r.h * (0.3 + 0.22 * row);
    for (let k = 0; k < 3; k++) {
      const x = r.x + r.w * (0.22 + 0.28 * k);
      ctx.beginPath();
      ctx.moveTo(x - r.w * 0.08, y + r.h * 0.12);
      ctx.lineTo(x, y - r.h * 0.06);
      ctx.lineTo(x + r.w * 0.08, y + r.h * 0.12);
      ctx.fill();
    }
  }
}

/** A dark iron stove with a glowing door. */
function drawStove(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, f: Furniture): void {
  const r = tileRect(camera, viewport, f.x, f.y);
  const pad = Math.max(1, Math.round(r.w * 0.12));
  ctx.fillStyle = '#3a3d42';
  ctx.fillRect(r.x + pad, r.y + pad, r.w - pad * 2, r.h - pad * 2);
  ctx.fillStyle = f.stored > 0 ? '#ff9a3b' : '#6b4a2e';
  ctx.fillRect(r.x + r.w * 0.3, r.y + r.h * 0.5, r.w * 0.4, r.h * 0.25);
}

/** A square wooden table top. */
function drawTable(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, x: number, y: number): void {
  const r = tileRect(camera, viewport, x, y);
  const pad = Math.max(1, Math.round(r.w * 0.15));
  ctx.fillStyle = COLOURS.tableTop;
  ctx.fillRect(r.x + pad, r.y + pad, r.w - pad * 2, r.h - pad * 2);
  ctx.strokeStyle = COLOURS.tableEdge;
  ctx.lineWidth = Math.max(1, r.w * 0.06);
  ctx.strokeRect(r.x + pad, r.y + pad, r.w - pad * 2, r.h - pad * 2);
}

/** Battered wall or door: a red wash and cracks that grow with the damage (0..1). */
function drawDamage(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, x: number, y: number, amount: number): void {
  const r = tileRect(camera, viewport, x, y);
  ctx.fillStyle = `rgba(255, 70, 50, ${0.12 + 0.4 * amount})`;
  ctx.fillRect(r.x, r.y, r.w, r.h);
  ctx.strokeStyle = 'rgba(20, 10, 10, 0.85)';
  ctx.lineWidth = Math.max(1, r.w * 0.06);
  ctx.beginPath();
  ctx.moveTo(r.x + r.w * 0.5, r.y + r.h * 0.1);
  ctx.lineTo(r.x + r.w * 0.4, r.y + r.h * (0.1 + 0.4 * amount));
  ctx.lineTo(r.x + r.w * (0.4 + 0.3 * amount), r.y + r.h * (0.1 + 0.8 * amount));
  ctx.stroke();
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

function drawItem(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  viewport: Viewport,
  kind: ItemKind,
  wx: number,
  wy: number,
  size: number,
): void {
  if (kind === 'food') drawFood(ctx, camera, viewport, wx, wy, size);
  else if (kind === 'meal') drawMeal(ctx, camera, viewport, wx, wy, size);
  else if (kind === 'medicine') drawMedicine(ctx, camera, viewport, wx, wy, size);
  else if (kind === 'bandage') drawBandage(ctx, camera, viewport, wx, wy, size);
  else if (kind === 'weapon') drawWeapon(ctx, camera, viewport, wx, wy, size);
  else if (kind === 'wood') drawWood(ctx, camera, viewport, wx, wy, size);
  else drawGlyph(ctx, camera, viewport, wx, wy, size, GLYPHS[kind] ?? ['#cccccc', '?']);
}

/** Flames: two flickering tongues (the flicker comes from the tick, so it's stable when paused). */
function drawFire(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, x: number, y: number, tick: number): void {
  const r = tileRect(camera, viewport, x, y);
  const flicker = ((tick + x * 7 + y * 13) % 6) / 6;
  ctx.fillStyle = 'rgba(255, 90, 20, 0.55)';
  ctx.fillRect(r.x, r.y, r.w, r.h);
  ctx.fillStyle = `rgba(255, ${170 + Math.round(60 * flicker)}, 60, 0.85)`;
  ctx.beginPath();
  ctx.moveTo(r.x + r.w * 0.2, r.y + r.h * 0.9);
  ctx.lineTo(r.x + r.w * (0.35 + 0.1 * flicker), r.y + r.h * 0.2);
  ctx.lineTo(r.x + r.w * 0.5, r.y + r.h * 0.9);
  ctx.lineTo(r.x + r.w * (0.65 - 0.1 * flicker), r.y + r.h * 0.35);
  ctx.lineTo(r.x + r.w * 0.8, r.y + r.h * 0.9);
  ctx.closePath();
  ctx.fill();
}

/** A dog: a small brown body with a darker head, and its name when zoomed in. */
function drawDog(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, d: Dog, alpha: number, size: number): void {
  const t = d.move ? Math.min(1, (d.move.progress + alpha) / d.move.duration) : 0;
  const wx = d.move ? d.move.from.x + (d.move.to.x - d.move.from.x) * t : d.pos.x;
  const wy = d.move ? d.move.from.y + (d.move.to.y - d.move.from.y) * t : d.pos.y;
  const p = worldToScreen(camera, viewport, wx + 0.5, wy + 0.5);
  const r = Math.max(2, size * 0.2);
  ctx.fillStyle = '#8a5a2e';
  ctx.fillRect(p.x - r * 1.3, p.y - r * 0.6, r * 2.2, r * 1.2);
  ctx.fillStyle = '#5a3a1e';
  ctx.fillRect(p.x + r * 0.7, p.y - r, r, r);
  if (size >= MIN_LABEL_PX) {
    ctx.fillStyle = COLOURS.label;
    ctx.font = `${Math.round(Math.min(11, size * 0.35))}px ui-monospace, Consolas, monospace`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.fillText(d.name, p.x, p.y - r - 2);
  }
}

/** Furniture without a drawing of its own. */
export const FURNITURE_GLYPHS: Partial<Record<Furniture['kind'], [string, string]>> = {
  tower: ['#7a5636', 'T'],
  well: ['#3c6496', 'W'],
  collector: ['#4a7ab4', 'R'],
  generator: ['#8c7a2e', 'E'],
  lamp: ['#b4a046', 'l'],
  fridge: ['#a0b4c8', 'F'],
};

/** Items without a drawing of their own: a coloured tile with a letter. */
export const GLYPHS: Partial<Record<ItemKind, [string, string]>> = {
  cloth: ['#c9b8a0', 'c'],
  coat: ['#8a5a3a', 'C'],
  armour: ['#7a8088', 'A'],
  boots: ['#4a3626', 'B'],
  gun: ['#2e3238', 'G'],
  ammo: ['#b08a2e', 'a'],
  corpse: ['#5a4a4a', '+'],
  book: ['#6a3a7a', 'b'],
  cart: ['#7a6040', 'K'],
};

function drawGlyph(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, wx: number, wy: number, size: number, [colour, letter]: [string, string]): void {
  const p = worldToScreen(camera, viewport, wx, wy);
  const s = size * 0.5;
  ctx.fillStyle = colour;
  ctx.fillRect(p.x - s / 2, p.y - s / 2, s, s);
  ctx.strokeStyle = 'rgba(0, 0, 0, 0.5)';
  ctx.lineWidth = 1;
  ctx.strokeRect(p.x - s / 2 + 0.5, p.y - s / 2 + 0.5, s - 1, s - 1);
  if (s < 8) return;
  ctx.fillStyle = '#ffffff';
  ctx.font = `bold ${Math.round(s * 0.7)}px ui-monospace, Consolas, monospace`;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(letter, p.x, p.y + 1);
}

/** A machete: a pale diagonal blade on a brown hilt. */
function drawWeapon(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, wx: number, wy: number, size: number): void {
  const p = worldToScreen(camera, viewport, wx, wy);
  const s = size * 0.3;
  ctx.lineCap = 'round';
  ctx.strokeStyle = COLOURS.blade;
  ctx.lineWidth = Math.max(2, size * 0.12);
  ctx.beginPath();
  ctx.moveTo(p.x - s * 0.3, p.y + s * 0.3);
  ctx.lineTo(p.x + s, p.y - s);
  ctx.stroke();
  ctx.strokeStyle = COLOURS.hilt;
  ctx.lineWidth = Math.max(2, size * 0.14);
  ctx.beginPath();
  ctx.moveTo(p.x - s, p.y + s);
  ctx.lineTo(p.x - s * 0.3, p.y + s * 0.3);
  ctx.stroke();
  ctx.lineCap = 'butt';
}

/** A green bottle. */
function drawMedicine(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, wx: number, wy: number, size: number): void {
  const p = worldToScreen(camera, viewport, wx, wy);
  const s = size * 0.2;
  ctx.fillStyle = '#4caf6a';
  ctx.fillRect(p.x - s * 0.6, p.y - s * 0.4, s * 1.2, s * 1.4);
  ctx.fillRect(p.x - s * 0.25, p.y - s * 0.9, s * 0.5, s * 0.5);
  ctx.fillStyle = '#f4f1ea';
  ctx.fillRect(p.x - s * 0.4, p.y, s * 0.8, s * 0.35);
}

/** A steaming bowl. */
function drawMeal(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, wx: number, wy: number, size: number): void {
  const p = worldToScreen(camera, viewport, wx, wy);
  const r = size * 0.22;
  ctx.fillStyle = '#c9a26b';
  ctx.beginPath();
  ctx.arc(p.x, p.y, r, 0, Math.PI);
  ctx.fill();
  ctx.fillStyle = '#e8743b';
  ctx.fillRect(p.x - r, p.y - r * 0.15, r * 2, r * 0.3);
  ctx.strokeStyle = 'rgba(240, 240, 240, 0.7)';
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(p.x - r * 0.3, p.y - r * 0.4);
  ctx.lineTo(p.x - r * 0.1, p.y - r * 1.1);
  ctx.moveTo(p.x + r * 0.3, p.y - r * 0.4);
  ctx.lineTo(p.x + r * 0.5, p.y - r * 1.1);
  ctx.stroke();
}

/** A white pad with a red cross. */
function drawBandage(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, wx: number, wy: number, size: number): void {
  const p = worldToScreen(camera, viewport, wx, wy);
  const s = size * 0.46;
  ctx.fillStyle = COLOURS.bandage;
  ctx.fillRect(p.x - s / 2, p.y - s / 2, s, s);
  ctx.fillStyle = COLOURS.bandageCross;
  const bar = Math.max(1, s * 0.22);
  ctx.fillRect(p.x - bar / 2, p.y - s * 0.35, bar, s * 0.7);
  ctx.fillRect(p.x - s * 0.35, p.y - bar / 2, s * 0.7, bar);
}

/** A tin can: easy to tell apart from wood planks at a glance. */
function drawFood(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, wx: number, wy: number, size: number): void {
  const p = worldToScreen(camera, viewport, wx, wy);
  const w = size * 0.42;
  const h = size * 0.5;
  ctx.fillStyle = COLOURS.foodCan;
  ctx.fillRect(p.x - w / 2, p.y - h / 2, w, h);
  ctx.fillStyle = COLOURS.foodLabel;
  ctx.fillRect(p.x - w / 2, p.y - h * 0.15, w, h * 0.3);
  ctx.strokeStyle = COLOURS.foodRim;
  ctx.lineWidth = 1;
  ctx.strokeRect(p.x - w / 2 + 0.5, p.y - h / 2 + 0.5, w - 1, h - 1);
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

/** A sickly green blob with red eyes; health bar once hurt; debug line to its target. */
/** A herd on the move: a red haze with its size, drawn even in the fog once heard. */
function drawHerd(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, h: Herd, size: number): void {
  const p = worldToScreen(camera, viewport, h.x + 0.5, h.y + 0.5);
  const r = Math.max(6, size * 1.2);
  ctx.fillStyle = 'rgba(255, 90, 74, 0.25)';
  ctx.beginPath();
  ctx.arc(p.x, p.y, r, 0, Math.PI * 2);
  ctx.fill();
  ctx.strokeStyle = 'rgba(255, 90, 74, 0.8)';
  ctx.lineWidth = 1.5;
  ctx.stroke();
  ctx.fillStyle = '#ffd0c8';
  ctx.font = `bold ${Math.round(Math.max(10, size * 0.5))}px ui-monospace, Consolas, monospace`;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(String(h.size), p.x, p.y);
}

/** A greyed survivor with a "?" above: waiting to be taken in. */
function drawWanderer(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, w: Wanderer, size: number, mark = '?'): void {
  const p = worldToScreen(camera, viewport, w.x + 0.5, w.y + 0.5);
  const radius = Math.max(3, size * 0.32);
  ctx.fillStyle = COLOURS.wanderer;
  ctx.strokeStyle = COLOURS.survivorOutline;
  ctx.lineWidth = 2;
  ctx.setLineDash([3, 2]);
  ctx.beginPath();
  ctx.arc(p.x, p.y, radius, 0, Math.PI * 2);
  ctx.fill();
  ctx.stroke();
  ctx.setLineDash([]);
  ctx.fillStyle = COLOURS.wandererMark;
  ctx.font = `bold ${Math.round(Math.max(10, size * 0.5))}px ui-monospace, Consolas, monospace`;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(mark, p.x, p.y);
  if (size >= MIN_LABEL_PX) {
    ctx.fillStyle = COLOURS.label;
    ctx.font = `${Math.round(Math.min(14, size * 0.45))}px ui-monospace, Consolas, monospace`;
    ctx.textBaseline = 'bottom';
    ctx.fillText(w.name, p.x, p.y - radius - 3);
  }
}

function drawZombie(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  viewport: Viewport,
  state: SimState,
  z: Zombie,
  options: DrawOptions,
  size: number,
): void {
  const world = survivorWorldPos(z, options.alpha);
  const p = worldToScreen(camera, viewport, world.x, world.y);
  const radius = Math.max(3, size * 0.34);
  if (options.showDebug && z.targetId) {
    const target = state.survivors.find((s) => s.id === z.targetId);
    if (target) {
      const tw = survivorWorldPos(target, options.alpha);
      const tp = worldToScreen(camera, viewport, tw.x, tw.y);
      ctx.strokeStyle = COLOURS.zombieChase;
      ctx.lineWidth = 1.5;
      ctx.setLineDash([2, 3]);
      ctx.beginPath();
      ctx.moveTo(p.x, p.y);
      ctx.lineTo(tp.x, tp.y);
      ctx.stroke();
      ctx.setLineDash([]);
    }
  }
  ctx.fillStyle = z.kind === 'raider' ? '#9a5a4a' : COLOURS.zombie;
  ctx.strokeStyle = COLOURS.zombieOutline;
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.arc(p.x, p.y, radius, 0, Math.PI * 2);
  ctx.fill();
  ctx.stroke();
  const eye = Math.max(1, radius * 0.18);
  ctx.fillStyle = COLOURS.zombieEye;
  ctx.fillRect(p.x - radius * 0.4 - eye / 2, p.y - radius * 0.2, eye, eye);
  ctx.fillRect(p.x + radius * 0.4 - eye / 2, p.y - radius * 0.2, eye, eye);
  if (z.health < z.maxHealth) {
    const w = Math.max(10, size * 0.8);
    const h = Math.max(2, Math.round(size * 0.1));
    ctx.fillStyle = 'rgba(0, 0, 0, 0.65)';
    ctx.fillRect(p.x - w / 2, p.y + radius + 3, w, h);
    ctx.fillStyle = COLOURS.needLow;
    ctx.fillRect(p.x - w / 2, p.y + radius + 3, (w * z.health) / z.maxHealth, h);
  }
}

function drawSurvivor(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  viewport: Viewport,
  state: SimState,
  s: Survivor,
  options: DrawOptions,
  size: number,
): void {
  const world = survivorWorldPos(s, options.alpha);
  const p = worldToScreen(camera, viewport, world.x, world.y);
  // Children are smaller.
  const radius = Math.max(3, size * (s.childUntil !== null ? 0.22 : 0.32));

  if (s.id === options.selectedId) {
    ctx.strokeStyle = COLOURS.selected;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.arc(p.x, p.y, radius + 3, 0, Math.PI * 2);
    ctx.stroke();
  }
  const asleep = s.sleep?.asleep === true;
  // Clothing shows as the outline: brown for a coat, grey for armour; boots as a dark bar below.
  const body = s.body ? state.items.find((i) => i.id === s.body) : undefined;
  ctx.fillStyle = asleep ? COLOURS.survivorAsleep : COLOURS.survivor;
  ctx.strokeStyle = body ? (GLYPHS[body.kind]?.[0] ?? COLOURS.survivorOutline) : COLOURS.survivorOutline;
  ctx.lineWidth = body ? 3 : 2;
  ctx.beginPath();
  ctx.arc(p.x, p.y, radius, 0, Math.PI * 2);
  ctx.fill();
  ctx.stroke();
  if (s.feet) {
    ctx.fillStyle = GLYPHS.boots![0];
    ctx.fillRect(p.x - radius * 0.6, p.y + radius - 1, radius * 1.2, Math.max(2, radius * 0.3));
  }
  const carried = s.carrying ? state.items.find((i) => i.id === s.carrying) : undefined;
  if (carried) drawItem(ctx, camera, viewport, carried.kind, world.x + 0.3, world.y + 0.25, size * 0.6);
  if (s.weapon) drawWeapon(ctx, camera, viewport, world.x - 0.35, world.y + 0.05, size * 0.85);

  // Need bars under the survivor once they matter: rest (blue) below half or
  // while sleeping, food (orange) below half or while eating. Red when urgent.
  const bars: { value: number; max: number; colour: string }[] = [];
  if (s.health < HEALTH_MAX) {
    bars.push({ value: s.health, max: HEALTH_MAX, colour: s.health < HEALTH_LOW ? COLOURS.needLow : COLOURS.health });
  }
  if (asleep || s.rest < REST_MAX / 2) {
    bars.push({ value: s.rest, max: REST_MAX, colour: s.rest < TIRED ? COLOURS.needLow : COLOURS.rest });
  }
  if (s.meal || s.satiety < SATIETY_MAX / 2) {
    bars.push({ value: s.satiety, max: SATIETY_MAX, colour: s.satiety < HUNGRY ? COLOURS.needLow : COLOURS.satiety });
  }
  const w = Math.max(10, size * 0.8);
  const h = Math.max(2, Math.round(size * 0.1));
  bars.forEach((bar, i) => {
    const y = p.y + radius + 3 + i * (h + 1);
    ctx.fillStyle = 'rgba(0, 0, 0, 0.65)';
    ctx.fillRect(p.x - w / 2, y, w, h);
    ctx.fillStyle = bar.colour;
    ctx.fillRect(p.x - w / 2, y, (w * bar.value) / bar.max, h);
  });

  if (size >= MIN_LABEL_PX) {
    ctx.fillStyle = COLOURS.label;
    ctx.font = `${Math.round(Math.min(14, size * 0.45))}px ui-monospace, Consolas, monospace`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.fillText(asleep ? `${s.name} z` : s.breakdown ? `${s.name} !` : s.name, p.x, p.y - radius - 3);
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
