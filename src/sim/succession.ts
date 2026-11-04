// The death rule: when the survivor you control dies, you choose who carries on.
//   an heir   someone in your group with the resolve to step up (STEP_UP or more; no children)
//   anew      a new survivor, alone, somewhere far off in the same world, with a solo kit
//   none      nobody: watch the colony get on without you
// Until you choose, state.succession says who died, and where (the UI asks, paused).

import { logEvent } from './events';
import { getTile, isWalkable, type Point } from './grid';
import { hashRoll } from './hash';
import { spawnItem } from './items';
import { rollTraits } from './skills';
import type { SimState } from './state';
import { addSurvivor, type Survivor } from './survivors';
import { ensureGeneratedAround } from './worldgen';

export const STEP_UP = 40;
/** How far off a new start is. */
const ANEW_MIN = 60;
const ANEW_SPREAD = 30;

export interface Succession {
  name: string;
  cause: string;
  x: number;
  y: number;
}

/** Who could take over now, most resolved first. */
export function heirs(state: SimState): Survivor[] {
  return state.survivors.filter((s) => s.childUntil === null && s.resolve >= STEP_UP).sort((a, b) => b.resolve - a.resolve || a.id.localeCompare(b.id));
}

/** Called as the survivor you control dies. */
export function beginSuccession(state: SimState, s: Survivor, cause: string): void {
  state.succession = { name: s.name, cause, x: s.pos.x, y: s.pos.y };
}

/** Choose who carries on. Returns an error, or null when done. */
export function succeed(state: SimState, choice: string): string | null {
  const was = state.succession;
  if (!was) return 'nobody needs replacing';
  if (choice === 'none') {
    state.succession = null;
    logEvent(state, `Nobody takes ${was.name}'s place. The others carry on as they can.`);
    return null;
  }
  if (choice === 'anew') {
    const s = startAnew(state, was);
    if (!s) return 'nowhere to start';
    state.succession = null;
    logEvent(state, `Far from where ${was.name} fell, ${s.name} sets out alone.`);
    return null;
  }
  const heir = heirs(state).find((s) => s.id === choice);
  if (!heir) return `they don't have it in them to take over`;
  take(state, heir);
  state.succession = null;
  logEvent(state, `${heir.name} takes up where ${was.name} left off.`);
  return null;
}

function take(state: SimState, s: Survivor): void {
  state.control = { survivorId: s.id, dx: 0, dy: 0, facing: { x: 0, y: 1 }, nextSwing: 0, busy: null };
  s.order = null;
  s.goal = null;
  s.path = [];
  s.note = 'carrying on';
}

/** A new survivor far off, with something to swing and a bandage. */
function startAnew(state: SimState, near: Point): Survivor | null {
  const { width, height } = state.grid;
  const angle = hashRoll(state.tick, near.x, near.y) * Math.PI * 2;
  const dist = ANEW_MIN + hashRoll(state.tick, near.y, near.x) * ANEW_SPREAD;
  const aim = {
    x: Math.max(2, Math.min(width - 3, Math.round(near.x + Math.cos(angle) * dist))),
    y: Math.max(2, Math.min(height - 3, Math.round(near.y + Math.sin(angle) * dist))),
  };
  ensureGeneratedAround(state, aim);
  // Rings outward from the aim (it may be in a river or a wall, so not a walk from there).
  let spot: Point | null = null;
  for (let r = 0; r <= 20 && !spot; r++) {
    for (let dy = -r; dy <= r && !spot; dy++) {
      for (let dx = -r; dx <= r && !spot; dx++) {
        if (Math.max(Math.abs(dx), Math.abs(dy)) !== r) continue;
        const x = aim.x + dx;
        const y = aim.y + dy;
        if (x >= 0 && y >= 0 && x < width && y < height && getTile(state.grid, x, y) === 'floor' && isWalkable(state.grid, x, y)) spot = { x, y };
      }
    }
  }
  if (!spot) return null;
  const s = addSurvivor(state, spot.x, spot.y);
  s.traits = rollTraits(s.id);
  take(state, s);
  const weapon = spawnItem(state, 'weapon', spot.x, spot.y);
  weapon.pos = null;
  weapon.carriedBy = s.id;
  s.weapon = weapon.id;
  spawnItem(state, 'bandage', spot.x, spot.y);
  // Somewhere to see from.
  for (let y = spot.y - 6; y <= spot.y + 6; y++) for (let x = spot.x - 6; x <= spot.x + 6; x++) if (x >= 0 && y >= 0 && x < width && y < height) state.grid.explored[y * width + x] = 1;
  return s;
}
