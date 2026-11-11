// Ported from src/sim/worldgen.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// World generation: the natural world first (NatureMod.cs: water, forest), then what was built on
// it (Worldplan.cs: towns sited on good land with streets they grew, country roads routed between
// them, lots cut along the streets and roads, a building planned in each lot), drawn chunk by chunk.
//
//  - Everything is a pure function of the seed and world position (stateless hashes, never the
//    sim RNG), and the plan is derived data, cached, never saved: so a chunk comes out the same
//    whatever order chunks are generated in.
//  - Generation runs chunk by chunk (CHUNK x CHUNK tiles): each chunk draws its tiles and its part
//    of every lot touching it (a lot may span chunks).
//  - Chunks are generated lazily: the start area when the world is made, then any chunk within
//    GENERATE_RADIUS chunks of a survivor, well ahead of what they can see (ensureGeneratedAround,
//    called from Fog.updateFog). What a chunk makes is named by the chunk (furniture "f{cx}.{cy}.{k}", the
//    dormant "d...", cars "v...": makeChunk), so ids don't depend on the order chunks are generated in.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Worldgen
{

    public const int CHUNK = 32;

    /// <summary>The world generation version (saved in WorldInfo.gen; absent is 1). Bump it whenever what a chunk is
    /// made of changes: a save keeps only what differs from making its chunks again, so a save made by another version
    /// is refused (Savefile.loadFile). 2: the buildings of 2026-11-10 (docs/buildings.md). 3: two-lane roads, and streets
    /// with a parking strip and sidewalks (2026-11-11).</summary>
    public const int GEN_VERSION = 3;

    /// <summary>Chunks this far (in chunks, each way) from any survivor are always generated.</summary>
    public const int GENERATE_RADIUS = 2;

    /// <summary>World sizes on offer (tiles a side, multiples of CHUNK). Development uses the small ones.</summary>
    public static readonly List<double> WORLD_SIZES = [64, 128, 256, 512, 1024, 2048];

    public const int WORLD_SIZE = 128;

    /// <summary>Tiles revealed around the start before anyone moves.</summary>
    public const int START_REVEAL_RADIUS = 14;

    public static Layout layout(double seed, double size, string map = "mainland")
    {
        var regions = Worldplan.regions(seed, size, map);
        return Js.With(Js.CopyAs<Layout>(regions.nature), __s0 => { __s0.regions = regions; });
    }

    /// <summary>A country road or town street on this tile.</summary>
    public static bool isRoad(Layout l, int x, int y)
    {
        return ((((x >= 0) && (y >= 0)) && (x < l.size)) && (y < l.size)) && l.regions.IsRoad(x, y);
    }
    public static bool isRoad(Layout l, double x, double y) => isRoad(l, Js.I(x), Js.I(y));

    /// <summary>On a lot (its yard and building are drawn by Lots.cs).</summary>
    public static bool onLot(Layout l, int x, int y)
    {
        return l.regions.OnLot(x, y);
    }
    public static bool onLot(Layout l, double x, double y) => onLot(l, Js.I(x), Js.I(y));

    /// <summary>A quarantine zone: a fence two tiles in from the edge, a gate where a road crosses it.</summary>
    public static string? fenceAt(Layout l, int x, int y)
    {
        if (!NatureMod.onFenceLine(l, x, y))
        {
            return null;
        }
        return isRoad(l, x, y) ? "door" : "wall";
    }
    public static string? fenceAt(Layout l, double x, double y) => fenceAt(l, Js.I(x), Js.I(y));

    public static readonly int FOREST = Grids.GROUNDS.indexOf("forest");

    public static void ground(SimState state, int x, int y, string g)
    {
        Grids.setGround(state.grid, Grids.tileIndex(state.grid, x, y), Grids.GROUNDS.indexOf(g));
    }
    public static void ground(SimState state, double x, double y, string g) => ground(state, Js.I(x), Js.I(y), g);

    /// <summary>Generates a chunk into the world: makes it on its own (makeChunk) and takes it in (adoptChunk).</summary>
    public static void generateChunk(SimState state, Layout l, int cx, int cy)
    {
        var c = makeChunk(state, l, cx, cy);
        SavefileChunks.noteMade(state, c, cx, cy); // its hash as made, for saves (WorldInfo.made)
        adoptChunk(state, c);
    }

    /// <summary>A chunk as generation makes it, on its own (2026-11-09, schema 75): into a small state of its own whose
    /// grid is the chunk (its corner at the chunk's: Grid.ox, oy), from the seed, the plan and the world's settings
    /// alone, so it comes out the same whenever and wherever it's made: the save keeps only what changed since
    /// (Savefile), and loading makes it again. What it makes is named by the chunk: furniture "f{cx}.{cy}.{k}", the
    /// dormant "d{cx}.{cy}.{k}", cars "v{cx}.{cy}.{k}", k their order made.</summary>
    public static SimState makeChunk(SimState state, Layout l, int cx, int cy)
    {
        int x0 = cx * CHUNK, y0 = cy * CHUNK;
        int w = (int)Math.Min(CHUNK, l.size - x0), h = (int)Math.Min(CHUNK, l.size - y0);
        var c = States.createState(new NewStateOptions { seed = l.seed, width = w, height = h });
        c.grid.ox = x0;
        c.grid.oy = y0;
        Grids.fillExplored(c.grid, 0);
        c.settings = state.settings;
        c.world = state.world;
        drawChunk(c, l, cx, cy);
        for (int k = 0; k < c.furniture.Count; k++) c.furniture[k].id = $"f{cx}.{cy}.{k}";
        for (int k = 0; k < c.dormant.Count; k++) c.dormant[k].id = $"d{cx}.{cy}.{k}";
        var cars = c.vehicles ?? [];
        for (int k = 0; k < cars.Count; k++) cars[k].id = $"v{cx}.{cy}.{k}";
        return c;
    }

    /// <summary>Takes a chunk made by makeChunk into the world: its tiles, ground and edges (noting each change, for
    /// rooms and reachability), its open doors and boarded windows, and what it made (furniture, the dormant, cars: each
    /// car's tiles marked as under a parked car).</summary>
    public static void adoptChunk(SimState state, SimState c)
    {
        var g = state.grid;
        var s = c.grid;
        int Map(int i) => (s.oy + i / s.width) * g.width + s.ox + i % s.width;
        int MapEdge(int e) => Map(e / 2) * 2 + e % 2;
        for (int i = 0, n = Grids.tileCount(s); i < n; i++)
        {
            int at = Map(i);
            var tile = Grids.tileAt(s, i)!;
            if (Grids.tileAt(g, at) != tile)
            {
                Grids.putTile(g, at, tile);
                Grids.noteChange(g, at);
            }
            Grids.setGround(g, at, Grids.groundAt(s, i));
            for (int side = 0; side < 2; side++)
            {
                int e = 2 * i + side, ge = 2 * at + side;
                int kind = Grids.edgeKind(s, e);
                if (Grids.edgeKind(g, ge) == kind) continue;
                Grids.putEdge(g, ge, kind);
                Grids.noteEdgeChange(g, ge);
            }
        }
        foreach (var i in s.open) g.open.push(Map(i));
        foreach (var e in s.edgeOpen) g.edgeOpen.push(MapEdge(e));
        foreach (var e in s.edgeBoarded) g.edgeBoarded.push(MapEdge(e));
        foreach (var f in c.furniture) FurnitureMod.adopt(state, f);
        foreach (var d in c.dormant) Dormant.add(state, d);
        if (c.vehicles is { Count: > 0 } cars)
        {
            state.vehicles ??= [];
            foreach (var v in cars)
            {
                state.vehicles.push(v);
                Vehicles.mark(state.grid, v); // in the way from the start (the chunk's own grid had it marked; this is the world's)
            }
        }
    }

    /// <summary>Terrain for every tile in the chunk, then its part of every lot touching it, then trees, then the dead
    /// lying out (into the chunk's own state: makeChunk). The phases are public for the harness's gen profile.</summary>
    static void drawChunk(SimState state, Layout l, int cx, int cy)
    {
        var wooded = drawTerrain(state, l, cx, cy);
        drawLots(state, l, cx, cy);
        drawTrees(state, l, cx, cy, wooded);
        strays(state, l, (double)cx * CHUNK, (double)cy * CHUNK);
    }

    /// <summary>The chunk's tiles and ground; returns each open tile's woodedness (NaN where it isn't open ground), for
    /// its tree.</summary>
    public static double[] drawTerrain(SimState state, Layout l, int cx, int cy)
    {
        var x0 = ((double)(cx) * (CHUNK));
        var y0 = ((double)(cy) * (CHUNK));
        var wooded = new double[CHUNK * CHUNK];
        Array.Fill(wooded, double.NaN);
        for (var y = y0; y < (y0 + CHUNK); y++)
        {
            for (var x = x0; x < (x0 + CHUNK); x++)
            {
                if (!Grids.inBounds(state.grid, x, y))
                {
                    continue;
                }
                var fence = fenceAt(l, x, y);
                bool road = isRoad(l, x, y);
                // Water (the sea is water too): a road over it is a bridge.
                bool water = !Js.T(fence) && NatureMod.isWater(l, x, y);
                if (Js.T(fence))
                {
                    Grids.setTile(state.grid, x, y, fence);
                }
                else if (road)
                {
                    ground(state, x, y, water ? "bridge" : "road");
                }
                else if (water)
                {
                    Grids.setTile(state.grid, x, y, "water");
                }
                else if (l.regions.IsSidewalk(Js.I(x), Js.I(y)))
                {
                    ground(state, x, y, "paving");
                }
                else if (onLot(l, x, y))
                {
                    continue;
                }
                else
                {
                    int tx = Js.I(x), ty = Js.I(y);
                    double w = NatureMod.woodedness(l, tx, ty);
                    wooded[(ty - Js.I(y0)) * CHUNK + (tx - Js.I(x0))] = w;
                    if (w > NatureMod.FOREST_AT) ground(state, x, y, "forest");
                    else if (NatureMod.isDirt(l, tx, ty)) ground(state, x, y, "dirt");
                }
            }
        }
        return wooded;
    }

    /// <summary>The chunk's part of every lot touching it.</summary>
    public static void drawLots(SimState state, Layout l, int cx, int cy)
    {
        double x0 = (double)cx * CHUNK, y0 = (double)cy * CHUNK;
        var ctx = lotContext(l);
        foreach (var id in l.regions.LotsInChunk(cx, cy))
        {
            Lots.drawLot(state, ctx, l.regions.Lot(id), x0, y0, JsMath.min(l.size, x0 + CHUNK) - 1, JsMath.min(l.size, y0 + CHUNK) - 1);
        }
    }

    /// <summary>Trees on the chunk's open ground (off roads, lots and the start's clearing), each a roll under
    /// NatureMod.treeChance (from drawTerrain's woodedness).</summary>
    public static void drawTrees(SimState state, Layout l, int cx, int cy, double[] wooded)
    {
        double x0 = (double)cx * CHUNK, y0 = (double)cy * CHUNK;
        for (var y = y0; y < (y0 + CHUNK); y++)
        {
            for (var x = x0; x < (x0 + CHUNK); x++)
            {
                if (((!Grids.inBounds(state.grid, x, y) || (l.regions.RoadAt(Js.I(x), Js.I(y)) != 0)) || onLot(l, x, y)) || NatureMod.nearStart(l, x, y, NatureMod.START_CLEAR_RADIUS))
                {
                    continue;
                }
                var i = Grids.tileIndex(state.grid, x, y);
                double w = wooded[(Js.I(y) - Js.I(y0)) * CHUNK + (Js.I(x) - Js.I(x0))];
                if ((Grids.tileAt(state.grid, i) == "floor") && !double.IsNaN(w) && (NatureMod.roll(l.seed, x, y, 12) < NatureMod.treeChanceAt(w)))
                {
                    FurnitureMod.addFurniture(state, "tree", x, y);
                }
            }
        }
    }
    public static void generateChunk(SimState state, Layout l, double cx, double cy) => generateChunk(state, l, Js.I(cx), Js.I(cy));

    /// <summary>No dead asleep nearer the start than this (tiles, straight line), in ruins or out in the open (was START_CLEAR_RADIUS + 24 for ruins, and none in the open).</summary>
    public const int STRAY_CALM = NatureMod.START_CLEAR_RADIUS + 14;

    /// <summary>Chance a road tile has one of the dead lying in it (dormant till someone comes near), at 100% zombies.
    /// (Kept when roads went from two tiles wide to six, 2026-11-11: the wider streets leave room for about a fifth
    /// fewer buildings and their sleepers, and the roads' dead make up most of it: harness census and road widths.)</summary>
    public const double ROAD_STRAY_CHANCE = 0.003;

    /// <summary>The same on open ground off the roads and lots (fields, forest, waste ground).</summary>
    public const double OPEN_STRAY_CHANCE = 0.0005;

    /// <summary>The dead out in the open, drawn with the chunk (stateless rolls: chunk order doesn't matter): dormant positions, woken by Herds like the ruins' sleepers.</summary>
    public static void strays(SimState state, Layout l, double x0, double y0)
    {
        var scale = (double)(state.settings.zombies) / (100);
        for (var y = y0; y < (y0 + CHUNK); y++)
        {
            for (var x = x0; x < (x0 + CHUNK); x++)
            {
                if ((!Grids.inBounds(state.grid, x, y) || onLot(l, x, y)) || NatureMod.nearStart(l, x, y, STRAY_CALM))
                {
                    continue;
                }
                var chance = ((double)(isRoad(l, x, y) ? ROAD_STRAY_CHANCE : OPEN_STRAY_CHANCE)) * (scale);
                if ((NatureMod.roll(l.seed, x, y, 0x7a10) >= chance) || (Grids.tileAt(state.grid, Grids.tileIndex(state.grid, x, y)) != "floor") || Js.T(FurnitureMod.furnitureAt(state, x, y)))
                {
                    continue;
                }
                Dormant.add(state, new() { id = $"d{Js.Str(state.nextId++)}", x = Js.I(x), y = Js.I(y) });
            }
        }
    }

    /// <summary>The dead already up and about when the world is made, at 100% zombies: within sight of the start but outside its calm (START_STRAY_NEAR to START_STRAY_FAR tiles).</summary>
    public const int START_STRAYS = 3;

    public const int START_STRAY_NEAR = 22;

    public const int START_STRAY_FAR = 34;

    /// <summary>Awake static zombies round the start (stateless rolls from the seed), on open floor out of the start's calm. They wander till they notice someone.</summary>
    public static void startStrays(SimState state, Layout l)
    {
        var n = Difficulty.zombieScale(state, START_STRAYS);
        for (var k = 0; k < n; k++)
        {
            var angle = ((double)(((double)(Hash.hashRoll(l.seed, 0x7a20, k)) * (JsMath.PI))) * (2));
            var r = START_STRAY_NEAR + ((double)(Hash.hashRoll(l.seed, 0x7a21, k)) * (START_STRAY_FAR - START_STRAY_NEAR));
            Point aim = new Point { x = Js.I(JsMath.round(l.start.x + ((double)(JsMath.cos(angle)) * (r)))), y = Js.I(JsMath.round(l.start.y + ((double)(JsMath.sin(angle)) * (r)))) };
            if (!Grids.inBounds(state.grid, aim.x, aim.y))
            {
                continue;
            }
            var p = Space.nearestTile(state.grid, aim, (x, y) => ((Grids.getTile(state.grid, x, y) == "floor") && !Js.T(FurnitureMod.furnitureAt(state, x, y))) && !NatureMod.nearStart(l, x, y, START_STRAY_NEAR), 200);
            if (!Js.T(p))
            {
                continue;
            }
            ZombieKinds.giveVariant(Zombies.addZombie(state, p.x, p.y, Zombies.ZOMBIE_HEALTH, "static"), 1);
        }
    }

    /// <summary>What drawing a lot needs to know about the world.</summary>
    public static LotContext lotContext(Layout l)
    {
        return new LotContext { seed = l.seed, lootAt = (x, y) => lootFor(JsMath.sqrt((JsMath.pow((x - l.start.x), 2)) + (JsMath.pow((y - l.start.y), 2)))), distance = (x, y) => JsMath.sqrt((JsMath.pow((x - l.start.x), 2)) + (JsMath.pow((y - l.start.y), 2))), calm = NatureMod.START_CLEAR_RADIUS + 24 };
    }

    /// <summary>Finds in a crate this far from the start: CRATE_LOOT nearby, up to three times that at the far edge.</summary>
    public static double lootFor(double distance)
    {
        return (FurnitureMod.CRATE_LOOT - 2) + JsMath.min(((double)(2) * (FurnitureMod.CRATE_LOOT)), JsMath.floor(((double)(distance) / (20))));
    }

    /// <summary>The start: an intact shelter (edge walls, a door, windows, a little furniture) with three beds (one for a
    /// solo start), the survivors outside its door, and some wood and food.</summary>
    public static void startCamp(SimState state, Layout l)
    {
        var __d1 = l.start;
        var sx = __d1.x;
        var sy = __d1.y;
        var x0 = sx - 4;
        var y0 = sy - 7;
        // The shelter: an ordinary edge-walled building (Buildings.startPlan), whole (no decay), its containers empty.
        var plan = Buildings.startPlan();
        for (var y = 0; y < plan.h; y++)
        {
            for (var x = 0; x < plan.w; x++)
            {
                ground(state, x0 + x, y0 + y, plan.ground[y * plan.w + x] ?? "boards");
            }
        }
        foreach (var e in plan.edges)
        {
            var (ox, oy, side) = Lots.edgeOwner(new Point(x0 + e.ax, y0 + e.ay), new Point(x0 + e.bx, y0 + e.by));
            Grids.setEdge(state.grid, ox, oy, side, e.window == true ? Grids.EDGE_WINDOW : e.door ? Grids.EDGE_DOOR : Grids.EDGE_WALL);
        }
        foreach (var f in plan.furniture)
        {
            var piece = FurnitureMod.addFurniture(state, Buildings.isContainerLook(f.look) ? "crate" : "decor", x0 + f.x, y0 + f.y);
            piece.look = f.look;
            piece.table = f.table;
            piece.loot = 0;
        }
        var solo = state.settings.start == "solo";
        foreach (var dx in Js.Iter(solo ? ((List<double>)[3]) : ((List<double>)[1, 3, 5])))
        {
            FurnitureMod.addFurniture(state, "bed", x0 + dx, y0);
        }
        foreach (var dx in Js.Iter(solo ? ((List<double>)[0]) : ((List<double>)[-2, 0, 2])))
        {
            var s = Survivors.addSurvivor(state, sx + dx, sy + 1);
            s.traits = Skills.rollTraits(s.id);
        }
        if (solo)
        {
            var you = Js.NN(Js.Get(state.survivors, 0));
            state.control = new Control { survivorId = you.id, dx = 0, dy = 0, facing = new Point { x = Js.I(0), y = Js.I(1) }, nextSwing = 0, busy = null };
            var weapon = Items.spawnItem(state, "weapon", you.pos.x, you.pos.y);
            weapon.pos = null;
            weapon.carriedBy = you.id;
            you.weapon = weapon.id;
            Inventory.readyQuick(state, you);  // on the hotbar from the start
            Items.spawnItem(state, "bandage", you.pos.x, you.pos.y);
        }
        for (var i = 0; i < (solo ? 4 : 12); i++)
        {
            Items.spawnItem(state, "wood", (sx - 6) + (i % 4), (sy + 3) + JsMath.floor(((double)(i) / (4))));
        }
        for (var i = 0; i < (solo ? 4 : 10); i++)
        {
            Items.spawnItem(state, "food", (sx + 3) + (i % 4), (sy + 3) + JsMath.floor(((double)(i) / (4))));
        }
        var c = Space.nearestTile(state.grid, new Point { x = Js.I(sx + 5), y = Js.I(sy + 1) }, (x, y) => ((Grids.getTile(state.grid, x, y) == "floor") && !Js.T(FurnitureMod.furnitureAt(state, x, y))) && !Js.T(Items.itemAt(state, x, y)));
        if (Js.T(c))
        {
            FurnitureMod.addFurniture(state, "collector", c.x, c.y).loot = START_WATER;
        }
    }

    /// <summary>Water in the start camp's rain collector.</summary>
    public const int START_WATER = 12;

    /// <summary>Generates any missing chunk within GENERATE_RADIUS of a point. New terrain can change walkability, so this bumps the version and moves anything left standing in a wall.</summary>
    public static void ensureGeneratedAround(SimState state, Point p)
    {
        var world = state.world;
        if (!Js.T(world))
        {
            return;
        }
        var pcx = JsMath.floor(((double)(p.x) / (CHUNK)));
        var pcy = JsMath.floor(((double)(p.y) / (CHUNK)));
        Layout? l = null;
        for (var cy = JsMath.max(0, pcy - GENERATE_RADIUS); cy <= JsMath.min(world.chunks - 1, pcy + GENERATE_RADIUS); cy++)
        {
            for (var cx = JsMath.max(0, pcx - GENERATE_RADIUS); cx <= JsMath.min(world.chunks - 1, pcx + GENERATE_RADIUS); cx++)
            {
                var i = ((double)(cy) * (world.chunks)) + cx;
                if (Js.Get(world.generated, i) == 1)
                {
                    continue;
                }
                world.generated[Js.Idx(i)] = 1;
                l ??= layout(world.seed, world.size, state.settings.map);
                generateChunk(state, l, cx, cy);
                settle(state, cx, cy);
                state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
            }
        }
    }

    /// <summary>Anything in a freshly generated chunk that ended up on a wall or water steps to the nearest open tile.</summary>
    public static void settle(SimState state, int cx, int cy)
    {
        bool inChunk(Point q)
        {
            return (JsMath.floor(((double)(q.x) / (CHUNK))) == cx) && (JsMath.floor(((double)(q.y) / (CHUNK))) == cy);
        }
        bool open(double x, double y)
        {
            return Grids.isWalkable(state.grid, x, y);
        }
        bool floorOnly(double x, double y)
        {
            return Grids.tileAt(state.grid, Grids.tileIndex(state.grid, x, y)) == "floor";
        }
        foreach (var z in Js.Iter(state.zombies))
        {
            if (!inChunk(z.pos) || floorOnly(z.pos.x, z.pos.y))
            {
                continue;
            }
            z.pos = nearestAcross(state, z.pos, (__f0, __f1) => floorOnly(__f0, __f1)) ?? z.pos;
            z.move = null;
            z.path = [];
        }
        foreach (var a in Js.Iter(state.animals))
        {
            if (!inChunk(a.pos) || floorOnly(a.pos.x, a.pos.y))
            {
                continue;
            }
            a.pos = nearestAcross(state, a.pos, (__f0, __f1) => floorOnly(__f0, __f1)) ?? a.pos;
            Js.Object_assign(a, Motion.placedAt(a.pos));
            a.move = null;
        }
        foreach (var s in Js.Iter(state.survivors))
        {
            if (!inChunk(s.pos) || open(s.pos.x, s.pos.y))
            {
                continue;
            }
            s.pos = nearestAcross(state, s.pos, (__f0, __f1) => open(__f0, __f1)) ?? s.pos;
            s.move = null;
            s.path = [];
            s.pathVersion = -1;
        }
    }
    public static void settle(SimState state, double cx, double cy) => settle(state, Js.I(cx), Js.I(cy));

    /// <summary>The nearest accepted tile in growing square rings, across anything (nearestTile only spreads over walkable ground).</summary>
    public static Point? nearestAcross(SimState state, Point p, Func<double, double, bool> accept)
    {
        for (var r = 1; r <= CHUNK; r++)
        {
            for (var y = p.y - r; y <= (p.y + r); y++)
            {
                for (var x = p.x - r; x <= (p.x + r); x++)
                {
                    if (Js.T(((JsMath.max(JsMath.abs(x - p.x), JsMath.abs(y - p.y)) == r) && Grids.inBounds(state.grid, x, y)) && Js.T(accept(x, y))))
                    {
                        return new Point { x = Js.I(x), y = Js.I(y) };
                    }
                }
            }
        }
        return null;
    }

    /// <summary>A new generated world; `you`, if given, becomes the first survivor (CharacterMod.cs).</summary>
    public static SimState createWorld(double seed, double size = WORLD_SIZE, Settings? settings = null, Character? you = default)
    {
        settings ??= Difficulty.NORMAL;
        var state = States.createState(new NewStateOptions { seed = seed, width = Js.I(size), height = Js.I(size) });
        state.settings = Js.CopyAs<Settings>(settings);
        var l = layout(seed, size, settings.map);
        var chunks = JsMath.ceil(((double)(size) / (CHUNK)));
        state.world = new WorldInfo { seed = seed, size = size, chunks = chunks, generated = Js.Filled<double>(((double)(chunks) * (chunks)), 0), gen = GEN_VERSION };
        ensureGeneratedAround(state, l.start);
        startCamp(state, l);
        startStrays(state, l);
        if (Js.T(you) && (CharacterMod.characterProblem(you) == null))
        {
            CharacterMod.applyCharacter(state, Js.NN(Js.Get(state.survivors, 0)), you);
        }
        state.harsh = true;
        TimelineMod.rollTimeline(state, seed);
        Settlements.placeSettlements(state, l.start, seed);
        var angle = ((double)(((double)(Hash.hashRoll(seed, 0x7a0c)) * (JsMath.PI))) * (2));
        Point near = new Point { x = Js.I(JsMath.round(l.start.x + ((double)(JsMath.cos(angle)) * (35)))), y = Js.I(JsMath.round(l.start.y + ((double)(JsMath.sin(angle)) * (35)))) };
        Point clamped = new Point { x = Js.I(JsMath.max(1, JsMath.min(size - 2, near.x))), y = Js.I(JsMath.max(1, JsMath.min(size - 2, near.y))) };
        var spot = Space.nearestTile(state.grid, clamped, (x, y) => ((Grids.getTile(state.grid, x, y) == "floor") && !Js.T(FurnitureMod.furnitureAt(state, x, y))) && !Js.T(Items.itemAt(state, x, y)));
        // The truck stands where its whole body fits on the heading it was left at (VehicleBody.cs), within a short walk of the spot.
        var truckHeading = JsMath.floor(((double)(Hash.hashRoll(seed, 0x7a0d)) * (32)));
        var park = (Js.T(spot) ? (Vehicles.parkable(state, spot, truckHeading, "truck") ? spot : Space.nearestTile(state.grid, spot, (x, y) => Vehicles.parkable(state, new Point { x = Js.I(x), y = Js.I(y) }, truckHeading, "truck"), 2000)) : null);
        if (Js.T(park))
        {
            Vehicles.addVehicle(state, park.x, park.y, truckHeading, ((double)(Vehicles.FUEL_MAX) / (2)), 700, "truck");
        }
        Grids.fillExplored(state.grid, 0);
        for (var y = l.start.y - START_REVEAL_RADIUS; y <= (l.start.y + START_REVEAL_RADIUS); y++)
        {
            for (var x = l.start.x - START_REVEAL_RADIUS; x <= (l.start.x + START_REVEAL_RADIUS); x++)
            {
                if (Grids.inBounds(state.grid, x, y) && NatureMod.nearStart(l, x, y, START_REVEAL_RADIUS + 1))
                {
                    Grids.setExplored(state.grid, Grids.tileIndex(state.grid, x, y), 1);
                }
            }
        }
        return state;
    }
}
