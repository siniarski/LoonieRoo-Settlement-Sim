// View state: everything the UI remembers that is NOT sim state (camera,
// selected tool, hover, speed). Kept separate so it never leaks into saves.

import type { Camera } from '../render/camera';
import type { Point } from '../sim/grid';
import type { Speed } from './loop';
import type { ToolId } from './tools';

export interface ViewState {
  camera: Camera;
  tool: ToolId;
  selectedId: string | null;
  hover: Point | null;
  speed: Speed;
  paused: boolean;
  showDebug: boolean;
}
