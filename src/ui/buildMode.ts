// Build mode state transitions. Pure functions over ViewState so they can be
// tested without a browser; input.ts and hud.ts both call these.

import { stepCategory, toolForKey, type CategoryId, type ToolId } from './tools';
import type { ViewState } from './view';

type BuildView = Pick<ViewState, 'buildOpen' | 'buildCategory' | 'tool' | 'selectedId'> & Partial<Pick<ViewState, 'areaDrag'>>;

export function openBuild(view: BuildView): void {
  view.buildOpen = true;
}

/** Closing build mode also drops the armed tool, back to select mode. */
export function closeBuild(view: BuildView): void {
  view.buildOpen = false;
  disarm(view);
}

export function toggleBuild(view: BuildView): void {
  if (view.buildOpen) closeBuild(view);
  else openBuild(view);
}

export function chooseCategory(view: BuildView, category: CategoryId): void {
  if (view.buildCategory !== category) view.tool = null;
  view.buildCategory = category;
  view.buildOpen = true;
}

/** Arms a tool (opening build mode on its category). Choosing the armed tool again disarms it. */
export function chooseTool(view: BuildView, tool: ToolId, category: CategoryId): void {
  view.buildOpen = true;
  view.buildCategory = category;
  view.tool = view.tool === tool ? null : tool;
}

/** Right-click or Escape with a tool armed: put the tool down (abandoning any box being dragged), stay in build mode. */
export function disarm(view: BuildView): void {
  view.tool = null;
  if (view.areaDrag) view.areaDrag = null;
}

/**
 * Keys that mean something in or for build mode. Returns true if handled.
 *   B          toggle build mode
 *   1-9        (build mode) arm the nth tool of the open category
 *   Tab / E    (build mode) next category; Shift+Tab / Q previous
 *   Escape     disarm the tool, then close build mode, then clear the selection
 */
export function handleBuildKey(view: BuildView, key: string, shift: boolean): boolean {
  if (key === 'b') {
    toggleBuild(view);
    return true;
  }
  if (key === 'Escape') {
    if (view.tool) disarm(view);
    else if (view.buildOpen) closeBuild(view);
    else view.selectedId = null;
    return true;
  }
  if (!view.buildOpen) return false;
  if (key === 'Tab' || key === 'e' || key === 'q') {
    const back = key === 'q' || (key === 'Tab' && shift);
    chooseCategory(view, stepCategory(view.buildCategory, back ? -1 : 1));
    return true;
  }
  const tool = toolForKey(view.buildCategory, key);
  if (tool) {
    chooseTool(view, tool.id, tool.category);
    return true;
  }
  return false;
}
