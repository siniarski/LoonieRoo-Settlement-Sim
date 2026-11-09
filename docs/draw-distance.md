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

## The bench again, and land not generated (2026-11-09, later)

With world generation fixed (docs/worldgen-speed.md) and the sound's furniture scan gone, the 2048 bench (every chunk generated, 8.7 s) runs in 22 s:

| range m | far trees | regions | far tris | frame ms | p95 | render cpu | render gpu |
|--:|---|--:|--:|--:|--:|--:|--:|
| 0 (today's ~56 m) | | 0 | 0 | 0.80 | 0.96 | 0.17 | 0.25 |
| 250 | yes | 62 | 1.1M | 1.02 | 1.27 | 0.33 | 0.32 |
| 500 | yes | 224 | 4.6M | 1.34 | 1.60 | 0.55 | 0.57 |
| 1000 | yes | 825 | 16.7M | 2.81 | 3.48 | 1.39 | 1.35 |
| 1000 | no | 825 | 0.45M | 1.26 | 1.41 | 0.34 | 0.27 |

So a kilometre costs ~2 ms a frame on this machine, three quarters of it the far trees (267k of them): a cheaper far tier (cards or a canopy mesh past ~400 m) is where to look if it's ever needed.

**Land not generated yet is drawn from the world plan** (FarField.PlanGround: the ground by generateChunk's own rules, the lots' yards; buildings from the lots' footprints; trees by generateChunk's roll), so in a game, where only the chunks near survivors exist, the far field isn't blank grass any more. A region is built again when its chunks are generated (its signature counts them). `DrawBench -- --lazy` (only the start generated): the same frame (2.47 ms at 1000 m), but a region takes 5.2 ms to build from the plan against 1.6 from the grid, so filling a 1 km view adds ~10 ms to a frame for its first few seconds (2 regions a frame). A worker thread for region building would hide that.
