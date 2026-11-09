# World generation speed (2026-11-09)

The player: "Why did world generation for a 2048 tile map take 19 minutes? Generate a world 10 times smaller and profile what takes the most time. Then create a strategy to improve generation times."

## How it was measured

`cs/Harness/GenProfile.cs` (not part of the check; Release build; seed 42, mainland):

    SIZES=648 dotnet test cs/Harness -c Release --filter "DisplayName=gen profile"          # createWorld, then every chunk by phase
    SIZES=1024,2048 dotnet test cs/Harness -c Release --filter "DisplayName=gen profile create"   # the world plan, step by step
    SIZES=2048 dotnet test cs/Harness -c Release --filter "DisplayName=gen profile roads"     # every road, timed
    SIZES=2048 DIAG=1005,820 dotnet test ... "gen profile roads"                               # one road's search, counted

Reports in cs/Harness/out/gen-profile*.txt.

## What takes the time

A world is made in two parts: **the world plan** (`Worldplan.planWorld`: nature, towns, streets, roads, lots; global, once, at `createWorld`) and **chunks** (`Worldgen.generateChunk` + `settle`, 32 x 32 tiles, generated as survivors come near).

| size | createWorld (the plan) | every chunk | of which: siteTowns | layRoads |
|--:|--:|--:|--:|--:|
| 648 (a tenth of 2048's area) | 1.0 s | 1.0 s (416 chunks) | | |
| 1024 | 2.3 s | 2.3 s (999) | 0.8 s | 0.3 s |
| 1448 | 6.0 s | 4.6 s (2,091) | 3.3 s | 0.4 s |
| 2048 | **334 s** | 9.0 s (4,071) | 14.4 s | **316 s** |

- **Chunks are fine**: 2.2 ms each at every size, flat as the world fills (nothing scans what's already there). 87% of it is the terrain loop (NatureMod's noise per tile). Chunks are lazy, so a game only pays for what's near its survivors.
- **The 19 minutes was one road.** At 2048, seed 42, one of the 280 roads (from (1005, 820) north to the map's edge) took 316 s; every other road took milliseconds. Its A* expanded one cell 1,048,576 times: 181 million expansions over a 262,144-cell grid, a heap of 56 million. **Cause: `Roads.route` keeps g in a `Float32Array` (as the TypeScript did) but compares in doubles.** A g stored rounds up past the double it came from, so `ng < g[nc]` stays true for the same ng, and neighbouring cells keep re-pushing each other with no progress. With g in doubles, the same road takes 17,472 expansions. It's seed-dependent (seed 7's 2048 world: 20 s), and grows likelier with size (longer roads, larger g, coarser float32 steps).
- **Then the draw bench ran 3.4x slower than the harness** (1,152 s vs 334 s): Godot builds the game, and the sim with it, in Debug (no JIT optimisation, and the JS layer's helpers aren't inlined).
- **`siteTowns` is quadratic in area**: for each town (one per 170 x 170 tiles) it scores every candidate site (every 8 tiles) by summing a disc of the land grid, and checks every town placed so far. 0.8 s -> 3.3 s -> 14.4 s as the area doubles: ~4.3x a doubling. At 10,000 x 10,000 (the scale target): ~2.3 hours.
- Smaller things that grow with towns x area: `Roads.costGrid` (every cell checks every town: 0.55 s at 2048), `farmLots` (every road point checks every town), `Heights.townness` (every height corner loops over every town: ~25 ms a height chunk, and the far field's cost too), `Roads.network` (O(n^3) spanning tree: 0.13 s at 146 nodes).
- **Memory, for later**: the plan's masks are size^2 (roadMask bytes, lotMask Int32: 16 MB at 2048, 400 MB at 10,000), and the grid's tiles are a string per tile.

## The strategy, in order

Each step measured by GenProfile before and after, with `./check.sh` green (the sim tests) and, where a step can change worlds, the harness's balance runs.

1. **Fix the road search (the 19 minutes).** g in doubles (a `double[]`), or compare in float32 (`(float)ng < g[nc]`). Either changes which of two equal-cost paths a road takes in some worlds, so roads, the lots along them and the heights near them can move for existing seeds. That's a world-format change: old saves rebuild their plan from the seed, and their saved grid would no longer match the new plan's roofs and heights. **Decision needed**: accept it (bump the schema, old saves of generated worlds are refused or regenerate), or keep the old search for old saves (a plan version in WorldInfo, set at creation). Whichever, a cap on expansions per road (say 8 x the cells) as a backstop.
2. **Build the sim in Release in the game.** `Optimize` on for cs/Sim in every configuration (or the game referencing Release). .NET doubles have no fast-math, so results are identical. About 3x on generation, and the ticks gain too. No change to worlds.
3. **`siteTowns` in linear time, same output.** Towns use a handful of radii; per radius, compute every site's counts (inside, dry, wooded, around, wet) once with row prefix sums over the land grid (a disc is a stack of row spans: O(diameter) per site, not O(area)), and keep placed towns in a coarse cell grid for the exclusion test. The counts are integers and the scan order is unchanged, so the towns are exactly the same (a test: the old and new give equal towns for a set of seeds and sizes).
4. **A spatial index of towns for every "each X checks every town" loop** (costGrid, farmLots, Heights.townness, the far field's coarse heights): a cell grid of town discs. Same results, and townness gets ~T times cheaper.
5. **Plain C# in the hot loops** (CLAUDE.md welcomes it where tests show nothing changes): the road heap (`PriorityQueue`, not lists of lists), `JsMath.pow(x, 2)` -> `x * x` where it's exact, typed arrays as plain arrays. Chunks' terrain loop: NatureMod's noise per tile is called several times for the same tile (isSea, isWater, isForest each), so compute each field once per chunk.
6. **Make the plan off the main thread, behind a loading screen.** The plan is pure (seed, size, map); the menu can start it while the player picks a character, and show progress. Same worlds.
7. **For the 10,000 x 10,000 target: a chunked plan.** The plan is global today (masks the size of the map; every town, road and lot planned at once). At that scale it should be hierarchical: regions planned on demand (towns per region, roads as a coarse graph between regions, lots per chunk from their region's plan), masks per chunk, cached and dropped like Heights' chunks. This is a redesign of Worldplan, and every world changes: its own session, after 1-6.

Steps 2-6 change no world (they're checked by "same output" tests). Step 1 changes some worlds and needs the save decision. Step 7 is a new world format.
