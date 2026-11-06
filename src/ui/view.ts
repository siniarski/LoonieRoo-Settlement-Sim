// View state: everything the UI remembers that is NOT sim state (camera,
// build mode, selection, hover, speed). Kept separate so it never leaks into saves.

import type { Camera } from '../render/camera';
import type { Point, Side } from '../sim/grid';
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
  /** Edge tools (Wall, Door): the tile border nearest the cursor, previewed (grid.ts edges). */
  hoverEdge: { x: number; y: number; side: Side } | null;
  speed: Speed;
  paused: boolean;
  showDebug: boolean;
  showPriorities: boolean;
  /** The menu screen over the game, or null when playing (menu.ts). */
  menu: 'main' | 'new' | 'character' | 'load' | null;
  /** A game is going on under the menu (so it offers Resume and Save, not Continue). */
  inGame: boolean;
  /** The New game screen's Customise section is open. */
  menuCustomOpen: boolean;
  /** The minimap (N). */
  showMinimap: boolean;
  /** The full-screen map (M: worldmap.ts). */
  showMap: boolean;
  /** The colony overview (O). */
  showOverview: boolean;
  /** The full key list under direct control (K); otherwise just a reminder. */
  showKeys: boolean;
}
