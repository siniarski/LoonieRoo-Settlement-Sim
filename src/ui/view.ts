// View state: everything the UI remembers that is NOT sim state (camera,
// build mode, selection, hover, speed). Kept separate so it never leaks into saves.

import type { Camera } from '../render/camera';
import type { Point } from '../sim/grid';
import type { Speed } from './loop';
import type { CategoryId, ToolId } from './tools';

export interface ViewState {
  camera: Camera;
  /** Build mode (B): the bottom build bar is open. */
  buildOpen: boolean;
  buildCategory: CategoryId;
  /** The armed build tool, or null: clicks select and right-clicks move. Only set while buildOpen. */
  tool: ToolId | null;
  selectedId: string | null;
  /** While dragging an area tool: the box being dragged out (tile corners). */
  areaDrag: { from: Point; to: Point } | null;
  /** A short message for the player (e.g. the planner's notes), shown until `until` (performance.now ms). */
  toast: { text: string; until: number } | null;
  hover: Point | null;
  speed: Speed;
  paused: boolean;
  showDebug: boolean;
  showPriorities: boolean;
  /** The Game menu (saves, files, bug report). */
  showGame: boolean;
}
