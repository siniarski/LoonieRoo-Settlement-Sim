// Auto-planner (milestone 8): "furnish this room for me".
//
// The planner is just another source of designations. It reads state and
// returns ordinary commands (plus notes explaining itself); the caller
// applies them through applyCommand like any player click. It never mutates
// state, so the command log stays the single source of truth for replays.

import type { Command } from './commands';
import { getTile, neighbours4, type Point } from './grid';
import { designationOf, evaluateRoom, labelAt, roomAt, TILES_PER_BED, type Room } from './rooms';
import type { SimState } from './state';

export interface Plan {
  commands: Command[];
  /** Human-readable explanation: what was planned, or why nothing could be. */
  notes: string[];
}

/** Plan furniture for the room containing (x, y). Pure. */
export function planFurnish(state: SimState, x: number, y: number): Plan {
  const room = roomAt(state, x, y);
  if (!room) return { commands: [], notes: ['Not an enclosed room: close it in with walls and a door first.'] };
  const designation = designationOf(state, room);
  if (!designation) return { commands: [], notes: ['This room has no purpose yet: make it Housing or Storage first.'] };

  const status = evaluateRoom(state, room, designation.purpose);
  const notes: string[] = [];
  // Problems furniture can't fix: explain them instead.
  for (const r of status.requirements) {
    if (r.met) continue;
    if (r.label === 'has a door') notes.push('Needs a door: build one in its wall.');
  }

  if (designation.purpose !== 'housing') {
    notes.push(`${designation.purpose === 'storage' ? 'Storage' : 'An armory'} needs no furniture yet.`);
    return { commands: [], notes };
  }
  return planHousing(state, room, notes);
}

/** Beds for the colony, up to the room's capacity, along walls and away from doors. */
function planHousing(state: SimState, room: Room, notes: string[]): Plan {
  const capacity = Math.floor(room.tiles.length / TILES_PER_BED);
  const inRoom = (p: Point) => labelAt(state, p.x, p.y) === room.index;
  const bedsHere =
    state.furniture.filter((f) => f.kind === 'bed' && inRoom(f)).length +
    state.blueprints.filter((b) => b.target === 'bed' && inRoom(b)).length;
  const bedsElsewhere = bedsInOtherHousing(state, room);
  const wanted = Math.min(capacity, Math.max(1, state.survivors.length - bedsElsewhere));
  const toAdd = wanted - bedsHere;

  if (capacity === 0) {
    notes.push(`Too small for a bed (needs ${TILES_PER_BED} tiles).`);
    return { commands: [], notes };
  }
  if (toAdd <= 0) {
    notes.push(`Already has ${bedsHere} bed${bedsHere === 1 ? '' : 's'}: enough for the colony.`);
    return { commands: [], notes };
  }

  const spots = bedSpots(state, room).slice(0, toAdd);
  const commands: Command[] = spots.map((p) => ({ type: 'designateBuild', x: p.x, y: p.y, structure: 'bed' }));
  notes.push(`Planned ${spots.length} bed${spots.length === 1 ? '' : 's'} (room holds ${capacity}, colony of ${state.survivors.length}).`);
  if (spots.length < toAdd) notes.push(`Only found room for ${spots.length}: clear some space.`);
  return { commands, notes };
}

function bedsInOtherHousing(state: SimState, room: Room): number {
  const housing = new Set(
    state.roomDesignations
      .filter((d) => d.purpose === 'housing')
      .map((d) => labelAt(state, d.x, d.y))
      .filter((label) => label >= 0 && label !== room.index),
  );
  return state.furniture.filter((f) => f.kind === 'bed' && housing.has(labelAt(state, f.x, f.y))).length;
}

/**
 * Candidate bed tiles, best first. A good spot is empty floor (no item,
 * furniture, plan, or plot), not next to a door (keep doorways clear), and
 * against as many walls as possible. Ties go top-to-bottom, left-to-right.
 */
function bedSpots(state: SimState, room: Room): Point[] {
  const { grid } = state;
  const taken = new Set<string>([
    ...state.items.filter((i) => i.pos).map((i) => `${i.pos!.x},${i.pos!.y}`),
    ...state.furniture.map((f) => `${f.x},${f.y}`),
    ...state.blueprints.map((b) => `${b.x},${b.y}`),
    ...state.farm.map((p) => `${p.x},${p.y}`),
  ]);
  const candidates: { p: Point; walls: number }[] = [];
  for (const i of room.tiles) {
    const p = { x: i % grid.width, y: Math.floor(i / grid.width) };
    if (taken.has(`${p.x},${p.y}`)) continue;
    const around = neighbours4(grid, p.x, p.y).map((n) => getTile(grid, n.x, n.y));
    if (around.includes('door')) continue;
    candidates.push({ p, walls: around.filter((t) => t === 'wall').length });
  }
  // Stable sort keeps scan order (y, then x) for equal wall counts.
  return candidates.sort((a, b) => b.walls - a.walls).map((c) => c.p);
}
