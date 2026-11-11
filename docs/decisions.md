# Decisions

Every design decision so far, with its reasoning, by area. Moved here from the decisions table in NOTES.md on 2026-11-10
(one entry per row of the old table, the wording unchanged). Read the section your task touches; `grep -n '^- \*\*Fire'`
finds one entry. Decisions about the vision and the look are in docs/design.md; newer decisions are in the session log
(`log/`), and NOTES.md "Start here" says what holds now. Entries keep the browser era's names where they were written
then (`sim/planner.ts` is now `cs/Sim/Colony/Planner.cs`: docs/modules.md).

## Project context (from initial planning)

- **Goal:** a learning and exploration project, not a shipping product. If it proves fun, port to another engine later
  for better platform support. Optimise for iteration speed and a good developer feedback loop.
- **Developer background:** a decade of software engineering, very little game dev (one Unity game, one simple Godot POC).
- **Game concept:** a macro-scale, 2D grid settlement survival sim. Inspirations: SimCity (emergent cities), Project
  Zomboid (emergent disasters), State of Decay (base and survivor management), Prison Architect and RimWorld
  (designation and job systems).
- **POC focus:** find or claim houses, designate rooms and their purposes (housing, mess hall, barracks, weapon storage),
  and either let the settlement self-organise (auto-planner) or micro-manage placement and cleanup. Each survivor has
  per-job-type priorities (scavenging, cleaning, zombie defence, etc.).
- **Core tension to preserve:** bigger settlement produces more, but a bigger perimeter is harder to defend. Tight
  fortress vs. risky expansion.

## Platform, tooling and the browser era

Mostly history: the TypeScript build is at git tag `ts-final`. Kept for the reasoning.

- **Platform.** Web, then Godot 4.6 .NET (2026-11-08)
  *Why:* The browser was the fastest loop while the design moved; ported once the sim settled (port steps 1-5 in the session log)
- **Language / tooling.** TypeScript, Vite, Vitest
  *Why:* Agents are strong in TS; HMR for instant feedback; headless sim tests
- **Renderer.** Canvas 2D, top-down
  *Why:* Readability matters more than prettiness when debugging job queues. PixiJS is a possible later swap
- **Isometric / 3D.** First person, a top-down map and colony tab; no isometric (2026-11-08)
  *Why:* Projection is purely a renderer concern; sim stays in grid coordinates
- **Engine (Godot etc.).** Godot, the sim in pure C#
  *Why:* Ported by translating the TypeScript and checking it state for state; the TypeScript retired at git tag ts-final
- **Hot reload.** `main.ts` self-accepts; on dispose it stashes sim + view state in `import.meta.hot.data` and tears down the app (RAF, listeners, HUD)
  *Why:* Editing any module (sim, render, ui) swaps code without losing the map you're building
- **Agent hooks.** `window.view.tileToScreen(x, y)` and `getView()` alongside `window.game`
  *Why:* Lets Playwright/agents click a specific tile without camera maths
- **Frame loop.** Next frame is scheduled before the work; errors are caught and logged once each
  *Why:* One bad frame (often a half-finished edit) used to kill the loop permanently
- **Rule enforcement.** `tsconfig.sim.json` (no DOM lib) + `tests/architecture.test.ts` (source scan)
  *Why:* Rules 2 and 3 fail the build instead of relying on review
- **Toolchain.** Node 24 LTS, TypeScript 7, Vite 8, Vitest 5
  *Why:* Current versions as of 2026-10-24
- **Vitest `fsModuleCache`.** Disabled
  *Why:* It doesn't refresh `import.meta.glob` when files are added, so the architecture scan missed new sim files. Suite runs in ~200ms, so the cache isn't needed
- **View state.** Camera, tool, hover, speed, pause live in `ui/view.ts`, never in sim state
  *Why:* Saves and replays stay free of UI concerns

## The sim's foundations

- **The grid's layers live in chunks, behind `Grids`** (2026-11-10; docs/worldplan-regions.md "The second job").
  Tiles, ground, explored, dirt, damage and edges are 32 x 32 chunk slots allocated where something differs from
  the layer's fill; nothing outside Grid.cs, GridLayers.cs and the save code touches them. The tile index
  (y * width + x) and JSON (flat arrays) are unchanged. A layer holds bytes (damage: doubles): a ground or edge kind
  outside 0..255 throws at the write.
  *Why:* memory and saves follow what was generated, not the map's area (10,000: 4.4 GB to 28 MB after createWorld).
- **Derived per-tile data is in chunks too, allocated where it is used**: room labels (copied on write), reachability
  labels (a bare chunk is one component, never flooded), A*'s and nearestTile's scratch (dropped past ~100 MB).
  *Why:* a played 10,000 world held 7.5 GB of it and its first tick took 12 s.
- **Rooms' change list holds up to 65,536 tiles (was 2,048)** before rooms are worked out in full. Either way gives
  the same rooms (tested against the plain whole-map flood, `Rooms.computeRoomsWhole`).
  *Why:* an update now costs what it touches, and generating a few town chunks at once passed 2,048.

- **Sim model.** Pure, deterministic, fixed tick (default 10/sec)
  *Why:* Enables headless runs, replayable bugs, trivial speed controls
- **Automation model.** Auto mode is a planner that emits the same designations the player does
  *Why:* Build manual first; auto is a layer on top, not a separate system
- **Replays.** Seed + initial state + `{tick, command}[]` log
  *Why:* Falls out of determinism; bug reports become files
- **RNG.** mulberry32, state is a single uint32 in sim state
  *Why:* Tiny, fast, trivially serialisable
- **State mutation.** Sim functions mutate the state they are given; `cloneState` (JSON round trip) when a copy is needed
  *Why:* Simpler than immutable updates; determinism doesn't need immutability. Replays clone the initial state first
- **Command log contents.** Only commands that changed state are logged (not rejected ones, not no-ops)
  *Why:* Drag-painting over existing walls would otherwise flood the log; skipped entries change nothing on replay
- **Schema version.** `SimState.schemaVersion`; hot reload discards state with a different version
  *Why:* Milestone 2's hot state crashed milestone 3 code (`survivors` missing). Bump it whenever SimState's shape changes
- **Invariant checker.** `sim/invariants.ts` cross-checks survivors/jobs/items/blueprints; tests run it every tick, the debug panel shows it live
  *Why:* Turns "two survivors on one job" style bugs into a red test
- **End-of-tick reconcile.** Room anchors and needs (beds/food) are reconciled at the end of each tick as well as the start
  *Why:* Work finished mid-tick (wall on an anchor, bed dismantled under a sleeper) must not leave dangling references between ticks; the fuzz test caught both
- **Room anchors between ticks.** `reconcileRoomDesignations` also runs at the end of each tick
  *Why:* A wall finished mid-tick on an anchor tile left it on the wall until the next tick; the fuzz test caught it
- **Event log.** `state.events` (capped at 50), shown as a feed under the status line
  *Why:* Generic: deaths now, attacks later
