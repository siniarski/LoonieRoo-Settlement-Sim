// Ported from src/sim/worldplan.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// The world plan: everything built on the land, laid out from the seed, a region at a time (WorldRegions.cs), in order:
//   1. nature (nature.ts): water and forest, per tile
//   2. towns sited on suitable land, each growing its streets (towns.ts)
//   3. country roads routed between towns, to the start, and out to the map's edge (roads.ts)
//   4. lots cut along the streets by zone (civic, shops, houses, industry), farms along the
//      country roads near towns, the odd cabin out in the wild
// (The building inside a lot is planned when the lot is first drawn or asked about: Lots.layoutLot.)
// The plan is plain data and derived (never saved): regions() caches it by seed, size and map.
// Chunks draw their part of it (worldgen.ts), so they come out the same in any order.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Worldplan
{

    /// <summary>A chunk's size, for the per-chunk index (a literal, not worldgen's CHUNK: see CLAUDE.md rule 12; a test checks they match).</summary>
    public const int PLAN_CHUNK = 32;

    /// <summary>Each world's regions (WorldRegions), by seed, size and map: the plan is derived, never saved.</summary>
    public static readonly JsMap<string, WorldRegions> cache = new JsMap<string, WorldRegions>();

    static string Key(double seed, double size, string map) => $"{Js.Str(seed)}|{Js.Str(size)}|{Js.Str(map)}";

    /// <summary>A world's plan, region by region (docs/worldplan-regions.md).</summary>
    public static WorldRegions regions(double seed, double size, string map)
    {
        var key = Key(seed, size, map);
        var hit = cache.get(key);
        if (Js.T(hit))
        {
            return hit;
        }
        var made = new WorldRegions(seed, size, map);
        remember(seed, size, map, made);
        return made;
    }

    /// <summary>The whole plan of a world of one region (no bigger than WorldRegions.R): the tests', the small tools'.
    /// A bigger world has no whole plan: ask its regions.</summary>
    public static WorldPlan planWorld(double seed, double size, string map)
    {
        var r = regions(seed, size, map);
        if (!r.Single) throw new InvalidOperationException($"planWorld: a {size} world is planned by region (Worldplan.regions)");
        return r.Region(0, 0);
    }

    /// <summary>Keeps a world's regions made elsewhere (the game makes a big world's first ones on a worker thread:
    /// prepare), so createWorld finds them.</summary>
    public static void remember(double seed, double size, string map, WorldRegions made)
    {
        var key = Key(seed, size, map);
        if (Js.T(cache.get(key))) return;
        if (cache.size > 6)
        {
            cache.delete(cache.keys()[0]);
        }
        cache.set(key, made);
    }

    /// <summary>A world's regions with the ones round the start planned (what createWorld asks for first): pure, so the
    /// game can run it on a worker thread and remember the result.</summary>
    public static WorldRegions prepare(double seed, double size, string map) => planStart(new WorldRegions(seed, size, map));

    /// <summary>Plans the regions round the start of a world's regions (any thread: WorldRegions is thread-safe for
    /// planning), so the game can take regions the menu's preview already began (`regions` on the main thread, this on
    /// a worker).</summary>
    public static WorldRegions planStart(WorldRegions r)
    {
        var s = r.nature.start;
        foreach (var (dx, dy) in new[] { (0, 0), (-200, 0), (200, 0), (0, -200), (0, 200) }) r.RegionAt(s.x + dx, s.y + dy);
        return r;
    }

    /// <summary>The whole map's plan as one region (worlds no bigger than WorldRegions.R).</summary>
    public static WorldPlan makePlan(Nature n) => makeRegion(n, Win.Whole(n.size), null, null);

    /// <summary>A region's plan (docs/worldplan-regions.md): its towns (sited inside it, or given), their streets, the
    /// roads among them, the given roads from its neighbours (`links`: the parts crossing it are drawn and lots
    /// follow them), its lots (wholly inside it), and its masks, all over its window. Over the whole map with no
    /// links, the plan as it always was.</summary>
    public static WorldPlan makeRegion(Nature n, Win w, List<Town>? given, List<List<PointD>>? links)
    {
        var size = n.size;
        var towns = given ?? Towns.siteTowns(n, Towns.landGrid(n, w), w);
        double W = w.W, H = w.H;
        var roadMask = new Uint8Array(((double)(W) * (H)));
        var streets = towns.flatMap((t, i) => Towns.growStreets(n, t, i));
        foreach (var s in Js.Iter(streets))
        {
            for (var y = s.y0; y <= (s.y1 + (Js.T(s.eastWest) ? (Towns.STREET - 1) : 0)); y++)
            {
                for (var x = s.x0; x <= (s.x1 + (Js.T(s.eastWest) ? 0 : (Towns.STREET - 1))); x++)
                {
                    if ((((x >= w.x0) && (y >= w.y0)) && (x < w.x1)) && (y < w.y1))
                    {
                        roadMask[Js.Idx(((double)(y - w.y0) * (W)) + (x - w.x0))] = 2;
                    }
                }
            }
        }
        var roads = layRoads(n, towns, streets, roadMask, w);
        if (links != null)
        {
            foreach (var line in links)
            {
                Roads.drawLine(roadMask, w, line, 1);
                roads.push(line);
            }
        }
        List<Lot> lots = [];
        var lotMask = new Int32Array(((double)(W) * (H)));
        // Lots stay a tile in from the map's edge (as always) and wholly inside the region.
        int loX = w.edgeL ? 1 : w.x0, loY = w.edgeT ? 1 : w.y0;
        double hiX = w.edgeR ? size - 1 : w.x1, hiY = w.edgeB ? size - 1 : w.y1;
        bool add(Lot lot)
        {
            for (var y = lot.y; y < (lot.y + lot.h); y++)
            {
                for (var x = lot.x; x < (lot.x + lot.w); x++)
                {
                    if ((((x < loX) || (y < loY)) || (x >= hiX)) || (y >= hiY))
                    {
                        return false;
                    }
                    var i = ((double)(y - w.y0) * (W)) + (x - w.x0);
                    if (((((Js.Get(roadMask, i) != 0) || (Js.Get(lotMask, i) != 0)) || NatureMod.isWater(n, x, y)) || NatureMod.onFenceLine(n, x, y)) || NatureMod.nearStart(n, x, y, NatureMod.START_CLEAR_RADIUS + 2))
                    {
                        return false;
                    }
                }
            }
            var id = lots.length;
            lots.push(Js.With(new Lot { id = (double)w.index * WorldRegions.LOTS_PER_REGION + id }, __s0 => Js.AssignSet(__s0, lot)));
            for (var y = lot.y; y < (lot.y + lot.h); y++)
            {
                for (var x = lot.x; x < (lot.x + lot.w); x++)
                {
                    lotMask[Js.Idx(((double)(y - w.y0) * (W)) + (x - w.x0))] = id + 1;
                }
            }
            return true;
        }
        townLots(n, towns, streets, (__f0) => add(__f0));
        farmLots(n, towns, roads, (__f0) => add(__f0));
        fieldLots(n, towns, (__f0) => add(__f0));
        cabinLots(n, roads, (__f0) => add(__f0), w);
        // Lots by plan chunk, over the region's chunks (a region is whole chunks: R is a multiple of PLAN_CHUNK).
        int cx0 = w.x0 / PLAN_CHUNK, cy0 = w.y0 / PLAN_CHUNK;
        double chunks = JsMath.ceil(((double)(w.x1) / (PLAN_CHUNK))) - cx0, rows = JsMath.ceil(((double)(w.y1) / (PLAN_CHUNK))) - cy0;
        List<List<double>> lotsByChunk = Js.ArrayFrom<List<double>>(((double)(chunks) * (rows)), (int _i) => []);
        foreach (var l in Js.Iter(lots))
        {
            for (var cy = JsMath.floor(((double)(l.y) / (PLAN_CHUNK))); cy <= JsMath.floor(((double)(((l.y + l.h) - 1)) / (PLAN_CHUNK))); cy++)
            {
                for (var cx = JsMath.floor(((double)(l.x) / (PLAN_CHUNK))); cx <= JsMath.floor(((double)(((l.x + l.w) - 1)) / (PLAN_CHUNK))); cx++)
                {
                    Js.NN(Js.Get(lotsByChunk, ((double)(cy - cy0) * (chunks)) + (cx - cx0))).push(l.id);
                }
            }
        }
        return new WorldPlan { nature = n, towns = towns, streets = streets, roads = roads, roadMask = roadMask, lots = lots, lotMask = lotMask, lotsByChunk = lotsByChunk, window = w, chunkX0 = cx0, chunkY0 = cy0, chunksW = (int)chunks };
    }

    public static double dist(double ax, double ay, double bx, double by)
    {
        return JsMath.sqrt((JsMath.pow((ax - bx), 2)) + (JsMath.pow((ay - by), 2)));
    }

    /// <summary>Roads between towns (and the start), and a few out to the map's edge.</summary>
    public static List<List<PointD>> layRoads(Nature n, List<Town> towns, List<Street> streets, Uint8Array mask) => layRoads(n, towns, streets, mask, Win.Whole(n.size));

    /// <summary>The main-street ends of town `ti` (its index in `towns`), where country roads leave it.</summary>
    public static List<Point> streetEnds(List<Street> streets, double ti) =>
        streets.filter((s) => (s.town == ti) && s.main).flatMap((s) => s.eastWest ? ((List<Point>)new List<Point> { new Point { x = Js.I(s.x0), y = Js.I(s.y0) }, new Point { x = Js.I(s.x1), y = Js.I(s.y0) } }) : ((List<Point>)new List<Point> { new Point { x = Js.I(s.x0), y = Js.I(s.y0) }, new Point { x = Js.I(s.x0), y = Js.I(s.y1) } }));

    /// <summary>The same inside a window: the start's node only if the start is in it; roads to the map's edge only on
    /// the sides of it that are the map's edge.</summary>
    public static List<List<PointD>> layRoads(Nature n, List<Town> towns, List<Street> streets, Uint8Array mask, Win w)
    {
        var size = n.size;
        List<Point> ends(double ti)
        {
            return streets.filter((s) => (s.town == ti) && s.main).flatMap((s) => s.eastWest ? ((List<Point>)new List<Point> { new Point { x = Js.I(s.x0), y = Js.I(s.y0) }, new Point { x = Js.I(s.x1), y = Js.I(s.y0) } }) : ((List<Point>)new List<Point> { new Point { x = Js.I(s.x0), y = Js.I(s.y0) }, new Point { x = Js.I(s.x0), y = Js.I(s.y1) } }));
        }
        List<Point> nodes = [.. towns.map((t) => new Point { x = Js.I(t.x), y = Js.I(t.y) })];
        if (w.Holds(n.start.x, n.start.y + 8)) nodes.push(new Point { x = Js.I(n.start.x), y = Js.I(n.start.y + 8) });
        Point nearestEnd(double node, Point toward)
        {
            if (node >= towns.length)
            {
                return Js.NN(Js.Get(nodes, node));
            }
            var options = ends(node);
            if (options.length == 0)
            {
                return Js.NN(Js.Get(nodes, node));
            }
            return options.reduce((a, b) => (dist(b.x, b.y, toward.x, toward.y) < dist(a.x, a.y, toward.x, toward.y)) ? b : a);
        }
        List<List<Point>> links = Roads.network(n.seed, nodes).map((__p1) =>
        {
            var (a, b) = __p1;
            return (List<Point>)[nearestEnd(a, Js.NN(Js.Get(nodes, b))), nearestEnd(b, Js.NN(Js.Get(nodes, a)))];
        });
        towns.forEach((t, i) =>
        {
            if (!((n.map == "quarantine") && (i == 0)) && (NatureMod.roll(n.seed, t.x, t.y, 0x7301) > ((n.map == "quarantine") ? 0.8 : 0.45)))
            {
                return;
            }
            List<Point> edges = [];
            if (w.edgeL) edges.push(new Point { x = Js.I(0), y = Js.I(t.y) });
            if (w.edgeR) edges.push(new Point { x = Js.I(size - 1), y = Js.I(t.y) });
            if (w.edgeT) edges.push(new Point { x = Js.I(t.x), y = Js.I(0) });
            if (w.edgeB) edges.push(new Point { x = Js.I(t.x), y = Js.I(size - 1) });
            if (edges.length == 0) return;
            var edge = edges.reduce((a, b) => (dist(b.x, b.y, t.x, t.y) < dist(a.x, a.y, t.x, t.y)) ? b : a);
            links.push([nearestEnd(i, edge), edge]);
        });
        var cost = Roads.costGrid(n, towns, w);
        List<List<PointD>> roads = [];
        foreach (var (a, b) in Js.Iter(links))
        {
            var line = Roads.route(n, cost, a, b, w);
            if (!Js.T(line))
            {
                continue;
            }
            roads.push(line);
            Roads.reuse(cost, w, line);
            Roads.drawLine(mask, w, line, 1);
        }
        return roads;
    }

    public static readonly JsRecord<List<double>> SIZES = new() { ["civic"] = [20, 26, 18, 24], ["commercial"] = [12, 15, 12, 14], ["residential"] = [12, 15, 13, 15], ["industrial"] = [22, 28, 18, 24] };

    /// <summary>Lots along both sides of every street, zoned by how far they are from the town's centre.</summary>
    public static void townLots(Nature n, List<Town> towns, List<Street> streets, Func<Lot, bool> add)
    {
        var civic = towns.map(_ => 0);
        JsRecord<double> CIVIC_MAX = new() { ["village"] = 1, ["town"] = 2, ["city"] = 4 };
        var ordered = Js.ListOf(streets).sort((a, b) => Js.Num(b.main) - Js.Num(a.main));
        ordered.forEach((s, si) =>
        {
            var t = Js.NN(Js.Get(towns, s.town));
            foreach (var side in Js.Iter((List<double>)[-1, 1]))
            {
                double p = s.eastWest ? s.x0 : s.y0;
                var end = s.eastWest ? (s.x1 + 1) : (s.y1 + 1);
                var k = 0;
                while (p < (end - 8))
                {
                    double r(double j)
                    {
                        return NatureMod.roll(n.seed, ((double)(si) * (31)) + side, p, (0x7400 + j) + ((double)(k) * (7)));
                    }
                    var mid = p + 9;
                    var __c2 = s.eastWest; var mx = __c2 ? mid : (s.x0 + ((double)(side) * (9))); var my = __c2 ? (s.y0 + ((double)(side) * (9))) : mid;
                    var share = ((double)(dist(mx, my, t.x, t.y)) / (t.radius));
                    string zone = "residential";
                    if ((((share < 0.45) && s.main) && (Js.NN(Js.Get(civic, s.town)) < Js.Get(CIVIC_MAX, t.kind))) && (r(4) < 0.6))
                    {
                        zone = "civic";
                    }
                    else if (share < ((t.kind == "village") ? 0.4 : 0.6))
                    {
                        zone = "commercial";
                    }
                    else if (((t.kind != "village") && (share > 0.6)) && (r(0) < 0.2))
                    {
                        zone = "industrial";
                    }
                    var (f0, f1, d0, d1) = Js.Get(SIZES, zone);
                    var front = f0 + JsMath.floor(((double)(r(1)) * (((f1 - f0) + 1))));
                    var deep = d0 + JsMath.floor(((double)(r(2)) * (((d1 - d0) + 1))));
                    var front2 = JsMath.min(front, end - p);
                    if (front2 < f0)
                    {
                        break;
                    }
                    TownLotsResult lotAt(double w, double d)
                    {
                        return s.eastWest ? new() { x = Js.I(p), y = Js.I((side < 0) ? ((s.y0 - 1) - d) : ((s.y0 + Towns.STREET) + 1)), w = Js.I(w), h = Js.I(d), facing = (side < 0) ? "south" : "north" } : new() { x = Js.I((side < 0) ? ((s.x0 - 1) - d) : ((s.x0 + Towns.STREET) + 1)), y = Js.I(p), w = Js.I(d), h = Js.I(w), facing = (side < 0) ? "east" : "west" };
                    }
                    var placed = add(Js.With(Js.CopyAs<Lot>(lotAt(front2, deep)), __s3 => { __s3.zone = zone; __s3.town = s.town; })) ? front2 : ((((zone != "civic") && (zone != "industrial")) && add(Js.With(Js.CopyAs<Lot>(lotAt(12, 12)), __s4 => { __s4.zone = zone; __s4.town = s.town; }))) ? 12 : 0);
                    if (placed > 0)
                    {
                        if ((zone == "civic") && (placed == front2))
                        {
                            civic[Js.Idx(s.town)]++;
                        }
                        p += placed + ((r(3) < 0.5) ? 1 : 0);
                    }
                    else
                    {
                        p += 2;
                    }
                    k++;
                }
            }
        });
    }

    /// <summary>Along the country roads round each town: houses where the town spills out along its roads, then farms (a farmstead and its fields, or fields alone) further out.</summary>
    public static void farmLots(Nature n, List<Town> towns, List<List<PointD>> roads, Func<Lot, bool> add)
    {
        // Towns by cell (TownIndex): each road point asks only the towns that could be in reach (the first in list order wins, as before).
        var index = new TownIndex(towns, n.size);
        double edgeReach = index.Biggest * 1.35 + 6 + 1, nearReach = index.Biggest * 2.6 + 24 + 1;
        var scratch = new List<int>();
        roads.forEach((line, ri) =>
        {
            double since = 99;
            for (var i = 1; i < line.length; i++)
            {
                var a = Js.NN(Js.Get(line, i - 1));
                var b = Js.NN(Js.Get(line, i));
                since += dist(a.x, a.y, b.x, b.y);
                var edge = index.Find(b.x, b.y, edgeReach, (t) =>
        {
            var d = dist(b.x, b.y, t.x, t.y);
            return (d > ((double)(t.radius) * (0.8))) && (d < (((double)(t.radius) * (1.35)) + 6));
        }, scratch);
                if (Js.T(edge) && (since >= 15))
                {
                    double r_1(double j)
                    {
                        return NatureMod.roll(n.seed, ri, i, 0x7480 + j);
                    }
                    var along_2 = JsMath.abs(b.x - a.x) >= JsMath.abs(b.y - a.y);
                    var cx_3 = JsMath.round(b.x);
                    var cy_4 = JsMath.round(b.y);
                    foreach (var side_5 in Js.Iter((List<double>)[-1, 1]))
                    {
                        if (r_1(side_5 + 2) > 0.7)
                        {
                            continue;
                        }
                        var front_6 = 12 + JsMath.floor(((double)(r_1(5 + side_5)) * (3)));
                        var deep_7 = 13 + JsMath.floor(((double)(r_1(8 + side_5)) * (3)));
                        FarmLotsLot lot_8 = along_2 ? new() { x = Js.I(cx_3 - JsMath.floor(((double)(front_6) / (2)))), y = Js.I((side_5 < 0) ? ((cy_4 - 3) - deep_7) : (cy_4 + 3)), w = Js.I(front_6), h = Js.I(deep_7), facing = (side_5 < 0) ? "south" : "north" } : new() { x = Js.I((side_5 < 0) ? ((cx_3 - 3) - deep_7) : (cx_3 + 3)), y = Js.I(cy_4 - JsMath.floor(((double)(front_6) / (2)))), w = Js.I(deep_7), h = Js.I(front_6), facing = (side_5 < 0) ? "east" : "west" };
                        if (add(Js.With(Js.CopyAs<Lot>(lot_8), __s5 => { __s5.zone = "residential"; __s5.town = towns.indexOf(edge); })))
                        {
                            since = 0;
                        }
                    }
                    continue;
                }
                if (since < 34)
                {
                    continue;
                }
                var near = index.Find(b.x, b.y, nearReach, (t) =>
        {
            var d = dist(b.x, b.y, t.x, t.y);
            return (d > ((double)(t.radius) * (1.2))) && (d < (((double)(t.radius) * (2.6)) + 24));
        }, scratch);
                if (!Js.T(near))
                {
                    continue;
                }
                double r(double j)
                {
                    return NatureMod.roll(n.seed, ri, i, 0x7500 + j);
                }
                if (r(0) > 0.85)
                {
                    continue;
                }
                var along = JsMath.abs(b.x - a.x) >= JsMath.abs(b.y - a.y);
                var side = (r(1) < 0.5) ? -1 : 1;
                var front = 26 + JsMath.floor(((double)(r(2)) * (14)));
                var deep = 22 + JsMath.floor(((double)(r(3)) * (10)));
                string zone = (r(4) < 0.6) ? "farm" : "field";
                var cx = JsMath.round(b.x);
                var cy = JsMath.round(b.y);
                FarmLotsLotShape22 lot = along ? new() { x = Js.I(cx - JsMath.floor(((double)(front) / (2)))), y = Js.I((side < 0) ? ((cy - 3) - deep) : (cy + 3)), w = Js.I(front), h = Js.I(deep), facing = (side < 0) ? "south" : "north" } : new() { x = Js.I((side < 0) ? ((cx - 3) - deep) : (cx + 3)), y = Js.I(cy - JsMath.floor(((double)(front) / (2)))), w = Js.I(deep), h = Js.I(front), facing = (side < 0) ? "east" : "west" };
                if (add(Js.With(Js.CopyAs<Lot>(lot), __s6 => { __s6.zone = zone; __s6.town = null; })))
                {
                    since = 0;
                }
            }
        });
    }

    /// <summary>Fields ringing each town beyond its edge, facing it (farm tracks run in from the town).</summary>
    public static void fieldLots(Nature n, List<Town> towns, Func<Lot, bool> add)
    {
        towns.forEach((t, ti) =>
        {
            var tries = JsMath.round(((double)(t.radius) / (2)));
            for (var i = 0; i < tries; i++)
            {
                double r(double j)
                {
                    return NatureMod.roll(n.seed, ti, i, 0x7700 + j);
                }
                var a = ((double)(((double)(r(0)) * (JsMath.PI))) * (2));
                var d = ((double)(t.radius) * ((1.25 + ((double)(r(1)) * (0.9)))));
                var cx = JsMath.round(t.x + ((double)(JsMath.cos(a)) * (d)));
                var cy = JsMath.round(t.y + ((double)(JsMath.sin(a)) * (d)));
                var w = 18 + JsMath.floor(((double)(r(2)) * (16)));
                var h = 16 + JsMath.floor(((double)(r(3)) * (14)));
                var dx = t.x - cx;
                var dy = t.y - cy;
                string facing = (JsMath.abs(dx) > JsMath.abs(dy)) ? ((dx > 0) ? "east" : "west") : ((dy > 0) ? "south" : "north");
                add(new Lot { x = Js.I(cx - JsMath.floor(((double)(w) / (2)))), y = Js.I(cy - JsMath.floor(((double)(h) / (2)))), w = Js.I(w), h = Js.I(h), facing = facing, zone = ((r(4) < 0.25) && (w >= 27)) ? "farm" : "field", town = null });
            }
        });
    }

    /// <summary>The same over a window: the map's 48-tile lattice, the cells starting inside it.</summary>
    public static void cabinLots(Nature n, List<List<PointD>> roads, Func<Lot, bool> add, Win w)
    {
        for (var gy = (int)Math.Ceiling(w.y0 / 48.0) * 48; gy < w.y1; gy += 48)
        {
            for (var gx = (int)Math.Ceiling(w.x0 / 48.0) * 48; gx < w.x1; gx += 48)
            {
                double r(double j)
                {
                    return NatureMod.roll(n.seed, gx, gy, 0x7600 + j);
                }
                // More cabins in wild country (a third of the cells where nobody settled, a fifth where towns are).
                if (r(0) > 0.2 + 0.15 * (1 - Towns.settled(n, gx + 24, gy + 24)))
                {
                    continue;
                }
                var x = (gx + 4) + JsMath.floor(((double)(r(1)) * (30)));
                var y = (gy + 4) + JsMath.floor(((double)(r(2)) * (30)));
                List<string> facings = ["north", "south", "east", "west"];
                add(new Lot { x = Js.I(x), y = Js.I(y), w = Js.I(13), h = Js.I(13), facing = Js.NN(Js.Get(facings, JsMath.floor(((double)(r(3)) * (4))))), zone = "cabin", town = null });
            }
        }
    }
}
