// The only way to change the sim from outside (architecture rule 5).
// Every accepted command that changed state is appended to state.commandLog
// for replays. No-ops (painting a wall over a wall while dragging) are not
// logged, since replaying them would change nothing.

import { escape } from './escape';
import { succeed } from './succession';
import { promise } from './wishes';
import { meetResident } from './residents';
import { raidSettlement } from './colonies';
import { TALK_RANGE, talk } from './talk';
import { ORDER_JOBS, SHOUT_RANGE, describeOrder, withinOrder, type Order } from './orders';
import type { Survivor } from './survivors';
import { addBlueprint, blueprintAt, cancelBlueprint, evictFrom, hasRemovable, type FurnitureTarget } from './designations';
import { addFurniture, furnitureAt, removeFurniture } from './furniture';
import { getTile, inBounds, isWalkable, setTile, type Point, type Tile } from './grid';
import { dropItem, spawnItem } from './items';
import { releaseSurvivor } from './jobs';
import { isJobType, isPriority, type JobType, type Priority } from './jobTypes';
import { addPlot, clearPlot, isCrop, plotAt, removePlot, type Crop } from './farming';
import { SATIETY_MAX } from './food';
import { HEALTH_MAX } from './health';
import { cancelMeal, cancelTreatment, wake } from './needs';
import { REST_MAX } from './rest';
import { ROOM_PURPOSES, STORE_CATEGORIES, designationOf, roomAt, type RoomPurpose, type StoreCategory } from './rooms';
import type { SimState } from './state';
import { addSurvivor, getSurvivor, setGoal, survivorAt } from './survivors';
import { getWanderer, removeWanderer } from './wanderers';
import { cancelArming } from './weapons';
import { cancelDressing } from './clothing';
import { cancelDrink } from './water';
import { stopReading } from './books';
import { giveGift } from './settlements';
import { decidePrisoner } from './prisoners';
import { fulfilRequest } from './radio';
import { isPolicy, type Policy } from './policies';
import { amputate } from './wounds';
import { recallOutpost } from './outposts';
import { sendExpedition } from './expeditions';
import { trade } from './visitors';
import { MAX_EXPLORE_SIDE, isExplored } from './fog';
import { damageStructure, isStructure } from './breach';
import { MAX_CRAFT_TARGET, isCraftKind, type CraftKind } from './crafting';
import { addZombie, evictZombies } from './zombies';
import { logEvent } from './events';
import { ignite } from './fire';
import { controlled, isControlled, swing, use, work, aim } from './control';

