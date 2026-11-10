# CLAUDE.md

## Project

A grid-based survival sim becoming the player's vision: **Project Zomboid-style survival in first person, with an
optional colony sim** once you settle, other settlements living their own lives, and survivors with stories. A
learning project: favour fast iteration, readable code and throwaway experiments over polish.

**Read NOTES.md "Start here" first** (state, commands, working agreements, what's next, where to read what), **then
only the `docs/` file your task touches.**

## Stack

- **The sim: C# (.NET 8), `cs/Sim`**: pure, no Godot. Tests: xUnit, `cs/Sim.Tests`. Harness (balance, bench,
  stress): xUnit, `cs/Harness`.
- **The game: Godot 4.6 .NET, `godot/`**: first person in 3D (`World3D/`), the map screen's Colony tab in 2D
  CanvasItem drawing (`Render/`), UI as Controls built in code (`Ui/`). Map: docs/modules.md, godot/README.md.
- .NET SDK 10.x (target net8.0); Godot 4.6.1 .NET at `C:\Program Files\Godot_v4.6.1-stable_mono_win64`. No NuGet
  packages beyond xUnit and the test SDK.
- TypeScript until 2026-11-08 (git tag `ts-final`); comments still name TS modules: `x.ts` is `cs/Sim/Systems/X.cs`.

**The C# keeps JavaScript's semantics, on purpose** (`cs/Sim/JsCompat.cs`: `Js.*`, `JsMath.*`, `JsString`,
`JsRegExp`, `JsMap`, `JsSet`, `JsRecord`, list methods `push`, `filter`, `map`, `splice`...). Saves, tests and balance
depend on them: write new sim code the same way. The port's traps:
- **Numbers are doubles.** Multiply in doubles (`int * int` overflows: squared distances in SUB units do). A JS number
  reaching an `int` parameter truncates: fractional values need `double` (road points are `PointD`).
- **"Undefined" is not 0.** `Js.Get(list, i)` past the end gives the default: check the length when it matters.
- **Rounding is JS's.** `JsMath.round` (halves up), `Js.ToFixed` (on the double's exact value).
- **Class initialisation is lazy.** A static from the clock (`Time.DAY_LENGTH`) is read on first use, maybe after
  `setDayScale`: keep scale-1 bases and rescale in `Time.onClock`.
- CS0472 ("a value type is never null") is silenced in Sim.csproj: when it fires, it is usually one of these.

Plain C# in place of a `Js.*` idiom is welcome where tests (and, for balance, the harness) show nothing changes.

## Commands

- `./check.sh`: builds the sim, tests and game (Release and Debug), runs the tests (~10 s). **Before every commit.**
  `--all` adds the Godot self-test (a window opens). Full output in `.check/`: **read it, don't rerun**.
- `./finish.sh "message"` (or `-F file`, `--all`): the check, then commits all (`git add -A`) only if green. No push.
- `./shot.sh <game args>`: builds Debug, runs Godot, prints a short result (`.check/shot.log`). Game flags (`--seed=`,
  `--load=`, `--ticks=`, `--shot=`, `--selftest`, text and trace output...) are in godot/README.md.
- Some tests: `dotnet test cs/Sim.Tests -c Release --filter "DisplayName~pathfinding"` (DisplayName is the
  describe/it path, e.g. `findPath > solves a maze`).
- Harness: `dotnet test cs/Harness -c Release --filter "DisplayName~solo balance"` (or `world balance`,
  `DisplayName=bench`, `DisplayName=stress`); env vars in docs/harness.md; reports in `cs/Harness/out/`.

## Architecture rules (non-negotiable)

1. **The sim is pure and deterministic.** Plain data plus static functions; same seed and inputs, same state.
2. **No Godot in `cs/Sim`.** Only the .NET base library.
3. **No `System.Random`, `DateTime.Now` or `Stopwatch` in the sim.** The seeded RNG in state (Rng.cs); time is ticks.
4. **Sim coordinates are grid coordinates** (integer tiles). Projection is the renderer's job.
5. **The renderer only reads state.** Input becomes commands through `Commands.applyCommand`, never direct mutation.
6. **Fixed timestep** (default 10 ticks/sec). Speed changes ticks per frame, not tick length.
7. **State is serialisable**: plain JSON (StateTypes.cs public properties, no hidden state, no delegates), for saves,
   scenarios, bug reports and dumps (StateJson.cs, Savefile.cs).
