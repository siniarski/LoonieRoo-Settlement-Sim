// Drawing rooms, furniture, items and blueprints. Reads sim state only.

import { worldPos } from './interp';
import type { Blueprint } from '../sim/designations';
import { growTicks, isRipe, type FarmPlot } from '../sim/farming';
import type { Furniture, Look } from '../sim/furniture';
import type { ItemKind } from '../sim/items';
import { evaluateRoom, getRoomMap, labelAt, roomAt, type Room, type RoomPurpose } from '../sim/rooms';
import type { SimState } from '../sim/state';
import type { Dog } from '../sim/dogs';
import { worldToScreen, type Camera, type Viewport } from './camera';
import { PURPOSE_NAMES, MIN_GRID_LINE_PX, MIN_LABEL_PX, tileRect } from './draw';
import { COLOURS, GLYPHS } from './palette';
type TileRange = { x0: number; y0: number; x1: number; y1: number };

/** Soft tint over designated rooms, by purpose. */
export function drawRoomTints(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, state: SimState, range: TileRange): void {
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
export function drawRoomOutlines(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, state: SimState, range: TileRange): void {
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
export function drawRoomLabels(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, state: SimState, size: number): void {
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
export function drawPlot(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, plot: FarmPlot): void {
  const r = tileRect(camera, viewport, plot.x, plot.y);
  ctx.fillStyle = COLOURS.soil;
  ctx.fillRect(r.x + 1, r.y + 1, r.w - 2, r.h - 2);
  ctx.fillStyle = COLOURS.furrow;
  for (const t of [0.3, 0.55, 0.8]) ctx.fillRect(r.x + 2, Math.round(r.y + r.h * t), r.w - 4, Math.max(1, Math.round(r.h * 0.06)));
  if (!plot.planted) return;
  const ripe = isRipe(plot);
  const grown = ripe ? 1 : plot.growth / growTicks(plot);
  const h = Math.max(2, r.h * (0.15 + 0.55 * grown));
  const w = Math.max(2, r.w * (0.12 + 0.3 * grown));
  ctx.fillStyle = ripe ? (plot.crop === 'wheat' ? '#e8c860' : plot.crop === 'herb' ? '#7ad7a0' : COLOURS.ripe) : COLOURS.sprout;
  ctx.fillRect(r.x + r.w / 2 - w / 2, r.y + r.h * 0.85 - h, w, h);
}

/**
 * Generated fittings and containers (buildings.ts): a shape and colour per look. Containers
 * also show their finds as pips, dim when searched out, and the marked outline.
 */
export function drawFixture(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, f: Furniture, size: number): void {
  const style = LOOKS[f.look ?? 'crate'];
  const r = tileRect(camera, viewport, f.x, f.y);
  const w = r.w * style[1];
  const h = r.h * style[2];
  const x = r.x + (r.w - w) / 2;
  const y = r.y + (r.h - h) / 2;
  const container = f.kind === 'crate';
  ctx.globalAlpha = container && f.loot === 0 ? 0.55 : 1;
  ctx.fillStyle = style[0];
  ctx.fillRect(x, y, w, h);
  ctx.strokeStyle = 'rgba(0, 0, 0, 0.45)';
  ctx.lineWidth = 1;
  ctx.strokeRect(x + 0.5, y + 0.5, w - 1, h - 1);
  ctx.globalAlpha = 1;
  if (!container) return;
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
    for (let i = 0; i < Math.min(f.loot, 5); i++) ctx.fillRect(x + 2 + i * (pip + 1.5), y + h - pip - 2, pip, pip);
  }
}

/** Colour, width and height (shares of a tile) per look. */
const LOOKS: Record<Look, [string, number, number]> = {
  counter: ['#6b5a48', 0.9, 0.55], icebox: ['#9aa8b0', 0.7, 0.8], shelf: ['#5e4a36', 0.9, 0.4], wardrobe: ['#5a4030', 0.9, 0.55],
  dresser: ['#6a4c34', 0.7, 0.5], cabinet: ['#7a8a8c', 0.6, 0.5], toolbox: ['#9a3a2a', 0.6, 0.4], locker: ['#5a6a7a', 0.55, 0.85],
  desk: ['#6a5038', 0.85, 0.55], pallet: ['#8a7050', 0.85, 0.85], sack: ['#a89060', 0.6, 0.6], crate: [COLOURS.crate, 0.72, 0.72],
  sofa: ['#5a4a6a', 0.9, 0.5], bed: ['#7a7a90', 0.7, 0.9], toilet: ['#c8cccc', 0.45, 0.55], bath: ['#b8bcc0', 0.85, 0.55],
  table: ['#5e4632', 0.75, 0.75], oven: ['#3a3a3c', 0.7, 0.7], tv: ['#1e1e22', 0.7, 0.25], car: ['#5a2a24', 0.95, 0.95],
  pew: ['#5a4028', 0.95, 0.35], altar: ['#c8b070', 0.8, 0.5], hay: ['#b8a050', 0.85, 0.85], tractor: ['#8a3a20', 0.9, 0.9],
  pump: ['#a0302a', 0.4, 0.6], bench: ['#6a5a40', 0.8, 0.3], cot: ['#7a8a7a', 0.6, 0.9], register: ['#4a4a4a', 0.5, 0.4],
  machine: ['#4a5058', 0.9, 0.9],
};

/** A slatted crate: dull when searched out, yellow dashed outline when marked, pips for finds left. */
export function drawCrate(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, f: Furniture, size: number): void {
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

export function drawBed(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, x: number, y: number): void {
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
export function drawWorkbench(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, f: Furniture, size: number): void {
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
export function drawTree(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, f: Furniture, size: number): void {
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
export function drawTrap(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, f: Furniture): void {
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
export function drawStove(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, f: Furniture): void {
  const r = tileRect(camera, viewport, f.x, f.y);
  const pad = Math.max(1, Math.round(r.w * 0.12));
  ctx.fillStyle = '#3a3d42';
  ctx.fillRect(r.x + pad, r.y + pad, r.w - pad * 2, r.h - pad * 2);
  ctx.fillStyle = f.stored > 0 ? '#ff9a3b' : '#6b4a2e';
  ctx.fillRect(r.x + r.w * 0.3, r.y + r.h * 0.5, r.w * 0.4, r.h * 0.25);
}

/** A square wooden table top. */
export function drawTable(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, x: number, y: number): void {
  const r = tileRect(camera, viewport, x, y);
  const pad = Math.max(1, Math.round(r.w * 0.15));
  ctx.fillStyle = COLOURS.tableTop;
  ctx.fillRect(r.x + pad, r.y + pad, r.w - pad * 2, r.h - pad * 2);
  ctx.strokeStyle = COLOURS.tableEdge;
  ctx.lineWidth = Math.max(1, r.w * 0.06);
  ctx.strokeRect(r.x + pad, r.y + pad, r.w - pad * 2, r.h - pad * 2);
}

/** Battered wall or door: a red wash and cracks that grow with the damage (0..1). */
export function drawDamage(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, x: number, y: number, amount: number): void {
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

export function drawDirt(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, x: number, y: number, size: number): void {
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

export function drawItem(
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
export function drawFire(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, x: number, y: number, tick: number): void {
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
export function drawDog(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, d: Dog, alpha: number, size: number): void {
  const w = worldPos(d, alpha);
  const wx = w.x - 0.5;
  const wy = w.y - 0.5;
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

export function drawGlyph(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, wx: number, wy: number, size: number, [colour, letter]: [string, string]): void {
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
export function drawWeapon(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, wx: number, wy: number, size: number): void {
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

export function drawBlueprint(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, bp: Blueprint, size: number): void {
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
