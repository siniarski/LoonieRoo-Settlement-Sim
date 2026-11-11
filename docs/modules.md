# Where things live

One line per module, by folder, so `grep -n 'Worldgen' docs/modules.md` finds one. Each file opens with a comment
stating its rules; read that next. `ArchitectureTest` (cs/Sim.Tests) fails when a sim file is missing from this page.

**The shape.** Three projects, one direction:

```
godot/  (the game: draws, plays sounds, takes input)
   |  reads state; changes it only through Commands.applyCommand, Tick.step and, for debug scenes, Staging
   v
cs/Sim/ (the simulation: plain data and static functions, deterministic, no Godot)
   ^
cs/Sim.Tests/ (xUnit), cs/Harness/ (balance, bench, stress)
```

**The sim is one namespace** (`LoonieRoo.Sim`) in a folder per area: the folders are for finding things, not for
access control. A system is a static class of functions over the state (`Zombies.update(state)`); the state is plain
data (`State/StateTypes.cs`). `Core/Tick.cs` is the order everything runs in each tick: start there.

**From the port.** File comments still name the TypeScript modules (`(worldplan.ts)`): `x.ts` is now class `X` in the
folder below, or `XMod` where X is also a type: BodyMod, CharacterMod, ControlMod, EndgameMod, FireMod, FurnitureMod,
NatureMod, ScenarioMod, StoryMod, SuccessionMod, TimelineMod. grid.ts is `Grids` in Grid/Grid.cs. The TypeScript is at
git tag `ts-final`.

## The sim (`cs/Sim/`)

### `Js/`: the JavaScript-semantics layer

- **JsCompat.cs**: Js, JsMath, JsString, JsRegExp, JsMap, JsSet, JsRecord, list extensions (`push`, `filter`...).
  Saves, tests and balance depend on JS's numbers and ordering (CLAUDE.md "The C# keeps JavaScript's semantics").
- **Js.cs**: the small helpers. **TypedArrays.cs**: Uint8Array etc. **GridDoubles.cs**: double overloads of the
  hand-written modules (a JS number reaching an `int` parameter).

### `State/`: what the world is

- **StateTypes.cs**: every type in the state, plain JSON-serialisable data (rule 7). **InlineTypes.cs**,
  **HandTypes.cs**: small shapes the TypeScript wrote inline.
- **State.cs**: the state's hand-written part (the RNG it holds). **States**: state constructors; `SCHEMA_VERSION`.
- **StateJson.cs**: the state as JSON (the serializer's options).

### `Core/`: the loop and the way in

- **Tick**: the order of everything each tick. **Commands**: the only way in (`applyCommand`); every applied command
  goes to `state.commandLog`. **Replay**: seed + initial state + command log = the game.
- **Time.cs**: the clock from `state.tick`; day length scale, `onClock`, `slowTick`.
- **Rng.cs**: the sim RNG (mulberry32, one uint32 in state). **Hash.cs**: stateless `hashRoll` for chances.
- **Lookup.cs**: finding things by id without scanning (derived maps, outside state). **Events**: the event log
  (capped). **Difficulty**: presets (start, zombies, weather, progression, nuclear winter, day length, map type).

### `Save/`

- **Savefile.cs**: saves and bug reports (`bugStart`, `restartLog`, `toBugReport`, `loadFile`).
  **SavefileChunks.cs**: generated worlds keep only changed chunks. **SavefileHash.cs**: a chunk's hash as generation
  made it, so saving only remakes chunks that differ. **SavefileWriter.cs**: saves written straight to UTF-8.

### `Grid/`: tiles, paths, rooms

- **Grid.cs** (`Grids`): tiles, walls, doors, edges: the only way to the grid's per-tile layers (`getTile`, `tileAt`,
  `groundAt`, `edgeKind`, `setTile`...), which **GridLayers.cs** keeps in 32 x 32 chunks allocated where something
  differs from bare land (also `TileMask`).
- **Pathfinding.cs**: A* with a `mover`, reachability labels, `crossable`, straightening (their per-tile data in
  chunks too). **Space.cs**: spatial search helpers (nearestTile).
- **Rooms**: flood fill, purposes, requirements. **RoomLabels.cs** (`Rooms`' core): room labels in chunks,
  `computeRooms` and `updateRooms`.

### `Debug/`: tools for tests, pictures and bug hunting

- **ScenarioMod**: ASCII maps for tests. **DebugText**: the ASCII renderer. **Invariants**: cross-checks of survivors,
  jobs, items. **Arena**: debug arenas (the dead round you from the first tick).
- **Staging**: scenes set by hand for the game's debug flags (a clock, weather, a fire, a gun in your hand, lineups
  of every item and furniture, a farm, building sites, visitors, the helicopter). Not commands, never logged; the
  game calls these and never writes state itself.

### `Movement/`

