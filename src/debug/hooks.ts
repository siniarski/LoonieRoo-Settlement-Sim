// window.game: the inspection API for agents, Playwright, and the console.

import { applyCommand, type Command, type CommandResult } from '../sim/commands';
import { loadScenario, type Scenario } from '../sim/scenario';
import { cloneState, type SimState } from '../sim/state';
import { step } from '../sim/tick';
import { dumpText } from './text';

export interface GameHooks {
  getState(): SimState;
  step(n?: number): void;
  load(scenario: Scenario): void;
  command(cmd: Command): CommandResult;
  dumpText(): string;
}

declare global {
  interface Window {
    game: GameHooks;
  }
}

export interface GameHandle {
  state: SimState;
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
      options.onLoad?.();
    },
    command: (cmd) => applyCommand(handle.state, cmd),
    dumpText: () => dumpText(handle.state),
  };
}
