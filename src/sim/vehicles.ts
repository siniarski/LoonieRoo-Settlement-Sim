// Vehicles: cars you get into and drive.
//
//   where       generated towns leave cars parked in the street in front of some houses and
//               shops (lots.ts parkCar), with a little fuel and some wear, by hash.
//   getting in  use a car next to you (control.ts use): with a fuel can in your bag and room in
//               the tank, you fill it up (FUEL_PER_CAN); otherwise you get in. Use again to get
//               out, beside it. Letting go of control, or dying, puts you out too.
//   driving     W accelerates (ACCEL a tick, up to MAX_SPEED), S brakes then reverses (down to
//               -REVERSE_MAX), and nothing held lets it roll to a stop (FRICTION). A and D turn
//               one of HEADINGS steps every TURN_EVERY ticks, only while it's moving (backwards,
//               the other way). It's a circle of VEHICLE_RADIUS that slides on walls like any
//               mover ('vehicle': floor only, no doors, no windows, no barricades).
//   fuel        a point a tick moving, a point every IDLE_BURN ticks idling; empty, it won't go.
//   noise       the engine (noise 'engine') every ENGINE_NOISE_EVERY ticks while you're in it.
//   crashes     hitting a wall faster than CRASH_SPEED stops you dead and dents it
//               ((speed - CRASH_SPEED) * CRASH_DAMAGE); at 0 health it's wrecked: it won't go.
//   the dead    run one down at RAM_SPEED or more and it takes RAM_DAMAGE per unit of speed and
//               is knocked aside; the car slows by a quarter and takes RAM_WEAR. The dead can't
//               bite you through it: they batter the car instead (BATTER_DAMAGE a bite).
// Positions are fixed point (motion.ts SUB), headings index HEADING_DIRS (integers, |v| = 256).

import { logEvent } from './events';
import { getTile, type Point } from './grid';
import { getItem, removeItem } from './items';
import { unpack } from './inventory';
import { BODY_RADIUS, centre, distance2, fits, slide, tileOf } from './motion';
import { makeNoise } from './noise';
import { nearestTile } from './space';
import type { SimState } from './state';
import type { Survivor } from './survivors';
import { damageZombie, knockBack } from './zombies';

export interface Vehicle {
  id: string;
  /** Centre, in SUB units; and where it was at the tick's start (for the renderer). */
  at: Point;
  was: Point;
  /** The tile under its centre. */
  pos: Point;
  /** Index into HEADING_DIRS: 0 east, 8 south, 16 west, 24 north. */
  heading: number;
  /** SUB units a tick along the heading; negative is reversing. */
  speed: number;
  fuel: number;
  health: number;
  /** Who's driving, or null. */
  driverId: string | null;
}

export const HEADINGS = 32;
/** cos and sin of each heading, times 256, rounded: integers, the same everywhere. */
export const HEADING_DIRS: readonly (readonly [number, number])[] = [
  [256, 0], [251, 50], [237, 98], [213, 142], [181, 181], [142, 213], [98, 237], [50, 251], [0, 256], [-50, 251], [-98, 237], [-142, 213], [-181, 181], [-213, 142], [-237, 98], [-251, 50],
  [-256, 0], [-251, -50], [-237, -98], [-213, -142], [-181, -181], [-142, -213], [-98, -237], [-50, -251], [0, -256], [50, -251], [98, -237], [142, -213], [181, -181], [213, -142], [237, -98], [251, -50],
];

export const VEHICLE_RADIUS = 110;
export const MAX_SPEED = 160;
export const REVERSE_MAX = 48;
export const ACCEL = 8;
export const BRAKE = 16;
export const FRICTION = 3;
export const TURN_EVERY = 2;
/** Slower than this, it doesn't turn. */
export const TURN_MIN_SPEED = 8;
export const FUEL_MAX = 3000;
export const FUEL_PER_CAN = 1500;
export const IDLE_BURN = 4;
export const VEHICLE_HEALTH = 1000;
export const CRASH_SPEED = 40;
export const CRASH_DAMAGE = 2;
export const RAM_SPEED = 24;
export const RAM_DAMAGE = 3;
export const RAM_WEAR = 10;
export const BATTER_DAMAGE = 20;
export const ENGINE_NOISE_EVERY = 20;
/** You get into a car whose centre is this close to yours. */
export const ENTER_REACH = 400;

