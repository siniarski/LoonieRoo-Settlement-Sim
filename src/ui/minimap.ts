// The minimap (M): the whole world at a glance, fogged where nobody has been.
// Survivors are white, zombies and herds you can see are red, points of
// interest yellow; the frame shows what the main view is looking at. Click
// to jump there. Reads state only; redrawn every few frames.

import { TILE_PX } from '../render/camera';
import { GROUNDS } from '../sim/grid';
import { HOSTILE } from '../sim/settlements';
import type { SimState } from '../sim/state';
import type { ViewState } from './view';

/** Longest side of the minimap, in CSS pixels. */
const MAX_PX = 180;
/** Redraw every this many frames: the world changes slowly at this scale. */
const REDRAW_EVERY = 10;

const RGB: Record<string, [number, number, number]> = {
  fog: [10, 11, 13],
  water: [36, 70, 94],
  wall: [139, 145, 156],
  door: [168, 113, 58],
  grass: [44, 52, 44],
  dirt: [58, 52, 44],
  road: [80, 82, 86],
  forest: [34, 64, 36],
  bridge: [96, 76, 52],
  field: [74, 68, 40],
  paving: [74, 75, 72],
  boards: [74, 60, 46],
  tiles: [66, 72, 74],
  concrete: [64, 66, 64],
};

export function createMinimap(view: ViewState) {
  const root = document.createElement('canvas');
  root.className = 'hud-minimap';
  const ctx = root.getContext('2d');
  let frame = 0;
  let scale = 1; // tiles per minimap pixel

  root.addEventListener('click', (e) => {
    const rect = root.getBoundingClientRect();
    const x = ((e.clientX - rect.left) / rect.width) * root.width * scale;
    const y = ((e.clientY - rect.top) / rect.height) * root.height * scale;
    view.camera = { ...view.camera, x, y };
  });

  return {
    root,
    update(state: SimState) {
      root.hidden = !view.showMinimap;
      if (!view.showMinimap || !ctx || frame++ % REDRAW_EVERY !== 0) return;
      const { grid } = state;
      scale = Math.max(1, Math.ceil(Math.max(grid.width, grid.height) / MAX_PX));
      const w = Math.ceil(grid.width / scale);
      const h = Math.ceil(grid.height / scale);
      if (root.width !== w || root.height !== h) {
        root.width = w;
        root.height = h;
        root.style.width = `${w * Math.max(1, Math.floor(MAX_PX / w))}px`;
      }
      const image = ctx.createImageData(w, h);
      for (let py = 0; py < h; py++) {
        for (let px = 0; px < w; px++) {
          const i = py * scale * grid.width + px * scale;
          const tile = grid.tiles[i]!;
          // A tile with an edge wall (or door) on its north or west side shows as that: at a pixel a
          // tile, the walls round a room still outline it.
          const edge = grid.edges[i * 2] || grid.edges[i * 2 + 1];
          const colour =
            grid.explored[i] !== 1 ? RGB.fog! : edge ? (edge === 1 ? RGB.wall! : RGB.door!) : tile === 'floor' ? RGB[GROUNDS[grid.ground[i]!] ?? 'grass']! : RGB[tile]!;
          const o = (py * w + px) * 4;
          image.data[o] = colour[0];
          image.data[o + 1] = colour[1];
          image.data[o + 2] = colour[2];
          image.data[o + 3] = 255;
        }
      }
      ctx.putImageData(image, 0, 0);
      const dot = (x: number, y: number, colour: string, size = 2) => {
        ctx.fillStyle = colour;
        ctx.fillRect(Math.floor(x / scale) - size / 2, Math.floor(y / scale) - size / 2, size, size);
      };
      const seen = (x: number, y: number) => grid.explored[y * grid.width + x] === 1;
      for (const p of state.pointsOfInterest) dot(p.x, p.y, '#ffd166', 3);
      for (const n of state.settlements) dot(n.x, n.y, n.people <= 0 ? '#777777' : n.reputation <= HOSTILE ? '#ff5a4a' : '#7ad7ff', 5);
      for (const z of state.zombies) if (seen(z.pos.x, z.pos.y)) dot(z.pos.x, z.pos.y, '#ff5a4a');
      for (const herd of state.herds) if (herd.heard || seen(herd.x, herd.y)) dot(herd.x, herd.y, '#ff5a4a', 4);
      for (const s of state.survivors) dot(s.pos.x, s.pos.y, '#ffffff');
      // What the main view shows (the canvas fills the window).
      const tiles = TILE_PX * view.camera.zoom;
      const vw = window.innerWidth / tiles / scale;
      const vh = window.innerHeight / tiles / scale;
      ctx.strokeStyle = 'rgba(140, 240, 164, 0.9)';
      ctx.lineWidth = 1;
      ctx.strokeRect(view.camera.x / scale - vw / 2 + 0.5, view.camera.y / scale - vh / 2 + 0.5, vw, vh);
    },
  };
}
