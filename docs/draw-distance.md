# Draw distance (2026-11-09)

The player: "create a world big enough to stress test a draw distance of 1000m with trees and buildings. If you need to build up to that draw distance."

## Before

First person drew 7 x 7 chunks of 16 tiles round you (~56 m), with the camera's far plane at 120 m and fog (density 0.018) hiding the edge. Nothing past that existed in 3D.

## The far field (godot/World3D/FarField.cs)

Past the built chunks, out to `FarField.Range` metres (`--view=M` in the game; 0, the default, is off):

- **Regions of 64 x 64 tiles**, nearest first, 2 a frame, dropped past Range + 64 m, rebuilt when a region's ground changes (checked every 300 frames).
- **Ground** on a 4-tile grid (512 triangles a region), vertex colours by ground, water flat at its level. Heights from `Heights.Field.coarse` (new, pure, never used by the sim): the rolling noise and the water level without the bank, road and building easing. Measured against the exact corners on land (a test): 0.13 m at the 95th percentile, 0.44 m at worst. The exact field costs ~25 ms per 32-tile chunk (chamfer distances, a capped cache of 1,024 chunks): fine round you, ~100 s for the ~4,000 chunks inside 1 km.
- **Buildings as blocks**: floor tiles (boards, tiles, concrete) merged into rectangles, walls to the eaves, a flat roof in one of five roof colours.
- **Trees**: the far LOD (Trees.TreeMesh(s, 2), 10-86 triangles) at the same place, size and tint as the near ones, one MultiMesh per species a region, no shadows.
- **The hole**: where the near chunks are, the far field's ground and blocks discard their pixels and its trees collapse to a point, in the shaders (a uniform: the chunks' square), so moving needs no rebuild. The far ground sits 8 cm under the near ground.
- With the far field on, the fog thins to `3.2 / Range` and the far plane goes to Range + 64 m.

## The bench (godot/World3D/DrawBench.tscn)

A 2048 world (seed 42, every chunk generated), you in the middle, noon; per range: wait for the far field to be built, a shot at eye height towards the town and one from 60 m up, then a full turn. Results: experiments/draw-bench/bench.md and the view_*.png shots.

    Godot --path godot res://World3D/DrawBench.tscn -- [--size=2048] [--runs=0,250,500,1000,1000:notrees] [--frames=360] [--out=DIR]

## Found on the way

- **The sound scanned every piece of furniture each frame** (Loops: generators; WorldNoises: turrets). A 2048 world has ~330k trees as furniture: ~8 ms a frame, with the far field off. Now each keeps its list and looks again only when the furniture list changes: 1.2 ms -> 0.02 ms on a 1024 world.
- **Generating a whole 2048 world takes ~19 minutes** (seed 42; a 1024 world: ~16 s). Creating one (only the start generated) is 20 s (the harness's new `new-2048` stress scenario). Superlinear somewhere in chunk generation or settling: a sim problem, left for a stress-test session.
- **In the game the far field only knows what's been generated.** The sim generates chunks as survivors come near, so past that the grid is blank grass and the far field draws it so. Drawing far land the right way means reading the world plan (Worldplan: nature, roads, lots; pure from the seed) for chunks not generated yet, not the grid.