- **Motion**: precise positions (`at`, `was` in SUB = 256 a tile), free movers' `slide` against walls and edges.
- **Survivors**: tile-to-tile movement along cached A* paths (making and finding one: Survivors/SurvivorRoster.cs). **Doors**: doors held open by whoever passes.
- **Walkers**: animals and dogs as free movers (straight at goals, A* round walls).
- **Leaps**: Space in first person: jump, vault or hop. **CarLeaps** (part of Leaps): over a parked car, onto it,
  walking on its roof and down off it (`control.onCar`), your height for the view. **Trunks**: a tree's trunk, solid to free movers, tile
  walkers' bodies (`at` only) and cars.
- **PoseIntents**: what a mover's body is doing, read for the renderer (docs/characters.md section 1).
- **Vehicles**: cars, fuel, the boot. **VehicleBody** (part of Vehicles): the room a car takes up, as long and wide as
  the car drawn (a row of circles), what is solid to it, turning about the rear axle, how near a point is to it.
  **VehicleParking** (part of Vehicles): the tiles under a parked car can't be walked on (a layer of the grid, not
  saved, laid in again when a state is read); park, unpark, who is moved off, which parked car is over a tile.
  **VehicleHeights** (part of Vehicles): how high a car's top is along its length and the room under it, for whoever
  climbs on or goes under one (you, the dead, animals). **VehicleActions** (part of Vehicles): timed car actions and
  anchors (get in, out, boot, bonnet).

### `Worldgen/`: making the world from a seed

- **Worldgen**: draws chunks lazily from the plan; map types; `GEN_VERSION`. **Terrain**: the land's large scale from
  the seed (elevation, moisture, settlement; rivers by drainage, lakes in hollows; cached, never saved).
- **NatureMod**: water, forest, trees, dirt per tile (noise, fBm, `treeChance`).
- **Worldplan**: the plan (towns, streets, roads, lots; cached, never saved). **WorldRegions**: the plan by region
  (docs/worldplan-regions.md). **Win**: a window of the map a region's plan covers.
- **Towns**: where towns stand and their streets. **TownIndex**: towns by coarse cell.
- **Roads**: country roads, network and A* routing (RoadPoint/PointD are fractional). **Lots**: what stands on a lot.
- **Buildings** (+ BuildingsHouses, BuildingsTrade, BuildingsCivic): plans as rooms with walls, doors and windows on
  edges, archetypes, furniture by function (docs/buildings.md). **Loot**: loot tables by room and building.
- **Heights**: the ground's height in metres from the seed and plan (cached, never saved, read-only so far).
- **WorldPreview**: a small picture of a world from its seed (New game, Load, loading screens; `Savefile.readPreview`).

### `World/`: the living world

- **Climate**: regions, radiation. **Seasons**: weather. **Weather**: temperature in degrees for any place and tick
  (derived, agrees with the cold and hot rules). **Snow**: depth by climate region, trodden tracks (capped), the
  slowing of deep snow (`state.snow`).
- **Growth**: foliage as a pure function of the tile and the growing days since it was cleared (grass, weeds, bushes,
  saplings that become trees, cracks and tufts on roads); the saved part is `state.cleared` (docs/trees.md "Growth").
- **FireMod**: fire, and wildfire through grass, undergrowth and trees (fuel from the tile, season and weather; scars
  in `state.burnt`; `settings.wildfire`; bounded by budgets and a far rule).
- **Lights**: room lights behind switches, porch lights, street lamps, headlights, the flashlight, fire; what is lit
  and by what (`near`, `lightAt`, `isLit`). **Power**, **Water**. **TimelineMod**: the world winding down (water,
  power, helicopter, alarms).
- **Sight**: lines of sight, what the dead notice, sneaking. **Noise**: noises that wake and draw the dead.
- **Fog**: exploring. **Mapnotes**: map notes; town names.
- **Farming**: plots and crops. **Forage**: foraging. **Animals**: deer, rabbits, wolf packs, bears.
  **AnimalCars**: animals and dogs at parked cars: under (rabbits; a dog under the pickup) or over, by kind and car.

### `Combat/`

- **Weapons**: wielding, swings, reloads. **WeaponTable**: every weapon as data (damage, reach, magazine, noise).
  **Shooting**: how a shot flies (a line with spread from a stateless hash).
- **Wounds**, **Infection**, **Corpses**.
- **Breach**: battering doors and walls. **Defences**: traps, barricades, turrets, lures, pits. **Towers**:
  watchtowers.

### `Dead/`

- **Zombies**: the dead's behaviour: the tick, moving, planning, noticing, hitting and being hurt. **ZombieSpawn**
  (part of Zombies): how many arrive each night, when and where. **ZombieCrowd** (part of Zombies): who is near whom
  (the dead by tile, survivors within a bite, bodies pushed apart). **ZombieKinds**: runners, screamers, brutes,
  crawlers.
- **Herds**: the dead out in the world (dormant and migrating). **Dormant**: dormant dead bucketed by place.
- **Hordes**: the dead moving together on one flow field.
- **Crossings**: the dead over parked cars (a crawler under a pickup) and runners over barricades: who, how long, what paths pay.

### `Items/`

- **Items**: items (**ItemNames**: what things are called). **Inventory**: your bag and containers
  (**InventoryGrid**: every container a grid of cells, footprints, stacks, pockets, first-fit, quick slots).
- **FurnitureMod**: furniture. **Crafting**: stations and recipes.

