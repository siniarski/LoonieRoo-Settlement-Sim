import { describe, expect, it } from 'vitest';
import { MAX_FRAME_MS, MAX_TICKS_PER_FRAME, MS_PER_TICK, ticksForFrame } from '../src/ui/loop';

describe('fixed-timestep loop', () => {
  it('runs one tick per MS_PER_TICK at 1x and carries the remainder', () => {
    const frame = ticksForFrame(0, MS_PER_TICK * 2.5, 1, false);
    expect(frame.ticks).toBe(2);
    expect(frame.accumulatorMs).toBeCloseTo(MS_PER_TICK * 0.5);
  });

  it('accumulates across short frames', () => {
    let acc = 0;
    let total = 0;
    for (let i = 0; i < 60; i++) {
      const frame = ticksForFrame(acc, 1000 / 60, 1, false);
      acc = frame.accumulatorMs;
      total += frame.ticks;
    }
    // One second at 1x is TICKS_PER_SECOND ticks (allow float drift of one).
    expect(total).toBeGreaterThanOrEqual(9);
    expect(total).toBeLessThanOrEqual(10);
  });

  it('scales tick count with speed, not tick length', () => {
    expect(ticksForFrame(0, 100, 1, false).ticks).toBe(1);
    expect(ticksForFrame(0, 100, 2, false).ticks).toBe(2);
    expect(ticksForFrame(0, 100, 4, false).ticks).toBe(4);
  });

  it('runs nothing while paused and keeps the accumulator', () => {
    expect(ticksForFrame(42, 1000, 4, true)).toEqual({ ticks: 0, accumulatorMs: 42 });
  });

  it('clamps long frames', () => {
    const frame = ticksForFrame(0, 10_000, 1, false);
    expect(frame.ticks).toBe(Math.floor(MAX_FRAME_MS / MS_PER_TICK));
  });

  it('caps ticks per frame and drops the backlog', () => {
    const frame = ticksForFrame(MS_PER_TICK * 100, 16, 4, false);
    expect(frame).toEqual({ ticks: MAX_TICKS_PER_FRAME, accumulatorMs: 0 });
  });
});
