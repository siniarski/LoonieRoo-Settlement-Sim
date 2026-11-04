// window.game: the inspection API for agents, Playwright, and the console.

import { applyCommand, type Command, type CommandResult } from '../sim/commands';
import { loadScenario, type Scenario } from '../sim/scenario';
import { cloneState, type SimState } from '../sim/state';
import { step } from '../sim/tick';
import { dumpText } from './text';
import { CHUNK, ensureGeneratedAround } from '../sim/worldgen';

export interface GameHooks {
  getState(): SimState;
  step(n?: number): void;
  load(scenario: Scenario): void;
  command(cmd: Command): CommandResult;
  dumpText(): string;
  /** Generates every chunk of a generated world and lifts the fog (for looking at worldgen). */
  reveal(): void;
}

declare global {
  interface Window {
    game: GameHooks;
  }
}

export interface GameHandle {
  state: SimState;
  /** Where play started (scenario or loaded save): bug reports replay from here. */
  initial: SimState;
}

export interface GameHookOptions {
  /** Runs after a scenario replaces the state, e.g. to recentre the camera. */
  onLoad?: () => void;
}

export function installGameHooks(handle: GameHandle, options: GameHookOptions = {}): void {
  window.game = {
    getState: () => cloneState(handle.state),
    step: (n = 1) => step(handle.state, n),
    load: (scenario) => {
      handle.state = loadScenario(scenario);
      handle.initial = cloneState(handle.state);
      options.onLoad?.();
    },
    command: (cmd) => applyCommand(handle.state, cmd),
    dumpText: () => dumpText(handle.state),
    reveal: () => {
      const s = handle.state;
      if (!s.world) return;
      for (let y = 0; y < s.world.size; y += CHUNK) for (let x = 0; x < s.world.size; x += CHUNK) ensureGeneratedAround(s, { x, y });
      s.grid.explored.fill(1);
    },
  };
}