export type Command =
  // Instant edits (debug, scenarios, Shift+paint).
  | { type: 'placeWall'; x: number; y: number }
  | { type: 'placeDoor'; x: number; y: number }
  | { type: 'clearTile'; x: number; y: number }
  | { type: 'placeBed'; x: number; y: number }
  | { type: 'placeTable'; x: number; y: number }
  | { type: 'placeWorkbench'; x: number; y: number }
  /** Keep this many of a craftable in stock (crafting.ts). */
  | { type: 'setCraftTarget'; kind: Exclude<CraftKind, 'meal'>; count: number }
  /** Give a bed to a survivor (their only one), or take it back with null. */
  | { type: 'assignBed'; bedId: string; survivorId: string | null }
  // Designations: become jobs for survivors.
  | { type: 'designateBuild'; x: number; y: number; structure: 'wall' | 'door' | 'barricade' | 'reinforce' | FurnitureTarget }
  | { type: 'designateRemove'; x: number; y: number }
  /** Cancels a blueprint on the tile, else removes a farm plot, else unmarks a crate there, else clears the purpose of the room the tile is in. */
  | { type: 'cancelDesignation'; x: number; y: number }
  /** Makes this floor tile a farm plot (no furniture on it), potatoes unless a crop is given. Drag it over an area. */
  | { type: 'designateFarm'; x: number; y: number; crop?: Crop }
  /** Marks every animal in a box for hunting (animals.ts). */
  | { type: 'designateHunt'; x0: number; y0: number; x1: number; y1: number }
  /** Marks the crate on this tile (if it has loot left) for scavenging. Paint it over an area. */
  | { type: 'designateScavenge'; x: number; y: number }
  | { type: 'designateRoom'; x: number; y: number; purpose: RoomPurpose }
  /** Scout a box of unexplored ground (fog.ts); corners in any order. */
  | { type: 'designateExplore'; x0: number; y0: number; x1: number; y1: number }
  // Survivors.
  | { type: 'spawnSurvivor'; x: number; y: number }
  | { type: 'moveSurvivor'; survivorId: string; x: number; y: number }
  | { type: 'setPriority'; survivorId: string; jobType: JobType; priority: Priority }
  // Wanderers waiting at the edge: take them in (they become a survivor) or send them away.
  | { type: 'acceptWanderer'; wandererId: string }
  | { type: 'dismissWanderer'; wandererId: string }
  /** Send survivors off the map to a point of interest (expeditions.ts). */
  | { type: 'sendExpedition'; survivorIds: string[]; poiId: string; outpost?: boolean }
  /** Bring an outpost home (outposts.ts). */
  | { type: 'recallOutpost'; expeditionId: string }
  /** Stockpile filter: let the storage room at (x, y) take a category of things, or not. */
  | { type: 'setStorageFilter'; x: number; y: number; category: StoreCategory; accept: boolean }
  /** A prisoner: talk them round to join, or let them go home (prisoners.ts). */
  | { type: 'recruitPrisoner'; prisonerId: string }
  | { type: 'releasePrisoner'; prisonerId: string }
  /** Day shift (sleeps at night) or night shift (sleeps by day). */
  | { type: 'setShift'; survivorId: string; shift: 'day' | 'night' }
  /** Plan a wall round a box, with a door in the middle of each side (the auto-wall planner). */
  | { type: 'planPerimeter'; x0: number; y0: number; x1: number; y1: number }
  /** Send a neighbour what they asked for on the radio (radio.ts). */
  | { type: 'fulfilRequest'; requestId: string }
  /** Switch a colony policy on or off (policies.ts). */
  | { type: 'setPolicy'; policy: Policy; on: boolean }
  /** Take off an infected survivor's bitten limb, while it can still save them (wounds.ts). */
  | { type: 'amputate'; survivorId: string }
  /** Send food to a neighbouring settlement (settlements.ts). */
  | { type: 'giftSettlement'; settlementId: string }
  /** Send your armed people to raid a neighbour for food (colonies.ts). */
  | { type: 'raidSettlement'; settlementId: string }
  /** Take a trader's offer (visitors.ts). */
  | { type: 'trade'; traderId: string; offer: number }
  /** Set a tile alight on purpose (fire.ts). */
  | { type: 'ignite'; x: number; y: number }
  /** Lock or unlock a door: raiders can't open locked ones. */
  | { type: 'toggleLock'; x: number; y: number }
  /** Open a closed door, or close an open one (not with anyone in the doorway; not if locked). */
  | { type: 'toggleDoor'; x: number; y: number }
  // Debug: set a survivor's rest (0..REST_MAX) or satiety (0..SATIETY_MAX), e.g. to make them tired/hungry now.
  | { type: 'setRest'; survivorId: string; rest: number }
  | { type: 'setSatiety'; survivorId: string; satiety: number }
  | { type: 'setHealth'; survivorId: string; health: number }
  // Debug/sandbox: drop a food item on (or near) a tile.
  | { type: 'spawnFood'; x: number; y: number }
  | { type: 'spawnWeapon'; x: number; y: number }
  // Debug: batter the wall or door at a tile, as a zombie would (it may break).
  | { type: 'damageStructure'; x: number; y: number; amount: number }
  // Debug/sandbox: put a zombie on a floor tile (they only last until dawn).
  | { type: 'spawnZombie'; x: number; y: number }
  // Direct control (control.ts): take over a survivor (null hands them back), hold a direction, swing or use towards a tile.
  | { type: 'takeControl'; survivorId: string | null }
  | { type: 'steer'; dx: number; dy: number; run?: boolean }
  /** Where you're looking (the mouse): a direction, each part an integer within AIM_MAX (control.ts). */
  | { type: 'aim'; x: number; y: number }
  | { type: 'swing'; x: number; y: number }
  | { type: 'use'; x: number; y: number }
  /** Under control: repair, barricade, reinforce or build at a tile next to you (handiwork.ts). */
  | { type: 'work'; x: number; y: number }
  /** Group orders (orders.ts): follow whoever you control, hold a spot (here, or where they stand), or settle into the colony's jobs. */
  | { type: 'setOrder'; survivorId: string; order: OrderKind; x?: number; y?: number }
  /** The same order to everyone within earshot of the survivor you control. */
  | { type: 'shout'; order: OrderKind; x?: number; y?: number }
  /** Talk to a survivor (talk.ts); without an id, the nearest one to the survivor you control. */
  | { type: 'talk'; survivorId?: string }
  /** Promise to see a survivor's wish done (wishes.ts). */
  | { type: 'promiseWish'; survivorId: string }
  /** After the one you controlled dies: an heir's id, 'anew' (someone new, far off) or 'none' (succession.ts). */
  | { type: 'succeed'; choice: string }
  /** The helicopter is down: send everyone but you, or nobody (escape.ts; everyone at once is a new game+, from the UI). */
  | { type: 'escape'; who: 'others' | 'none' };

