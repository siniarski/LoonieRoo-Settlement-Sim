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

Godot here is the .NET build (`C:\Program Files\Godot_v4.6.1-stable_mono_win64`), but no .NET SDK is
installed, so only GDScript runs; C# was not measured.

## Results (this machine, 20 threads)

| Step | Now (Vite, TypeScript, Vitest) | Godot 4.6 (GDScript) |
|---|---|---|
| Save a file, see it running | 20-74 ms (hot update, game state kept) | ~0.9 s to rerun the scene on a 128 world; 4.2 s on 1024 (GDScript can also hot-reload into a game run from the editor: not measured) |
| Cold start | dev server 0.16 s; page load with world generation 0.12 s | editor open 2.6 s; first import of the project 3.7 s |
| Typecheck | 0.8 s | (GDScript checks as it loads) |
| One test file / all 792 tests | 2.1 s / 7.4 s | headless script run 0.3 s startup + the work (no test framework installed) |
| Sim work: 177 BFS floods, 128 world | 10.3 ms | 1,657 ms (160x slower); 440 ms written for GDScript (43x, bench_inline.gd) |
| Drawing, 128 world | ~0.3 ms a frame (top-down canvas, measured earlier) | 0.25 ms a frame (isometric) |
| Drawing, 1024 world (1M tiles, 96k furniture) | (not drawn whole: the canvas draws what's on screen) | 1.2 ms mean, 2.8 ms p99 a frame; loading it: JSON 107 ms, filling the tile maps 570 ms, first frame 2.3 s |

What it says:
- **Rendering is not the problem in either.** Godot draws a million-tile isometric map in about a millisecond.
- **The loop gets 15-50 times slower** for visual changes (tens of milliseconds with state kept, against about a
  second to rerun), and the editor sits between you and the code.
- **Why GDScript is slow at this:** it is interpreted bytecode with no JIT, and every value is a Variant, so each `dist[j]`, comparison and add is a checked, dynamically typed operation, and each function call costs a few hundred nanoseconds. V8 compiles the TypeScript loop to machine code over raw typed arrays. Inlining the per-neighbour call and keeping everything typed and local (bench_inline.gd) gets 3.8x back, and it is still 43x behind. The engine's own C++ grid A* (AStarGrid2D) found 170 paths of about 130 steps each in 50 ms (0.3 ms a path): fast because it is C++, but only for what it does.
- **The sim can't be GDScript.** Hot loops run about 160 times slower than the same TypeScript. A port would be
  C# (needs the .NET SDK; it adds a compile on every change, not measured here), or the TypeScript sim kept and
  fed to Godot (Godot doesn't run JavaScript: the sim would run in Node and stream a render snapshot over a
  socket, the same snapshot problem as the Web Worker).
