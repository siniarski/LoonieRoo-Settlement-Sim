# Where things live

One line per module, by area, so `grep -n 'Worldgen' docs/modules.md` finds one. The sim is `cs/Sim/Systems/<Name>.cs`
unless a path is given: pure C#, deterministic, no Godot. Each file opens with a comment stating its rules; read that
next. The game (`godot/`) is mapped file by file in godot/README.md; a summary is at the end here.

**From the port.** File comments still name the TypeScript modules (`(worldplan.ts)`): `x.ts` is now
`cs/Sim/Systems/X.cs`, class `X`, or `XMod` where X is also a type: BodyMod, CharacterMod, ControlMod, EndgameMod,
FireMod, FurnitureMod, NatureMod, ScenarioMod, StoryMod, SuccessionMod, TimelineMod. grid.ts is `Grids` in Grid.cs.
Hand-written from the start (in `cs/Sim/`): Grid.cs, Pathfinding.cs, Space.cs, Rng.cs, Hash.cs, Time.cs, Lookup.cs,
Savefile.cs, StateJson.cs, TypedArrays.cs, JsCompat.cs. The TypeScript is at git tag `ts-final`.

## Core (`cs/Sim/`)

- **State.cs**: the state's hand-written part (the RNG it holds); fields generated (State.g.cs) from StateTypes.cs.
- **StateTypes.cs**: all state's types (InlineTypes.cs, HandTypes.cs: small shapes the TypeScript wrote inline).
- **States**: state constructors; `SCHEMA_VERSION` (77 on 2026-11-10).
- **Tick**: the order of everything each tick.
- **Commands**: the only way in (`applyCommand`); every applied command goes to `state.commandLog`.
- **Time.cs**: the clock from `state.tick`; day length scale, `onClock`, `slowTick`.
- **Rng.cs**: the sim RNG (mulberry32, one uint32 in state). **Hash.cs**: stateless `hashRoll` for chances.
- **Lookup.cs**: finding things by id without scanning (derived maps, outside state).
- **Space.cs**: spatial search helpers (nearestTile). **Grid.cs** (`Grids`): tiles, walls, doors, edges.
- **Pathfinding.cs**: A* with a `mover`, reachability labels, `crossable`, straightening.
- **Replay**: seed + initial state + command log = the game. **Savefile.cs**, **SavefileChunks.cs** (generated
  worlds keep only changed chunks), **StateJson.cs** (state as JSON).
- **ScenarioMod**: ASCII maps for tests. **Invariants**: cross-checks of survivors, jobs, items. **DebugText**: the
  ASCII renderer. **Events**: the event log (capped). **Alerts**: what needs attention, worst first (pure, read-only).
- **Arena**: debug arenas (the dead round you from the first tick).
- **The JS layer**: JsCompat.cs (Js, JsMath, JsString, JsRegExp, JsMap, JsSet, JsRecord, list extensions), Js.cs,
  TypedArrays.cs (Uint8Array etc.), GridDoubles.cs (double overloads of the hand-written modules).

## Movement

- **Motion**: precise positions (`at`, `was` in SUB = 256 a tile), free movers' `slide` against walls and edges.
- **Survivors**: tile-to-tile movement along cached A* paths. **Doors**: doors held open by whoever passes.
- **Walkers**: animals and dogs as free movers (straight at goals, A* round walls).
- **Leaps**: Space in first person: jump, vault or hop. **Trunks**: a tree's trunk, solid to free movers.
- **PoseIntents**: what a mover's body is doing, read for the renderer (docs/characters.md section 1).
- **VehicleActions** (part of Vehicles): timed car actions and anchors (get in, out, boot, bonnet).

## World

- **Worldgen**: draws chunks lazily from the plan; map types. **Terrain**: the land's large scale from the seed
  (elevation, moisture, settlement; rivers by drainage, lakes in hollows; cached, never saved).
- **NatureMod**: water, forest, trees, dirt per tile (noise, fBm, `treeChance`).
- **Worldplan**: the plan (towns, streets, roads, lots; cached, never saved). **WorldRegions**: the plan by region
  (docs/worldplan-regions.md). **Win**: a window of the map a region's plan covers.
