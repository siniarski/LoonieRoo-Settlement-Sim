# CLAUDE.md

## Project

A 2D grid-based survival sim. It began as a RimWorld-style colony sim (claim buildings, designate rooms, survivors take jobs by priority) and is becoming the player's vision: **Project Zomboid-style survival where you control one survivor directly, with an optional colony sim** when you settle, other settlements living their own lives, and survivors with stories and requests.

**Read NOTES.md "Start here" first**: current state, how to run it, controls, a map of every module, the harness, what's next, and the player's working agreements (push only when asked, every time; commit only on a green check).

This is a **learning and exploration project**. Favour fast iteration, readable code, and throwaway experiments over polish.

## Stack

- **The sim: C# (.NET 8), `cs/Sim`.** Pure C#, no Godot: it builds, tests and runs anywhere.
- **The game: Godot 4.6 .NET, `godot/`**, referencing `cs/Sim`. Rendering is 2D CanvasItem drawing (`godot/Render`), the UI Godot Controls built in code (`godot/Ui`).
- **Tests: xUnit, `cs/Sim.Tests`.** The harness (balance players, bench, stress) is xUnit too, `cs/Harness`.
- The .NET SDK (10.x installed; projects target net8.0) and Godot 4.6.1 .NET at `C:\Program Files\Godot_v4.6.1-stable_mono_win64`. No NuGet packages beyond xUnit and the test SDK.

**Where it came from.** Until 2026-11-08 this was a TypeScript browser game. The sim was machine-translated to C# and checked state for state against the TypeScript, and the TypeScript was retired: **git tag `ts-final`** has it (and the translator and parity checker). File comments still name the TypeScript modules: `x.ts` is `cs/Sim/Systems/X.cs` (NOTES.md "Start here" has the mapping).

**The C# keeps JavaScript's semantics, on purpose** (`cs/Sim/JsCompat.cs`: `Js.*`, `JsMath.*`, `JsString`, `JsRegExp`, `JsMap`, `JsSet`, `JsRecord`, and JS-style list methods `push`, `filter`, `map`, `splice`...). Saves, tests and the balance figures all depend on them, so write new sim code the same way, and know the traps the port hit:
- **Numbers are doubles.** Multiply in doubles (`int * int` overflows 32 bits: squared distances in SUB units do). A JS number that reaches an `int` parameter truncates: fractional values need `double` (road points are `PointD`, not `Point`).
- **"Undefined" is not 0.** `Js.Get(list, i)` past the end gives the default (0 for numbers): check the length when "nothing there" matters.
- **Rounding is JS's.** `JsMath.round` (halves up, not to even or away from zero), `Js.ToFixed` (on the double's exact value).
- **Class initialisation is lazy.** A static field computed from the clock (`Time.DAY_LENGTH`) is read when the class is first used, maybe after `setDayScale`: keep scale-1 bases and rescale in `Time.onClock`.

## Commands

- `./check.sh` builds everything (the sim, its tests, the Godot game) and runs the sim's tests. **Run it before every commit.** `./check.sh --all` adds the Godot self-test (a window opens for a few seconds).
- `dotnet test cs/Sim.Tests -c Release --filter "DisplayName~pathfinding"` runs some of the tests (DisplayName is the describe/it path, e.g. `findPath > solves a maze`).
- The game: open `godot/` in Godot 4.6 .NET and press Play, or `dotnet build godot/LoonieRoo.csproj` then Godot with `--path godot`. Command-line options (`--seed=`, `--start=solo|group`, `--load=save.json`, `--ticks=`, `--shot=out.png`, `--selftest`, ...) are in `godot/README.md`. Not `experiments/godot-bench`: that's a render benchmark (it pans and quits).
- The harness: `dotnet test cs/Harness -c Release --filter "DisplayName~solo balance"` (or `world balance`, `DisplayName=bench`, `DisplayName=stress`); env vars and what each does in NOTES.md "Harness"; reports in `cs/Harness/out/`. Not part of the check.

## Architecture rules (non-negotiable)

