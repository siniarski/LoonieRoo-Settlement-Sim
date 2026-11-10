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

## The second job: the grid in chunks (designed 2026-11-10; "Built" is at the end of this file)

The sketch of 2026-11-09: the grid's per-tile layers kept per 32 x 32 chunk, allocated when the chunk is generated (ungenerated land costs nothing), behind the Grids accessors (most code read `grid.tiles[i]` directly: a sweep through every system). Saves keep only generated chunks, and for those only what differs from regenerating them from the seed. Pathfinding's and reachability's per-tile data per chunk too.

### The design

**The layers.** The grid has six per-tile layers: `tiles` (one of five kinds), `ground` (an index into GROUNDS), `explored` (0 or 1), `dirt` (0 or 1), `damage` (a double) and `edges` (two a tile: its north edge, then its west). Everything else on `Grid` is already sparse: lists of tile or edge indexes (`dirtList`, `damageList`, `locked`, `reinforced`, `open`, `passing`, `edgeOpen`, `edgePassing`, `edgeLocked`, `edgeBoarded`) and the `edgeDamage` record. Those stay exactly as they are, on the grid, keyed by the global index, saved as they were.

**Storage.** Each layer is a `GridLayer<T>`: an array of chunk slots (`chunks[cy * cw + cx]`, cw = ceil(width / 32)), each null or an array of 32 x 32 values (2 x 32 x 32 for edges), and one `fill` value. A null slot reads as `fill`. Five layers hold bytes (a tile kind is a code into a small table of names: floor, wall, door, water, barricade, and any other string a hand-made state holds, registered as met), `damage` holds doubles. A chunk on the map's east or south edge is still a whole 32 x 32 array (the part past the edge is never read): no special cases. Chunk slots are aligned with Worldgen's chunks and reachability's (both 32), so a generated chunk is one slot in each layer.

**When a slot is allocated.** On the first write of a value that isn't the layer's fill. So each layer is sparse on its own: a forest chunk has tiles and ground and nothing else (2 KB); a town chunk adds edges (2 KB); explored arrives where someone has looked (1 KB), dirt and damage where there is some (rare). Per tile that is 2 to 5 bytes where there was 36, and nothing for land never generated. The slot arrays themselves cost 8 bytes a chunk a layer (4.7 MB at 10,000).

**A read of ungenerated land** returns the fill: floor, ground 0 (grass), no edges, clean, undamaged, and unexplored in a generated world (a hand-made map made by `createGrid` starts explored and filled with whatever tile it was asked for, so its fills are those). That is what the full-size lists held there, so nothing that reads beyond the generated chunks sees a difference, and "bare land" in the save is the same thing as before. Whether a slot is allocated is never observable: only values are.

**The global tile index stays the key.** `i = y * width + x` and `e = 2 * i + side` are unchanged: rooms, paths, the sparse lists, `state.cleared`, furniture's index, saves and bug reports all keep using them. A layer read by index splits it (`y = i / width`, one integer division) and then goes by (x, y); reads by (x, y) skip the division, so hot loops that have x and y use those forms. Edges need nothing special: an edge belongs to its tile, so it lives in its tile's chunk at `2 * local + side`; the edge between two chunks is the north or west edge of the tile on the far side, as it always was.

