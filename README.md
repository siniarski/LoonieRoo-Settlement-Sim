# LoonieRoo Settlement Sim

A grid-based survival game: Project Zomboid-style survival in first person, with an optional colony sim once you
settle. A learning project. This page is for reading the code; the working notes for a session are in NOTES.md and
the rules an agent works to are in CLAUDE.md.

## The shape

```
godot/         the game: draws the world, plays sounds, takes input          (Godot 4.6 .NET, C#)
   |              reads the state; changes it only by sending commands and running ticks
   v
cs/Sim/        the simulation: plain data and static functions               (pure C#, no Godot)
   ^              deterministic: the same seed and commands give the same world
cs/Sim.Tests/  ~940 xUnit tests of the sim
cs/Harness/    balance runs, benchmarks and stress tests of the sim
art/           Blender scripts that build every model, run headless
```

Everything that happens in the world happens in `cs/Sim`. The game is a view of it and a source of commands, so the
sim can be tested, replayed and benchmarked with no window.

## Reading order

1. **`cs/Sim/State/StateTypes.cs`**: what the world is. Every type in the state, as plain data.
2. **`cs/Sim/Core/Tick.cs`**: what happens each tick, in order. Each line is a system; follow the ones you care about.
3. **`cs/Sim/Core/Commands.cs`**: the only way in from outside. Every player action is a command, logged for replays.
4. **A system or two**, each a static class in its area's folder: `Colony/Jobs.cs` and `Colony/Work.cs` (how colonists
   pick and do work), `Dead/Zombies.cs`, `You/ControlMod.cs` (the survivor you play), `Worldgen/Worldgen.cs`.
   Every file opens with a comment saying what it is and the rules it keeps.
5. **`godot/App/Game.cs`**: the game's frame (run ticks, then update the views), and its parts beside it.
6. **`godot/World3D/FirstPerson.cs`**: how the state becomes a picture.

`docs/modules.md` has one line for every module. `docs/sim-model.md` explains the core model (designations, jobs,
workers, rooms, paths, doors). `docs/decisions.md` records why things are the way they are.

## Layout

| Folder | What is in it |
| --- | --- |
| `cs/Sim/State`, `Core`, `Save` | The state's types; the tick, commands, clock and RNG; save files |
| `cs/Sim/Grid`, `Movement` | Tiles, walls, paths and rooms; how people, animals and cars move |
| `cs/Sim/Worldgen`, `World` | Making the world from a seed; weather, growth, fire, light, power, farming, animals |
| `cs/Sim/Combat`, `Dead` | Weapons, wounds, defences; the dead |
| `cs/Sim/Items`, `Colony`, `Survivors` | Items and inventory; jobs and work; needs, health, mood, skills |
| `cs/Sim/You`, `Others` | The survivor you play; wanderers, traders, neighbours |
| `cs/Sim/Js`, `Debug` | The JavaScript-semantics layer (below); test maps, ASCII views, hand-set debug scenes |
| `godot/App` | The game class in parts: the frame, the command line, sessions, input, screenshots |
| `godot/World3D`, `Characters`, `Audio` | First person: the world's meshes, people, light, weather; bodies; sound |
| `godot/Ui`, `Render` | The HUD, inventory, menu, map; the 2D top-down Colony tab |
| `godot/SelfTest`, `Labs` | The game driven by real input and checked; stand-alone scenes and benches |
| `art/scripts` | One Blender script per kind of model (`build_items.py`, `build_furniture.py`, ...) |
| `docs`, `log`, `experiments` | Design notes by topic; a dated entry per piece of work; write-ups behind decisions |

## Two things that look odd

- **The sim's C# reads like JavaScript** (`list.push`, `Js.T(x)`, `JsMath.round`, lower-case method names). It was
  ported from TypeScript on 2026-11-08, and saves, tests and balance depend on JavaScript's number and ordering
  rules, so the port keeps them on purpose (`cs/Sim/Js/JsCompat.cs`). Plain C# replaces an idiom only where tests
  show nothing changes.
- **The sim is one namespace of static classes**, not objects with methods. State is data; systems are functions
  over it. The folders under `cs/Sim` are for finding things, not for access control.

## The rules the code keeps

The full list is in CLAUDE.md ("Architecture rules"). The ones that shape everything:

- The sim is pure and deterministic: no Godot, no wall clock, no unseeded randomness. The RNG's state is a number
  in the sim state.
- The game only reads state. Input becomes commands (`Commands.applyCommand`); a debug flag's hand-set scene is a
  call to `Staging`.
- State is plain JSON, so a save, a test scenario and a bug report are the same thing.
- Every per-tick system's cost is bounded.

`cs/Sim.Tests/ArchitectureTest.cs` checks the first two against the source on every run.

## Build, test, run

Needs the .NET SDK (8 or later) and Godot 4.6.1 .NET.

```bash
./check.sh
```

builds the sim, tests and game and runs the tests (about 10 s); `./check.sh --all` also runs the game's self-test
in a window.

```bash
dotnet test cs/Sim.Tests -c Release --filter "DisplayName~pathfinding"
```

runs some tests. To play, open `godot/` in Godot 4.6 .NET and press Play; the command line's options (a seed, a
save, screenshots, a text trace) are listed at the top of `godot/App/GameArgs.cs` and in `godot/README.md`.
