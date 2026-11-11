// The world plan by region (2026-11-09, docs/worldplan-regions.md). A world is cut into R x R regions; each one's plan
// (Worldplan.makeRegion: towns, streets, roads, lots, masks over its window) is made the first time something asks
// about a tile in it, and kept (KEEP of them, the oldest made dropped first; dropping one only costs remaking it:
// plans are pure).
//
// What a region's plan depends on: (seed, size, map, its window), and its neighbours' towns, for the one road
// joining each pair of neighbouring regions (Link: between the nearest pair of towns across their border, routed over
// both regions' windows). Towns keep Towns.REGION_MARGIN from a shared border, so neighbours' towns never clash. So a
// region comes out the same whichever order regions are asked for (WorldRegionsTest).
//
// A world no bigger than R is one region whose window is the whole map: exactly the plan it always had.
//
// Thread-safe for planning: the caches are behind a lock and a plan is made outside it (two threads making the same
// region get the same plan; the first kept wins), so the game can plan regions ahead on a worker (PlanSoon: the far
// field asks it for the land it will draw). TownsNear keeps a scratch list: the main thread's only.
#nullable disable warnings
namespace LoonieRoo.Sim;

public sealed class WorldRegions
{
    /// <summary>A region's side in tiles (a multiple of PLAN_CHUNK, of Worldgen.CHUNK and of the road cells).</summary>
    public const int R = 1024;
    /// <summary>Region plans kept (each ~5 MB of masks at R = 1024).</summary>
    public const int KEEP = 16;
    /// <summary>Lot ids are a region's index x this + the lot's place in it.</summary>
    public const double LOTS_PER_REGION = 65536;

    public readonly double seed;
    public readonly int size;
    public readonly string map;
    public readonly Nature nature;
    /// <summary>Regions to a side.</summary>
    public readonly int side;
    public bool Single => side == 1;

    readonly Dictionary<int, WorldPlan> plans = new();
    readonly List<int> planOrder = new();
    readonly Dictionary<int, (List<Town> towns, List<Street> streets, TownIndex index)> townsOf = new();
    readonly Dictionary<(int, int), List<PointD>> links = new();
    readonly List<int> scratch = new();
    readonly object gate = new();
    readonly HashSet<int> inFlight = new();

    public WorldRegions(double seed, double size, string map)
    {
        this.seed = seed;
        this.size = (int)size;
        this.map = map;
        nature = NatureMod.natureFor(seed, size, map);
        side = this.size <= R ? 1 : (int)Math.Ceiling(this.size / (double)R);
    }

    /// <summary>Regions planned so far (the debug panel, the tools).</summary>
    public int Planned { get { lock (gate) return plans.Count; } }

    public int IndexOf(int rx, int ry) => ry * side + rx;

    public Win WindowOf(int rx, int ry)
    {
        if (Single) return Win.Whole(size);
        int x0 = rx * R, y0 = ry * R, x1 = Math.Min(size, x0 + R), y1 = Math.Min(size, y0 + R);
        return new Win(x0, y0, x1, y1, x0 == 0, y0 == 0, x1 == size, y1 == size, IndexOf(rx, ry));
    }

    (int rx, int ry) RegionOfTile(double x, double y) =>
        (Math.Clamp((int)Math.Floor(x / R), 0, side - 1), Math.Clamp((int)Math.Floor(y / R), 0, side - 1));

    /// <summary>A region's towns and streets (sited, not planned: cheap next to a whole plan), kept for good.</summary>
    (List<Town> towns, List<Street> streets, TownIndex index) TownsAndStreets(int rx, int ry)
    {
        int i = IndexOf(rx, ry);
        lock (gate) if (townsOf.TryGetValue(i, out var hit)) return hit;
        (List<Town>, List<Street>, TownIndex) made;
        if (Single)
        {
            var p = Region(0, 0);
            made = (p.towns, p.streets, new TownIndex(p.towns, size));
        }
        else
        {
            var w = WindowOf(rx, ry);
            var towns = Towns.siteTowns(nature, Towns.landGrid(nature, w), w);
            var streets = towns.flatMap((t, ti) => Towns.growStreets(nature, t, ti));
            made = (towns, streets, new TownIndex(towns, size));
        }
        lock (gate)
        {
            if (townsOf.TryGetValue(i, out var other)) return other;
            townsOf[i] = made;
        }
        return made;
    }