### `Colony/`

- **Designations** -> **Jobs** (the queue, `reconcileJobs`) -> **JobChoice** (who takes what) -> **Work** (doing it).
  **JobTypes**: job types and priorities. The model: docs/sim-model.md.
- **Mess**: mess halls. **Medicine**. **Planner** (the auto-planner: ordinary commands). **Policies**.
- **Director** (storyteller). **EndgameMod** (cure, evacuation, sieges). **Escape** (the helicopter, new game+).
- **Alerts**: what needs attention, worst first (pure, read-only).

### `Survivors/`

- **Needs**, **Food**, **Rest**, **Health**, **BodyMod** (endurance, panic, body heat, wetness), **Wellbeing**
  (social, comfort), **Mood** (thoughts), **Moodlets**, **Resolve**, **Wishes**, **Skills** (skills, traits),
  **Relationships**, **Leisure**, **Books**, **Clothing**, **Family**, **Dogs**.
- **SurvivorRoster** (part of Survivors, whose walking is Movement/Survivors.cs): a new survivor, the names handed
  out, finding one by id or tile.
- **CharacterMod**: character creation. **CharacterSheet**: who a survivor is and what state they are in, as data and
  as text (pure, read-only; the Tab screen draws it).

### `You/`: the survivor you play

- **ControlMod**: direct control (steer, swing, use, work). **Handiwork**: build, repair, forage, hunt.
- **Orders**: follow, hold, loot. **Talk** and **StoryMod**: backstories, talks, tasks.
- **SuccessionMod**: the death rule.

### `Others/`: everyone else

- **Wanderers**, **Visitors** (traders, raiders), **Prisoners**, **Radio**, **Expeditions**, **Outposts**.
- **Settlements**: reputation, gifts. **Colonies**: neighbours' off-screen life. **Residents**: their people in
  person. **Runs**: neighbours' scavenging parties. **Caravans**. **Errands**: neighbours asking you for help.

## Tests, harness, assets

- `cs/Sim.Tests`: xUnit, flat: `XTest.cs` is system X's (Vitest.cs keeps `expect`); `scenarios/` holds the JSON maps
  they load. Some files ported from the TypeScript cover several systems under an old name: ArmsTest and GunsTest
  (weapons), BalanceTest (trees, yields), BedsTest, CartsTest, CookingTest, FittingsTest (a house's fittings),
  HungerTest (food), InjuriesTest (wounds), LandTest (heaters, land), NeighboursTest (caravans, settlements),
  PeopleTest (books, traits), ScavengeTest, StorageTest, ThreatsTest (zombie kinds), ToolsRadioTest, WildlifeTest
  (animals), World2Test (map types). **ArchitectureTest**: the rules above, checked against the source.
  **SeamsTest**: where separately built features meet.
- `cs/Harness`: balance players, bench, stress (docs/harness.md).
- `art/`: the Blender pipeline, run headless (art/README.md). `experiments/`: write-ups and pictures behind decisions.

## The game (`godot/`; file by file in godot/README.md)

- `App/`: the game itself, class `Game` in parts: `Game.cs` (what it is made of; the frame: ticks, then the views),
  `GameArgs.cs` (the command line and debug flags), `GameSession.cs` (new, load, save, bug reports, the menu's
  actions), `GameInput.cs` (keys and mouse into commands), `GameShots.cs` (`--shot`), `GameText.cs` (`--trace`).
- `World3D/`: first person (FirstPerson, WorldMesh chunks, Roofs, Terrain, People, Animals, Items, Furniture, Held,
  OwnBody, Trees, Undergrowth, FarField, Bodies, Footfalls, Lights, Weather, Fires: flames, smoke, embers, scorch and
  charred stumps in a fixed few nodes; ModelSet: a file of models drawn in batches, used by Fields: farm plots and
  crops, Sites: blueprints, Cars, Aircraft: the helicopter, Others: stand-in bodies for wanderers, traders,
  prisoners, caravans and scavenging parties; Cockpit: the car you drive from the inside, its instruments drawn by
  GaugeCluster).
- `Characters/`: what a body needs at runtime: looks and wardrobe, gaits, foot and hand IK, head look, stumps, the
  merged mid tier and VAT crowds (docs/characters.md).
- `Audio/`: sounds made in code (Synth*), played from what changes (Sounds), muffling, ambience, volume.
- `Ui/`: HUD and panels, inventory, character sheet, card, build tools, menu, options (Graphics), saves, minimap,
  map, ScreenTabs (the M screen's Map and Colony tabs), WorldPreviewView, View (view state), Ui (panel kit).
- `Render/`: the 2D top-down Colony tab (Ctx a Canvas 2D-style context; Draw, DrawThings, DrawPeople; Camera; Palette).
- `SelfTest/`: `--selftest`, the game driven by real input and checked, a file per feature.
- `Labs/`: scenes run by path, never part of the game: the character lab (Lab*), LookLab, StyleLineup, AnimTest, the
  benches (Bench, HordeBench, DrawBench, ItemBench, TreeBench) and MapShot.
- `assets/`: imported models (characters, furniture, items, trees, world, cars) and shaders.