type OrderKind = 'follow' | 'hold' | 'loot' | 'settle';

export type CommandResult = { ok: true; changed: boolean } | { ok: false; reason: string };

/** Plans the player can only make on ground someone has seen. */
const DESIGNATIONS = new Set<Command['type']>(['ignite', 'designateBuild', 'designateRemove', 'designateRoom', 'designateScavenge', 'designateFarm']);

export function applyCommand(state: SimState, command: Command): CommandResult {
  const result = execute(state, command);
  if (result.ok && result.changed) {
    state.commandLog.push({ tick: state.tick, command: { ...command } });
  }
  return result;
}

function execute(state: SimState, command: Command): CommandResult {
  if (command.type === 'takeControl') return takeControl(state, command.survivorId);
  if (command.type === 'work') {
    const s = controlled(state);
    if (!s) return reject('nobody is under control');
    if (!Number.isInteger(command.x) || !Number.isInteger(command.y)) return reject('not a tile');
    const done = work(state, s, command);
    s.note = done;
    return state.control!.busy ? changed() : reject(done);
  }
  if (command.type === 'steer' || command.type === 'swing' || command.type === 'use' || command.type === 'aim') {
    const s = controlled(state);
    if (!s) return reject('nobody is under control');
    if (command.type === 'aim') {
      const why = aim(state.control!, command.x, command.y);
      return why === null ? changed() : why === 'same' ? unchanged() : reject(why);
    }
    if (command.type === 'steer') {
      const { dx, dy } = command;
      if (![dx, dy].every((d) => d === -1 || d === 0 || d === 1)) return reject('steer takes -1, 0 or 1');
      const run = command.run === true;
      if (state.control!.dx === dx && state.control!.dy === dy && (state.control!.running === true) === run) return unchanged();
      state.control!.dx = dx;
      state.control!.dy = dy;
      state.control!.running = run;
      return changed();
    }
    if (!Number.isInteger(command.x) || !Number.isInteger(command.y)) return reject('not a tile');
    const done = command.type === 'swing' ? swing(state, s, command) : use(state, s, command);
    if (!done) return reject('nothing to do there');
    s.note = done;
    return changed();
  }
  if (command.type === 'escape') {
    if (command.who !== 'others' && command.who !== 'none') return reject('others or none');
    const why = escape(state, command.who);
    return why ? reject(why) : changed();
  }
  if (command.type === 'succeed') {
    const why = succeed(state, command.choice);
    return why ? reject(why) : changed();
  }
  if (command.type === 'promiseWish') {
    const s = getSurvivor(state, command.survivorId);
    if (!s) return reject(`no survivor ${command.survivorId}`);
    const why = promise(state, s);
    return why ? reject(why) : changed();
  }
  if (command.type === 'raidSettlement') {
    const why = raidSettlement(state, command.settlementId);
    return why ? reject(why) : changed();
  }
  if (command.type === 'talk') {
    const you = controlled(state);
    // In person, a neighbour's resident close by counts too (residents.ts).
    if (you && !command.survivorId) {
      const r = state.residents.find((x) => Math.max(Math.abs(x.pos.x - you.pos.x), Math.abs(x.pos.y - you.pos.y)) <= TALK_RANGE);
      if (r) {
        const why = meetResident(state, you, r);
        return why ? reject(why) : changed();
      }
    }
    const s = command.survivorId
      ? getSurvivor(state, command.survivorId)
      : you
        ? state.survivors.filter((o) => o !== you).sort((a, b) => Math.abs(a.pos.x - you.pos.x) + Math.abs(a.pos.y - you.pos.y) - (Math.abs(b.pos.x - you.pos.x) + Math.abs(b.pos.y - you.pos.y)))[0]
        : undefined;
    if (!s) return reject(command.survivorId ? `no survivor ${command.survivorId}` : 'nobody to talk to');
    const why = talk(state, s);
    return why ? reject(why) : changed();
  }
  if (command.type === 'setOrder') {
    const s = getSurvivor(state, command.survivorId);
    if (!s) return reject(`no survivor ${command.survivorId}`);
    const at = command.x !== undefined && command.y !== undefined ? { x: command.x, y: command.y } : s.pos;
    if ((command.order === 'hold' || command.order === 'loot') && !isWalkable(state.grid, at.x, at.y)) return reject(`(${at.x}, ${at.y}) is not walkable`);
    return giveOrder(state, s, command.order, at) ? changed() : unchanged();
  }
  if (command.type === 'shout') {
    const you = controlled(state);
    if (!you) return reject('nobody is under control');
    const heard = state.survivors.filter((s) => s !== you && Math.abs(s.pos.x - you.pos.x) + Math.abs(s.pos.y - you.pos.y) <= SHOUT_RANGE);
    let any = false;
    if (command.order === 'loot' && (command.x === undefined || command.y === undefined || !inBounds(state.grid, command.x, command.y))) return reject('loot where?');
    // Everyone holds where they stand; everyone loots the same spot.
    for (const s of heard) any = giveOrder(state, s, command.order, command.order === 'loot' ? { x: command.x!, y: command.y! } : s.pos) || any;
    return any ? changed() : unchanged();
  }
  if (command.type === 'setPriority') return setPriority(state, command.survivorId, command.jobType, command.priority);
  if (command.type === 'designateExplore') {
    const x0 = Math.max(0, Math.min(command.x0, command.x1));
    const y0 = Math.max(0, Math.min(command.y0, command.y1));
    const x1 = Math.min(state.grid.width - 1, Math.max(command.x0, command.x1), x0 + MAX_EXPLORE_SIDE - 1);
    const y1 = Math.min(state.grid.height - 1, Math.max(command.y0, command.y1), y0 + MAX_EXPLORE_SIDE - 1);
    if (![command.x0, command.y0, command.x1, command.y1].every(Number.isInteger) || x1 < x0 || y1 < y0) return reject('not a box on the map');
    let dark = false;
    for (let y = y0; y <= y1 && !dark; y++) for (let x = x0; x <= x1 && !dark; x++) dark = !isExplored(state, x, y);
    if (!dark) return reject('already explored');
    state.exploreAreas.push({ id: `e${state.nextId++}`, x0, y0, x1, y1 });
    return changed();
  }
  if (command.type === 'designateHunt') {
    const [x0, x1] = [Math.min(command.x0, command.x1), Math.max(command.x0, command.x1)];
    const [y0, y1] = [Math.min(command.y0, command.y1), Math.max(command.y0, command.y1)];
    const marked = state.animals.filter((a) => !a.hunted && a.pos.x >= x0 && a.pos.x <= x1 && a.pos.y >= y0 && a.pos.y <= y1);
    for (const a of marked) a.hunted = true;
    return marked.length > 0 ? changed() : unchanged();
  }
  if (command.type === 'assignBed') {
    const bed = state.furniture.find((f) => f.id === command.bedId && f.kind === 'bed');
    if (!bed) return reject(`no bed ${command.bedId}`);
    const who = command.survivorId;
    if (who !== null && !getSurvivor(state, who)) return reject(`no survivor ${who}`);
    if (bed.owner === who) return unchanged();
    for (const f of state.furniture) if (who !== null && f.owner === who) f.owner = null;
    bed.owner = who;
    // Someone else asleep in it (or on the way) moves out next tick.
    if (bed.occupant && bed.occupant !== who) {
      const sleeper = getSurvivor(state, bed.occupant);
      if (sleeper) wake(state, sleeper, 'their bed was given away');
    }
    return changed();
  }
  if (command.type === 'damageStructure') {
    const { x, y, amount } = command;
    if (!isStructure(getTile(state.grid, x, y))) return reject(`(${x}, ${y}) is not a wall or door`);
    if (!Number.isInteger(amount) || amount <= 0) return reject('damage must be a positive integer');
    damageStructure(state, x, y, amount);
    return changed();
  }
  if (command.type === 'setCraftTarget') {
    const { kind, count } = command;
    if (!isCraftKind(kind)) return reject(`can't craft ${String(kind)}`);
    if (!Number.isInteger(count) || count < 0 || count > MAX_CRAFT_TARGET) return reject(`target must be 0-${MAX_CRAFT_TARGET}`);
    if (state.craftTargets[kind] === count) return unchanged();
    state.craftTargets[kind] = count;
    return changed();
  }
  if (command.type === 'setStorageFilter') {
    const room = roomAt(state, command.x, command.y);
    const d = room ? designationOf(state, room) : undefined;
    if (!d || d.purpose !== 'storage') return reject('not a storage room');
    if (!STORE_CATEGORIES.includes(command.category)) return reject(`unknown category ${String(command.category)}`);
    const refuses = new Set(d.refuses ?? []);
    if (refuses.has(command.category) !== command.accept) return unchanged();
    if (command.accept) refuses.delete(command.category);
    else refuses.add(command.category);
    d.refuses = STORE_CATEGORIES.filter((c) => refuses.has(c));
    return changed();
  }
  if (command.type === 'recruitPrisoner' || command.type === 'releasePrisoner') {
    const failed = decidePrisoner(state, command.prisonerId, command.type === 'recruitPrisoner');
    return failed ? reject(failed) : changed();
  }
  if (command.type === 'setShift') {
    const s = getSurvivor(state, command.survivorId);
    if (!s) return reject(`no survivor ${command.survivorId}`);
    if (command.shift !== 'day' && command.shift !== 'night') return reject('shift is day or night');
    if (s.shift === command.shift) return unchanged();
    s.shift = command.shift;
    return changed();
  }
  if (command.type === 'planPerimeter') return planPerimeter(state, command.x0, command.y0, command.x1, command.y1);
  if (command.type === 'fulfilRequest') {
    const failed = fulfilRequest(state, command.requestId);
    return failed ? reject(failed) : changed();
  }
  if (command.type === 'setPolicy') {
    if (!isPolicy(command.policy)) return reject(`unknown policy ${String(command.policy)}`);
    const on = state.policies.includes(command.policy);
    if (on === command.on) return unchanged();
    state.policies = command.on ? [...state.policies, command.policy] : state.policies.filter((p) => p !== command.policy);
    return changed();
  }
  if (command.type === 'amputate') {
    const s = getSurvivor(state, command.survivorId);
    if (!s) return reject(`no survivor ${command.survivorId}`);
    const failed = amputate(state, s);
    return failed ? reject(failed) : changed();
  }
  if (command.type === 'recallOutpost') {
    const failed = recallOutpost(state, command.expeditionId);
    return failed ? reject(failed) : changed();
  }
  if (command.type === 'giftSettlement') {
    const failed = giveGift(state, command.settlementId);
    return failed ? reject(failed) : changed();
  }
  if (command.type === 'trade') {
    const failed = trade(state, command.traderId, command.offer);
    return failed ? reject(failed) : changed();
  }
  if (command.type === 'toggleDoor') {
    const why = toggleDoor(state, command.x, command.y);
    return why ? reject(why) : changed();
  }
  if (command.type === 'toggleLock') {
    const { x, y } = command;
    if (getTile(state.grid, x, y) !== 'door') return reject(`(${x}, ${y}) is not a door`);
    const i = y * state.grid.width + x;
    const open = state.grid.open.indexOf(i);
    if (open >= 0) state.grid.open.splice(open, 1); // a locked door is a shut one
    const at = state.grid.locked.indexOf(i);
    if (at >= 0) state.grid.locked.splice(at, 1);
    else {
      state.grid.locked.push(i);
      evictZombies(state, { x, y }); // a raider in the doorway is shut out
    }
    state.walkabilityVersion += 1; // raiders' paths change
    return changed();
  }
  if (command.type === 'sendExpedition') {
    const members = command.survivorIds.map((id) => getSurvivor(state, id));
    if (members.some((s) => !s) || new Set(command.survivorIds).size !== members.length) return reject('no such survivor');
    if (!state.pointsOfInterest.some((p) => p.id === command.poiId)) return reject(`no point of interest ${command.poiId}`);
    if ((members as Survivor[]).some((s) => s.childUntil !== null)) return reject('children stay home');
    for (const s of members as Survivor[]) {
      // Drop everything here: work, meals, errands, bed, leisure.
      if (s.jobId) releaseSurvivor(state, s, 'off on an expedition');
      wake(state, s, 'off on an expedition');
      cancelMeal(state, s, 'off on an expedition');
      cancelTreatment(state, s, 'off on an expedition');
      cancelArming(state, s, 'off on an expedition');
      cancelDressing(state, s, 'off on an expedition');
      cancelDrink(s, 'off on an expedition');
      stopReading(state, s, 'off on an expedition');
      s.leisure = null;
      s.breakdown = null;
      s.goal = null;
      s.move = null;
      s.path = [];
    }
    const failed = sendExpedition(state, members as Survivor[], command.poiId, command.outpost === true);
    return failed ? reject(failed) : changed();
  }
  if (command.type === 'acceptWanderer' || command.type === 'dismissWanderer') {
    const w = getWanderer(state, command.wandererId);
    if (!w) return reject(`no wanderer ${command.wandererId}`);
    removeWanderer(state, w);
    if (command.type === 'acceptWanderer') {
      const s = addSurvivor(state, w.x, w.y, w.name);
      s.traits = [...w.traits];
      // Met while you're out and about: they come with you.
      if (state.control) s.order = { kind: 'follow' };
      logEvent(state, `${w.name} joined the colony`);
    } else {
      logEvent(state, `${w.name} was turned away`);
    }
    return changed();
  }
  if (command.type === 'setRest') return setNeed(state, command.survivorId, 'rest', command.rest, REST_MAX);
  if (command.type === 'setSatiety') return setNeed(state, command.survivorId, 'satiety', command.satiety, SATIETY_MAX);
  if (command.type === 'setHealth') return setNeed(state, command.survivorId, 'health', command.health, HEALTH_MAX);
  const { x, y } = command;
  if (!inBounds(state.grid, x, y)) return reject(`(${x}, ${y}) is out of bounds`);
  if (DESIGNATIONS.has(command.type) && !isExplored(state, x, y)) return reject(`(${x}, ${y}) is unexplored`);
  switch (command.type) {
    case 'placeWall':
      if (survivorAt(state, x, y)) return reject(`(${x}, ${y}) is occupied`);
      return setStructure(state, x, y, 'wall');
    case 'placeDoor':
      return setStructure(state, x, y, 'door');
    case 'clearTile': {
      const f = furnitureAt(state, x, y);
      if (f) {
        removeFurniture(state, f);
        return changed();
      }
      return setStructure(state, x, y, 'floor');
    }
    case 'placeBed':
    case 'placeTable':
    case 'placeWorkbench':
      if (getTile(state.grid, x, y) !== 'floor') return reject(`(${x}, ${y}) is not floor`);
      if (furnitureAt(state, x, y)) return unchanged();
      addFurniture(state, command.type === 'placeBed' ? 'bed' : command.type === 'placeTable' ? 'table' : 'workbench', x, y);
      return changed();
    case 'designateBuild':
      return designateBuild(state, x, y, command.structure);
    case 'designateRemove':
      return designateRemove(state, x, y);
    case 'cancelDesignation':
      return cancelDesignation(state, x, y);
    case 'designateFarm': {
      if (command.crop !== undefined && !isCrop(command.crop)) return reject(`unknown crop ${String(command.crop)}`);
      if (getTile(state.grid, x, y) !== 'floor' || furnitureAt(state, x, y)) return unchanged();
      const crop = command.crop ?? 'potato';
      const existing = plotAt(state, x, y);
      if (existing?.crop === crop) return unchanged();
      if (existing) removePlot(state, existing);
      addPlot(state, x, y, crop);
      return changed();
    }
    case 'designateScavenge': {
      const crate = furnitureAt(state, x, y);
      if ((crate?.kind !== 'crate' && crate?.kind !== 'tree') || (crate.kind === 'crate' && crate.loot === 0) || crate.scavenge) return unchanged();
      crate.scavenge = true;
      return changed();
    }
    case 'designateRoom':
      return designateRoom(state, x, y, command.purpose);
    case 'ignite':
      return ignite(state, x, y) ? changed() : unchanged();
    case 'spawnSurvivor':
      return spawnSurvivor(state, x, y);
    case 'spawnZombie':
      if (getTile(state.grid, x, y) !== 'floor') return reject(`(${x}, ${y}) is not floor`);
      addZombie(state, x, y);
      return changed();
    case 'spawnFood':
    case 'spawnWeapon':
      if (!isWalkable(state.grid, x, y)) return reject(`(${x}, ${y}) is not walkable`);
      spawnItem(state, command.type === 'spawnFood' ? 'food' : 'weapon', x, y);
      return changed();
    case 'moveSurvivor':
      return moveSurvivor(state, command.survivorId, x, y);
  }
}

