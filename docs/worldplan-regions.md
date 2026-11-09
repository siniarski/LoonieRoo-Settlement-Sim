# The world plan by region: a design (2026-11-09, for approval)

Step 7 of docs/worldgen-speed.md: make 10,000 x 10,000 worlds possible. Nothing here is built yet.

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
