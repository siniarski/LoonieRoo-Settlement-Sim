// seed + initial state + command log = the full game.
// Commands logged at tick T are applied before tick T advances, matching how
// live input is applied between frames.

import { applyCommand } from './commands';
import { cloneState, type LoggedCommand, type SimState } from './state';
import { tick } from './tick';

export function replay(initial: SimState, log: readonly LoggedCommand[], untilTick: number): SimState {
  const state = cloneState(initial);
  let next = 0;
  for (;;) {
    for (let entry = log[next]; entry !== undefined && entry.tick <= state.tick; entry = log[++next]) {
      if (entry.tick < state.tick) {
        throw new Error(`replay: command at tick ${entry.tick} is earlier than tick ${state.tick} (log unsorted?)`);
      }
      const result = applyCommand(state, entry.command);
      if (!result.ok) {
        throw new Error(`replay: command at tick ${entry.tick} was rejected: ${result.reason}`);
      }
    }
    if (state.tick >= untilTick) return state;
    tick(state);
  }
}
