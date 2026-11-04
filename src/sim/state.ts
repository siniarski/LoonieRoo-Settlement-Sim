// Sim state types and constructors. Everything here must stay plain
// JSON-compatible data (architecture rule 7).

import type { Command } from './commands';
import type { Blueprint } from './designations';
import type { GameEvent } from './events';
import type { FarmPlot } from './farming';
import type { Furniture } from './furniture';
import { createGrid, type Grid } from './grid';
import type { Item } from './items';
import type { Job } from './jobs';
import { seedRng } from './rng';
import type { RoomDesignation } from './rooms';
import type { Survivor } from './survivors';
import type { Wanderer } from './wanderers';
import type { CraftKind } from './crafting';
import type { ExploreArea, PointOfInterest } from './fog';
import type { DormantZombie, Herd } from './herds';
import type { WorldInfo } from './worldgen';
import type { Weather } from './seasons';
import type { Expedition } from './expeditions';
import type { Trader } from './visitors';
import type { Fire } from './fire';
import type { Knowledge } from './books';
import type { Dog } from './dogs';
import type { Settlement } from './settlements';
import type { Resident } from './residents';
import type { Animal } from './animals';
import type { Prisoner } from './prisoners';
import type { SupplyRequest } from './radio';
import { newEndgame, type Endgame } from './endgame';
import type { Policy } from './policies';
import type { Zombie } from './zombies';
import type { Control } from './control';
import type { Succession } from './succession';
import type { Task } from './talk';
import { NORMAL, type Settings } from './difficulty';

/**
 * Bump when the shape of SimState changes. Hot reload and (later) save
 * loading discard state with a different version instead of crashing on it.
 */
export const SCHEMA_VERSION = 66;

export interface LoggedCommand {
  /** The tick the command was applied at (before that tick advanced). */
  tick: number;
  command: Command;
}

export interface SimState {
  schemaVersion: number;
  tick: number;
  /** mulberry32 state, a uint32. */
  rng: number;
  grid: Grid;
  /** Bumped whenever a wall or door changes. Caches compare against it. */
  walkabilityVersion: number;
  survivors: Survivor[];
  zombies: Zombie[];
  /** People waiting at the edge to be taken in (see wanderers.ts). */
  wanderers: Wanderer[];
  /** Stock to keep of each craftable (see crafting.ts). */
  craftTargets: Record<Exclude<CraftKind, 'meal'>, number>;
  /** Zombies due tonight, fixed at nightfall (zombies.ts); null by day. */
  zombiesTonight: number | null;
  /** Boxes the player wants scouted (fog.ts). */
  exploreAreas: ExploreArea[];
  /** Zombies asleep in the world, not yet real (herds.ts). */
  dormant: DormantZombie[];
  /** Herds on the move, as markers (herds.ts). */
  herds: Herd[];
  /** Things scouts found worth a note: ruins with loot, and so on (fog.ts). */
  pointsOfInterest: PointOfInterest[];
  /** Generated worlds: what's been generated so far (worldgen.ts). Null on hand-made maps. */
  world: WorldInfo | null;
  /** Today's weather, rolled at dawn (seasons.ts). */
  weather: Weather;
  /** Survivors away off the map (expeditions.ts). */
  expeditions: Expedition[];
  /** Traders waiting to swap goods (visitors.ts). */
  traders: Trader[];
  /** Burning tiles (fire.ts). */
  fires: Fire[];
  /**
   * The harsher survival rules: corpses and sickness, thirst, spoiling food. On in generated
   * worlds; hand-made maps opt in (so tests of other things don't have to feed and water everyone).
   */
  harsh: boolean;
  /** Difficulty: the preset or sandbox settings this world was made with (difficulty.ts). */
  settings: Settings;
  /** What the colony has learned from books (books.ts). */
  knowledge: Knowledge[];
  dogs: Dog[];
  /** Neighbours in a generated world (settlements.ts). */
  settlements: Settlement[];
  /** Neighbours' people, in their unfolded compounds (residents.ts). */
  residents: Resident[];
  /** Wild animals (animals.ts). */
  animals: Animal[];
  /** When the storyteller last left a cache (director.ts). */
  lastRelief: number;
  /** Raiders held in the cells (prisoners.ts). */
  prisoners: Prisoner[];
  /** Neighbours' asks, heard on the radio (radio.ts). */
  requests: SupplyRequest[];
  /** The two ways to win (endgame.ts). */
  endgame: Endgame;
  /** Colony policies switched on (policies.ts). */
  policies: Policy[];
  /** The colony's leader, settled each dawn (policies.ts). */
  leaderId: string | null;
  /** The survivor the player is steering directly, if any (control.ts). */
  control: Control | null;
  /** The one you controlled has died: who carries on? (succession.ts) */
  succession: Succession | null;
  /** What survivors have asked you to do (talk.ts). */
  tasks: Task[];
  /** Tiles foraged today, by tile index -> day (handiwork.ts); cleared at dawn. */
  foraged: Record<string, number>;
  items: Item[];
  furniture: Furniture[];
  /** Farm plots (see farming.ts). */
  farm: FarmPlot[];
  blueprints: Blueprint[];
  /** Player-assigned room purposes, anchored to a tile. Rooms themselves are derived (rooms.ts). */
  roomDesignations: RoomDesignation[];
  /** The shared job queue. Derived from blueprints and dirt by reconcileJobs each tick. */
  jobs: Job[];
  /** Next numeric id for entities. Ids are strings like "s1", "i7", "b12", "j20". */
  nextId: number;
  /** How many survivor names have been handed out (survivors.ts). */
  namesGiven: number;
  /** Every command that changed state, in order. seed + initial state + this = replay. */
  commandLog: LoggedCommand[];
  /** Notable happenings (deaths, ...), newest last, capped. See events.ts. */
  events: GameEvent[];
}

export interface NewStateOptions {
  seed: number;
  width: number;
  height: number;
}

export function createState({ seed, width, height }: NewStateOptions): SimState {
  return {
    schemaVersion: SCHEMA_VERSION,
    tick: 0,
    rng: seedRng(seed),
    grid: createGrid(width, height),
    walkabilityVersion: 0,
    survivors: [],
    zombies: [],
    wanderers: [],
    craftTargets: { weapon: 0, bandage: 0, medicine: 0, ammo: 0, gun: 0, coat: 0, armour: 0, boots: 0, cart: 0, axe: 0, hammer: 0, medkit: 0 },
    zombiesTonight: null,
    exploreAreas: [],
    dormant: [],
    herds: [],
    pointsOfInterest: [],
    world: null,
    weather: 'clear',
    expeditions: [],
    traders: [],
    fires: [],
    harsh: false,
    settings: { ...NORMAL },
    knowledge: [],
    dogs: [],
    settlements: [],
    residents: [],
    animals: [],
    lastRelief: -1e9,
    prisoners: [],
    requests: [],
    endgame: newEndgame(),
    policies: [],
    leaderId: null,
    control: null,
    succession: null,
    tasks: [],
    foraged: {},
    items: [],
    furniture: [],
    farm: [],
    blueprints: [],
    roomDesignations: [],
    jobs: [],
    nextId: 1,
    namesGiven: 0,
    commandLog: [],
    events: [],
  };
}

/** Deep copy via JSON. Doubles as a check that state is JSON-safe. */
export function cloneState(state: SimState): SimState {
  return JSON.parse(JSON.stringify(state)) as SimState;
}