- **Towns**: where towns stand and their streets. **TownIndex**: towns by coarse cell.
- **Roads**: country roads, network and A* routing (RoadPoint/PointD are fractional). **Lots**: what stands on a lot.
- **Buildings**: plans with rooms and furniture. **Loot**: loot tables by room and building.
- **Heights**: the ground's height in metres from the seed and plan (cached, never saved, read-only so far).
- **WorldPreview**: a small picture of a world from its seed (New game, Load, loading screens; `Savefile.readPreview`).
- **Climate**: regions, radiation. **Seasons**: weather. **Fog**: exploring. **Mapnotes**: map notes; town names.
- **Sight**: lines of sight, what the dead notice, sneaking. **Noise**: noises that wake and draw the dead.
- **Rooms**: flood fill, purposes, requirements. **Breach**: battering doors and walls. **FireMod**: fire.
- **Defences**: traps, barricades, turrets, lures, pits. **Towers**: watchtowers. **Power**, **Water**.
- **Farming**: plots and crops. **Animals**: deer, rabbits, wolf packs, bears. **Forage**: foraging.
- **Vehicles**: cars, fuel, the boot. **TimelineMod**: the world winding down (water, power, helicopter, alarms).
- **CharacterMod**: character creation. **Difficulty**: presets (start, zombies, weather, progression, nuclear
  winter, day length, map type).

## The dead

- **Zombies**: the dead's behaviour. **ZombieKinds**: runners, screamers, brutes, crawlers.
- **Herds**: the dead out in the world (dormant and migrating). **Dormant**: dormant dead bucketed by place.
- **Hordes**: the dead moving together on one flow field.

## Colony

- **Designations** -> **Jobs** (the queue, `reconcileJobs`) -> **JobChoice** (who takes what) -> **Work** (doing it).
- **JobTypes**: job types and priorities. **Items**: items (ItemNames: what things are called). **Inventory**: your
  bag and containers (InventoryGrid: every container a grid of cells, footprints, stacks, pockets, first-fit, quick
  slots). **FurnitureMod**: furniture. **Crafting**: stations and recipes. **Mess**: mess halls.
- **Medicine**, **Planner** (the auto-planner: ordinary commands), **Policies**, **Director** (storyteller),
  **EndgameMod** (cure, evacuation, sieges), **Escape** (the helicopter, new game+).

## Survivors

- **Needs**, **Food**, **Rest**, **Health**, **BodyMod** (endurance, panic, body heat, wetness), **Wellbeing**
  (social, comfort), **Mood** (thoughts), **Moodlets**, **Resolve**, **Wishes**, **Skills** (skills, traits),
  **Relationships**, **Leisure**, **Books**, **Clothing**, **Weapons**, **Wounds**, **Infection**, **Corpses**,
  **Family**, **Dogs**.

## You

- **ControlMod**: direct control (steer, swing, use, work). **Handiwork**: build, repair, forage, hunt.
- **Orders**: follow, hold, loot. **Talk** and **StoryMod**: backstories, talks, tasks.
- **SuccessionMod**: the death rule.

## Others

- **Wanderers**, **Visitors** (traders, raiders), **Prisoners**, **Radio**, **Expeditions**, **Outposts**.
- **Settlements**: reputation, gifts. **Colonies**: neighbours' off-screen life. **Residents**: their people in
  person. **Runs**: neighbours' scavenging parties. **Caravans**. **Errands**: neighbours asking you for help.

## Tests, harness, assets

- `cs/Sim.Tests`: xUnit, one file per system (Vitest.cs keeps `expect`); `scenarios/` holds the JSON maps they load.
- `cs/Harness`: balance players, bench, stress (docs/harness.md).
- `art/`: the Blender pipeline, run headless (art/README.md). `experiments/`: write-ups and pictures behind decisions.

## The game (`godot/`; file by file in godot/README.md)

- `Game.cs`: the fixed-timestep loop, input to commands, saves, the command line, which view.
- `World3D/`: first person (FirstPerson, WorldMesh chunks, Roofs, Terrain, People, Animals, Items, Furniture, Held,
  OwnBody, Trees, FarField, Bodies, Footfalls) and benches (DrawBench, ItemBench, TreeBench, MapShot).
- `Characters/`: the character lab, look lab, IK, ragdoll, VAT crowds (docs/characters.md).
- `Audio/`: sounds made in code (Synth*), played from what changes (Sounds), muffling, ambience, volume.
- `Ui/`: HUD and panels, inventory, card, build tools, menu, options (Graphics), saves, minimap, map, ScreenTabs
  (the M screen's Map and Colony tabs), WorldPreviewView, View (view state), Ui (panel kit).
- `Render/`: the 2D top-down Colony tab (Ctx a Canvas 2D-style context; Draw, DrawThings, DrawPeople; Camera; Palette).
- `SelfTest*.cs`: `--selftest`. `assets/`: imported models (characters, furniture, items, trees).
