# Godot benchmark (2026-11-07)

A throwaway Godot 4.6 project to measure what moving to Godot would cost the developer feedback loop,
and how an isometric view of our worlds performs there. Not part of the game.

- `npm run harness -- godotexport` writes `data/world-128.json` and `data/world-1024.json` (a generated
  world from the TypeScript sim) and times the TypeScript half of the sim benchmark (`harness/out/godot-ts-bench.txt`).
- `main.gd` draws an exported world isometric: ground on one `TileMapLayer`, walls, water, doors, trees and
  furniture as raised blocks on a Y-sorted second one. It prints load, build and frame timings and quits.
  `godot --path experiments/godot-bench -- --size=1024 --frames=240` (`--shot` saves `shot.png`).
- `bench.gd` is the sim half, headless: the same 177 BFS floods over the same 128 world as the TypeScript
  side. `godot --headless --path experiments/godot-bench --script res://bench.gd`

Godot here is the .NET build (`C:\Program Files\Godot_v4.6.1-stable_mono_win64`). Installed for the C# and C++ runs
(2026-11-07, with the player's OK): the .NET 10 SDK, Visual Studio 2022's C++ workload (MSVC 14.44, CMake, Ninja),
Python 3.12 (godot-cpp generates its bindings with it), and godot-cpp 4.5 (cloned outside the repo).

- C#: `BenchCs.cs` in `benchcs.tscn` (`LoonieBench.csproj`). `dotnet build LoonieBench.csproj` builds it Debug, as running
  from the editor does; `-p:BenchOpt=1` turns the optimiser on. Run: `godot --headless --path experiments/godot-bench res://benchcs.tscn`
- C++: a GDExtension. `cpp/` (CMake) builds `bin/loonie_bench.windows.template_debug.x86_64.dll`, loaded through
  `bin/loonie_bench.gdextension`; `bench_cpp.gd` calls `BenchCpp.bfs_floods` once for all the floods. Build from a VS x64
  prompt (vcvars64) with Python on PATH: `cmake -S cpp -B cpp/build -G Ninja -DCMAKE_BUILD_TYPE=Release -DGODOTCPP_TARGET=template_debug -DGODOT_CPP_DIR=<godot-cpp>`, then `cmake --build cpp/build`.

## Results (this machine, 20 threads)

| Step | Now (Vite, TypeScript, Vitest) | Godot 4.6 (GDScript) |
|---|---|---|
| Save a file, see it running | 20-74 ms (hot update, game state kept) | ~0.9 s to rerun the scene on a 128 world; 4.2 s on 1024 (GDScript can also hot-reload into a game run from the editor: not measured) |
| Cold start | dev server 0.16 s; page load with world generation 0.12 s | editor open 2.6 s; first import of the project 3.7 s |
| Typecheck | 0.8 s | (GDScript checks as it loads) |
| One test file / all 792 tests | 2.1 s / 7.4 s | headless script run 0.3 s startup + the work (no test framework installed) |
| Sim work: 177 BFS floods, 128 world | 10.3 ms | GDScript 1,657 ms (160x slower); 440 ms written for GDScript (43x, bench_inline.gd) |
| ...in C# | | 24.7 ms as Godot builds it from the editor (Debug); 5.7 ms optimised (1.8x faster than TypeScript) |
| ...in a C++ plugin (GDExtension) | | 7.0 ms (one call for all the floods; godot-cpp's debug target keeps bounds checks on Godot's arrays) |
| Edit sim code, see it | 20-74 ms, game kept | C#: 1.0 s to build + 0.4 s to start the scene, no reloading into a running game. C++: 2.1 s to build after a one-file edit + 0.3 s to start (first build of godot-cpp 44 s, configure 4.7 s); the editor can reload a GDExtension, not measured |
| Drawing, 128 world | ~0.3 ms a frame (top-down canvas, measured earlier) | 0.25 ms a frame (isometric) |
| Drawing, 1024 world (1M tiles, 96k furniture) | (not drawn whole: the canvas draws what's on screen) | 1.2 ms mean, 2.8 ms p99 a frame; loading it: JSON 107 ms, filling the tile maps 570 ms, first frame 2.3 s |

What it says:
- **Rendering is not the problem in either.** Godot draws a million-tile isometric map in about a millisecond.
- **The loop gets 15-50 times slower** for visual changes (tens of milliseconds with state kept, against about a
  second to rerun), and the editor sits between you and the code.
- **Why GDScript is slow at this:** it is interpreted bytecode with no JIT, and every value is a Variant, so each `dist[j]`, comparison and add is a checked, dynamically typed operation, and each function call costs a few hundred nanoseconds. V8 compiles the TypeScript loop to machine code over raw typed arrays. Inlining the per-neighbour call and keeping everything typed and local (bench_inline.gd) gets 3.8x back, and it is still 43x behind. The engine's own C++ grid A* (AStarGrid2D) found 170 paths of about 130 steps each in 50 ms (0.3 ms a path): fast because it is C++, but only for what it does.
- **C# or C++ run the sim fine:** as fast as the TypeScript or faster (5.7 and 7.0 ms against 10.3). What they cost is the
  loop: every sim change is a build and a restart (about 1.4 s for C#, 2.4 s for C++), with the game's state lost unless
  saved and reloaded, against tens of milliseconds with the state kept now. The 792 tests would need porting too.
- **The sim can't be GDScript.** Hot loops run about 160 times slower than the same TypeScript. A port would be
  C# (needs the .NET SDK; it adds a compile on every change, not measured here), or the TypeScript sim kept and
  fed to Godot (Godot doesn't run JavaScript: the sim would run in Node and stream a render snapshot over a
  socket, the same snapshot problem as the Web Worker).

## Pathfinding (A*)

The same A* in every language (4 ways, Manhattan distance, a binary heap of f * 2^21 + tile, stale entries skipped;
C++ uses std::priority_queue over the same keys) over the same start and goal pairs, picked in each world's biggest
open area: `npm run harness -- godotastar` (TypeScript, after `godotexport`), `astar.gd` (GDScript, the C++ plugin's
`BenchCpp.astar_paths`, and Godot's own `AStarGrid2D`), `astarcs.tscn` (C#). Every one found the same total path
length (16,479 steps on the 128 world, 43,690 on the 1024), so they did the same work. Tiles only: edge walls are
ignored, as `AStarGrid2D` can't express them. One timed run after a warm-up.

| | 128 world, 200 paths | 1024 world, 60 long paths |
|---|---|---|
| TypeScript (Node, V8) | 12.6 ms | 341 ms |
| The game's own findPath (TypeScript, with edge walls and doors) | 17.1 ms | 975 ms |
| C#, Debug (as the editor runs it) | 15.6 ms | 471 ms |
| C#, optimised | 7.8 ms | 190 ms |
| C++ plugin (GDExtension) | 3.8 ms | 116 ms |
| Godot's AStarGrid2D (C++) | 2.8 ms (+0.7 ms to set up the grid) | 458 ms (+47 ms setup) |
| GDScript | 266 ms | 8,231 ms |

- Against TypeScript: optimised C# is 1.6-1.8x faster, the C++ plugin 2.9-3.3x, GDScript 21-24x slower.
- AStarGrid2D is the quickest on short paths and slower than our own C++ (and C#) on long ones.
- The game's findPath costs 1.4-2.9x the plain A* for what it adds (edge walls, doors, and its caches).

