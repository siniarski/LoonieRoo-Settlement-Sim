# Style lineup (2026-11-09)

`godot/Characters/StyleLineup.tscn`: the survivor, the dead, a wolf, a rabbit, trees and items at five detail tiers, in the game's daylight at first person's eye (1.65 m, 60 degrees). All from the models we already have; nothing new was modelled.

    Godot --path godot res://Characters/StyleLineup.tscn -- --shots=DIR   # the pictures here
    Godot --path godot res://Characters/StyleLineup.tscn -- --perf=DIR    # perf.md
    (no arguments: the scene; keys 1-5 switch tier)

| tier | people | animals | trees | maps |
|---|---|---|---|---|
| A Blocky | pz ladder bottom, ~680 | far LOD (wolf 500) | far LOD | 128 px colour, flat-shaded |
| B Zomboid | pz 3k | mid (wolf 1.6k) | mid | 512 px painted, no normal map |
| C Mid | plus 6.7k | near (wolf 5k, the top) | near | 2k colour + 2k normal |
| D Near | plus 14.5k | near | near | same |
| E Now | plus 34.7k (with hair) | near | near | same: the game today |

Items have one level (36-252 tris) in every tier.

Pictures: `row_*.png` (each subject across the tiers, ~4 m), `close_*.png` (arm's length), `scene_*.png` / `scenes.png` (a first-person moment per tier), `items.png`.

## What the numbers say (perf.md; RTX 5070, 720p)

- **Triangles hardly matter in a real scene.** With LOD by height on screen, C, D and E are within 0.02 ms GPU of each other: past ~3 m every tier drops down the same ladder. A vs E at 40 people: 0.27 vs 0.40 ms GPU.
- **Animated characters are the cost, on the CPU.** 40 -> 200 people adds ~5-6 ms a frame in every tier alike (~30 us each: the skeleton and AnimationPlayer), the same as the look lab's 35 ms per 1,000.
- **Texture memory is the real gap between tiers.** Two plus looks loaded uncompressed: 69 MB; pz: 6 MB; blocky: 0.2 MB. With 8 looks in the game that's ~275 MB for plus (VRAM compression would cut it about 4-6x).
- **Trees and items don't decide it**: the earlier benches (docs/trees.md, experiments/item-bench) put them at well under 1 ms.