    /// <summary>A region's towns.</summary>
    public List<Town> TownsOf(int rx, int ry) => TownsAndStreets(rx, ry).towns;

    /// <summary>Every town in the world (sites every region: a tool's question, not the game's).</summary>
    public List<Town> AllTowns()
    {
        var all = new List<Town>();
        for (int ry = 0; ry < side; ry++)
            for (int rx = 0; rx < side; rx++) all.AddRange(TownsOf(rx, ry));
        return all;
    }

    /// <summary>The towns of the regions a box of `reach` round (x, y) touches whose centre is within the box (by
    /// their region's TownIndex), region by region, each region's in list order. Over one region, TownIndex.Near.</summary>
    public List<Town> TownsNear(double x, double y, double reach, List<Town> into)
    {
        into.Clear();
        var (rx0, ry0) = RegionOfTile(x - reach, y - reach);
        var (rx1, ry1) = RegionOfTile(x + reach, y + reach);
        for (int ry = ry0; ry <= ry1; ry++)
            for (int rx = rx0; rx <= rx1; rx++)
            {
                var (towns, _, index) = TownsAndStreets(rx, ry);
                foreach (var i in index.Near(x, y, reach, scratch)) into.Add(towns[i]);
            }
        return into;
    }

    /// <summary>The road joining regions a and b (neighbours, a before b), or null: from the nearest pair of their towns
    /// (a's first, then b's, in list order), main-street end to main-street end, over both regions' windows.</summary>
    List<PointD> Link(int ax, int ay, int bx, int by)
    {
        var key = (IndexOf(ax, ay), IndexOf(bx, by));
        lock (gate) if (links.TryGetValue(key, out var hit)) return hit;
        var (ta, sa, _) = TownsAndStreets(ax, ay);
        var (tb, sb, _) = TownsAndStreets(bx, by);
        List<PointD> line = null;
        if (ta.Count > 0 && tb.Count > 0)
        {
            int bi = 0, bj = 0;
            double best = double.PositiveInfinity;
            for (int i = 0; i < ta.Count; i++)
                for (int j = 0; j < tb.Count; j++)
                {
                    double d = Worldplan.dist(ta[i].x, ta[i].y, tb[j].x, tb[j].y);
                    if (d < best) { best = d; bi = i; bj = j; }
                }
            Point End(List<Street> streets, Town t, int ti, Town toward)
            {
                var options = Worldplan.streetEnds(streets, ti);
                if (options.length == 0) return new Point { x = t.x, y = t.y };
                return options.reduce((p, q) => (Worldplan.dist(q.x, q.y, toward.x, toward.y) < Worldplan.dist(p.x, p.y, toward.x, toward.y)) ? q : p);
            }
            var u = Win.Union(WindowOf(ax, ay), WindowOf(bx, by));
            var both = new List<Town>(ta);
            both.AddRange(tb);
            var cost = Roads.costGrid(nature, both, u);
            line = Roads.route(nature, cost, End(sa, ta[bi], bi, tb[bj]), End(sb, tb[bj], bj, ta[bi]), u);
        }
        lock (gate)
        {
            if (links.TryGetValue(key, out var other)) return other;
            links[key] = line;
        }
        return line;
    }