1. **The sim is pure and deterministic.** Everything in `cs/Sim` is plain data plus static functions. Same seed plus same inputs must produce the same state, always.
2. **No Godot in `cs/Sim`.** It references nothing but the .NET base library, so it runs in tests, the harness and the game alike.
3. **No `System.Random`, `DateTime.Now` or `Stopwatch` in the sim.** Use the seeded RNG held in sim state (Rng.cs). Time is measured in ticks.
4. **All sim coordinates are grid coordinates** (integer tile x, y). Projection to pixels (top-down now, maybe isometric later) is purely the renderer's job (`godot/Render/Camera.cs`).
5. **The renderer only reads state.** Input handlers translate clicks into sim *commands* (`placeWall`, `designateRoom`, `setPriority`) applied through `Commands.applyCommand`, never by mutating state directly.
6. **Fixed timestep.** The sim advances in discrete ticks (default 10/sec). The renderer may interpolate. Speed controls (pause, 1x, 2x, 4x) change how many ticks run per frame, not tick length.
7. **State must be serialisable** (plain JSON: StateTypes.cs classes with public properties, no hidden state, no delegates in saved state). This enables save/load, scenario files, bug reports and debugging dumps (StateJson.cs, Savefile.cs).
8. **The RNG state lives in sim state as a plain number.** Rng.cs exposes pure functions over it. Saving and loading restores the RNG exactly.
9. **Iteration order must be deterministic.** Store collections as lists or as records with string keys (`JsRecord`, `JsMap`/`JsSet` keep insertion order). Never let a result depend on `Dictionary`/`HashSet` enumeration order, or on unsorted keys.
10. **The world is on the grid; movers have precise positions** (Motion). Every mover has `at` (its centre in fixed-point SUB = 256 units per tile: integers) and `was` (at the tick's start, for the renderer to blend), plus `pos`, the tile under it, which most of the sim uses. Free movers (the survivor you control, zombies, and animals and dogs: Walkers.cs) own `at` and move continuously, colliding with walls as circles (`slide`); other movers walk tile paths (`move`: from, to, progress) and `at` follows their step. Reach and arcs are distances between `at`s. Use `JsMath.sqrt`, never a hypot (exact everywhere), and integer maths where it decides outcomes (in doubles: see the traps above).
11. **Chances that mustn't shift the RNG stream use a stateless hash** (Hash.cs `hashRoll` of ids and ticks), so adding a new chance doesn't change every other random outcome. The sim RNG is for the core rolls that were always there.
12. **Never compute a static field from another class's static field** (`static readonly double X = Other.Y * 2`): initialisation order between classes is the first-use order, and day-length-derived values must follow the clock. Derive them in a function, or register with `Time.onClock`.
13. **Day length is a world setting** (`settings.dayScale`). Durations come from Time.cs (`DAY_LENGTH`, `TICKS_PER_HOUR`) and recompute through `onClock`; things that happen at a rate per tick (needs, crops, work, timed actions) run only on `slowTick` ticks. `Tick.tick` sets the scale from state each tick.
14. **Keep every per-tick system's cost bounded.** Budget expensive work per tick (zombies plan at most PLANS_PER_TICK paths), keep far-off things abstract (dormant zombies, herds, settlements), and never flood the whole map per entity per tick. Measure with the harness's bench and stress and the debug panel's tick ms.

## Replays and the command log

Rule 1 gives us replays for free: **seed + initial state + `{tick, command}[]` = the full game.** Every command applied through `Commands.applyCommand` is appended to `state.commandLog`. A bug report (the menu's "Report a bug", `Savefile.toBugReport`) is a file with the starting state and the log: load it (`--load=` or the menu's file load) and the game replays to where it was. A scenario file is a starting state plus an optional command log.

## Layout

```
cs/
  Sim/              # the sim: Systems/ (one class per system, ~90), StateTypes.cs, State.cs, the hand-written
                    #   core (Grid, Pathfinding, Space, Rng, Hash, Time, Lookup, Savefile, StateJson), the JS layer (JsCompat.cs...)
  Sim.Tests/        # xUnit: one file per system (Vitest.cs keeps the ported tests' expect())
  Harness/          # balance players (ColonyHarness, SoloHarness), Bench.cs (bench, stress); out/ for reports
  LoonieRoo.slnx    # the sim, its tests, the Godot game
godot/              # the game: Game.cs (loop, input), Render/ (drawing), Ui/ (HUD, menu, map), SelfTest.cs
scenarios/          # JSON maps the tests load
experiments/        # old measurements (godot-bench; the browser render-bench is at ts-final)
check.sh            # the check
NOTES.md            # Start here, decisions, design direction, open questions, session log
```

NOTES.md "Start here" maps every system to what it does. Each file opens with a comment stating its rules.

## Core design model

**Designations -> jobs -> workers**

- The player (or the auto-planner) creates *designations*: build wall, place bed, designate room, clean tile, scavenge building.
- Designations become *jobs* in a shared queue, each with a type (build, haul, clean, scavenge, guard, ...).
- Each survivor has a priority per job type (1-4, or off). When idle, a survivor picks the highest-priority job type they can do, then the nearest available job of that type.
- Jobs are **reserved** when claimed so two survivors never take the same one.
- Jobs can become **invalid mid-execution** (target walled off, item removed, room changed). Always handle this by releasing the reservation and returning the survivor to idle. Never leave a survivor stuck.

**Rooms**

- Rooms are found by flood fill over walls and doors, and recomputed only when walls or doors change.
- A room has a player-designated purpose with requirements (e.g. housing needs N beds). Unmet requirements produce a visible "incomplete" state.
- The auto-planner (Planner) is just another source of designations. It goes through the same command path as the player.

**Pathfinding**

- A* on the grid (Pathfinding.cs). Cache paths and invalidate only when the walkability map changes.
- A single `walkabilityVersion` counter in state, bumped whenever a wall or door changes. Cached paths and rooms record the version they were computed at and are stale if it differs.

**Doors**

- Survivors can always walk through doors (and barricades). Zombies can't walk through them: to a moving zombie a door is a wall. But a zombie that can't reach anyone batters doors (and walls) down (Breach). Pathfinding takes a `mover` (`survivor`, `zombie`, `breacher`, `raider`, `crawler`, `free`) for this. Raiders open doors unless they're locked.
- For flood fill, doors are room boundaries: they split rooms the way walls do, but survivors can path through them.

**Walls and doors on tile edges**

- Walls and doors also stand on the border between two tiles (`grid.edges`: each tile owns its north and west side). Generated buildings and the Wall and Door tools use edges; tile walls remain (barricades, the planner, old maps).
- Crossing between neighbours goes through `crossable` (Pathfinding.cs) with the same door rules; reach attacks go through `openBetween` (nobody bites or hits through an edge wall); movers' circles collide with edge lines (Motion fits).
- Reachability labels are keyed on walkabilityVersion and, for movers that need doors open, on which doors are open (doors held open for someone passing don't bump the version).

## Debugging and inspection conventions

They are how the agent (and you) can see what the game is doing.

1. **The command line** (`godot/README.md`): `--seed= --ticks=N --shot=out.png` renders any moment to a file you can look at; `--load=save.json` loads a save or a bug report; `--selftest` drives the running game with real input. Prefer these to asking the player to look.
2. **Text renderer** (`DebugText`): `#` wall, `D` door, `.` floor, `s` survivor, `b` bed, etc. Use it in test failure output so the map is visible in the terminal.
3. **Scenarios:** JSON maps (`scenarios/`, `ScenarioMod`) with survivors and designations. Tests load a scenario, run N ticks, and assert on the result (e.g. nobody is stuck, the room completed).
4. **Debug panel** (the backtick key): fps, tick/sim/draw ms, each survivor's position, job and why they're idle, the job queue, invariants, and path previews and room outlines on the map. "Why is she just standing there?" should be answerable there.
5. **Godot's log** (`%APPDATA%\Godot\app_userdata\LoonieRoo\logs`): C# exceptions thrown in a frame are printed there and the game carries on.
6. **Comparing two runs:** the harness's LOG= and LOG_AT= dump whole states; with determinism, two runs that should agree can be bisected tick by tick to the first difference (how the port's bugs were found).

## Testing

- Write tests for sim logic before or alongside the code: pathfinding, flood fill, job reservation, priority selection, invalid-job recovery. New test files follow the existing ones (xUnit `[Fact(DisplayName = "system > case")]`, `expect(...)` from Vitest.cs).
- The **stuck-state invariant tests**: after running a scenario for N ticks, no survivor is idle with available jobs they could do, and none has been stuck on the same job for too long (no more than **600 ticks** on one job; no more than **20 ticks** idle while a reachable, unreserved job of an enabled type exists). Tune thresholds as needed and record changes in NOTES.md.
- Run `./check.sh` before every commit. A red check means stop and fix.
- Prefer headless sim runs over screenshots to verify behaviour. Use screenshots (`--shot`) for rendering questions, but do look at anything visible before calling it done; `--selftest` (or a new step in SelfTest.cs) for input and UI.
- Balance questions go to the harness (scripted players over several seeds, run in parallel), and the results go in the NOTES.md session log.
- The sim's clock scale (Time.cs) is static state: a test that changes `settings.dayScale` must reset it (`Time.setDayScale(1)`) afterwards. Tests run one at a time, in file order, for this reason.

## Workflow

- **Start tiny.** Get the smallest thing working, play with it, then extend.
- **Commit at each working milestone** with a clear message so experiments can be rolled back.
- **Keep `NOTES.md`** as a decisions log. After any non-trivial experiment, record: what was tried, what happened, what was decided. Read it at the start of each session.
- **Rewrites are fine.** If a system is fighting you, say so and propose a rewrite rather than piling on workarounds. That includes the JS layer: replacing a `Js.*` idiom with plain C# is welcome where tests (and, for balance, the harness) show nothing changes.
- Ask before adding dependencies (NuGet packages, Godot add-ons, tools like dotnet-trace). Prefer fewer.
- Keep functions small and files focused. Prefer plain data and static functions over class hierarchies in `cs/Sim`.

## Milestones

The original eight (grid and text renderer; canvas and tools; a survivor with A*; jobs with reservation and priorities; rooms; needs; night zombies and guards; the auto-planner) are all done, along with a long list of systems since, and the port to C# and Godot (see NOTES.md). The plan follows NOTES.md "Design direction" and "Start here": **next is the player's playtest in Godot**, then whatever it says about movement and combat feel, inventory, and the visual target.

## Known pitfalls

- Hauling multiplies job types and edge cases. Start with a simplified version (one item at a time, no stacks).
- Survivors blocked by newly placed walls or doors. Re-validate jobs and paths when walkability changes.
- Recomputing everything every tick. Recompute rooms and paths only on change.
- Letting render concerns leak into the sim (sprite sizes, pixel positions, animation state). Don't.
- The JS-semantics traps listed under Stack (truncation, overflow, undefined as 0, rounding, lazy statics).
