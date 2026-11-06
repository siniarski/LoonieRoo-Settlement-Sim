// The map (M): the whole world as far as you've seen it, full screen.
//
// The ground you've explored (fogged where nobody has been), town names once you've seen their
// middle, neighbours' settlements, places of interest, cars you've seen, your people, and you
// (ringed). Your own notes (sim/mapnotes.ts): click the map to pin one, right-click a pin to take
// it off. Neighbours' errands are ringed in orange (errands.ts); caravans on ground you've seen
// are small carts (caravans.ts). Wheel to zoom, drag to pan, double-click to look there in the
// game. M or Escape closes.
// Reads state only; notes go through commands.

import type { Command } from '../sim/commands';
import { caravansOf } from '../sim/caravans';
import { errandsOf } from '../sim/errands';
import { GROUNDS } from '../sim/grid';
import { NOTE_MAX, notesOf, townNames } from '../sim/mapnotes';
import { HOSTILE } from '../sim/settlements';
import type { SimState } from '../sim/state';
import { SUB } from '../sim/motion';
import { vehiclesOf } from '../sim/vehicles';
import type { ViewState } from './view';

const RGB: Record<string, [number, number, number]> = {
  fog: [10, 11, 13],
  water: [36, 70, 94],
  wall: [150, 156, 166],
  door: [168, 113, 58],
  grass: [52, 62, 50],
  dirt: [70, 62, 52],
  road: [96, 98, 102],
  forest: [36, 70, 40],
  bridge: [110, 88, 60],
  field: [88, 80, 46],
  paving: [88, 89, 86],
  boards: [88, 72, 54],
  tiles: [78, 84, 86],
  concrete: [76, 78, 76],
};
/** Rebuild the ground picture every this many frames (exploring changes it slowly). */
const REBUILD_EVERY = 20;
/** A right-click this close (CSS px) to a pin takes it off. */
const PIN_REACH = 10;

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

