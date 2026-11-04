// Render-time interpolation. The sim moves in whole ticks; the renderer
// blends between them using `alpha`, the fraction of the next tick that
// has elapsed: from where a mover was at the start of the tick to where it is
// (motion.ts). Returns the world position of its centre in tiles.

import type { Point } from '../sim/grid';
import { SUB } from '../sim/motion';

/** Anything with a precise position: survivors, zombies, dogs, animals, residents. */
export interface Mobile {
  at: Point;
  was: Point;
}

export function worldPos(entity: Mobile, alpha: number): Point {
  const a = Math.min(1, Math.max(0, alpha));
  return {
    x: (entity.was.x + (entity.at.x - entity.was.x) * a) / SUB,
    y: (entity.was.y + (entity.at.y - entity.was.y) * a) / SUB,
  };
}

/** Kept for existing callers and tests. */
export const survivorWorldPos = worldPos;