- **Harsh rules.** `state.harsh`: on in generated worlds, off on hand-made maps (tests of other things don't have to feed and water everyone; the new tests opt in). Covers corpses,
  sickness, thirst, spoilage and births
  *Why:* Kept 400+ older tests' numbers stable
- **Save and load.** Game menu (≡ Game): 3 slots plus an autosave at every dawn (06:00, after the zombies leave), all in localStorage; Export save / Import file; Report bug.
  Formats live in `sim/savefile.ts` (pure, tested): a save is `{format, schemaVersion, state}`; a bug report is the state play started from (`handle.initial`: the scenario or the
  last loaded save) plus the commands since and the tick, and importing one replays it. Other schema versions are refused, not migrated. Loading pauses the game
  *Why:* Long games need saves; bug reports turn "it got stuck" into a file that reproduces it
- **Fast saves (2026-11-10, schema 78).** Saves are written straight to UTF-8 (`SaveWriter`), never as JSON nodes, in the
  same layout byte for byte. Each generated chunk keeps its hash as made (`WorldInfo.made`); a save makes again only chunks
  whose hash now differs. The hashes are remade on every load from the chunks loading makes (so they always match this
  build's generation, and a schema-77 save gains them); a file of an older schema loads only if its state says the same
  schema (77 and up). A bug report's start is the packed state (`Savefile.bugStart`), not a copy of the state
  *Why:* a 512 save took ~0.5 s and a fully explored 2048 ~7 s; a whole copy for bug reports won't fit at 10,000
- **The ground's height (2026-11-09).** A pure height field in the sim (`Heights.cs`), derived from the seed and the world plan, cached by 32-tile chunk, never saved (no schema
  bump); metres at tile corners, bilinear between; read-only (nothing in the tick reads it)
  *Why:* In the sim so line of sight, movement cost and water can use it later without a second source of truth, and the renderer only reads it (rule 5). Derived like the plan, so
  saves don't grow. Read-only until the player OKs effects on play, after the big stress test (storeys and slopes as data come after it). Large relief adds in at `Heights.relief`

## Movement and pathfinding

- **Movement.** 4-way, `TICKS_PER_TILE = 4` (0.4s/tile at 1x). Next step starts on the same tick the previous one ends
  *Why:* No per-tile stutter. Confirmed by playtest: 0.4s/tile feels right, no diagonals for now
- **A* tie-breaking.** Lower f, then lower h, then insertion order; neighbours N, E, S, W
  *Why:* Deterministic paths, needed for replays
- **Path cache.** Per-survivor `path` + `pathVersion`; checked at every tile boundary, full repath on any walkability change
  *Why:* Simplest correct scheme. Fine for a handful of survivors; revisit with many survivors or big maps
- **Path/room invalidation.** One `walkabilityVersion` counter
  *Why:* Simplest thing that works; refine if too coarse
- **Reachability.** Connected-component labels cached per grid + walkabilityVersion (WeakMap, outside sim state)
  *Why:* Unreachable candidates cost O(1) instead of a failed A* that floods the map
- **Unreachable goals.** Survivor drops the goal, goes idle, `note` says why
  *Why:* Never stuck; the debug panel answers "why is she standing there"
- **Walls on survivors.** `placeWall` is rejected on a tile a survivor is on or stepping onto
  *Why:* Survivors can never end up inside a wall. Doors are still allowed
- **Doors.** Always walkable; act as room boundaries for flood fill
  *Why:* Locking/factions deferred
- **Roads.** Road and bridge tiles are a tick quicker to walk (min 2 a tile)
  *Why:* Roads matter a little
- **Over and onto parked cars (2026-11-11).** Space at a parked car within CAR_REACH (320 SUB, a swing's reach: walking
  into a car's end stops you at its tiles, most of a tile short of its body) goes over it when you are walking into it
  and a body fits past the far side, and climbs onto it when you are standing still (or there is no room past it). No
  new key: walking into a thing to go over it and standing to climb it is how a body does it. The kind of crossing (a
  slide over the bonnet or boot, the bed, the roof) is where your line crosses the car along its length; the landing is
  fixed and checked at take-off, in integers. On top is one plain field, `control.onCar` (the car's id), kept right at
  the start of each controlled tick (`Leaps.settle`): the car gone, driven, got into, or you off its body, and you are put
  where a body fits. No schema bump: the field is null on the ground and left out of the save then, and a save without
  it reads the same. The car's top is a profile of heights along its length from the models' sizes, read only by the
  view (`Leaps.height`). The dead reach you on a roof by distance, as on the ground: a roof is a lookout, not a refuge
  *Why:* The player asked to get over the things a person plainly could; a parked car (solid since the same day) was
  the one thing that stopped you that a person climbs. The simplest rule for the dead keeps the two branches (theirs
  and this) independent until they merge
- **A broken window and a fence stop you on foot (2026-11-11).** You (the "free" mover) no longer cross a broken
  window's or a barricade edge's line walking (Pathfinding.crossable); Space just in front vaults it (Leaps.cs), with
  the vault clip and the eye going over. Yard fences are barricade edges (Lots.cs), so this is what stops you at a
  fence; the quarantine fence is wall tiles, solid already. Colonists still step over barricades and use doors; the
  dead and animals still climb through broken windows. Straddling one (an old save), you walk as a colonist or the
  dead would until clear (ControlMod.walkMover). The scripted solo player vaults the fences on its paths.
  *Why:* The player walked through fences and slowly through windows; a person climbs both, deliberately
- **A car action goes on while you push against the car (2026-11-11).** Steering cancels getting in, the boot and
  the bonnet only if the tick's step moves you (more than STILL_AT_CAR, 12 SUB): W still held from walking up to it,
  the body stops you and E works. *Why:* The player's "E doesn't get me in" was W still down: the old rule cancelled
  the 2.5 s of getting in on the first tick
- **Into the water: you wade and swim; nobody else does (2026-11-11, Swim.cs; log/2026-11-11-21-play-fixes-rivers.md).**
  Depth is the distance from the bank, tiles only: a water tile with land (not a bridge's deck) among its eight
  neighbours is shallow, any other deep. The Heights' bed agrees (under half a metre by the bank, 0.7 m to 1.3 m
  further out) but the tick doesn't read heights: hand-made maps have none, and the tick stays on the grid. Your body
  moves as a new mover, "swimmer" (the free mover's rules and water), used only by your walking, jumps and strokes;
  no path or reachability is planned for it, so colonists, the dead, animals and dogs keep treating water as before
  (a one-line change would let the dead wade the shallows, but their reachability labels, hordes and the herd paths
  all assume water is a wall: left for its own change). Space in deep water is a stroke (a leap of its own, busy
  "stroke", carried like a jump at the water's level) rather than a "swim up": the sim has no depth to dive in, and a
  rhythm of strokes that is cheaper than paddling makes the choice to swim well a real one. The hop over one tile of
  water stays: dry and quicker than wading. Letting go of control in the water puts you on the nearest land. One
  side effect, kept: your body may now overlap the water's edge, so walking a bank no longer slides you along it as a
  wall; it moved three of six solo balance runs by a few hundred ticks each way (log).
  *Why:* The player's playtest: "rivers cannot be entered (perhaps the heightmap should continue underwater and your
  player gets wet as a result of entering and swimming". A depth from the bank is cheap, needs no heights in the tick,
  and makes a wide river the risk it should be.

## Jobs, building and hauling

- **Hauling v1.** One item, one carrier, no stacks, no stockpile filters
  *Why:* Hauling multiplies edge cases
- **Designations.** Wall/Door tools place blueprints (1 wood each: haul, then build). Remove marks a wall/door; removing refunds the wood. Cancel tool (5). Shift = instant (old
  behaviour)
  *Why:* Gives hauling a purpose; instant mode kept for sandboxing
- **Build rules.** Blueprints only on empty floor (to turn a wall into a door: remove, then build). Completing a wall evicts survivors and items on that tile
  *Why:* Simple and never traps anyone
- **Job queue.** Derived from the world by `reconcileJobs` at the start of each tick: one job per blueprint (haul while it needs material, then build), one clean job per dirty
  tile. Invalid jobs are dropped and their worker released
  *Why:* Event-driven job creation would drift out of sync; a linear reconcile can't
- **Job choice.** Highest-priority type first (ties: build, haul, clean), then nearest job of that type (Manhattan) that is actually doable, checking up to 8 candidates. Idle with
  nothing doable: retry every 5 ticks
  *Why:* Matches CLAUDE.md; the cap and back-off bound per-tick pathfinding cost
- **Default priorities.** 3 / 3 / 3 for build / haul / clean
  *Why:* Neutral start; type order breaks ties
- **Direct orders.** A move order releases the survivor's job (dropping any carried item); they go back to work after arriving
  *Why:* Proper drafting can come with the threat milestone
- **Stuck-state thresholds.** Max 600 ticks on one job; max 20 idle ticks with a doable job
  *Why:* Starting values, tune later
- **Dirt.** 1/30 chance per step onto floor, via the sim RNG (drawn on every floor step, so RNG use doesn't depend on dirt)
  *Why:* Gives cleaning a steady trickle; deterministic
- **Cleaning scope.** Indoors only (any enclosed room): dirt is only tracked indoors, clean jobs only for indoor dirt
  *Why:* Answers the M4 home-area question; no more scrubbing the street
- **Cancel tool.** On a blueprint cancels it; otherwise clears the purpose of the room under the click
  *Why:* One "undo a designation" tool
- **Auto-planner (M8).** `planFurnish(state, x, y)` in sim/planner.ts is pure: it returns ordinary commands plus notes, and the Furnish tool (Rooms, 4) applies them through
  applyCommand, so the log holds plain designateBuild commands and replays need no planner. Housing: one bed per survivor minus beds in other housing, at least 1, capped by
  capacity (1 per 4 tiles), existing beds/plans counted; spots are empty floor not next to a door, most walls first. Storage: nothing to add. Unfixable problems (no door, not
  enclosed, no purpose) are explained instead. Notes show as a 6 s message under the status line
  *Why:* CLAUDE.md: "the auto-planner is just another source of designations"
- **Crafting.** Workbench furniture (`k`, blueprint `K`, 1 wood; no room needed for now). New job type Craft (between Haul and Scavenge in the tie order). The player sets stock
  targets per craftable in the Workbench orders panel (weapon 2 wood, bandage 1 wood; default 0, max 20). While anything is below target, each bench offers one craft job: the
  crafter carries wood to the bench one piece at a time (`stored` on the bench, refunded if it is removed), then works 60 ticks and the item drops beside them. What gets made is
  decided when the wood is in: the first craftable below target. Wielded weapons don't count as stock
  *Why:* Wood gets a use beyond building, and weapons and bandages stop depending on scavenging luck
- **Cooking.** Crafting generalised to stations: each recipe names its station, input and count. Stove (1 wood) + Cook job: raw food into hot meals, two kept per survivor while
  food lasts. Hungry survivors eat a hot meal first: 2600 satiety (raw 2000) and "had a hot meal" (+5)
  *Why:* Makes the mess hall and food chain matter
- **Carts, stockpile filters.** `cart` items, made at a workbench; haulers pick one up like clothing. On a store haul they take up to 3 more stray things within 4 tiles (their own
  store jobs lapse) and unload on free storage tiles. Storage rooms can refuse food, materials, medical or gear (`refuses` on the designation; overview panel)
  *Why:* Hauling was the bottleneck; storage control
- **Tools.** axe (scavenge), hammer (build), medic kit (doctor) in a `tool` slot, picked up like clothing for a job type the survivor does; +1 work step every 3 ticks at that job
  *Why:* More for the workbench to make
- **Wall in.** `planPerimeter`: wall plans round a box's edge (explored, empty floor), a door mid-way along each side. Land tool
  *Why:* The auto-planner, and walls for the balance harness
- **No drafting.** (2026-11-09: no pause key any more; the map and the menu pause, the colony tab plays on.) Planning happens while paused (Space): designate freely, nothing ticks,
  unpause to start work. Or build on the fly. Direct move orders stay temporary
  *Why:* Player's call: pause-to-plan fits the game better than a drafted mode. Verified in the browser: 0 ticks while paused, jobs created and claimed on unpause

## Rooms and furniture

Later rows revise earlier ones (bed ownership, for one); the code is the truth.

- **Rooms.** Derived by flood fill over floor (walls and doors are boundaries); regions touching the map edge are outdoors. Memoised per grid + walkabilityVersion, outside sim
  state
  *Why:* Recomputed only when walls/doors change; nothing to keep in sync
- **Room purposes.** Stored as designations anchored to a tile; apply to whatever room contains the anchor. Built-over anchors slide to a neighbouring floor tile; when rooms merge
  the older designation wins; an anchor that ends up outdoors is kept and shows "not enclosed"
  *Why:* Survives remodelling (e.g. remove a wall, then build a door) without the player redoing it
- **Requirements.** Both: enclosed, has a door. Housing: ≥1 bed, ≥4 tiles per bed. Storage: ≥4 tiles
  *Why:* Small and visible; first unmet one is shown on the room label
- **First room purposes.** Housing (N beds), storage (designated area only)
  *Why:* Enough to exercise the requirements loop; mess hall waits for needs
- **Beds.** Furniture (`state.furniture`), built from a blueprint with 1 wood, walkable, items can't be dropped on them. Remove refunds the wood
  *Why:* Housing needs something to require; beds were due in milestone 6 anyway
- **Storage.** Complete storage rooms are haul destinations: each loose item outside storage gets a store job; destination tile chosen and reserved at claim time. Builders take
  wood back out of storage
  *Why:* Answers the M4 stockpile question with the existing haul job type
- **Mess hall.** Tables (`t`, blueprint `T`, 1 wood, built like beds). Room purpose Mess hall: walls, door, at least 1 table. A seat is a free floor tile beside a table in a
  complete hall, one eater per seat (`meal.seat`). A hungry survivor picks the free seat nearest the food at the moment they decide to eat, carries the food there (`carrying`
  during a meal is allowed) and eats; no free seat, or the seat stops being one, means eating where they stand. The planner furnishes a hall with one table per 4 survivors, in the
  open and spaced apart
  *Why:* Player pick; makes room-building matter beyond beds
- **Bed ownership.** First come, first served (milestone 6). Assigned beds can come later as room types with bed assignment (e.g. barracks vs private quarters)
  *Why:* Simplest thing that makes sleep work
- **Bed ownership and barracks.** Revises "beds first come, first served" (the milestone 6 decision; bed assignment was always planned for later). Your first ordinary bed becomes
  yours (`owner`); nobody else sleeps in it; a death frees it; `assignBed` gives a bed to someone (their only one; a sleeper in it moves out) or frees it. Infirmary beds are never
  owned. Bed choice, in order: infirmary if hurt (and not if not), your own, barracks if you guard (and not if not), nearest. Barracks purpose (as housing: beds, 4 tiles each):
  guards asleep in a barracks bed wake when a zombie comes within the danger radius, instead of only when hit. The planner gives every guard a barracks bed, up to capacity
  *Why:* Player asked for bed assignment via room types at milestone 6; light-sleeping guards make a barracks worth building
- **Doctor and infirmary.** New job type Doctor (second in the tie order, after Guard). A patient is a hurt survivor asleep in a bed with no treatment under way; each gets one
  doctor job: fetch the nearest free bandage, carry it to the bedside, 20 ticks, +300 health (same as treating yourself), and the patient sleeps through it. Infirmary purpose
  (walls, door, at least 1 bed): hurt survivors take its beds first and heal twice as fast there; everyone else prefers other beds. The planner furnishes one infirmary bed per 4
  survivors
  *Why:* Medicine next steps from the injuries work: the bedridden couldn't fetch bandages for themselves

## Needs, health and people

- **The inventory as grids (2026-11-10).** Every container is a grid of cells and every kind a footprint (Inventory.ITEM_SIZE, beside the weights; weapons by name), turnable a
  quarter turn; small things stack in a cell to a per-kind limit (stacks are items sharing a cell: no counts, so nothing that eats or burns one changed). Weight stays a second
  limit with its old meaning (PACK_MAX 12 kg kept). The bag is one fixed 8 x 6 grid (12 kg of any kind but medkits fits by area); no backpack item, because adding one would
  mean reweighting every loot table; a coat's pockets (2 x 2) and a vest's pouches (3 x 1) add grids when worn or carried and keep their contents wherever they go, one level
  deep. Old saves and a search's finds are placed by first-fit when the grid is first touched (layout is pure, so it shows the same before it's written). Quick slots are nine
  assignable slots on the survivor, filled on pick-up.
  *Why:* The player asked for extraction-shooter and attache-case style management; first-fit through every code path kept the scripted solo player's runs unchanged.
- **Rest and sleep.** Integer rest 0-2000, drains 1/tick awake (tired below 500, ~2.5 min at 1x). Tired survivors drop their job (via releaseSurvivor), reserve the nearest
  reachable free bed, sleep until full. Bed +4/tick (~37s), floor +2/tick (~75s) when no free reachable bed. Survivors start at 100/85/70/55% by index (no RNG). Bed gone / path
  blocked → re-plan or floor, never stuck. A direct order wakes them. `setRest` debug command
  *Why:* Sleep first, hunger later. Integers keep it exactly deterministic; stagger avoids everyone tiring at once. Single drain rate for now (no faster drain while working)
- **Hunger and food.** Food is an item (`f` in maps, tin cans on screen), stored like any item, never used for building. Satiety 0-3000 drains 1/tick always (hungry below 750,
  ~3.75 min at 1x). A hungry survivor with reachable free food drops their job, reserves the nearest food, walks to it and eats it in place (30 ticks, +2000). No food: keep
  working, look again every 20 ticks. Eat before sleeping; sleepers aren't woken to eat. Debug commands `setSatiety`, `spawnFood`
  *Why:* "Keep it simple at first": mirrors sleep, reuses item reservation, no mess hall or carrying food
- **Day/night.** Clock derived from the tick (nothing stored): 200 ticks/hour, 4800-tick day (8 min at 1x), start 06:00, night 21:00-05:00 with 2 h dusk/dawn fades. At night
  survivors go to bed below 75% rest instead of 25%. Dark overlay over the world (not survivors or labels); clock in the top bar
  *Why:* Sleep gets a natural rhythm; zombies will need "night"
- **Health and death.** Health 0-1000. Starving (satiety 0) costs 1/tick (~100 s to die from full); +1/tick asleep, +1 per 5 ticks awake and fed. At 0 the survivor dies: job,
  carried item, bed and food are released, they're removed after the survivor loop (never spliced mid-iteration), and "X died of starvation/injuries" goes to the event log
  *Why:* Gives hunger teeth; zombies will deal damage through the same path
- **Injuries and medicine.** Health tiers: above 60% normal; 30-60% hurt (5 ticks/tile instead of 4, work and guard blows at 3/4 speed); below 30% critical (drops any job, even
  guarding, and goes to bed, zombies or not). Sleepers stay in bed until rested *and* back to 60%, but always wake when hungry (a starving sleeper would lose health instead of
  healing). Bandages (`m`, a scavenge find: food 50% / wood 35% / bandage 15%) are fetched and applied by hurt survivors like a meal: +300 health, reserved so two survivors never
  go for the same one
  *Why:* Player pick from four candidate systems; makes zombie fights cost something without making them deadly
- **Mood.** 0-100, 50 baseline, from timed thoughts (one per kind, a repeat refreshes): ate at a table +10 / without -5 (1 day), slept a full night in a bed +5 / on the floor -10
  (1 day), someone died -15 (2 days), and -10 while hurt. Under 30 is miserable: work at 3/4 speed (same skip rule as hurt; both together are still 3/4). Shown as a Mood column in
  the priority panel, with the reasons in its tooltip and the debug panel
  *Why:* Crude on purpose: gives tables and beds a payoff, and deaths a cost, without a full needs model
- **Mood consequences.** New thoughts: hungry (-10 while it lasts), attacked by a zombie (-10, half a day). Breakdowns: a miserable survivor (under 30) is rolled every 200 ticks
  (25%, sim RNG); a breakdown drops their job and blocks work for 600 ticks (needs still run), then "got it out of their system" (+15, a day) so it doesn't repeat at once. Rec room
  purpose (walls, door, 4+ tiles): a survivor who found no job, with mood under 70 and not relaxed today, walks to a free spot and unwinds for 200 ticks (+10, a day); a job turning
  up ends the visit (checked every idle-retry). New debug command `damageStructure`
  *Why:* Mood did nothing in balance pass 1 (never under 30); this gives low mood a cost and a remedy
- **Skills and traits.** 400 xp a level, up to 10, from ticks of work; each level is 5% quicker (an extra step on level/20 ticks); guards get +5 damage a level. Traits (tough,
  lazy, green thumb, cheerful, gloomy, quick) for wanderers and a world's first survivors, from a hash of the id (not the sim RNG); none on hand-made maps, so tests stay put
  *Why:* Choosing who to take in becomes a decision
- **Earned traits.** hardened (15 kills: +10 a blow), vigilant (20 night hours on a tower: +3 sight), haunted (lost a friend: -5 mood). Kept apart from the born traits rollTraits
  picks from
  *Why:* Experience leaves a mark
- **Infection, workshop, medicine.** Zombie hits infect 3% of the time (hash of zombie and tick; was 15%, balance pass 3), half that in armour. Two days untreated (was one) and the
  victim dies and rises as a zombie; an infirmary bed halves the spread. Medicine (2 wood, any workbench since pass 3; benches in a complete Workshop craft in 40 ticks, not 60)
  cures it, self-fetched or brought by a doctor
  *Why:* Gives doctors, infirmaries and the workshop real work
- **Contagion, wounds.** Sickness spreads indoors within 2 tiles (10% a check, doubled under 6 tiles a head); autumn and winter flu (15% a dawn). Blows wound an arm or leg 15% of
  the time (2 days: slower work or walking) and bleed for up to 300 ticks or until bandaged. An infecting bite can be amputated in the first quarter of the infection: cured, -300
  health, one-armed or one-legged for good
  *Why:* Grim but on theme
- **Relationships.** Opinions -100..100; +2 every 200 ticks for survivors within 3 tiles, both awake. A breakdown lashes out at the nearest survivor (-25 both ways); if they
  already disliked the breaker (-40 or worse) both take 60 damage. A friend's death (30+) is "lost a friend" (-25, 3 days) instead of "someone died"
  *Why:* Social texture from existing systems
- **Thirst.** Hydration 2400, thirsty at 800, drains 1 a tick (harsh only). Drink beside a well or a rain collector with water (clean), else the river (5% sick). Interrupts work
  like hunger. Collectors fill 1 per 50 ticks in rain/snow (max 20). A new world's camp has a collector with 12
  *Why:* A third need that makes water and weather matter
- **Spoilage.** Food rots at 4 days of age, meals at 1; aging twice as fast in summer, not in winter, half in complete storage, not within 2 of a powered fridge. Checked every 100
  ticks (harsh only)
  *Why:* Stockpiling for winter has to be planned
- **Corpses, sickness, graveyards.** The dead stay as `corpse` items (tag: name, wear: tick of death); the infected still rise instead. Rotting (half a day, outside a complete
  graveyard): "the stench" within 4 tiles and an 8% chance per 200 ticks of sickness (a day: slowed, -1 health per 40 ticks, -10 mood; medicine cures). Zombie bodies go after 3
  days. Haulers take bodies to a graveyard (room purpose; the only home for them). The grieving pay respects there (leisure, +15). Rooms tools went to ten: key 0 is the tenth
  *Why:* Fighting leaves a mess to deal with
- **Children, ageing.** Ages from a hash (18-62); a year per 20 days; 65+ work slower. Harsh games: two adults (both 60+ opinion, 50 or under) with a spare bed may have a baby (25%
  a day, one a day). Children have no priorities (can't be given any), grow up after 10 days with two levels in two skills
  *Why:* Growth that isn't only wanderers
- **Shifts.** `shift`: day (sleeps at night) or night (sleeps by day): isTired swaps which part of the day is bedtime. Priority panel column
  *Why:* Watch at night without everyone up
- **Policies, leader.** Rationing (satiety drains 3 ticks in 4, -5 mood), curfew (no outdoor work at night but guard, firefight, doctor), quarantine (the infected and sick keep to
  bed and stop spreading it). Leader: the best average opinion, 20+, with three or more people, settled at dawn: +5 mood; their death -20 for two days
  *Why:* Big choices without more clicks
- **Books.** `book` items with a subject from a hash of the id: three in four a skill (+1 level to the reader), one in four knowledge (gunsmithing: ammo and guns; tailoring:
  armour), which those recipes need. Idle survivors read (300 ticks; jobs interrupt)
  *Why:* Exploration with a goal
- **Dogs.** `state.dogs`. Strays (12% a day, hash) and 25% of expeditions bring one. Follow the nearest survivor, sense zombies at 10: bark (wakes guards, every 400 ticks), chase,
  bite 40. Zombies with nobody else to hit go for dogs (300 health). Near a dog: +5 mood
  *Why:* Early warning and company
- **Clothing.** Two slots: body (coat or padded armour) and feet (boots), carried like the weapon. Coat: no winter-night cold, no "soaked"; armour: blows x0.7, half the infection
  chance; boots: spike traps (which now hurt the barefoot, 40) don't. Wear: coat per hour of weather, armour per blow, boots per trap; falls apart at its life; half-worn ones are
  mended with a cloth. Put on between jobs like arming (guards prefer armour). Cloth from crates and traders; workbenches make clothing. Stations now remember what they hold
  (`storedKind`)
  *Why:* Ties scavenging, crafting, winter and fighting together

## Zombies, raiders and defence

- **Zombies.** Late milestone, crude at first
  *Why:* The base-building loop must be fun on its own
- **Zombies (M7, crude).** Night N brings 1 + N zombies (2, 3, 4, ...; was 3 + N until the player found 4 on night one too much), spread evenly through the night (night 1: 23:00,
  03:00), each on an outdoor map-edge tile (sim RNG, not within 8 of a survivor); a "Night N falls: K zombies expected" notice at 21:00; at most 20 alive at once (safety cap). They
  walk floor only (doors stop them: CLAUDE.md door rule revised), 6 ticks/tile, chase the nearest reachable survivor (re-plan every 15 ticks), hit adjacent survivors for 40 every
  10 ticks, wander when nobody is reachable, all leave at dawn. Zombie damage uses health; deaths say "died of a zombie attack"
  *Why:* Night pressure that rewards enclosing the base; dawn retreat keeps it crude
- **Guards.** New `guard` job type, one job per zombie, first in the tie order. Guards close in and hit for 50 on arrival, then every 10 ticks (4 hits kill). Needs never pull a
  survivor off a guard job; nobody goes to bed with a zombie within 10 tiles; a hit wakes a sleeper
  *Why:* Without the guard-duty rule a tired guard flip-flopped between hunting and bed as the zombie drifted across the danger radius (the fuzz test caught it)
- **Weapons and armory.** Weapons (`/`) are a scavenge find (food 45% / wood 30% / bandage 15% / weapon 10%). Anyone with guarding switched on and no weapon fetches the nearest
  free one *between jobs* (reserved like food and bandages; hunger or wounds cancel the errand). Equipped weapons sit in a separate `weapon` slot (item `carriedBy`, no `pos`), so
  hauling still works. Armed blows do 100 instead of 50 (2 hits per zombie instead of 4); a weapon breaks after 5 kills (`wear` on the item) and drops where its owner dies. New
  room purpose Armory (walls, door, 4+ tiles): weapons are hauled there if one is complete, else to storage, and move over when an armory is finished
  *Why:* Player pick; gives scavenging a reason to continue and guards something to gear up with
- **Breaching.** Revises "a closed building is safe". A zombie that can't reach anyone heads for the nearest survivor anyway: through doors if a survivor could walk the route,
  otherwise straight through walls (`'breacher'` mover: everything passable). It batters the door or wall in its way (40 per blow, every 10 ticks; doors 400, walls 1200). Damage
  lives in `grid.damage`, cleared whenever a tile changes. A broken structure becomes floor and leaves a blueprint to rebuild it (needs wood). Damaged structures get repair jobs
  (build type, no blueprint) at 2 damage per tick: half a zombie's rate, so a repairer slows a breach but can't hold a door alone
  *Why:* Nights were solved once you had four walls; this makes defence (guards, repairs, rebuilding) matter
- **Zombie groups.** From night 4 zombies arrive in groups of ceil(night / 3) on neighbouring outdoor tiles, same total per night (night 4: 2, 2, 1)
  *Why:* Balance pass 1: arriving singly, guards picked them off one at a time and a guarded colony never lost a door
- **Zombie scaling.** Each night's zombies get +20 health (night 10: 380), and each night brings one extra zombie per 4 survivors, fixed at nightfall (`zombiesTonight`). Early
  nights with 3 survivors are unchanged
  *Why:* Balance pass 2: the colony grew a survivor a day and a dozen guards mobbed each 200-health zombie; one death in 48 colony-days
- **Zombies of the world.** Three sources: night zombies (as before, but arriving in the dark past explored ground; map edge on fully explored maps; the only ones that leave at
  dawn); dormant zombies in ruins (positions only, woken within 10 tiles of a survivor); herds (markers crossing the map edge to edge, 1 tile per 8 ticks, 50% chance a day from day
  2, 40% of them routed past the colony, 2 + day/3 strong, breaking into real zombies within 24 tiles of a survivor and walking on unless someone is near). Static and herd zombies
  only notice survivors within 20 tiles
  *Why:* Player's design: static zombies about the map, migrating herds cutting across it
- **Zombie kinds.** `variant` on zombies, by a hash of the id once their night comes: runners (night 3+, quick, frail), screamers (4+, a scream is a noise), brutes (5+, double
  health, batter walls 3x), crawlers (6+, a `crawler` mover passes barricades). Raiders stay walkers
  *Why:* Each counters a defence
- **The dead cross low things (2026-11-11, `Crossings.cs`).** What each kind does at what stops it:
  - *A parked car:* every kind gets over it, a leg (one tile to the next) at a time: walkers, screamers and raiders 2 s a leg, runners 1.4 s, brutes 2.7 s (the walker's
    20 ticks by each one's pace), so a car's width is two legs (4 s for a walker) and a sedan lengthways in a gate six. A crawler goes under a pickup (0.45 m clear in
    the model; 1 s a leg) and hauls itself over a sedan (0.28 m clear, too low for its shoulders; 4 s a leg, the slowest of all). On the car none of them bites; they
    can be hit, a blow holds them where they are (no shove), and the crowd doesn't push them.
  - *A barricade (edge or tile):* runners vault it (0.8 s a leg); crawlers go under as before; walkers, screamers, brutes and raiders batter it as before.
  - *Walls, doors, windows, boards:* battered as before (a broken window is still climbed at half speed). *Water:* never. *Pits and traps:* catch whoever steps on them
    (a pit is covered: nothing to vault). *Furniture:* never stopped the dead.
  - *Paths, not bodies.* `isPassable` and `crossable` still say no at a car for every body on foot, so nothing walks, slides or is pushed through one. The dead's paths
    (A*, reachability, the horde's field) may go onto what they cross at a cost in steps: 12 to climb onto each tile of a car, 3 to vault a barricade. Round a car's end
    (8 or so steps more) is taken; over is for when the way round is a dozen steps longer, or there is none. A runner's paths are planned for a mover of their own
    (`runner`); the breacher's still prefer a wall (1 step) to a car (12), which only matters when nothing can be reached anyway.
  - *One leg at a time.* The crossing on the zombie is one straight leg between points a tile apart (what, from, to, ticks left, of); at its end on the car the next leg
    follows the path. Before the first leg the whole way over is followed (the path; a horde member's field) to ground where a body fits, or it isn't begun. If the way
    on goes while it is up there it gets down wherever a body fits, else waits a second and looks again. Anything else that moves it ends the crossing where it was put.
  - *No queue.* Several may be on one car at once.
  *Why:* The player asked for zombies that vault or crawl over what they couldn't pass, and a parked car across a gate sealed it against everything (log 06's leftover).
  Every kind crosses a car so that nothing is sealed by one and no kind is left pressing at it; the paces keep the kinds' characters. Runners alone vault barricades:
  with crawlers that is two kinds of five (about a fifth of a late night's dead) a barricade doesn't hold, and it still holds the walkers, screamers and brutes that
  are most of them; brutes keep battering because walls are what they are for. Bodies stay stopped by cars so that every place that moves a body (chasing, the crowd's
  push, a shove) needed no new rule, and a missed case fails safe (no path) rather than putting a zombie inside a car. The path cost is more than the time is worth
  (two legs take the time of seven steps and cost thirteen) so the dead keep to the ground when round is near. Stuck-state invariants unchanged (they are the
  survivors'); for the dead the tests hold that none south of a blocked gap or on the car stands still 100 ticks.
- **Sieges.** From night 14, every 7th night has 1.25x the zombies, warned two days ahead (balance pass 5: night 7 at 2x wiped out every colony)
  *Why:* Preparation pays
- **Storyteller.** `threatScale`: 1 + 0.1 per 100 goods (not wood, food or bodies) - 0.15 per death in the last day and a half, within 0.5-1.6; scales the night's zombies and the
  raid chance. Two deaths and food for under a day: a cache (6 food, 2 bandages) at most every 3 days
  *Why:* Smooths the death spiral
- **Darkness.** Harsh games: at night a zombie can only be fought if seen: within 8 tiles of someone (10 from a tower, +3 vigilant), in lamplight, by a fire, or near a dog. Balance
  pass 5: turning it off didn't change the outcome much
  *Why:* Lamps and towers matter
- **Noise.** `makeNoise(point, kind)`: wakes dormant zombies within the radius, sends idle static zombies to look, swings herds and herd zombies through the point. Building (10, on
  completion), fighting (6, per blow), gunfire (30), a running generator (12, hourly). Nothing stored
  *Why:* Gives the world's zombies a reason to come; building and shooting have a cost
- **Guns, ammo.** Guns share the weapon slot; `wear` is rounds loaded. Loaded: shoot from 6 tiles with a clear line (Bresenham through walls/doors), 150 a shot, every 15 ticks,
  loud. A guard with a loaded gun claims a guard job without walking and closes in a tile at a time. Empty guns are clubs; guards fetch ammo (6 a box) like a weapon. Ammo and guns:
  workshop recipes needing gunsmithing
  *Why:* Ranged fights, and a scarce resource
- **Watchtowers.** Furniture. Standing on one: sight 16, gun range +3. Idle guards climb a free one at night ("keeping watch", after looking for jobs). Any tower: herds heard 1.5x
  further
  *Why:* Defence that isn't just walls
- **Defences.** Spike traps (furniture, 1 wood): 100 damage to a zombie stepping on one, 3 charges, then a rebuild plan. Barricades (structure, 1 wood, 15 work): survivors walk
  through, zombies must batter them (600 health), repairable, rebuild plan when broken; a room boundary like a door. No lockable doors: zombies already can't open doors, so locking
  only matters with other factions
  *Why:* Herds and night scaling made defence more needed
- **Scrap, defences.** Scrap (crates) reinforces walls, doors and barricades (a blueprint delivering scrap): 2.5x the battering, fireproof (`grid.reinforced`). Turrets (fed ammo by
  fuel jobs) shoot what they can see within 7. Lures rattle hourly at night (noise 25). Pits hold a zombie 100 ticks
  *Why:* Answers to brutes and fire
- **Fire.** `state.fires` (tile + ticks left). Walls, doors, barricades and furniture burn (300 ticks), bare ground less (100), twice as fast in rain/snow. Spreads by hash rolls
  every 20 ticks; 5 a tick to anyone in it. Burns out: structure to floor, furniture and items gone. Sources: stove (2% per hot meal), raider firebombs (5% of blows on structures),
  the Burn order. New job type `firefight` (between guard and doctor)
  *Why:* Danger and a tool (burn a choke point)

## The world, food and other people

- **Generated world.** The game starts in a generated world (`worldgen.ts`): 128x128 by default while we develop, 64/128/256/512 on offer (`?size=N` and `?seed=N` in the URL, or
  the size picker by New world in the Game menu); scenarios and tests still use hand-made maps. Roads every ~64 tiles, ruined buildings in 16x16 cells near roads (walls with gaps,
  a door, sometimes an inner wall, crates whose loot grows with distance), forests (trees), a river and lakes (new `water` tile, impassable; bridges where roads cross), a visual
  ground layer, and a cleared start with an intact three-bed hut. Rooms touching water, or over 600 tiles, are outdoors
  *Why:* Player wanted a much larger world explored slowly
- **Lazy chunks, compact saves.** Worlds generate the 5x5 chunks round the start, then any chunk within two chunks (64 tiles) of a survivor, from fog.ts each tick; a new chunk
  bumps walkability and moves anything left in a wall (ring search, since nearestTile can't spread out of water). Saves and bug reports run-length encode the grid layers (a 256
  world save is over 3x smaller); plain saves still load. IndexedDB not needed yet: saves stay well under localStorage's limit at these sizes
  *Why:* Two of the big-world steps, cheap now
- **Fog and exploring.** `grid.explored`; survivors reveal 8 tiles round them each tick. Nothing can be planned on unexplored ground. Explore box order (Orders, max 64 a side)
  gives one Scout job (new job type) that walks to the nearest unexplored walkable tile in the box until it's all seen
  *Why:* Exploration as the way the world opens up
- **Scouting through the day.** Scouts drop the job at 19:00 (even mid-walk) and walk home to their own bed or the others; scout jobs wait for morning. A newly seen crate with loot
  marks its ruin as a point of interest (once per ruin, removed when its crates are empty)
  *Why:* Scouts were wandering into the night
- **Scavenging.** Ruins hold crates (`c` in maps, furniture with 5 loot). The Scavenge tool (Orders, 1) drags a box; every crate with loot in it is marked. Marked crates get one
  scavenge job each (new job type, own priority column, tie order build > haul > scavenge > clean): search from an adjacent tile for 40 ticks, drop one find (food 60% / wood 40%,
  sim RNG), repeat until empty, then unmark. Finds are brought home by the existing store hauling; without storage they stay at the ruin. Cancel unmarks; Remove dismantles a crate
  for wood
  *Why:* Answers "where does food come from" with the systems already there; RNG keeps it deterministic
- **Trees.** Renewable wood. Trees (`Y`) are furniture with 3 wood, marked with the Scavenge tool like crates and chopped by scavengers (wood only, no roll). They stay marked when
  bare and grow back one wood every 2 days. The sandbox has two groves (14 trees)
  *Why:* Balance pass 1: crates were the only wood source, so wood hit 0 by day 4-5 and building, crafting and rebuilding stopped for good
- **Farming.** Farm area tool (Rooms, 3) lays plots on bare floor. New `farm` job type (tie order guard > build > farm > haul > scavenge > clean): stand on an empty plot to sow (20
  ticks), it grows for 2400 ticks (half a day), then harvest (20 ticks) drops one food on the plot and it gets re-sown. Building a wall or door on a plot removes it; Cancel removes
  plots. ~4 plots per survivor keeps up; 12 plots fed 3 survivors through 3 days with surplus
  *Why:* Renewable food with the existing job, haul, and eat systems; outdoor fields are exposed to zombies
- **Crops, greenhouses.** Plots have a crop: potatoes (half a day, 1 food), wheat (a day, 3 food), herbs (a day, 1 medicine). A complete Greenhouse room grows its plots at full
  speed all year
  *Why:* Winter food, and a second source of medicine
- **Coops, hunting.** Coops lay a food every 1200 ticks (2400 in winter). Deer (60% of mornings, hash) wander in at the frontier for two days, shying from survivors; the Hunt order
  marks them and the new hunt job (after scavenge) gets 3 food
  *Why:* Food that isn't only farming and crates
- **Seasons and weather.** 5-day seasons from spring; crops full speed spring/summer, half autumn, none winter. Dawn weather from day 2 (sim RNG; day 1 is clear so tick-0 tests
  keep their random stream): rain (+50% crops, "soaked" outdoors), snow in winter. Winter nights outdoors: no healing, -1 health per 10 ticks, "out in the cold"
  *Why:* Stockpiling and shelter
- **Weather events.** The top 15% of the same dawn roll: blizzard (winter: cold by day), heatwave (summer: thirst x2, spoilage +1, fire spread x2), storm (else: wet, lightning 30%
  an hour sets outdoor furniture alight). `isWet` replaces "not clear"
  *Why:* Weather that changes the day's plans
- **Power.** Generator: haulers feed it wood (fuel jobs: haul jobs with a `furnitureId`), up to 4; burns one per 2 hours while a lamp (at night) or fridge is within 12. Lit lamps:
  no night arrivals within 16; guards hit 25% harder within 4
  *Why:* A wood sink with a payoff
- **Heaters.** Furniture fuelled like a generator (fuel jobs cover both). On cold nights (winter, or a blizzard) burns a wood per 3 hours and keeps anyone within 4 from the cold;
  1% an hour it sets its tile alight
  *Why:* Cold outdoors had only coats as an answer
- **Wanderers.** From day 2, one wanderer at 10:00 on an edge floor tile (sim RNG), a second at 14:00 only if beds outnumber survivors plus waiting wanderers. They wait in place;
  Take in (`acceptWanderer`) makes them a survivor with default priorities, Turn away (`dismissWanderer`) removes them; ignored for 3 hours or still there at nightfall, they leave.
  A wall built on one nudges them to a free neighbour (or they leave). Names come from their own counter (`namesGiven`, 26 names) so they no longer depend on entity ids
  *Why:* Player pick; growth is a choice, gated softly on beds
- **Traders, raiders, locks.** Traders from day 3, every 3rd day at noon, three offers (sim RNG), leave after 4 hours or at dusk. Raiders from day 6, 30% of nightfalls, 2 + day/6
  of them: zombie-kind 'raider' with their own mover (unlocked doors open), 250 health, 50 damage, no infection, leave at dawn. Lock door tool; locking evicts anyone in the doorway
  *Why:* Other people, and a use for locks
- **Settlements.** Three neighbours placed by hash in generated worlds, reputation -100..100. Traders come from them in turn; trade +5, an empty-handed trader -5, a gift of 3 food
  +10. -30 or worse: hostile, raids at 50% not 30%. 40 or better: sends a wanderer every 5 days
  *Why:* Other people with memory
- **Prisoners.** Raiders a guard brings down are taken alive half the time (hash) into a free spot in a complete Cells room. A food each dawn or they get away (also if the cell
  opens). After 2 days: recruit (joins; their settlement -10) or let go (+15)
  *Why:* Raiders become people
- **Expeditions.** sendExpedition takes survivors off the map to a point of interest for 2 x distance x 4 ticks + 400. Back home: the ruin's crate loot, up to 4 finds each
  (scavenge rolls); sleepers near the ruin make death and injury likely and are cleared. Away members stay valid for beds, opinions and weapons (invariants use everyone())
  *Why:* Turns scouting finds into missions, cheaply
- **Truck, outposts.** A generated world leaves a truck ~35 tiles out; seen, it takes 2 fuel per expedition: half the travel, twice the carrying. An expedition can stay as an
  outpost: finds every dawn (a find per person), a 15% daily risk to someone there, until recalled
  *Why:* Expeditions that grow
- **Radio.** Powered; at 10:00, 50% (hash): a distress call (a point of interest 30-60 tiles out; an expedition there brings 1-2 people in) or a neighbour's ask (`state.requests`,
  2 days: send the goods for +20 and 2 of something back)
  *Why:* Purpose for expeditions and power
- **Endgame.** `state.endgame`. Cure: zombies a guard brings down leave a sample 15% of the time (hash); a powered lab bench gets a research job (new job type) to study them; 40
  studied wins. Evacuation: from day 20 a powered beacon and radio call a helicopter for the dawn 3 days on, after a triple-strength siege. Winning is marked and told; play goes on
  *Why:* The game needed an ending
- **Growth is computed, with a sparse record (2026-11-10).** Foliage, and trees coming back, are a pure function of the seed, the tile, its ground and the growing days since it was cleared; the only saved state is `state.cleared` (tile -> clearing tick), trimmed once regrown. No per-tile state, no sweep of the map, no schema change. Rates: docs/trees.md "Growth". Saplings that become trees are planted from per-chunk plans in tick order, so the result doesn't depend on who is near or which chunk was made first; they are `tree` furniture like any other.
  *Why:* The world is going to 10,000 x 10,000, saves keep only what differs from the seed, and the player wanted places to look abandoned as days pass
- **Textures are a neutral detail layer over flat colour (2026-11-11).** Every surface keeps its vertex colour; a small tileable texture (one of twenty in
  one array, docs/textures.md) multiplies grain and scale into it, laid by position, so no mesh is unwrapped. Each texture averages to no change. A face's layer
  is a number on its vertices (the UV), not a material: a chunk of the world is one surface and one draw, as before
  *Why:* The look is low-poly in plain colours and the goal is 60 fps on a weak machine: one sample a fragment and no new draws cost 3-5% of GPU time where a
  material per kind would have multiplied the chunks' draws. Neutral textures keep colour where it can be changed per thing (a house's paint, a car's) and let
  the far view go without textures and still match
- **The 3D ground has colours of its own (2026-11-11).** First person and the far view take the ground's colour from `WorldLook.GroundColour` (grass a muted
  green, roads dark grey, dirt brown); the Colony tab and the minimap keep `COLOURS.ground`
  *Why:* The palette's near-greys were made for a dark 2D map; under the 3D sun grass, road and paving were one pale grey that no texture could make read as grass
- **Roads are two lanes; streets have a parking strip and sidewalks (2026-11-11, the player's playtest).** A street's asphalt is 8 tiles
  (`Towns.STREET`): a parking strip of 2 along its low side (north of an east-west street, west of a north-south one) and a carriageway of 6, two
  lanes of 3 m; a paved sidewalk of 2 each side and round its ends (the plan's road mask 3: paving, not a road), which the lots face. Lots on both
  sides park their car in the one strip (`Lot.park`). Country roads are five to six tiles across (`Roads.ROAD_REACH` 2.9) and meet a main street on
  the middle of its carriageway. A town's streets cross on a centre square at `Towns.anchor` (the town's point, or the nearest clear spot), which
  is always laid. World generation version 3
  *Why:* A pickup is 1.9 m across and the streets were 2: a car filled the street and every parked car blocked it (0.7 m beside one). One strip
  on one side, rather than parking on both, keeps both lanes clear whatever is parked (a car on each side of a 6 m street would leave 2 m). The
  strip is always on the low side so a lot knows where it is from its own frame, without knowing its street. Town radii were left alone, so a
  town has about a third fewer streets and a fifth fewer lots: bigger towns would have moved every town's site
- **The kerb is in the sim's heights, by sinking the asphalt (2026-11-11).** `Heights` step 6: a street's asphalt lies `KERB` (14 cm) under the
  corners (`sunk`, `kerb`, `surface`); the corners stay the level of sidewalks, lots and doors. The game's one height hook reads `surface`, and
  the ground mesh draws a sunk tile lower with the kerb's face beside it. The drop eases out over 6 tiles toward a country road. Nothing in a
  tick reads it. A car's top speed on paving is a crawl (`Vehicles.KERB_CRAWL` 24); a driveway's crossover is concrete, which is not
  *Why:* Sinking puts the one step where a kerb is; raising the sidewalk would have put a second step along every lot's front, path and door.
  In the heights, not only drawn, because everything in the 3D view stands on that one hook: a drawn-only kerb leaves people on one side of it
  floating or sunk. A crawl on paving (rather than a blocking kerb) because the sim has no heights that block, and it also slows a car on a
  path or a square, which reads right
- **Slanted roads are bevelled in the drawing only (2026-11-11).** A tile of open ground in the inside corner of a step of a road's edge is
  drawn half road (`WorldMesh.RoadCorner`). The sim's ground, the map and the minimap stay tiles
  *Why:* It took one check of four neighbours a ground tile. Straight edges at any angle need the road drawn from its line (a ribbon over the
  ground), which is a renderer of its own
- **The start shelter is an edge-walled building (2026-11-11).** `Buildings.startPlan`: one room, eight by six, on the footprint the old hut of
  wall tiles had; a door, windows, a stove, a table, shelves (empty), beds along the back wall. The same for every seed, never decayed
  *Why:* It was the only building in a generated world made of wall tiles a tile thick, beside neighbours on tile edges

## Controls and UI (the browser's, mostly carried into the Colony tab)

- **Hands in first person (2026-11-11, the first hand-played pass).** The quick slots are a hotbar: 1-9 send `quick` (that slot's thing in hand or used; the same key again puts it away), 0 sends `holster`; a full bag keeps the weapon in hand and says so. The right mouse held with a gun in hand aims down the sights (`sights`: the gun's own and moving spread halved, 60% pace, no running); without a gun a right-click is still "use", and E always is. An empty gun's click stays the sim's blow (Weapons.BUTT) and is drawn as one, a pistol-whip, not a dry click: `control.shotTick` tells a shot from a blow
  *Why:* The player could not put a weapon away, saw a shot that never happened, and could not aim a pistol; E keeps "use" whatever is in hand

- **Controls.** **Select mode** (default): left-click selects a survivor (empty ground deselects), right-click moves the selected survivor. **Build mode** (B): bottom bar with
  categories Structure / Furniture / Rooms / Orders; 1-9 arm a tool in the open category, Tab/E and Shift+Tab/Q switch category, left-drag paints (Bresenham fills gaps), Shift =
  instant, right-click puts the tool down, Esc steps back (tool → build mode → selection). Always: right/middle-drag pans (a right press that moves < 4px is a click), wheel zooms
  at cursor, WASD/arrows pan, P priorities, Space pause, `` ` `` debug, Home recentre
  *Why:* Playtest feedback (2026-10-25): the flat 8-tool bar was hard to navigate. Layout and RimWorld-style select/right-click-move chosen by the player. The Move tool is gone;
  the first survivor is no longer auto-selected
- **Build bar.** Six categories now: Structure, Furniture, Utilities (well, collector, generator, lamp, fridge, heater, radio), Rooms (Cells is the tenth, key 0), Land (crops,
  greenhouse, coop, hunt, wall in), Orders (Furnish moved here)
  *Why:* Ran out of number keys
- **Area tools.** A tool can be `area: true`: drag a box, applied to every tile on release (Scavenge). Painting tools stay line-based
  *Why:* "Designate a building or area" needs a box; painting a line over a ruin missed crates
- **Beds by click, room labels.** With a survivor selected, clicking a bed gives it to them. Room labels sit on the room's top wall, shorten to fit the room, and step down past
  each other
  *Why:* Ownership was invisible; labels covered furniture and overlapped
- **Minimap and herd warnings.** Minimap (M, bottom right, click to jump): fogged world, survivors, visible zombies, herds, points of interest, the main view's frame. A herd within
  48 tiles of anyone is announced once with a compass direction and from then on shows through the fog
  *Why:* Finding your way round a bigger world; herds arrived without warning
- **Alerts, overview.** `alerts(state)` (sim, pure): threats, the infected, hunger and thirst, winter food, beds, bodies, fuel, neighbours, visitors; worst first. HUD shows the top
  six (click to look); O opens the overview (people, stock, storage filters, knowledge, neighbours)
  *Why:* Too much going on to follow from the event feed
- **A car's body (2026-11-11).** A car is as long and wide in the sim as the model drawn (the sedan 4.2 x 1.7 m, the pickup 5 x 1.9 m): a row of circles its own width, so
  `Motion.fits` does the walls. It turns about its rear axle; other cars are solid; people and the dead are not obstacles to it. A car that starts out in something solid may
  move as long as no more of it ends up in something. "Near a car" is near its body, and the boot is reached at the boot
  *Why:* The cars became real-size models while the sim's car was a 0.43 m circle: they stood into walls, and you got in through the side of one
- **A refused command changes nothing (2026-11-11).** Refused commands are not logged, so anything one changed (the way you face, a note) is missing from a replay. `work` and `use` put your facing back when refused and write no note;
  the game shows the reason. An unknown command type is a reject, not an exception. `applyCommand` sets the day's scale from the state first, so a command before the first tick runs at its own world's. *Why:* replays and bug reports must give the game back exactly (log/2026-11-11-13-clear-bugs.md).
- **Parked cars are in the way (2026-11-11).** The tiles whose centre is under a car nobody is driving can't be walked on by anyone on foot: a layer of the grid that
  `Pathfinding.isPassable` asks, so paths go round and free movers stop at it with no new collision code. It is not saved: it is what `state.vehicles` says, kept in step
  at every place a car is put down, entered, left or taken away, and laid in again when a state is read. A tile the car only overhangs stays open, so a car down the
  middle of a three-tile street leaves both sides to walk. You are also stopped at the car's body itself; the dead and animals at the tiles, as their paths are tiles
  *Why:* Car-sized cars that the dead and colonists walked through; and collision that disagreed with pathfinding would leave the dead pressed against a car for ever
- **Animals at parked cars (2026-11-11, AnimalCars.cs).** The small go under, the rest go over, by size against the two models (the sedan's underside 0.28 m up,
  roof 1.28 m; the pickup's underside 0.45 m, bed 1.0 m, cab 1.75 m). Ticks are for a car's width (two tiles); longer crossings take longer in proportion:

  | kind | sedan | pickup | why |
  |---|---|---|---|
  | rabbit (0.35 m) | under, 8 | under, 7 | room under either; a car is cover, so it flees under one as soon as round |
  | dog (0.67 m) | over, 9 | under, 14 | a belly-crawl fits the pickup's 0.45 m, not the sedan's 0.28; a sedan's bonnet is a jump |
  | deer (1.43 m) | leap, 6 | leap, 7 | clears either in one bound; flees straight over one rather than being cornered |
  | wolf (1.0 m) | over, 10 | over, 13 | scrambles up and down; the pickup's bed is higher |
  | bear (1.0 m, 700 hp) | over, 30 | over, 40 | too big to go under, too heavy to leap: a slow climb |

  A crossing is a short straight move it can't steer out of (like your leaps), plain state on the animal (`cross`), integer steps, saved. Paths may cross a car's tiles at
  a cost by kind (rabbit 1, deer 2, dog and wolf 3, bear 8 tiles' worth), so animals go round when round is short. The far side is checked before (the body fits,
  the way is open but for cars) and again each tick; one that can't cross is refused and plans off cars for 60 ticks. **Under** is out of reach: no blow, bite or
  shot finds it (a shot passes it by, a colonist hunting waits, a predator loses it, a zombie's blow on a dog lands on the car), and a rabbit stays under while
  something it fears is within FLEE (15 s at most), and your own swing misses it (ControlMod's swing looks only at `AnimalCars.inTheOpen`). **Over** is in plain
  sight and can be hit, but can't bite until down
  *Why:* Parked cars stopped every animal, so a deer could be cornered against one and a dog left behind by it; the player asked for the small to go under and the
  rest over. A timed crossing, rather than letting some movers stand on a car's tiles, keeps collision and planning in agreement (no mover ever stands where its
  body doesn't fit but while crossing)
- **The code's structure (2026-11-11).** `cs/Sim` is a folder per area (Js, State, Core, Save, Grid, Debug, Movement, Worldgen, World, Combat, Dead, Items, Colony,
  Survivors, You, Others) in one namespace: the folders are for finding things, and nothing in the code changed with the move. Tests stay flat (`XTest.cs`; the files ported
  from TypeScript cover several systems each, so folders would mislead). The game class is in parts by concern under `godot/App`; labs and benches are in `godot/Labs`, the
  self-test in `godot/SelfTest`. Debug flags that set a scene call `Staging` (cs/Sim/Debug): the game no longer writes a state field anywhere. `ArchitectureTest` checks the
  sim's purity, the game's no-writes rule, and that every sim file is in an area folder and in docs/modules.md
  *Why:* The player wants to review the project in person; 125 files in one folder and a 1,050-line game file hid the shape, and rule 5 had exceptions nobody could see