/** The cars on the map (older saves have none). */
export function vehiclesOf(state: SimState): Vehicle[] {
  return state.vehicles ?? [];
}

export function addVehicle(state: SimState, x: number, y: number, heading = 0, fuel = FUEL_MAX, health = VEHICLE_HEALTH): Vehicle {
  state.vehicles ??= [];
  const at = centre({ x, y });
  const v: Vehicle = { id: `v${state.nextId++}`, at, was: { ...at }, pos: { x, y }, heading: ((heading % HEADINGS) + HEADINGS) % HEADINGS, speed: 0, fuel, health, driverId: null };
  state.vehicles.push(v);
  return v;
}

/** The car this survivor is driving, if any. */
export function vehicleOf(state: SimState, s: Survivor): Vehicle | undefined {
  return vehiclesOf(state).find((v) => v.driverId === s.id);
}

/** The nearest car within reach of a point (SUB units), or undefined. */
export function vehicleNear(state: SimState, at: Point, reach = ENTER_REACH): Vehicle | undefined {
  let best: Vehicle | undefined;
  for (const v of vehiclesOf(state)) if (distance2(v.at, at) <= reach * reach && (!best || distance2(v.at, at) < distance2(best.at, at))) best = v;
  return best;
}

/** Use a car: fill it from a fuel can in your bag, or get in. Returns what happened, or why not. */
export function useVehicle(state: SimState, s: Survivor, v: Vehicle): string {
  if (v.driverId) return 'someone is driving it';
  const can = s.pack.map((id) => getItem(state, id)).find((i) => i?.kind === 'fuel');
  if (can && v.fuel <= FUEL_MAX - FUEL_PER_CAN / 2) {
    v.fuel = Math.min(FUEL_MAX, v.fuel + FUEL_PER_CAN);
    unpack(s, can.id);
    removeItem(state, can);
    return 'filled the tank';
  }
  v.driverId = s.id;
  s.move = null;
  s.at = { ...v.at };
  s.pos = { ...v.pos };
  return v.health <= 0 ? "got in: it's a wreck, it won't go" : v.fuel <= 0 ? 'got in: the tank is empty' : 'got in';
}

/** Out of the car, onto the nearest free tile beside it. */
export function leaveVehicle(state: SimState, s: Survivor): string | null {
  const v = vehicleOf(state, s);
  if (!v) return null;
  v.driverId = null;
  v.speed = 0;
  const spot = nearestTile(state.grid, v.pos, (x, y) => (x !== v.pos.x || y !== v.pos.y) && fits(state.grid, centre({ x, y }), 'free')) ?? v.pos;
  s.at = centre(spot);
  s.pos = spot;
  return 'got out';
}

/** One tick behind the wheel: the held direction is the pedals and the wheel (control.ts dx, dy). */
export function drive(state: SimState, s: Survivor, dx: number, dy: number): void {
  const v = vehicleOf(state, s);
  if (!v) return;
  const runs = v.fuel > 0 && v.health > 0;
  // Pedals: W (dy -1) forward, S (dy 1) brake then reverse; neither, it rolls to a stop.
  if (runs && dy < 0) v.speed = v.speed < 0 ? Math.min(0, v.speed + BRAKE) : Math.min(MAX_SPEED, v.speed + ACCEL);
  else if (runs && dy > 0) v.speed = v.speed > 0 ? Math.max(0, v.speed - BRAKE) : Math.max(-REVERSE_MAX, v.speed - ACCEL);
  else v.speed = v.speed > 0 ? Math.max(0, v.speed - FRICTION) : Math.min(0, v.speed + FRICTION);
  // The wheel: only while moving, and backwards it turns the other way.
  if (dx !== 0 && Math.abs(v.speed) >= TURN_MIN_SPEED && state.tick % TURN_EVERY === 0) {
    v.heading = (v.heading + dx * Math.sign(v.speed) + HEADINGS) % HEADINGS;
  }
  if (runs) {
    if (v.speed !== 0) v.fuel -= 1;
    else if (state.tick % IDLE_BURN === 0) v.fuel -= 1;
    if (v.fuel <= 0) logEvent(state, `${s.name}'s car ran out of fuel`);
    if (state.tick % ENGINE_NOISE_EVERY === 0) makeNoise(state, v.pos, 'engine');
  }
  if (v.speed !== 0) move(state, s, v);
  s.at = { ...v.at };
  s.pos = { ...v.pos };
}

