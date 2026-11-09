# The world plan by region (2026-11-09: designed, approved, built)

Step 7 of docs/worldgen-speed.md: make 10,000 x 10,000 worlds possible. Built as designed below; "Built" at the end has what came out.

## Measured (cs/Harness/GenProfile.cs, "gen profile big"; seed 42, Release)

| size | siteTowns | layRoads | makePlan (all) | plan memory | createState (the grid) |
|--:|--:|--:|--:|--:|--:|
| 2048 | 2.0 s (146 towns) | 0.7 s (280 roads) | 4.0 s | 28 MB | 0.04 s, 151 MB |
| 4096 | 32.1 s (581) | 4.7 s (1,099) | 44.9 s | 113 MB | 0.16 s, 604 MB |
| 10,000 | ~20 min (est.: x16 per 2x) | | | ~700 MB (est.) | 0.86 s, **3,600 MB** |

Three walls at 10,000:

1. **The plan is global and still quadratic.** siteTowns places towns one at a time, each scoring every site on the map, so it's towns x sites, both growing with area (the prefix sums of step 3 made each site cheap, not the loop linear: docs/worldgen-speed.md said linear, wrongly). Roads link every town, lots cover the whole map, and the road and lot masks are the map's size.
2. **The grid is flat lists, ~36 bytes a tile**: 3.6 GB at 10,000 before pathfinding's per-tile data. Every system indexes it directly.
3. **Saves write the whole grid** as JSON nodes (run-length encoded afterwards): 100 million tiles x 6 layers can't go through that.

This design is for (1), the world plan. (2) and (3) are a second job, sketched at the end.

## The plan by region

The map is cut into **regions of R x R tiles (R = 1024)**. Each region's plan is made when something first asks about a tile in it, from (seed, size, map, region) alone, and cached (at most ~16 regions kept, oldest dropped, as Heights does its chunks).

