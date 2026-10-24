// Event log: notable things that happened (deaths now; attacks later),
// for the HUD feed and for tests. Plain data in state, capped so it doesn't
// grow without bound.

import type { SimState } from './state';

export interface GameEvent {
  tick: number;
  message: string;
}

export const MAX_EVENTS = 50;

export function logEvent(state: SimState, message: string): void {
  state.events.push({ tick: state.tick, message });
  if (state.events.length > MAX_EVENTS) state.events.splice(0, state.events.length - MAX_EVENTS);
}