const reject = (reason: string): CommandResult => ({ ok: false, reason });
const changed = (): CommandResult => ({ ok: true, changed: true });
const unchanged = (): CommandResult => ({ ok: true, changed: false });

function setStructure(state: SimState, x: number, y: number, tile: Tile): CommandResult {
  if (getTile(state.grid, x, y) === 'water') return reject(`(${x}, ${y}) is water`);
  if (!setTile(state.grid, x, y, tile)) return unchanged();
  // Any wall/door/floor change can alter walkability or room boundaries.
  state.walkabilityVersion += 1;
  if (tile !== 'floor') {
    // Instant walls/doors flatten furniture (debug/sandbox behaviour, no refund).
    const f = furnitureAt(state, x, y);
    if (f) removeFurniture(state, f);
  }
  if (tile === 'wall') evictFrom(state, { x, y });
  if (tile !== 'floor') {
    evictZombies(state, { x, y });
    clearPlot(state, { x, y });
  }
  return changed();
}

function designateBuild(state: SimState, x: number, y: number, structure: 'wall' | 'door' | 'barricade' | 'reinforce' | FurnitureTarget): CommandResult {
  const existing = blueprintAt(state, x, y);
  if (existing?.target === structure) return unchanged();
  if (structure === 'reinforce') {
    if (!isStructure(getTile(state.grid, x, y)) || state.grid.reinforced.includes(y * state.grid.width + x)) return unchanged();
    if (existing) cancelBlueprint(state, existing);
    addBlueprint(state, x, y, 'reinforce');
    return changed();
  }
  if (getTile(state.grid, x, y) !== 'floor' || furnitureAt(state, x, y)) {
    return reject(`(${x}, ${y}) is not empty floor; remove what's there first`);
  }
  if (existing) cancelBlueprint(state, existing);
  addBlueprint(state, x, y, structure);
  return changed();
}

