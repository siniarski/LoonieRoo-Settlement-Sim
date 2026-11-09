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

## On by default, crowns past 350 m, and the Options page (2026-11-09, later still)

The player: "can we increase the draw distance and perhaps introduce our first options in menu to adjust/cull if needed."

**Far trees past 350 m are crowns.** The far LOD's cost was draw calls as much as triangles: one MultiMesh per species per region, ~2,000 of the 2,659 draw calls at 1 km, and render CPU (1.5 ms) as big as the GPU's. Now each far tree past `FarField.TREE_TIER` (350 m, to the region's middle) is a crown: a diamond of 8 triangles (a point at the foliage's base, a ring of four at its widest, a tip) as tall, wide and green as that species' far LOD, measured from the model's green vertices. A region's crowns are baked into one mesh in the ground's material: two draw calls a far region, not seven. The far LOD's MultiMeshes and the crowns swap by visibility range, so moving needs no rebuild. From 60 m up at 1 km the tiered view and the far LOD everywhere look the same (`experiments/draw-bench/options/bench_1000_*_high.png`); crowns all the way (`simple`) read as conifers close up.

**Region builds are budgeted by time** (4 ms a frame, at least one), not two a frame: building from the world plan is ~7 ms a region, so filling 500 m (225 regions) takes ~1.5 s of build time spread over the first seconds, nearest first.

`DrawBench -- --lazy` (a 2048 world, only the start generated, as in a game), the same machine (RTX 5070, Core Ultra 7 265K), 1280x720; `tiered` is the game's far trees:

| range m | far trees | regions | far tris | far trees drawn | build ms | frame ms | p95 | render cpu | render gpu | draw calls |
|--:|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| 0 | | 0 | 0 | 0 | 0 | 0.64 | 0.95 | 0.15 | 0.30 | 338 |
| 250 | tiered | 62 | 1.14M | 18,099 | 439 | 0.85 | 1.18 | 0.27 | 0.39 | 554 |
| **500** | **tiered** | 225 | 2.13M | 68,156 | 1517 | **1.03** | 1.28 | 0.41 | 0.48 | 724 |
| 750 | tiered | 482 | 2.86M | 141,532 | 3266 | 1.23 | 1.53 | 0.49 | 0.53 | 864 |
| 1000 | tiered | 826 | 3.85M | 242,122 | 5595 | 1.57 | 1.99 | 0.61 | 0.61 | 1049 |
| 1000 | lod2 (before) | 826 | 15.3M | 242,122 | 4333 | 3.07 | 4.04 | 1.54 | 1.36 | 2660 |
| 500 | simple | 225 | 0.67M | 68,156 | 1452 | 0.89 | 1.12 | 0.23 | 0.37 | 475 |
| 1000 | simple | 826 | 2.38M | 242,122 | 5484 | 1.41 | 1.84 | 0.49 | 0.51 | 804 |
| 1000 | none | 826 | 0.45M | 0 | 1586 | 1.20 | 1.57 | 0.36 | 0.34 | 576 |

(Before the crowns, the same lazy bench: 500 m 1.59 ms, 1000 m 2.74 ms.) So 1 km now costs 0.9 ms over the near view, not 2.1, and the trees in it 0.4 ms, not 1.5. Building is ~30% slower at 1 km (the crowns' vertices).

**The game draws 500 m by default** (+0.4 ms a frame here). Full report: `experiments/draw-bench/options/bench.md`; the game from 50 m up at 0, 250, 500 and 1000 m: `game_*_high.png`.

**The Options page** (the menu's main page, Options; `godot/Ui/Graphics.cs`): view distance (near only, 250, 500, 750, 1000 m), detail distance (the near chunks' radius: 40, 56, 72, 88 m: WorldMesh, trees, furniture and roofs), far trees (off, simple, detailed), shadows (off; low 24 m, a 2048 atlas; medium 40 m, 4096, as before; high 80 m, 8192), character models (the nearest 10, 20, 40, 60 people, and as many animals), 3D resolution (50, 75, 100%; FSR below 100, the HUD stays sharp) and anti-aliasing (off, FXAA, MSAA 4x). Saved in `user://settings.cfg` (`[graphics]`; the volume keeps `[audio]`), loaded at start, applied at once (the renderer's knobs are statics read every frame; a tree mode or a detail distance builds in nearest first). `--view=M` and `--detail=N` win for the run and aren't saved; a `--shot` and `--selftest` never read or write the player's file. Not measured per option: shadows, resolution and anti-aliasing were not benched (the bench drives FarField directly).