export function createWorldMap(view: ViewState, command: (cmd: Command) => void) {
  const root = el('div', 'worldmap');
  root.hidden = true;
  const head = el('div', 'worldmap-head');
  const title = el('span', 'worldmap-title', 'Map');
  const hint = el('span', 'worldmap-hint', 'click: pin a note · right-click a pin: remove · wheel: zoom · drag: pan · double-click: look there · M or Esc: close');
  const close = el('button', 'hud-button', 'Close');
  close.addEventListener('click', () => (view.showMap = false));
  head.append(title, hint, close);
  const canvas = el('canvas', 'worldmap-canvas');
  const note = el('input', 'menu-input worldmap-note');
  note.maxLength = NOTE_MAX;
  note.placeholder = 'a note, then Enter';
  note.hidden = true;
  root.append(head, canvas, note);
  const ctx = canvas.getContext('2d');

  // The ground at a pixel a tile, redrawn now and then; then scaled up.
  const ground = document.createElement('canvas');
  const gctx = ground.getContext('2d');
  let frame = 0;
  /** Map view: the tile at the canvas centre, and CSS pixels per tile. */
  let cam: { x: number; y: number; zoom: number } | null = null;
  let state: SimState | null = null;
  let pending: { x: number; y: number } | null = null;
  let drag: { x: number; y: number; moved: boolean } | null = null;

  const size = () => ({ w: canvas.clientWidth, h: canvas.clientHeight });
  const toTile = (px: number, py: number) => {
    const { w, h } = size();
    return { x: cam!.x + (px - w / 2) / cam!.zoom, y: cam!.y + (py - h / 2) / cam!.zoom };
  };
  const toScreen = (tx: number, ty: number) => {
    const { w, h } = size();
    return { x: w / 2 + (tx - cam!.x) * cam!.zoom, y: h / 2 + (ty - cam!.y) * cam!.zoom };
  };
  const local = (e: MouseEvent) => {
    const r = canvas.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  };

  canvas.addEventListener('wheel', (e) => {
    e.preventDefault();
    if (!cam) return;
    const p = local(e);
    const before = toTile(p.x, p.y);
    cam.zoom = Math.max(0.5, Math.min(24, cam.zoom * Math.exp(-e.deltaY * 0.0015)));
    const after = toTile(p.x, p.y);
    cam.x += before.x - after.x;
    cam.y += before.y - after.y;
  }, { passive: false });
  canvas.addEventListener('pointerdown', (e) => {
    if (e.button !== 0) return;
    const p = local(e);
    drag = { x: p.x, y: p.y, moved: false };
  });
  canvas.addEventListener('pointermove', (e) => {
    if (!drag || !cam) return;
    const p = local(e);
    if (Math.abs(p.x - drag.x) + Math.abs(p.y - drag.y) > 3) drag.moved = true;
    if (!drag.moved) return;
    cam.x -= (p.x - drag.x) / cam.zoom;
    cam.y -= (p.y - drag.y) / cam.zoom;
    drag.x = p.x;
    drag.y = p.y;
  });
  canvas.addEventListener('pointerup', (e) => {
    const was = drag;
    drag = null;
    if (!was || was.moved || !cam || !state) return;
    // A click: a note here.
    const p = local(e);
    const t = toTile(p.x, p.y);
    const x = Math.floor(t.x);
    const y = Math.floor(t.y);
    if (x < 0 || y < 0 || x >= state.grid.width || y >= state.grid.height) return;
    pending = { x, y };
    note.hidden = false;
    note.value = '';
    note.style.left = `${Math.min(p.x, canvas.clientWidth - 220)}px`;
    note.style.top = `${p.y + 40}px`;
    note.focus();
  });
  canvas.addEventListener('dblclick', (e) => {
    if (!cam) return;
    const t = toTile(local(e).x, local(e).y);
    view.camera = { ...view.camera, x: t.x, y: t.y };
    note.hidden = true;
    pending = null;
    view.showMap = false;
  });
  canvas.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    if (!cam || !state) return;
    const p = local(e);
    for (const n of notesOf(state)) {
      const s = toScreen(n.x + 0.5, n.y + 0.5);
      if (Math.abs(s.x - p.x) <= PIN_REACH && Math.abs(s.y - p.y) <= PIN_REACH) {
        command({ type: 'removeMapNote', id: n.id });
        return;
      }
    }
  });
  note.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && pending) {
      if (note.value.trim() !== '') command({ type: 'addMapNote', x: pending.x, y: pending.y, text: note.value });
      pending = null;
      note.hidden = true;
    } else if (e.key === 'Escape') {
      pending = null;
      note.hidden = true;
    }
  });
  note.addEventListener('blur', () => {
    pending = null;
    note.hidden = true;
  });

  const rebuild = (s: SimState) => {
    const { grid } = s;
    if (!gctx) return;
    if (ground.width !== grid.width || ground.height !== grid.height) {
      ground.width = grid.width;
      ground.height = grid.height;
    }
    const image = gctx.createImageData(grid.width, grid.height);
    for (let i = 0; i < grid.width * grid.height; i++) {
      const edge = grid.edges[i * 2] || grid.edges[i * 2 + 1];
      const tile = grid.tiles[i]!;
      const c =
        grid.explored[i] !== 1 ? RGB.fog! : edge ? (edge === 2 ? RGB.door! : RGB.wall!) : tile === 'floor' ? RGB[GROUNDS[grid.ground[i]!] ?? 'grass']! : RGB[tile] ?? RGB.wall!;
      image.data[i * 4] = c[0];
      image.data[i * 4 + 1] = c[1];
      image.data[i * 4 + 2] = c[2];
      image.data[i * 4 + 3] = 255;
    }
    gctx.putImageData(image, 0, 0);
  };

  return {
    root,
    update(s: SimState) {
      root.hidden = !view.showMap;
      state = s;
      if (!view.showMap || !ctx) {
        frame = 0;
        cam = null;
        return;
      }
      const { w, h } = size();
      if (canvas.width !== Math.round(w * devicePixelRatio) || canvas.height !== Math.round(h * devicePixelRatio)) {
        canvas.width = Math.round(w * devicePixelRatio);
        canvas.height = Math.round(h * devicePixelRatio);
      }
      // Opened: centred on what the game is looking at, the whole map in view.
      if (!cam) cam = { x: view.camera.x, y: view.camera.y, zoom: Math.max(0.5, Math.min(w / s.grid.width, h / s.grid.height) * 0.95) };
      if (frame++ % REBUILD_EVERY === 0) rebuild(s);
      ctx.setTransform(devicePixelRatio, 0, 0, devicePixelRatio, 0, 0);
      ctx.fillStyle = '#0a0b0d';
      ctx.fillRect(0, 0, w, h);
      ctx.imageSmoothingEnabled = false;
      const o = toScreen(0, 0);
      ctx.drawImage(ground, o.x, o.y, s.grid.width * cam.zoom, s.grid.height * cam.zoom);
      const seen = (x: number, y: number) => s.grid.explored[Math.floor(y) * s.grid.width + Math.floor(x)] === 1;
      const label = (tx: number, ty: number, text: string, colour: string, font = '12px ui-monospace, monospace') => {
        const p = toScreen(tx, ty);
        ctx.font = font;
        ctx.textAlign = 'center';
        ctx.lineWidth = 3;
        ctx.strokeStyle = 'rgba(0, 0, 0, 0.85)';
        ctx.strokeText(text, p.x, p.y);
        ctx.fillStyle = colour;
        ctx.fillText(text, p.x, p.y);
      };
      const dot = (tx: number, ty: number, colour: string, r: number) => {
        const p = toScreen(tx, ty);
        ctx.fillStyle = colour;
        ctx.beginPath();
        ctx.arc(p.x, p.y, r, 0, Math.PI * 2);
        ctx.fill();
      };
      // Towns: named once you've seen the middle.
      for (const t of townNames(s)) if (seen(t.x, t.y)) label(t.x + 0.5, t.y + 0.5, t.name.toUpperCase(), '#e8d9a8', 'bold 14px ui-monospace, monospace');
      for (const n of s.settlements) {
        const colour = n.people <= 0 ? '#888888' : n.reputation <= HOSTILE ? '#ff7a6a' : '#7ad7ff';
        dot(n.x + 0.5, n.y + 0.5, colour, 5);
        label(n.x + 0.5, n.y - 1, n.name, colour);
      }
      for (const p of s.pointsOfInterest) {
        dot(p.x + 0.5, p.y + 0.5, '#ffd166', 3);
        if (cam.zoom >= 3) label(p.x + 0.5, p.y - 0.6, p.label, '#ffd166', '11px ui-monospace, monospace');
      }
      // Neighbours' errands for you (errands.ts): where, and what.
      for (const e of errandsOf(s)) {
        const n = s.settlements.find((x) => x.id === e.settlementId);
        const p = toScreen(e.x + 0.5, e.y + 0.5);
        ctx.strokeStyle = '#ffa24a';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(p.x, p.y, 7, 0, Math.PI * 2);
        ctx.stroke();
        label(e.x + 0.5, e.y - 1, `${e.kind === 'nest' ? 'the dead' : `missing: ${e.who}`} (${n?.name ?? '?'})`, '#ffbf80');
      }
      // Caravans on the road, on ground you've seen.
      for (const c of caravansOf(s)) {
        if (!seen(c.x, c.y)) continue;
        const p = toScreen(c.x + 0.5, c.y + 0.5);
        ctx.fillStyle = '#c49a5a';
        ctx.fillRect(p.x - 4, p.y - 3, 8, 6);
        if (cam.zoom >= 3) label(c.x + 0.5, c.y - 0.6, 'caravan', '#e0c9a0', '11px ui-monospace, monospace');
      }
      for (const v of vehiclesOf(s)) {
        if (!seen(v.pos.x, v.pos.y)) continue;
        const p = toScreen(v.at.x / SUB, v.at.y / SUB);
        ctx.fillStyle = v.health <= 0 ? '#6b6560' : '#c9c2b0';
        ctx.fillRect(p.x - 3, p.y - 2, 6, 4);
      }
      const you = s.control?.survivorId;
      for (const sv of s.survivors) {
        if (sv.id === you) continue;
        dot(sv.at.x / SUB, sv.at.y / SUB, '#ffffff', 2.5);
      }
      const me = s.survivors.find((x) => x.id === you);
      if (me) {
        const p = toScreen(me.at.x / SUB, me.at.y / SUB);
        ctx.strokeStyle = '#8cf0a4';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(p.x, p.y, 7, 0, Math.PI * 2);
        ctx.stroke();
        dot(me.at.x / SUB, me.at.y / SUB, '#ffffff', 3);
        label(me.at.x / SUB, me.at.y / SUB - 1.2, 'you', '#8cf0a4', '11px ui-monospace, monospace');
      }
      // Your notes: a pin and the words.
      for (const n of notesOf(s)) {
        const p = toScreen(n.x + 0.5, n.y + 0.5);
        ctx.fillStyle = '#ff5a4a';
        ctx.beginPath();
        ctx.arc(p.x, p.y - 6, 4, 0, Math.PI * 2);
        ctx.fill();
        ctx.strokeStyle = '#ff5a4a';
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.moveTo(p.x, p.y - 2);
        ctx.lineTo(p.x, p.y + 2);
        ctx.stroke();
        label(n.x + 0.5, n.y - 0.8 - 8 / cam.zoom, n.text, '#ffe0c8');
      }
      if (pending) dot(pending.x + 0.5, pending.y + 0.5, '#ff5a4a', 4);
    },
  };
}