/** Wall plans round the box's edge (explored, empty floor only), a door plan mid-way along each side. */
function planPerimeter(state: SimState, ax: number, ay: number, bx: number, by: number): CommandResult {
  const x0 = Math.max(0, Math.min(ax, bx));
  const x1 = Math.min(state.grid.width - 1, Math.max(ax, bx));
  const y0 = Math.max(0, Math.min(ay, by));
  const y1 = Math.min(state.grid.height - 1, Math.max(ay, by));
  if (x1 - x0 < 2 || y1 - y0 < 2) return reject('too small to wall in');
  const mx = Math.floor((x0 + x1) / 2);
  const my = Math.floor((y0 + y1) / 2);
  let planned = 0;
  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) {
      if (x !== x0 && x !== x1 && y !== y0 && y !== y1) continue;
      if (!isExplored(state, x, y) || getTile(state.grid, x, y) !== 'floor' || furnitureAt(state, x, y) || blueprintAt(state, x, y)) continue;
      const door = (x === mx && (y === y0 || y === y1)) || (y === my && (x === x0 || x === x1));
      addBlueprint(state, x, y, door ? 'door' : 'wall');
      planned += 1;
    }
  }
  return planned > 0 ? changed() : unchanged();
}

function designateRemove(state: SimState, x: number, y: number): CommandResult {
  const existing = blueprintAt(state, x, y);
  if (existing?.target === 'floor') return unchanged();
  if (existing) {
    // Removing a planned build just cancels the plan.
    cancelBlueprint(state, existing);
    return changed();
  }
  if (!hasRemovable(state, x, y)) return unchanged();
  addBlueprint(state, x, y, 'floor');
  return changed();
}

