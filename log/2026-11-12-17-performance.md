- **2026-11-12, the whole game measured, and what cost most made cheaper.** Nothing had measured the whole game since 2026-11-11 and -12 put in textures, held items, road ribbons, smooth banks and edges, the far view's agreement, wading, furniture collision, pile-ups, solid cars, alarms and more self-test. Measured first (the sim's bench, stress and profile; the game's frame by place and its chunk builds; start-up), then fixed from the top: two in the sim's tick, five in the frame and the builds. Nothing the sim decides changed: after each sim change the 48 tick dumps, solo balance seeds 1-6 and the world plans' and heights' hashes were byte-identical to the base commit (c41bd96).
  - **How it was measured.** This machine (fast GPU; four other sessions building and running Godot beside it, so whole ticks and frames swing 10-30% between runs: every before/after below is alternating runs, three each way unless said). The game runs its Debug build (what Godot runs); an optimized build (`OPT=1`) changed nothing measurable, so the costs are not the JIT's. New tools, all kept: `experiments/perf/bench.sh` (the light bench's 300 frames, vsync off, in a street, a furnished house, the country, by the river's bridge and in a crowd of the dead (`--arena=crowd`), near chunks only and the far view at 500 m; with it the chunk builds over the run, a warm chunk bench by part, the frame by part and the far field's rebuild bench, all printed by `--lightbench`); in the harness the **crowd probe** (ZombieCrowd's parts), the **nature probe** (the world plan's per-tile functions, and a check that the rewritten hash and noise agree with the originals), and the profile's **worst ms** column, `Fog: ensureGeneratedAround` timed on its own and `SCENES=spread-1024`.
  - **The first numbers** (before anything changed; the logs of 2026-11-11-12 and -07 for comparison where they had any).

    | sim | then | 11-11 (dead-climb) |
    |---|---|---|
    | bench 128, 3/20/50/100 colonists | 0.106/0.244/0.372/0.603 ms | 0.088/0.238/0.413/0.645 |
    | bench 128, 49/152/137 zombies | 0.316/0.692/0.689 ms | 0.296/0.449/1.144 |
    | stress colony-512, mean (p99) | 1.55 (4.4) | 1.63 (4.4) |
    | stress horde-512 | 0.73 (2.1) | 0.73 (2.0) |
    | stress spread-1024 | 0.86 (10.2), max 15.3 | 2.00 (63.9) |
    | stress city-1024 | 1.78 (6.2) | 1.62 (3.7) |
    | build gen-1024 / new-2048 | 2.8 s / 4.7 s | |
    | profile horde-512, the dead's update | 0.55 ms of ~2-4: pushApart 0.30, followPath 0.17, reach and attack 0.16 | |

    | game (frame ms / GPU / render CPU; near only, then 500 m) | street | house | country | river | crowd |
    |---|---|---|---|---|---|
    | frame | 2.5-3.0 / 2.7-3.5 | 2.5-3.1 / 3.4-4.3 | 2.7-3.9 / 2.9-3.4 | 3.0 / 4.2-4.4 | 3.9-4.4 / 3.7-4.0 |
    | GPU | 0.34 / 0.37 | 0.31 / 0.33 | 0.48 | 0.26 / 0.28 | 0.26-0.49 |
    | a chunk built in play (cold, JIT and heights in), mean / worst | 3.2-6.7 / 33-48 | 3.0-6.7 / 19-82 | 3.7-5.3 / 33-122 | 2.8-3.4 / 25 | 3.0 / 13-14 |

    The 11-11 texture bench's frames were 2.6-3.4 ms in the same places; GPU here 0.26-0.49 ms, as then. A chunk built warm: 1.3-1.7 ms, 90% of it the ground (banks 0.35-0.45, edges 0.35-0.70, the plain quads the rest), furniture nothing, the commits 0.1. The frame by part (street / crowd): the HUD **0.75-0.85 ms**, more than the whole 3D update (0.45 / 0.85, where People is 0.65), Land 0.16, sounds 0.02-0.07, roofs 0.005. **On a 2048 world the frame was 16.7 ms**: the far view building one region a frame (13.9 ms each, 391 regions at 500 m), for the first ~7 s. Start-up to the first picture (Godot's whole run, two runs each): 128 world 3.4 s, 256 3.8 s, 2048 8.45 s.
  - **Fixed, in the order of what they cost a weak machine** (each its own commit, each on a green `./check.sh --all`).

    | what | before | after | measured by |
    |---|---|---|---|
    | The world plan's noise (NatureMod: four hashes a sample, not six; the hash in ints, the same bits) | hash 24 ns, noise 144, isWater 673 a tile, woodedness 596 | 4.8, 17.5, 133, 113-125 | nature probe (2.88 M calls checked: none differ) |
    | ... a chunk generated | 1.77 ms (1024), 1.54 (2048) | 1.16, 0.56 | gen profile, two runs each |
    | ... createWorld | 1.5 s (1024), 4.7 s (2048) | 0.9, 1.8-1.9 | gen profile |
    | ... ticks where survivors walk into new ground (spread-1024) | p99 14.3-14.8, worst 21.7-22.1, mean 2.26-2.27 ms | 8.9-9.9, 13.8-15.4, 1.87-2.11 | stress, alternating, loaded machine |
    | ... a 2048 world's first picture | 8.45 s | 4.65 s | Godot's run, two each |
    | ... the far view filling a 2048 world | 13.9 ms a frame (frame 16.7-16.9) | 9.3-9.7 (12.3-12.9) | light bench |
    | A far region near you keeps its plan tiles between builds (FarField: rebuilt whenever a chunk in it is generated) | 8.66-8.75 ms a rebuild | 4.64-4.91 | far rebuild bench, 2048 world (`FAR_KEEP=0` to compare) |
    | Kit.SetActive restyles a button only when it changes (the HUD set 7 buttons' style boxes anew every frame) | HUD 0.74-0.84 ms; frame 2.52-2.56 (street), 3.36-3.41 (crowd) | 0.35-0.41; 1.98-2.32, 2.76-3.02 | light bench |
    | The character card is not worked out while stowed in first person | HUD 0.35-0.44 ms | 0.27-0.29 | light bench, street |
    | ZombieBuckets in one open-addressed table of ints (was a Dictionary of doubles behind a bitmask) | build 0.12 ms, nine-tile lookups 0.085, pushApart 0.28, 95 KB | 0.063, 0.067, 0.22, 87 KB | crowd probe, horde-512, three runs |
    | ... whole ticks | colony-512 2.45-2.52 ms, p99 7.1-8.6 | 2.39-2.42, 6.4-6.5 (horde-512 inside its noise) | stress, alternating |
    | Chunks and roofs through C# lists (MeshBuf), not SurfaceTool's engine call per vertex attribute | 1.87, 1.87, 1.77 ms a chunk (river) | 1.67, 1.70, 1.52 | warm chunk bench (the street swings 0.7-1.6 either way) |

  - **Tried and dropped (no gain):** the chunk's tiles read once for the banks and edges (the grid reads were not the cost); a window of corner heights and of `Cor` per chunk build (no gain once it was in the right place: the first try landed in `Signature` and drew the bed over the water under a bridge, which the river picture caught); the alerts worked out once a tick (`UpdateAlerts`' lap was the button restyles before it); the minimap's pixels in one `SetData` (no difference); People posing only the dead within range (the crowd's dead are all in range; it pays only with many active dead far off, which no scene here has).
  - **Measured, not fixed, by cost to a weak machine:**
    1. **A far region's first build is still ~9-10 ms on a 2048 world** (one a frame for the first seconds, and in every region you come near): the plan's tiles are 68 x 68 a region at full resolution for a ground drawn every 4 tiles. Next: the woodedness and isLake fBm (~100 ns a tile each still), or a worker thread (no help on one core).
    2. **Chunk generation is still the tick's spikes** (p99 ~9 ms, worst ~14-15 in spread-1024): a survivor crossing into new ground generates a row of five 32-tile chunks in one tick (a diagonal, nine). Spreading them over ticks would change when the dead in them appear: a sim decision, for the player.
    3. **A near chunk's build: 1.4-1.7 ms warm, 2.5-5 cold, 4 a frame** (6-20 ms frames walking into new ground here, several times that on a weak machine). 90% is the ground's smooth banks and edges, and neither grid reads nor corner lookups are it: this needs a real CPU profile (dotnet-trace, not installed: ask) or a time budget per frame instead of four builds.
    4. **People.Update 0.65 ms with a crowd of 40 models**, and it poses every active zombie in the world each frame before the range test (the change is a few lines; worth it once many active dead are far off).
    5. **The sim's reconcileJobs: 0.33 ms in horde-512**, a guard job per zombie (1,944 jobs; invalidReason 0.16 ms over them): a design question (one job per threat near the colony?).
    6. **pushApart 0.22 ms and the buckets made twice a tick** (87 KB each): reusing the arrays would save the allocations.
    7. **The HUD's remaining 0.27-0.29 ms**: the minimap and overview 0.12, crafting 0.04, conditions 0.03, the clock's colour override set every frame 0.03.
    8. **Roofs sums the generated-chunks list every frame**: 0.014 ms at 2048, ~0.3 ms by the same sum at 10,000 (390k entries); every generated chunk already bumps `walkabilityVersion`, which Roofs checks, so the sum can go.
    9. **Sounds build dictionaries of every active zombie twice a frame** (0.04-0.07 ms in a crowd): only those within the scream range plus a margin are needed.
  - **Left.** The death screen's last three self-test checks failed once under the machine's load and passed on the rerun (timing-sensitive?). Pictures: the street byte-identical through the chunk and roof changes, the house the same to the eye; the river under its bridge looked at; a `--shot` with a real window can be turned by the real mouse (two runs of one build differed), so pictures were compared only where repeatable. Check green (1,019 tests, self-test 166).