function move(state: SimState, s: Survivor, v: Vehicle): void {
  const [cx, cy] = HEADING_DIRS[v.heading]!;
  const want = { x: Math.trunc((cx * v.speed) / 256), y: Math.trunc((cy * v.speed) / 256) };
  const to = slide(state.grid, v.at, want.x, want.y, 'vehicle', VEHICLE_RADIUS);
  const got = { x: to.x - v.at.x, y: to.y - v.at.y };
  v.at = to;
  v.pos = tileOf(to);
  // Stopped short (more than a third of the way lost): a crash.
  const lost = distance2(want, got) * 9 > want.x * want.x + want.y * want.y;
  if (lost) {
    const fast = Math.abs(v.speed);
    if (fast > CRASH_SPEED) {
      dent(state, v, (fast - CRASH_SPEED) * CRASH_DAMAGE);
      logEvent(state, `${s.name} crashed the car`);
    }
    v.speed = 0;
    return;
  }
  // The dead in the way.
  if (Math.abs(v.speed) < RAM_SPEED) return;
  const reach = VEHICLE_RADIUS + BODY_RADIUS;
  for (const z of [...state.zombies]) {
    if (distance2(z.at, v.at) > reach * reach) continue;
    const killed = damageZombie(state, z, Math.abs(v.speed) * RAM_DAMAGE, s);
    if (!killed) knockBack(state, z, v.at);
    v.speed = Math.trunc((v.speed * 3) / 4);
    dent(state, v, RAM_WEAR);
    if (Math.abs(v.speed) < RAM_SPEED) break;
  }
}

function dent(state: SimState, v: Vehicle, amount: number): void {
  const was = v.health;
  v.health = Math.max(0, v.health - amount);
  if (was > 0 && v.health === 0) logEvent(state, `The car at (${v.pos.x}, ${v.pos.y}) is wrecked`);
}

/** A zombie's bite at someone in a car lands on the car. True if they were in one. */
export function batterVehicle(state: SimState, s: Survivor): boolean {
  const v = vehicleOf(state, s);
  if (!v) return false;
  dent(state, v, BATTER_DAMAGE);
  return true;
}

/** Every tick: blending positions, and drivers who are gone (dead, or no longer yours) get out. */
export function updateVehicles(state: SimState): void {
  for (const v of vehiclesOf(state)) {
    v.was = { ...v.at };
    if (!v.driverId) continue;
    const s = state.survivors.find((x) => x.id === v.driverId);
    if (!s) {
      v.driverId = null;
      v.speed = 0;
    } else if (state.control?.survivorId !== s.id) leaveVehicle(state, s);
  }
}

/** A heading pointing along (dx, dy) (one of the four ways). */
export function headingOf(dx: number, dy: number): number {
  return dx > 0 ? 0 : dy > 0 ? 8 : dx < 0 ? 16 : 24;
}

/** Can a car be parked here (a floor tile it fits on)? */
export function parkable(state: SimState, p: Point): boolean {
  return getTile(state.grid, p.x, p.y) === 'floor' && fits(state.grid, centre(p), 'vehicle', VEHICLE_RADIUS);
}

