// Drawing people and creatures: survivors, zombies, wanderers, herds, task marks,
// and the debug paths. Reads sim state only.

import type { Task } from '../sim/talk';
import type { Point } from '../sim/grid';
import { getJob } from '../sim/jobs';
import { HUNGRY, SATIETY_MAX } from '../sim/food';
import { HEALTH_LOW, HEALTH_MAX } from '../sim/health';
import type { Zombie } from '../sim/zombies';
import { REST_MAX, TIRED } from '../sim/rest';
import type { SimState } from '../sim/state';
import type { Survivor } from '../sim/survivors';
import type { Wanderer } from '../sim/wanderers';
import type { Herd } from '../sim/herds';
import { worldToScreen, type Camera, type Viewport } from './camera';
import { survivorWorldPos } from './interp';
import { MIN_LABEL_PX, type DrawOptions, tileRect } from './draw';
import { COLOURS, GLYPHS } from './palette';
import { drawItem, drawWeapon } from './drawThings';
export function drawJobLink(
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
export function drawHerd(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, h: Herd, size: number): void {
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
export function drawWanderer(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, w: Wanderer, size: number, mark = '?'): void {
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

export function drawZombie(
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
  // Brutes are big, runners and crawlers small.
  const scale = z.variant === 'brute' ? 1.35 : z.variant === 'walker' || z.variant === 'screamer' ? 1 : 0.8;
  const radius = Math.max(3, size * 0.34 * scale);
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
  ctx.fillStyle = z.kind === 'raider' ? '#9a5a4a' : { walker: COLOURS.zombie, runner: '#8fbf5a', brute: '#4f6b3a', screamer: '#9a7ab8', crawler: '#6a7a5a' }[z.variant];
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

/** A task's spot: a gold diamond (shown through fog, like a heard herd), labelled once zoomed in. */
export function drawTask(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, t: Task, size: number): void {
  const p = worldToScreen(camera, viewport, t.x + 0.5, t.y + 0.5);
  const r = Math.max(4, size * 0.4);
  ctx.strokeStyle = COLOURS.controlled;
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(p.x, p.y - r);
  ctx.lineTo(p.x + r, p.y);
  ctx.lineTo(p.x, p.y + r);
  ctx.lineTo(p.x - r, p.y);
  ctx.closePath();
  ctx.stroke();
  if (size < MIN_LABEL_PX) return;
  ctx.fillStyle = COLOURS.controlled;
  ctx.font = `${Math.round(Math.min(13, size * 0.42))}px ui-monospace, Consolas, monospace`;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'bottom';
  ctx.fillText({ home: 'old home', bury: 'the dead', pet: 'the dog', burn: 'burn this' }[t.kind], p.x, p.y - r - 2);
}

export function drawSurvivor(
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
  const control = state.control?.survivorId === s.id ? state.control : null;
  if (control) {
    // You: a gold ring, a tick showing which way you face, and a bar while searching or eating.
    ctx.strokeStyle = COLOURS.controlled;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.arc(p.x, p.y, radius + 5, 0, Math.PI * 2);
    ctx.stroke();
    const len = Math.hypot(control.facing.x, control.facing.y);
    const fx = control.facing.x / len;
    const fy = control.facing.y / len;
    ctx.lineWidth = 3;
    ctx.beginPath();
    ctx.moveTo(p.x + fx * (radius + 2), p.y + fy * (radius + 2));
    ctx.lineTo(p.x + fx * (radius + size * 0.35), p.y + fy * (radius + size * 0.35));
    ctx.stroke();
    if (control.busy) {
      const w = Math.max(12, size);
      ctx.fillStyle = 'rgba(0, 0, 0, 0.65)';
      ctx.fillRect(p.x - w / 2, p.y - radius - 9, w, 4);
      ctx.fillStyle = COLOURS.controlled;
      ctx.fillRect(p.x - w / 2, p.y - radius - 9, (w * control.busy.progress) / control.busy.duration, 4);
    }
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
export function drawPath(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, s: Survivor, alpha: number): void {
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

export function drawGoal(ctx: CanvasRenderingContext2D, camera: Camera, viewport: Viewport, goal: Point, size: number): void {
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
