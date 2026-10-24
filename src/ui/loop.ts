// Fixed-timestep accumulator. Speed changes how many ticks run per frame,
// never the tick length (architecture rule 6).

import { TICKS_PER_SECOND } from '../sim/tick';

export type Speed = 1 | 2 | 4;
export const SPEEDS: readonly Speed[] = [1, 2, 4];

export const MS_PER_TICK = 1000 / TICKS_PER_SECOND;
/** Frames longer than this (tab in background, breakpoint) are clamped. */
export const MAX_FRAME_MS = 250;
/** Never run more than this many ticks in one frame, to avoid a spiral of death. */
export const MAX_TICKS_PER_FRAME = 40;

export interface FrameTicks {
  ticks: number;
  accumulatorMs: number;
}

export function ticksForFrame(accumulatorMs: number, frameMs: number, speed: Speed, paused: boolean): FrameTicks {
  if (paused) return { ticks: 0, accumulatorMs };
  const acc = accumulatorMs + Math.min(frameMs, MAX_FRAME_MS) * speed;
  const ticks = Math.floor(acc / MS_PER_TICK);
  if (ticks > MAX_TICKS_PER_FRAME) return { ticks: MAX_TICKS_PER_FRAME, accumulatorMs: 0 };
  return { ticks, accumulatorMs: acc - ticks * MS_PER_TICK };
}