8. **The RNG state is a plain number in sim state**; Rng.cs is pure functions over it.
9. **Iteration order is deterministic.** Lists or string-keyed records (`JsRecord`; `JsMap`/`JsSet` keep insertion
   order); never `Dictionary`/`HashSet` enumeration order or unsorted keys.
10. **The world is on the grid; movers have precise positions** (Motion): `at` (centre, SUB = 256 a tile, integers),
    `was` (at the tick's start, for blending), `pos` (the tile most of the sim uses). Free movers (you, zombies,
    animals, dogs: Walkers.cs) own `at`, colliding with walls as circles (`slide`); others walk tile paths (`move`:
    from, to, progress). Reach and arcs are distances between `at`s: `JsMath.sqrt`, never a hypot, and integer maths
    (in doubles) where it decides outcomes.
11. **Chances that mustn't shift the RNG stream use a stateless hash** (Hash.cs `hashRoll` of ids and ticks); the
    sim RNG is for the core rolls that were always there.
12. **Never compute a static field from another class's static field**: init order is first use, and day-length
    values must follow the clock. Derive in a function or register with `Time.onClock`.
13. **Day length is a world setting** (`settings.dayScale`): durations from Time.cs (`DAY_LENGTH`, `TICKS_PER_HOUR`)
    via `onClock`; per-tick rates (needs, crops, work) run on `slowTick` ticks; `Tick.tick` sets the scale each tick.
14. **Bound every per-tick system's cost.** Budget work per tick (zombies plan at most PLANS_PER_TICK paths), keep
    far-off things abstract (dormant zombies, herds, settlements), never flood the map per entity per tick. Measure
    with the harness's bench and stress and the debug panel's tick ms.

The core model (designations -> jobs -> workers, rooms, paths, doors, edge walls, replays): docs/sim-model.md.
Render concerns (sprite sizes, pixels, animation state) never leak into the sim.

## Inspecting and testing

- Text first: tests, DebugText (`#` wall, `D` door, `s` survivor; use it in failure output), the game's text and
  trace flags. Screenshots (`./shot.sh --shot=`) for rendering questions, but look at anything visible before calling
  it done; `--selftest` (or a new SelfTest.cs step) for input and UI.
- Scenarios (`scenarios/`, ScenarioMod): tests load one, run N ticks, assert. Bug reports replay with `--load=`.
  Debug panel: backtick. Godot's log: `%APPDATA%\Godot\app_userdata\LoonieRoo\logs`.
- Tests alongside sim logic: `[Fact(DisplayName = "system > case")]`, `expect(...)` from Vitest.cs.
- **Stuck-state invariants:** after N ticks no survivor is on one job over **600 ticks**, or idle over **20 ticks**
  with a reachable, unreserved job of an enabled type. Record threshold changes in docs/decisions.md.
- A test that changes `settings.dayScale` resets it (`Time.setDayScale(1)`). Tests run one at a time in file order
  per process; the check shards whole classes over processes by `.check/test-times.txt` (seed:
  `cs/Sim.Tests/test-times.txt`): split a class that takes many seconds.
- Balance questions go to the harness (several seeds in parallel), results to a session-log entry.

## Workflow

- **Push only when the player asks, every time. Commit only on a green check** (`./finish.sh`). Red: stop and fix.
- One small task per session; multi-part requests one piece at a time in one session, unless the player asks for
  parallel sessions (cheaper models for mechanical work). Start tiny; commit at each working milestone.
- Log non-trivial work as a new file in `log/` (NOTES.md "Session log"). Edit NOTES.md only when "Start here" or an
  open question changes; decisions go in docs/decisions.md.
- Rewrites are fine: if a system fights you, say so and propose one. Ask before adding dependencies. Small functions,
  focused files, plain data and static functions in `cs/Sim`.

## Layout

`cs/Sim/` (Systems/: a class per system; StateTypes.cs; the core; the JS layer), `cs/Sim.Tests/`, `cs/Harness/`,
`godot/` (Game.cs, World3D/, Characters/, Audio/, Render/, Ui/, SelfTest*.cs, assets/), `art/` (Blender, headless),
`scenarios/` (test maps), `experiments/` (write-ups), `docs/` (read on demand), `log/` (session log), NOTES.md,
check.sh, finish.sh, shot.sh. Every module: docs/modules.md.
