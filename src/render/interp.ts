// Render-time interpolation. The sim moves in whole ticks; the renderer
// blends between them using `alpha`, the fraction of the next tick that
// has elapsed. Returns the world position of the entity's centre
// (tile coordinates, +0.5 to land in the middle of the tile).

import type { Point } from '../sim/grid';
import type { Movement } from '../sim/survivors';

/** Anything that moves tile to tile: survivors and zombies. */
export interface Mobile {
  pos: Point;
  move: Movement | null;
}

export function worldPos(entity: Mobile, alpha: number): Point {
  const move = entity.move;
  if (!move) return { x: entity.pos.x + 0.5, y: entity.pos.y + 0.5 };
  const t = Math.min(1, Math.max(0, (move.progress + alpha) / move.duration));
  return {
    x: move.from.x + (move.to.x - move.from.x) * t + 0.5,
    y: move.from.y + (move.to.y - move.from.y) * t + 0.5,
  };
}

/** Kept for existing callers and tests. */
export const survivorWorldPos = worldPos;
