- **2026-11-12, the second quality pass: the sim** (one of three parallel sessions; the game and the tests, harness and
  docs had the others). The first pass's "Safe, and left for another pass" list for the sim, then what landed since
  in a hurry (cars, crossings, water, doors), then `isPassable` and `crossable` laid out per mover. No behaviour
  change anywhere: fifteen commits, each on a green `./check.sh`, and solo balance on seeds 1 to 6 gave the same lines
  as the baseline after every one of them that touched code (`diff` of the reports, empty).
  - **What changed.**
    - Helpers for what was written out many times: `Space.chebyshev` (the square distance, 14 files), `Items.freeOfKind`
      (free items of a kind on the ground, 7), `Survivors.walkTo` ("walk there or give up", 11 sites in 8 files:
      eating, treating, sleeping twice, reading, dressing, relaxing, arming, a watchtower, foraging, drinking; the
      commit message says ten), `Work.pickUp` (fetching the job's item: hauling, fuel, research, crafting, the
      doctor; the commit message says "the gravedigger's corpse" for the fuel worker, wrongly), `Vehicles.getVehicle`
      (a car by id, 10, and CarLeaps' private `carById` gone), `Space.firstInRings` (the first tile in growing square
      rings: Worldgen.nearestAcross, the settlements' nearest land, your heir's spot), `Breach.board` (boards go on,
      from Designations and Handiwork, beside `damageEdge` where they come off).
    - Rules into their files: tile doors' `toggleDoor` and the `toggleLock` command's two branches out of Commands.cs
      into Doors.cs (ControlMod no longer calls up into the command layer); the colony's storage rules out of
      Grid/Rooms.cs into Colony/RoomStorage.cs (a part of Rooms: no call site changed).
    - Parts out of long files, moved whole: Needs.cs (606 lines) to NeedsTreatment.cs and NeedsDeath.cs, Hordes.cs
      (749) to HordeField.cs (the costs, the field, a member's step), Worldgen.cs to WorldStart.cs (createWorld, the
      camp, the first strays).
    - `GridChunk` (GridLayers.cs): the chunk size 32 (and its 5 and 31) in one place for the layers, reachability,
      generation, room labels and both search scratches. Worldplan's PLAN_CHUNK is its own and stays.
    - `isPassable` and `crossable` (Pathfinding.cs): first what holds for nearly everyone, then one line per mover with
      a comment. `PassableLayoutTest` keeps the old two and compares old and new for every mover (and an unknown name)
      on three random maps: every tile kind, doors shut, open, locked and both, shallow and deep water, parked cars,
      off the map; every edge kind open, locked and boarded, each way. The coverage is asserted exactly (19 tile
      states; 49 edge states a mover), and two deliberate breaks of a rule both failed it. The boarded test is still a
      list lookup asked last, only where the answer turns on it, so the hot path costs what it did (not benchmarked).
      Delete the test when a rule changes on purpose: it says so.
    - Headers: Swim.cs said nobody else stands on water, and Pathfinding's mover list had animals on floor only and no
      swimmer, from before wading; both now match the code, and modules.md has every new part.
  - **Found clean.** No state declared outside State/: the architecture test's partial-class check still catches it,
    and the only `{ get; set; }` outside State/ are interfaces (IWalker, IBody, IHasId), Point and the world preview's
    file. The car files' parts (body, parking, heights, actions, cabin, push, alarm, hide) each hold one concern and
    their headers and modules.md agree with them. The three crossings (Dead/Crossings, Movement/CarLeaps,
    World/AnimalCars) look for cars in different ways on purpose (the tile under a step, a ray ahead, the nearest
    within a tile), so no shared helper there. FurnitureBody, VehicleAlarm, VehicleCabin, VehicleHide, VehiclePush:
    headers read true.
  - **For the player, ranked** (each would change behaviour; not done).
    1. **A locked edge door opens at a touch.** `Doors.toggleEdgeDoor` (Movement/Doors.cs:151, from `use`,
       ControlMod.cs:690) never looks at `edgeLocked`; a tile door refuses ("it's locked", `Doors.toggleDoor`). Locking
       an edge door shuts it, and E opens it again. Fix: refuse when locked, as the tile door does (and say whether a
       wide door is locked as one).
    2. **Locking a tile door shuts it on whoever stands in it.** `Doors.toggleLock` (Doors.cs:252 on) takes the door
       out of `open` and turns out the dead (`Zombies.evictZombies`), but a survivor or resident in the doorway stays,
       in a shut locked door; `toggleDoor` refuses that ("someone is in the doorway").
    3. **Dogs move as colonists** (`Pathfinding.SURVIVOR`, Survivors/Dogs.cs:125 and 141; AnimalCars.moverOf), so a
       dog goes through locked and boarded doors and over barricades, where the dead and animals don't. Fine if "you
       and yours" is meant to include the dog.
  - **Safe, and left** (no behaviour change; not done for the budget): the two chunked search scratches (Space's and
    A*'s) made one; `seesBetween` as `seesThrough` of the edge between; the arc `4 * h * t * (1 - t)` written four
    times (Leaps.lift, Crossings.lift twice, AnimalCars.lift); Swim.nearestLand and Towns' anchor as `firstInRings`
    with options.
  - **Not verified.** The game was not run (`--all` not needed: nothing in godot/ moved); no bench or stress run for
    the isPassable layout (same work per call by reading). Check green, 1016 tests.
