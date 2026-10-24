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

/** `onChange` runs after anything that alters state, so the view can redraw. */
export function installGameHooks(handle: GameHandle, onChange: () => void): void {
  window.game = {
    getState: () => cloneState(handle.state),
    step: (n = 1) => {
      step(handle.state, n);
      onChange();
    },
    load: (scenario) => {
      handle.state = loadScenario(scenario);
      onChange();
    },
    command: (cmd) => {
      const result = applyCommand(handle.state, cmd);
      onChange();
      return result;
    },
    dumpText: () => dumpText(handle.state),
  };
}