    /// <summary>A region's plan, made the first time it's asked for.</summary>
    public WorldPlan Region(int rx, int ry)
    {
        int i = IndexOf(rx, ry);
        lock (gate) if (plans.TryGetValue(i, out var hit)) return hit;
        WorldPlan made;
        if (Single) made = Worldplan.makePlan(nature);
        else
        {
            // The roads to its four neighbours, each keyed lower region first (so both sides get the same one).
            var mine = new List<List<PointD>>();
            foreach (var (nx, ny) in new[] { (rx, ry - 1), (rx - 1, ry), (rx + 1, ry), (rx, ry + 1) })
            {
                if (nx < 0 || ny < 0 || nx >= side || ny >= side) continue;
                var line = IndexOf(nx, ny) < i ? Link(nx, ny, rx, ry) : Link(rx, ry, nx, ny);
                if (line != null) mine.Add(line);
            }
            made = Worldplan.makeRegion(nature, WindowOf(rx, ry), TownsOf(rx, ry), mine);
        }
        lock (gate)
        {
            if (plans.TryGetValue(i, out var other)) return other;
            plans[i] = made;
            planOrder.Add(i);
            if (planOrder.Count > KEEP)
            {
                plans.Remove(planOrder[0]);
                planOrder.RemoveAt(0);
            }
        }
        return made;
    }

    /// <summary>The region holding tile (x, y).</summary>
    public (int rx, int ry) RegionOf(double x, double y) => RegionOfTile(x, y);

    /// <summary>Whether a region's plan is made (and kept).</summary>
    public bool IsPlanned(int rx, int ry)
    {
        lock (gate) return plans.ContainsKey(IndexOf(rx, ry));
    }

    /// <summary>Makes a region's plan on a worker thread, if it isn't made or being made (the far field, ahead of you).</summary>
    public void PlanSoon(int rx, int ry)
    {
        int i = IndexOf(rx, ry);
        lock (gate)
        {
            if (plans.ContainsKey(i) || !inFlight.Add(i)) return;
        }
        System.Threading.Tasks.Task.Run(() =>
        {
            try { Region(rx, ry); }
            finally { lock (gate) inFlight.Remove(i); }
        });
    }

    /// <summary>The plan of the region holding tile (x, y).</summary>
    public WorldPlan RegionAt(double x, double y)
    {
        var (rx, ry) = RegionOfTile(x, y);
        return Region(rx, ry);
    }

    /// <summary>The road mask at a tile (0 none, 1 a country road, 2 a street); 0 off the map.</summary>
    public int RoadAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= size || y >= size) return 0;
        var p = RegionAt(x, y);
        var w = p.window;
        return p.roadMask.a[(y - w.y0) * w.W + (x - w.x0)];
    }

    public bool IsRoad(int x, int y) => RoadAt(x, y) != 0;

    /// <summary>The lot covering a tile, or null.</summary>
    public Lot LotAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= size || y >= size) return null;
        var p = RegionAt(x, y);
        var w = p.window;
        int v = p.lotMask.a[(y - w.y0) * w.W + (x - w.x0)];
        return v == 0 ? null : p.lots[v - 1];
    }

    public bool OnLot(int x, int y) => LotAt(x, y) != null;

    /// <summary>A lot by its id.</summary>
    public Lot Lot(double id)
    {
        int region = (int)Math.Floor(id / LOTS_PER_REGION), local = (int)(id - region * LOTS_PER_REGION);
        var p = Region(region % side, region / side);
        return p.lots[local];
    }

    /// <summary>The ids of the lots touching plan chunk (cx, cy) (PLAN_CHUNK tiles), in the plan's order.</summary>
    public List<double> LotsInChunk(int cx, int cy)
    {
        var p = RegionAt(cx * Worldplan.PLAN_CHUNK, cy * Worldplan.PLAN_CHUNK);
        int at = (cy - p.chunkY0) * p.chunksW + (cx - p.chunkX0);
        return at >= 0 && at < p.lotsByChunk.Count ? p.lotsByChunk[at] : Empty;
    }

    static readonly List<double> Empty = new();

    /// <summary>Roads laid in the regions planned so far (MapShot's count).</summary>
    public int RoadsPlanned { get { lock (gate) return plans.Values.Sum((p) => p.roads.Count); } }
}