**The API.** The layers are not public: `Grids` is the only way in (the fields are internal to the sim, and inside it only Grid.cs and the save code's layer helpers touch them).
- by tile: `getTile` / `setTile` (as before), `tileAt(grid, i)`, `groundAt(grid, x, y | i)` / `setGround`, `isExplored` / `exploredAt` / `setExplored`, `isDirty` / `setDirty` (as before) / `dirtAt`, `damageAt` / `setDamage` (as before), `edgeKind(grid, e)` (as before) / `setEdge` (as before);
- edges by their tiles, with no index to split (the forms for hot loops): `edgeBetween(grid, ax, ay, bx, by, out kind)`, `edgeKindBetween`, `edgeAt(grid, x, y, side)`;
- raw writes that note nothing (generation, loading, tests): `putTile`, `putEdge`, `putDirt`;
- whole layers: `fillExplored`, `fillGround`, `anyUnexplored`, `anyEdges`, `tileCount`, `dirtyTiles`, `damagedTiles`, `memory` (slots allocated), and flat copies for tests, dumps and the text map (`Grids.allTiles(grid)`, `allGround`, `allExplored`, `allDirt`, `allDamage`, `allEdges`: O(map), never in a tick);
- inside the sim only (the save code, rooms, reachability): a chunk's rows (`GridLayer.ReadRow` / `WriteRow`), `structureTiles` (the tiles with something on them), `bareChunk`, `tileCodeAt`.
An index out of range reads as the old `Js.Get` did (null tile, 0), and a write there throws.

**JSON.** Rule 7 holds: `StateJson` writes each layer as the flat array it always was (a converter per layer; the grid shapes them into chunks once its size is known, `IJsonOnDeserialized`), so state dumps, clones, hand-made-map saves and bug reports are the same bytes. The chunked save never wrote the layers whole; it now walks only the slots that exist.

**Small maps and scenarios** are a grid like any other: a 20 x 15 map is one chunk slot a layer. Worldgen's `makeChunk` grid (its corner at `ox, oy`) is one slot too, which is what lets `adoptChunk` hand its arrays over.

### Decided against

- **A chunk object holding all six layers** (`chunks[c].tiles[...]`): one more indirection on every read, and every layer allocated together (5 KB more for each forest chunk). Per-layer slots are both faster and smaller.
- **Keeping `List<T>`-shaped layers with a chunked indexer** (so `grid.tiles[i]` still compiles): every read would pay the division, list helpers (`fill`, `includes`, `Count`, spans) would silently walk 100 million tiles, and the next person would write `grid.tiles[i]` in a loop. Accessors make the expensive forms visible.
- **Changing the tile index** to chunk-major order (chunk * 1024 + local): cheaper to split, but it is the key in saves, rooms, paths and every sparse record; all of it would change for a division that the (x, y) forms avoid anyway.
- **Bits for explored and dirt, nibbles for edges**: 1 KB a chunk less at most; not worth the masking in Fog and Sight until memory says so.
- **A dictionary of chunks** (keyed by chunk index) instead of a slot array: the slot array is 0.8 MB a layer at 10,000, a lookup is one array read, and iteration order stays the index order (rule 9) for free.
- **Strings in the tile layer**: 8 bytes a tile, and `getTile(...) == "floor"` on strings read back from JSON compares characters; codes give back the same five interned constants.
- **Values wider than a byte for ground and edges**: GROUNDS has 10 entries and edges 6 kinds. A value outside 0..255 throws at the write (loud) rather than being stored.

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

**Faster saves, chunk hashes (2026-11-10, schema 78; log/2026-11-10-05-save-speed.md).** Saves are written straight to text (`SaveWriter`: the state's members in the serializer's order, the grid's layers, furniture rows and runs value by value; the same files as before, byte for byte, but the new field). `WorldInfo.made` holds each generated chunk's hash as made (`SavefileHash.cs`: tiles, ground, edges, and the chunk's furniture and dormant in list order); a save makes again only the chunks whose hash now differs (a played 2048 world: 6 of 4,096). The hashes are made when a chunk is generated and again on load from the chunks loading makes anyway (so a schema-77 save loads, and gets them). Bug reports start from the packed state (`Savefile.bugStart`, a world's changes only) instead of a whole copy. Save times: 512 fully generated 0.54 s to 10 ms, 2048 fully generated 9.3 s to 79 ms, 10,000 (the start) 1.4 to 0.6 s (scanning the whole grid's bare chunks: the grid in chunks removes it). Loading is still making every generated chunk (2048 fully generated: ~7 s, most of it generation).

## The grid in chunks: built (2026-11-10; no schema change; log/2026-11-10-06-grid-chunks.md)

Built as designed above ("The second job"), in four stages, each its own commit, every one with the state dumps and the save files byte-identical to the branch's start.

- **Code.** `GridLayers.cs`: `GridLayer<T>` (chunk slots, a fill, rows in and out, `AnyNot`, `SameAs`, `CopyFrom`), `TileLayer` / `ByteLayer` / `EdgeLayer` / `DoubleLayer` with their JSON converters (flat arrays), `TileMask`. `Grid.cs`: the layers are internal fields under their JSON names (`[JsonInclude]`), shaped once the size is known (`IJsonOnDeserialized`); every accessor of the design; tile kinds as codes. Nothing outside Grid.cs, GridLayers.cs, the save code, RoomLabels.cs, Pathfinding.cs and WorldPreview.cs sees a layer. The save code reads a chunk a row at a time (`Differs`, `WriteLayer`), hashes it off its own arrays (`GridHash`) and lays generated chunks in with `CopyFrom`.
- **What else was sized by the map, and isn't now** (found by measuring a played 10,000 world, harness `big world`: after the grid itself was in chunks the heap was still 3.9 GB, the first tick 9 s and a tick 15-20 ms):
  - **Rooms** (`RoomLabels.cs`): labels in 32 x 32 chunks under 32 x 32 blocks, copied on write (an update copied a label for every tile: 400 MB at 10,000, at every wall or door change); `computeRooms` seeds the bounded flood from the tiles with something on them (`Grids.structureTiles`) and their neighbours: the rooms of a bare map, updated by "everything that isn't bare changed". `computeRoomsWhole` is the old whole-map flood, kept as the tests' definition. `MAX_TRACKED` 2,048 to 65,536. The cache is weak now (a JsMap kept every grid alive).
  - **Reachability**: a chunk's labels are its own array of shorts, made when it is flooded; a bare chunk (nothing in its tile or edge slots) is one component and is never flooded; two bare chunks side by side share one link.
  - **A\* and nearestTile**: their per-tile scratch in chunks allocated where a search goes (24 bytes a tile for A\*, stamped, dropped past 4,096 chunks: about 100 MB).
  - **Sight.visibleNow**: a `TileMask`, in a weak table. **The save's picture**: only chunks holding a seen tile. **Invariants**: dirt and damage from the chunks that hold any.
- **Still sized by the map** (small, or the game's): `WorldInfo.generated` (a number a chunk: 0.8 MB at 10,000) and the save's per-chunk passes (98,000 cheap steps); reachability's per-chunk tables and its union over every chunk at each walkability change (about 0.3 ms for six movers at 512, growing with the chunk count: per-chunk versions or a hierarchy are the fix); in the game, the map screen and minimap build an image of the whole map (`godot/Ui/WorldMap.cs`, `Minimap.cs`).

### Measured (seed 42, Release, this machine shared with other sessions; before is the branch's start, 2e8d9a2)

Memory and time for a new world (`gen profile big`, `big world`):

| | 2048 before | 2048 now | 10,000 before | 10,000 now |
|---|--:|--:|--:|--:|
| heap after createWorld | 211 MB | 26 MB | 4,389 MB | 28 MB |
| createWorld | 5.3 s | 5.2 s | 4.0 s | 3.0 s |
| the first tick | 595 ms | 77 ms | 11.9 s | 15 ms |
| heap after 200 ticks | 347 MB | 28 MB | 7,545 MB | 43 MB |
| a tick (the first 200) | 1.04 ms | 0.20 ms | 19.8 ms | 8.6 ms (0.39 ms once the next region is planned: below) |
| 6 chunks walked east | 1.84 s | 34 ms | 4.3 s | 1.5 s (a region's plan) |
| save | 30 ms | 8 ms | 623 ms | 16 ms |
| load | 115 ms | 94 ms | 968 ms | 129 ms |
| the loaded state's heap | 152 MB | 1 MB | 3,606 MB | 10 MB |
| 20 ticks on the loaded state | 357 ms | 9 ms | 11.8 s | 12 ms |
| peak working set (2048 then 10,000 in one process) | | | 13,480 MB | 259 MB |

4096: heap after createWorld 766 MB to 28 MB (the bound the harness holds it to is 64 MB: `SIZES=4096 BOUND_MB=64`), the first tick 1.9 s to 22 ms, a tick 1.76 to 0.16 ms.

Saves (`save speed`, a played state, best of 3; save and load):

| world | save before | save now | load before | load now |
|---|--:|--:|--:|--:|
| 2048, the start (35 chunks) | 37.5 ms | 18.7 ms | 92.5 ms | 54.0 ms |
| 2048, every chunk (4,096) | 65.0 ms | 62.3 ms | 6,349 ms | 6,116 ms (generation) |
| 10,000, the start (40 chunks) | 581 ms | 10.8 ms | 1,043 ms | 65.7 ms |
| 10,000, a bug report's start | 571 ms | 10.1 ms | | |

The whole-map save a hand-made map gets (the harness applies it to a 2048 world, which no game does) is a little slower than it was (2048, the start: save 15.7 to 24 ms, load 93 to 158 ms): it copies each layer flat before run-length encoding it. Hand-made maps are a few thousand tiles.

The tick (`profile`, 2,000 ticks after 300, builds alternating, each build's runs sorted; ms a tick):

| scene | before, the whole tick | now | before, replayed step by step | now | KB allocated a tick |
|---|---|---|---|---|---|
| 512 (3 colonists) | 0.082 0.086 0.100 0.111 | 0.082 0.085 0.085 0.087 | 0.090 0.092 0.110 0.122 | 0.083 0.084 0.084 0.090 | 38.8 to 37.7 |
| horde-512 | 1.278 1.297 1.391 1.526 | 1.383 1.404 1.428 1.447 | 0.785 0.787 0.787 0.813 | 0.745 0.756 0.769 0.773 | 705 to 695 |
| colony-512 | 0.362 0.364 0.368 0.375 | 0.354 0.361 0.371 0.374 | 0.342 0.344 0.348 0.358 | 0.339 0.341 0.344 0.371 | 333 to 319 |
| colony-128 (100 colonists) | 0.171 0.172 0.173 0.175 | 0.168 0.168 0.176 0.229 | 0.193 0.193 0.195 0.197 | 0.185 0.189 0.195 0.208 | 162.6 to 162.5 |
| mixed-128 | 0.156 0.157 0.157 0.159 | 0.153 0.160 0.161 0.184 | 0.166 0.168 0.171 0.178 | 0.163 0.171 0.175 0.190 | 170.2 to 170.2 |

The first pass (the whole tick, with the allocation sampler on) is the noisier of the two: horde-512 read 6% slower there in three of five rounds over the session and faster in two; the replay of the same ticks, a steadier pass, has it 4% faster, and the stress harness over its first 1,500 ticks (three alternating runs: 1.90 1.91 2.22 before, 1.85 1.88 1.95 now; colony-512 1.49 1.56 1.70 before, 1.57 1.63 1.63 now) agrees. No scene is slower by anything these measurements can hold on to.

Whole ticks could not be told apart to better than a few percent on this machine, so each reader was timed alone (`micro`, colony-512, best of 7 rounds, ns an operation, old to new): getTile 5.3 to 5.6, isWalkable 8.7 to 8.3, crossable 11.2 to 11.7, isPassable (zombie) 20-27 to 25, Rooms.labelAt 47.8 to 19.0, sightLine 572 to 366, sightOf 80,000 to 57,200, findPath 13,400 to 13,500, a zombie's path 15,700 to 16,200, canReach 206 to 219, nearestTile 9,900 to 9,900, a wall changed then rooms 1,188,000 to 43,000, then reachability for six movers 326,000 to 333,000 (runs of each spread about 8%).

**Identical states.** After every stage: the 48 `tick dumps` and the 35 balance dumps (colony `LOG=`, solo `LOG=` with `LOG_AT=1234`, seeds 1-4, 5 days) have the same SHA-256 as the branch's start (83 files); the saves, bug reports and state JSON `save compat` writes (20 files, five played states) are the same bytes, and the old build's files load to identical states and are written again the same. Tests: `GridChunksTest` (chunk borders and the map's edge, off the grid, what allocates, ungenerated land, two chunk orders, a hand-made map in JSON, a played 300 world through a save and through JSON), and `RoomsIncrementalTest` now holds both the update and the from-nothing pass to the whole-map flood after each of its changes. Check green at 781.

**Found on the way, not the grid's:** at 10,000 `createWorld` plans the start's region only; the first time play asks beyond it (within the first 200 ticks, and again walking six chunks east) a region is planned inside a tick: about 1.5 s. The far field already asks on a worker (`PlanSoon`); the sim's askers don't.

### Unloading chunks (stage 5): not built; what it would take

**What a generated chunk costs** (`chunk cost`, 1024, every chunk generated: 51 KB a chunk):

| part | a chunk | of it |
|---|--:|--:|
| the grid's layers (tiles in 361 of 1,024 chunks, ground in all, edges in 536) | 2.5 KB | 5% |
| the dormant (3,060) | 0.5 KB | 1% |
| furniture and its index (93,714 pieces, 81,437 of them trees; 516 bytes a piece) | 48 KB | 94% |
| on top, derived by play: reachability for six movers, rooms and labels | 14.5 KB | |

So dropping the grid's layers of untouched chunks would return a twentieth of what a generated chunk holds. Unloading only pays if furniture goes with the chunk, and that is where it stops being safe to do in this session:

1. **"Next needed" isn't defined for furniture.** 31 files read furniture or the dormant as lists of the whole world (42 `FurnitureMod.ofKind(s)` calls, 22 uses of `state.furniture`: the nearest tree or crate anywhere, counts for alerts and jobs, expeditions, herds). With a chunk's pieces gone each of those either reloads it (then far reads keep everything loaded) or is defined not to look that far: an active simulation region, which changes outcomes and was out of scope.
2. **List order is an outcome.** `state.furniture`'s order (and `IndexesEntry.seq`, which `ofKinds` sorts by) decides ties. A chunk made again must come back at its old place in that order, whatever was unloaded in between: furniture needs a per-chunk home and an order that doesn't depend on history before any of it can leave.
3. **A read can't generate.** Reading an unloaded chunk's tile has to make the chunk again (1.5 ms, or 1.5 s when its region isn't planned), from wherever the read is: a path search, the far field, the renderer's thread. `makeChunk`'s statics (Worldplan, Heights, Time) aren't thread-safe, and nothing bounds how many reads a tick would do it.
4. **What holds on to a chunk.** Rooms and reachability are fine (a chunk coming or going is a batch of `noteChange`s, as `adoptChunk` is now; their chunked data can be dropped with it). Paths in flight, jobs, blueprints, designations, items, fires, cars, the dead and the living on its tiles, map notes and settlements all pin a chunk: "far from every survivor" has to become "nothing refers to it", checked per chunk.
5. **Knowing it is unchanged.** The hash (`WorldInfo.made`) covers tiles, ground, edges, furniture and the dormant, so it is the test, but it costs a pass over the chunk's pieces (the save does it for every generated chunk: about 60 ms at 2048 fully generated, about 1.5 s at a fully explored 10,000). A per-chunk "touched since made" mark would make both the save and unloading cost what changed; furniture's fields are written directly all over the sim (`crate.loot = 0`), so the mark needs those writes to go through one place first.
6. **The save.** Furniture and the dormant are written as runs over the lists; an unloaded chunk has to be written as "as made" without its pieces in the list (the count as made kept beside the hash: a schema change).

**The order to do it in:** (a) furniture per chunk with a history-free order, and its writes through accessors (this alone allows the "touched" mark, and makes saves of a fully explored world cost what changed); (b) trees out of `Furniture`: four fifths of the pieces are trees at 516 bytes each, and a byte or two a tile as a chunk layer would cut a generated chunk from 51 KB to about 12 without unloading anything; (c) the active region: which systems may look how far, the rest abstract as herds and the dormant already are; (d) read-through generation off a thread-safe `makeChunk`; (e) unloading by the mark, and the save's "as made" form. Measure after (b): unloading may not be needed for memory at all.