- **Nature** stays as it is: per-tile pure functions (water, forest, the river, the sea).
- **Towns, per region.** siteTowns runs on the region's window: its own count of towns (area / 170^2 as now), sites only inside the region, kept `radius + 27` from a border shared with another region (`radius + 3` from the map's edge, as now). Two neighbouring regions' towns then stay at least `r1 + r2 + 48` apart, the rule today, without either knowing the other's. Cost per region: today's siteTowns at 1024 (~0.5 s, ~37 towns); over a 10,000 map ~100 regions, each planned only when needed.
- **Streets**: as now, per town.
- **Roads, two kinds.** Inside a region: today's network (spanning tree and extra links) among its towns, routed on the region's coarse cost grid. Between two neighbouring regions: one road joining the nearest pair of towns across the shared border (main-street ends, as now), routed over both regions' coarse grids. A road between A and B depends on A's and B's towns only, so it's the same whichever is planned first. A region's road mask holds its own roads and the parts of its 4 neighbour links that cross it; planning a region sites its neighbours' towns (not their roads or lots). Roads to the map's edge: as now, for towns in regions on the map's edge.
- **Lots, per region**: town, farm, field and cabin lots as now, kept wholly inside their region. Lot ids stay numbers: the region's index x 65,536 + its own index.
- **Masks per region**: roadMask (1 MB) and lotMask (4 MB) at R = 1024; ~80 MB for 16 regions kept.
- **A world no bigger than R is one region, whose borders are all the map's edge**: exactly today's plan. So 64-1024 worlds (all the tests, the harness's balance players and most stress scenarios) don't change, and the "same output" hashes check it. Worlds over 1024 change (schema bump; old saves refused, as for 73).

### What reads the plan, and how it changes

`WorldPlan` (lots, lotMask, roadMask, towns, streets, roads, lotsByChunk) becomes a small API over the region cache: `isRoad(x, y)`, `lotAt(x, y)`, `lotsNear(chunk)`, `townsNear(x, y, reach)`, `townsOf(region)`, `roadsOf(region)`. Readers: Worldgen.generateChunk (isRoad, onLot, lotsByChunk), Heights (lots, roadMask, towns for townness), Settlements (nature), Mapnotes (town names: today every town on the map; becomes the towns of regions planned so far, names from a stable index), and in the game Roofs, FarField, MapShot. TownIndex and Heights' townness ask townsNear.

### Order and determinism

A region's plan is a pure function of its own window and its neighbours' towns (themselves pure functions of their windows). A test plans the regions of a 3072 map in several orders (row by row, backwards, a spiral) and checks every mask, lot and town is the same. Rule 9 (iteration order) holds: everything is lists in a fixed order.

### What it costs and what's left

- createWorld at 10,000 plans the start's region and its neighbours (~1-2 s), not the map. Generating a chunk asks its region (planned once).
- The far field asks regions out to 1 km (up to ~9 regions: fine).
- MapShot of a whole 10,000 map plans every region (~100 x 0.5-1 s): a tool, not the game.
- Not solved by this: the grid's memory and saves (below).

## The second job: the grid in chunks (not designed in detail)

The grid's per-tile layers kept per 32 x 32 chunk, allocated when the chunk is generated (ungenerated land costs nothing), behind the Grids accessors (most code reads `grid.tiles[i]` directly today: a sweep through every system). Saves keep only generated chunks, and for those only what differs from regenerating them from the seed (most of a chunk is unchanged). Pathfinding's and reachability's per-tile data per chunk too. That's what makes 10,000 fit in memory and on disk; it touches nearly every system, so it's its own session after this one.

## Built (2026-11-09; schema 74)

- **Code:** `Win` (a window: tiles, which sides are the map's edge, the region's index), `WorldRegions` (the cache and the questions: `Region`, `RegionAt`, `RoadAt`/`IsRoad`, `LotAt`/`OnLot`, `Lot(id)`, `LotsInChunk`, `TownsOf`, `TownsNear`, `AllTowns`, `PlanSoon`), `Worldplan.makeRegion` (makePlan over a window, with the neighbour links), and windowed `Towns.landGrid/siteTowns`, `Roads.route/costGrid/reuse/drawLine`, `Worldplan.layRoads/cabinLots`. `Worldplan.regions(seed, size, map)` replaces the plan cache; `planWorld` is kept for one-region worlds (the tests) and refuses bigger ones; `Worldplan.prepare` plans the regions round the start (the game runs it on a worker for a new world over 512). Readers moved: Worldgen (Layout.regions), Heights (Field.regions; townness by TownsNear), Mapnotes (towns of regions with a generated chunk, names by `Town.index`), Settlements, and in the game Roofs, FarField, MapShot. `Lots.layouts` is a ConditionalWeakTable (lots of dropped regions go with them).
- **The same for worlds of 1024 or less:** 120 plans (seeds 1-30 at 128, 256, 512, 1024), 12 whole generated worlds (their save files) and 90 height fields hashed before and after: identical. The balance players use 128 worlds, so they can't have moved.
- **Tests** (WorldRegionsTest): a 2560 map's 9 regions the same planned row by row or in a spiral; towns of the whole map at least r1 + r2 + 48 apart; every lot inside its region; at least 4 of the 6 east borders crossed by a road; lots found again by id and by tile; a one-region world equal to makePlan.
- **Times** (harness, Release, seed 42; "gen profile big"):

| size | createWorld before | now | regions planned |
|--:|--:|--:|--:|
| 2048 | 4.3 s | 3.3 s | 4 |
| 4096 | ~45 s | 4.1 s | 4 |
| 10,000 | ~30 min (est.) | 2.2 s | 1 |

  Memory after createWorld at 10,000: 4.3 GB, of which the grid is 3.6 GB (the second job). In the game's build, a whole 4096 world (MapShot): plan and start 4.9 s, every chunk 26 s.
- **The far field** asks for a region's plan on a worker (`PlanSoon`) and draws nature alone there until it's made, so travelling never stalls a frame on a plan (~1 s a region).
- **Seen in the overviews** (experiments/map-overview, 2000 and 4096): the regions join up, roads cross their borders (`_border.jpg`). Towns keep radius + 27 from a shared border, so a band ~100 tiles wide along each border has fewer towns (farms, cabins and roads still in it); on a 2000 map the start sits where four regions meet.
- **Left:** the grid in chunks and saves of only what changed (above); the bands along borders, if they bother (a looser margin with a cross-border check against the neighbour's towns would cost siting the neighbour's towns first); the suite's time (the region tests add ~12 s).

## Saves of what changed (2026-11-09; schema 75)

The player asked whether the world could be made again from the seed and the save keep only the chunk changes. Done for saves (the second job's steps 1-3; the grid in chunks in memory is still to do):

1. **Ids from the chunk.** Generation made ids from `state.nextId`, so they depended on the order chunks were made in. A chunk is now made on its own (`Worldgen.makeChunk`: a small state whose grid is the chunk, `Grid.ox/oy` its corner, which `Grids.tileIndex/inBounds/edgeIndex` take into account) and its furniture, dormant and cars are named `f{cx}.{cy}.{k}`, `d...`, `v...`. `adoptChunk` takes it into the world (noting every changed tile and edge for rooms and reachability). `FurnitureMod.ofKinds` sorted by the number in the id (list order while ids came from the counter); it sorts by a count the index keeps now (`IndexesEntry.seq`). The furniture index is a ConditionalWeakTable (scratch lists don't leak).
2. **Generation free of the state.** A chunk reads the seed, the plan and the world's settings only (checked: Lots, Difficulty, Climate read `state.settings` and `state.world`). A car's parking room is now judged inside its chunk (an edge just over the chunk's east or south border no longer counts), so some cars park differently.
3. **Saves.** `SavefileChunks`: the state without the grid's layers, furniture and the dormant; per chunk, the layers that differ from making it again (or from bare land), run-length encoded; furniture and the dormant as runs of pieces still as made, the rest in full. Loading makes the generated chunks again and lays the differences over them. Hand-made maps are saved whole as before. `loadFile` no longer copies the state it has just built (at 10,000 the copy went through one JSON string and ran out of memory); bug reports still do, in Replay.

Measured ("gen profile saves", seed 42, Release):

| world | whole save | chunked save | load |
|---|--:|--:|--:|
| 512, every chunk | 1.26 MB, 0.5 s | 0.04 MB, 1.1 s | 0.7 s |
| 2048, the start only | 0.14 MB, 3.8 s | 0.02 MB, 0.3 s | 1.0 s |
| 2048, every chunk | 21.4 MB, 7.2 s | 0.47 MB, 5.6 s | 5.0 s |
| 4096, the start only | 0.16 MB, 20.8 s | 0.04 MB, 0.2 s | 5.4 s |
| 10,000, the start only | (couldn't) | 0.21 MB, 1.4 s | 1.0 s |

Tests (ChunkSaveTest): a played and changed world (a tree cut, a crate emptied, a wall up, an edge wall down, a floor dirtied, one of the dormant woken, a workbench built) comes back exactly at 256 and 1536 (two regions a side), and saving it again gives the same file; a chunk made again later in a played state is the same; chunks made in reverse order give the same world; an untouched 256 world keeps at most 9 chunks and its furniture as runs.

Left: saving a fully explored world regenerates every generated chunk to compare (5.6 s at 2048): a hash of each chunk as made, kept in WorldInfo, would skip the unchanged ones. The grid still lives whole in memory (3.6 GB at 10,000): the grid in chunks is the next job. A save made before this change won't load (schema 75).
