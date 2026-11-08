# Animation bench (2026-11-08)

How many animated characters a frame holds in Godot 4.6, by how they're animated and how they're built.
The scene is `godot/Characters/Bench.tscn` (Bench.cs says what each mode is); run it:

```bash
G="/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe"
"$G" --path godot res://Characters/Bench.tscn -- --out="$(pwd -W)/experiments/anim-bench/windowed.md"
"$G" --headless --path godot res://Characters/Bench.tscn -- --counts=500,2000 --out=...   # CPU only
```

The character is the test mannequin (1,792 triangles, 56 bones), in a grid, vsync off, no shadows.
Machine: Intel Core Ultra 7 265K, RTX 5070 (a fast desktop: a low-end target is several times slower).

- `windowed.md`: everything, clips with all 168 channels (56 bones x translation, rotation, scale).
- `headless.md`: the CPU side alone (no drawing). Headless frames don't go below ~6.9 ms, so only the big rows say much.
- `windowed-15-tracks.md`: the merged mesh again, clips trimmed to the 15 channels that move.
- `windowed-update-rates.md`: the animation update rate (every 2nd, 3rd, 4th frame at ~60+ fps).

## What it says

Per character per frame (2,000 merged characters, minus the same 2,000 not animated):

| | cost | 60 fps budget (16.7 ms) spent on animation alone |
|---|--:|---|
| AnimationTree (4-way synced blend + time scale) | ~18 µs | ~900 characters |
| AnimationPlayer (one clip) | ~12 µs | ~1,400 characters |
| AnimationPlayer every 4th frame, staggered | ~3.6 µs | ~4,600 characters |

1. **The cost is per animated skeleton, not per track.** Trimming every clip from 168 channels to 15 changed
   2,000 players from 32.8 to 31.1 ms. What costs is the mixer and the skeleton update (56 bones' poses and
   global transforms, the skin) per character. The levers are fewer animated skeletons per frame (throttle, LOD),
   fewer bones in far LODs, or no skeleton at all (vertex animation textures). The trimmed export stays (smaller
   files, no harm).
2. **Separate meshes per character cost far more than animation.** The 14-mesh region build draws 14 times as
   many objects: 2,000 of them not even animated took 77 ms (55 ms of it render CPU, 28,000 draw calls) against
   8 ms merged. Body regions and outfits as separate meshes are fine for a few dozen near characters
   (30 x 14 = 420 draw calls); everything past the near tier must be **merged into one mesh**, as planned.
3. **Throttling works:** a quarter of the players each frame cut 2,000 merged from 31 to 15 ms, and at a distance
   the 15 fps update isn't visible. That's the mid tier.
4. **Even unanimated, 2,000 skinned merged characters cost ~7.5 ms** (3 ms at 1,000). For hordes (the colony
   zoom, hundreds to thousands on screen) the far tier needs **VAT on MultiMesh**: no skeletons, one draw call per
   outfit variant. That's the next measurement (bake a clip to a texture, draw 5,000-20,000).

5. **Update rate is the cheapest lever.** Animation cost for 2,000 merged characters (frame minus unanimated):

   | update | animation ms | per character |
   |---|--:|--:|
   | every frame (60+ fps) | 23.6 | 11.8 µs |
   | every 2nd (30 fps) | 13.5 | 6.8 µs |
   | every 3rd (20 fps) | 9.5 | 4.8 µs |
   | every 4th (15 fps) | 7.5 | 3.8 µs |

   Not quite 1/N: about 1 µs per character stays whatever the rate (the skeleton and skin still update each frame).
   The clips' own key rate (30 fps sampled) doesn't matter: Godot interpolates, the cost is the evaluation.
   Near characters at full rate, mid at 20 or 15, staggered so the work is spread over frames.

Budget this suggests for the LOD tiers on a fast machine at 60 fps: near (tree, IK, separate meshes) ~30-40
characters ≈ 1 ms; mid (merged, throttled player) ~200-300 ≈ 1-1.5 ms; far: VAT. On a low-end target, halve the
near and mid counts or more.

## In the lab (crowd tab, with shadows, vsync off)

500 humanoids: all near 47 ms a frame (24,000 draw calls); auto (40 near, 460 mid at 20 fps) 7.8 ms; all mid at
15 fps 3.9 ms. 1,000 in auto (40 near, 960 mid at 20 fps): 12.5-13.6 ms (73-80 fps).