function cancelDesignation(state: SimState, x: number, y: number): CommandResult {
  const bp = blueprintAt(state, x, y);
  if (bp) {
    cancelBlueprint(state, bp);
    return changed();
  }
  const plot = plotAt(state, x, y);
  if (plot) {
    removePlot(state, plot);
    return changed();
  }
  const crate = furnitureAt(state, x, y);
  if (crate?.kind === 'crate' || crate?.kind === 'tree') {
    if (!crate.scavenge) return unchanged();
    crate.scavenge = false;
    return changed();
  }
  const room = roomAt(state, x, y);
  const d = room ? designationOf(state, room) : state.roomDesignations.find((r) => r.x === x && r.y === y);
  if (!d) return unchanged();
  state.roomDesignations.splice(state.roomDesignations.indexOf(d), 1);
  return changed();
}

function designateRoom(state: SimState, x: number, y: number, purpose: unknown): CommandResult {
  if (!ROOM_PURPOSES.includes(purpose as RoomPurpose)) return reject(`unknown room purpose ${String(purpose)}`);
  const room = roomAt(state, x, y);
  if (!room) return reject(`(${x}, ${y}) is not inside an enclosed room`);
  const existing = designationOf(state, room);
  if (existing?.purpose === purpose) return unchanged();
  if (existing) existing.purpose = purpose as RoomPurpose;
  else state.roomDesignations.push({ id: `r${state.nextId++}`, x, y, purpose: purpose as RoomPurpose });
  return changed();
}

