// Tool palette, speed controls, status line, and the debug panel (DOM).

import type { SimState } from '../sim/state';
import { SPEEDS } from './loop';
import { TOOLS } from './tools';
import type { ViewState } from './view';

export interface Hud {
  update(state: SimState, fps: number): void;
  dispose(): void;
}

export function createHud(root: HTMLElement, view: ViewState): Hud {
  root.innerHTML = '';

  const bar = el('div', 'hud-bar');
  const toolButtons = TOOLS.map((tool) => {
    const button = el('button', 'hud-button');
    button.textContent = `${tool.key} ${tool.label}`;
    button.dataset['tool'] = tool.id;
    button.addEventListener('click', () => (view.tool = tool.id));
    bar.append(button);
    return { id: tool.id, button };
  });

  bar.append(el('span', 'hud-sep'));

  const pauseButton = el('button', 'hud-button');
  pauseButton.addEventListener('click', () => (view.paused = !view.paused));
  bar.append(pauseButton);
  const speedButtons = SPEEDS.map((speed) => {
    const button = el('button', 'hud-button');
    button.textContent = `${speed}x`;
    button.addEventListener('click', () => {
      view.speed = speed;
      view.paused = false;
    });
    bar.append(button);
    return { speed, button };
  });

  const status = el('span', 'hud-status');
  bar.append(status);

  const debug = el('pre', 'hud-debug');
  root.append(bar, debug);

  // Only touch the DOM when text actually changes.
  let lastStatus = '';
  let lastDebug = '';

  return {
    update(state, fps) {
      toolButtons.forEach(({ id, button }) => button.classList.toggle('active', id === view.tool));
      pauseButton.textContent = view.paused ? '▶ Resume' : '❚❚ Pause';
      pauseButton.classList.toggle('active', view.paused);
      speedButtons.forEach(({ speed, button }) =>
        button.classList.toggle('active', !view.paused && speed === view.speed),
      );

      const hover = view.hover;
      const tile =
        hover && hover.x >= 0 && hover.y >= 0 && hover.x < state.grid.width && hover.y < state.grid.height
          ? state.grid.tiles[hover.y * state.grid.width + hover.x]
          : undefined;
      const text = `tick ${state.tick}  ·  ${hover && tile ? `(${hover.x}, ${hover.y}) ${tile}` : '—'}`;
      if (text !== lastStatus) status.textContent = lastStatus = text;

      debug.hidden = !view.showDebug;
      if (view.showDebug) {
        const lines = [
          `fps            ${fps.toFixed(0)}`,
          `tick           ${state.tick}`,
          `speed          ${view.paused ? 'paused' : `${view.speed}x`}`,
          `map            ${state.grid.width}x${state.grid.height}`,
          `walkability v  ${state.walkabilityVersion}`,
          `commands       ${state.commandLog.length}`,
          `camera         ${view.camera.x.toFixed(1)}, ${view.camera.y.toFixed(1)} @ ${view.camera.zoom.toFixed(2)}x`,
          `rng            ${state.rng}`,
        ].join('\n');
        if (lines !== lastDebug) debug.textContent = lastDebug = lines;
      }
    },
    dispose() {
      root.innerHTML = '';
    },
  };
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  node.className = className;
  return node;
}