function spawnSurvivor(state: SimState, x: number, y: number): CommandResult {
  if (!isWalkable(state.grid, x, y)) return reject(`(${x}, ${y}) is not walkable`);
  if (survivorAt(state, x, y)) return reject(`(${x}, ${y}) is occupied`);
  addSurvivor(state, x, y);
  return changed();
}

function moveSurvivor(state: SimState, id: string, x: number, y: number): CommandResult {
  const survivor = getSurvivor(state, id);
  if (!survivor) return reject(`no survivor ${id}`);
  if (!isWalkable(state.grid, x, y)) return reject(`(${x}, ${y}) is not walkable`);
  if (survivor.goal?.x === x && survivor.goal.y === y && !survivor.jobId) return unchanged();
  if (isControlled(state, survivor)) return reject(`${survivor.name} is under your control`);
  // A direct order overrides work and sleep. The survivor returns to jobs (or bed, if still tired) after arriving.
  dropEverything(state, survivor, 'an order');
  setGoal(survivor, { x, y });
  return changed();
}

/** Stops whatever a survivor was doing: their job, sleep, meal, and so on. */
function dropEverything(state: SimState, survivor: Survivor, why: string): void {
  if (survivor.jobId) releaseSurvivor(state, survivor, `ordered elsewhere`);
  wake(state, survivor, `woken by ${why}`);
  cancelMeal(state, survivor, `meal interrupted by ${why}`);
  cancelTreatment(state, survivor, `treatment interrupted by ${why}`);
  cancelArming(state, survivor, `interrupted by ${why}`);
  cancelDressing(state, survivor, `interrupted by ${why}`);
  cancelDrink(survivor, `interrupted by ${why}`);
  stopReading(state, survivor, `interrupted by ${why}`);
  survivor.leisure = null;
}

/** Gives one survivor an order. Returns false if it changed nothing. */
function giveOrder(state: SimState, s: Survivor, kind: OrderKind, at: Point): boolean {
  if (isControlled(state, s) || s.childUntil !== null) return false;
  const order: Order | null =
    kind === 'settle' ? null : kind === 'follow' ? { kind } : kind === 'loot' ? { kind, x: at.x, y: at.y, crateId: null, progress: 0 } : { kind, x: at.x, y: at.y };
  if (JSON.stringify(order) === JSON.stringify(s.order)) return false;
  s.order = order;
  // Drop what they were doing, unless it's a job they'd still take (a fight nearby).
  const job = s.jobId ? state.jobs.find((j) => j.id === s.jobId) : undefined;
  if (order && job && !(ORDER_JOBS.includes(job.type) && withinOrder(state, s, job))) releaseSurvivor(state, s, describeOrder(order));
  if (order) {
    stopReading(state, s, describeOrder(order));
    s.leisure = null;
  }
  s.nextSeekTick = 0;
  s.note = describeOrder(order);
  return true;
}

/** Open or close a door. Returns why not, or null. */
export function toggleDoor(state: SimState, x: number, y: number): string | null {
  if (getTile(state.grid, x, y) !== 'door') return `(${x}, ${y}) is not a door`;
  const i = y * state.grid.width + x;
  const at = state.grid.open.indexOf(i);
  const passing = state.grid.passing.indexOf(i);
  if (passing >= 0) {
    // Held open for someone passing: now it's yours, and stays open.
    state.grid.passing.splice(passing, 1);
    return null;
  }
  if (at < 0) {
    if (state.grid.locked.includes(i)) return 'it\'s locked';
    state.grid.open.push(i);
  } else {
    const inTheWay = [...state.survivors, ...state.zombies, ...state.residents].some((e) => e.pos.x === x && e.pos.y === y);
    if (inTheWay) return 'someone is in the doorway';
    state.grid.open.splice(at, 1);
  }
  state.walkabilityVersion += 1; // zombies' ways in change
  return null;
}

function takeControl(state: SimState, id: string | null): CommandResult {
  if (id === null) {
    if (!state.control) return unchanged();
    const was = controlled(state);
    state.control = null;
    if (was) {
      // On their own they only carry things for a job: whatever you had them holding goes down.
      const carried = was.carrying ? state.items.find((i) => i.id === was.carrying) : undefined;
      was.carrying = null;
      if (carried) dropItem(state, carried, was.pos);
      was.note = 'back to their own devices';
    }
    return changed();
  }
  const survivor = getSurvivor(state, id);
  if (!survivor) return reject(`no survivor ${id}`);
  if (state.control?.survivorId === id) return unchanged();
  if (survivor.childUntil !== null) return reject(`${survivor.name} is a child`);
  dropEverything(state, survivor, 'you');
  survivor.goal = null;
  survivor.path = [];
  survivor.pathVersion = -1;
  survivor.note = 'under your control';
  state.control = { survivorId: id, dx: 0, dy: 0, facing: { x: 0, y: 1 }, nextSwing: 0, busy: null };
  return changed();
}

function setNeed(state: SimState, id: string, need: 'rest' | 'satiety' | 'health', value: unknown, max: number): CommandResult {
  const survivor = getSurvivor(state, id);
  if (!survivor) return reject(`no survivor ${id}`);
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 0 || value > max) {
    return reject(`${need} must be an integer 0-${max}`);
  }
  if (survivor[need] === value) return unchanged();
  survivor[need] = value;
  if (need === 'satiety') survivor.nextMealTick = 0; // re-check food straight away
  return changed();
}

function setPriority(state: SimState, id: string, jobType: unknown, priority: unknown): CommandResult {
  const survivor = getSurvivor(state, id);
  if (!survivor) return reject(`no survivor ${id}`);
  if (!isJobType(jobType)) return reject(`unknown job type ${String(jobType)}`);
  if (!isPriority(priority)) return reject(`priority must be 0-4, got ${String(priority)}`);
  if (survivor.childUntil !== null) return reject(`${survivor.name} is a child`);
  if (survivor.priorities[jobType] === priority) return unchanged();
  survivor.priorities[jobType] = priority;
  // Re-evaluate soon rather than waiting out an idle back-off.
  survivor.nextSeekTick = 0;
  return changed();
}
