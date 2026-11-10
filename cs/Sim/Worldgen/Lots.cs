// What stands on a lot (worldplan.ts cuts the lots), and drawing it a chunk at a time (2026-11-10, the buildings
// session: docs/buildings.md). Ported from src/sim/lots.ts at first.
//
// A lot faces its street or road. Its layout is planned in the lot's own frame, u along the frontage and v back from
// the street, then turned onto the map. Every building and the one-tile apron round it (Heights levels both) stays
// inside the lot.
//   residential  a house (Buildings.housePlan, by lot size) set back 2-3 tiles: a paved path to the front door, often
//                a porch, a concrete driveway to a garage (where a car is parked), a back yard behind; some yards
//                fenced in planks with a gate, some with a shed or a vegetable patch
//   commercial   a shop, bar, diner, workshop, office or petrol station at the street behind a paved front
//   civic        a police station, clinic, school, offices or a church on a paved square, benches in front
//   industrial   a warehouse or factory on a concrete yard
//   farm         a farmhouse and barn by the road, a silo, bales and a tractor in the yard, fields behind, hedged
//   field        fields only (or pasture)
//   cabin        a cabin or a trailer alone, a path and maybe a porch
// Windows on a side wall within a tile of the lot's edge are left out (the neighbour's wall would be a hand's breadth
// off). Everything is a pure function of the seed and the lot (layoutLot is cached per lot), and drawLot draws only
// what falls inside the chunk being generated, so a lot that spans chunks comes out whole and the same whichever chunk
// comes first.
//
// Drawn: walls fall in with the settings' ruin decay (outer more than inner), some doors broken or left open, some
// windows broken or boarded, a few houses boarded up whole; containers hold loot (more further out, some already
// searched), and the dead sleep inside bigger and further-out buildings.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Lots
{
    /// <summary>Where a plan's cell (x, y) lands, turned to face `facing` (its front was the bottom), maybe mirrored.</summary>
    public static Point transform(BuildingsPlan p, int x, int y, string facing, bool mirror)
    {
        var mx = mirror ? ((p.w - 1) - x) : x;
        switch (facing)
        {
            case "south": return new Point { x = mx, y = y };
            case "north": return new Point { x = (p.w - 1) - mx, y = (p.h - 1) - y };
            case "east": return new Point { x = y, y = (p.w - 1) - mx };
            case "west": return new Point { x = (p.h - 1) - y, y = mx };
        }
        throw new InvalidOperationException("unreachable: every case returns");
    }
    public static Point transform(BuildingsPlan p, double x, double y, string facing, bool mirror) => transform(p, Js.I(x), Js.I(y), facing, mirror);

    /// <summary>A plan cell's map tile for a placed building.</summary>
    public static Point planToMap(Placement b, int x, int y)
    {
        var t = transform(b.plan, x, y, b.facing, b.mirror);
        return new Point { x = b.ox + t.x, y = b.oy + t.y };
    }

    /// <summary>The lot's frontage (along its street) and depth.</summary>
    public static FrameResult frame(Lot lot)
    {
        var across = (lot.facing == "north") || (lot.facing == "south");
        return across ? new() { F = lot.w, D = lot.h } : new() { F = lot.h, D = lot.w };
    }

    /// <summary>The map tile at (u, v) in the lot's frame (u along the frontage, v back from the street).</summary>
    public static Point lotToMap(Lot lot, double u, double v)
    {
        switch (lot.facing)
        {
            case "north": return new Point { x = Js.I(lot.x + u), y = Js.I(lot.y + v) };
            case "south": return new Point { x = Js.I(lot.x + u), y = Js.I(((lot.y + lot.h) - 1) - v) };
            case "west": return new Point { x = Js.I(lot.x + v), y = Js.I(lot.y + u) };
            case "east": return new Point { x = Js.I(((lot.x + lot.w) - 1) - v), y = Js.I(lot.y + u) };
        }
        throw new InvalidOperationException("unreachable: every case returns");
    }

    /// <summary>(u, v) in the lot's frame for a map tile.</summary>
    public static MapToLotResult mapToLot(Lot lot, int x, int y)
    {
        switch (lot.facing)
        {
            case "north": return new MapToLotResult { u = x - lot.x, v = y - lot.y };
            case "south": return new MapToLotResult { u = x - lot.x, v = ((lot.y + lot.h) - 1) - y };
            case "west": return new MapToLotResult { u = y - lot.y, v = x - lot.x };
            case "east": return new MapToLotResult { u = y - lot.y, v = ((lot.x + lot.w) - 1) - x };
        }
        throw new InvalidOperationException("unreachable: every case returns");
    }
    public static MapToLotResult mapToLot(Lot lot, double x, double y) => mapToLot(lot, Js.I(x), Js.I(y));

    /// <summary>A plan set in the lot at (u0, v0): its front `v0` back from the street, `u0` along.</summary>
    public static Placement setIn(Lot lot, BuildingsPlan plan, double u0, double v0, bool mirror, string? facing = null)
    {
        facing ??= lot.facing;
        var a = lotToMap(lot, u0, v0);
        var b = lotToMap(lot, (u0 + plan.w) - 1, (v0 + plan.h) - 1);
        return new Placement { plan = plan, ox = Js.I(JsMath.min(a.x, b.x)), oy = Js.I(JsMath.min(a.y, b.y)), facing = facing, mirror = mirror };
    }

    /// <summary>Each lot's layout, kept while the lot is (a region's plan dropped takes its lots, and these, with it).
    /// Looked up only, never walked, so its order doesn't matter (rule 9).</summary>
    public static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Lot, LotLayout> layouts = new();

    public static LotLayout layoutLot(double seed, Lot lot)
    {
        if (layouts.TryGetValue(lot, out var hit)) return hit;
        var made = makeLayout(seed, lot);
        layouts.AddOrUpdate(lot, made);
        return made;
    }

    /// <summary>The commercial kinds and their shares (a cumulative roll).</summary>
    public static readonly List<(string kind, double upTo)> TRADES = [("grocery", 0.25), ("pharmacy", 0.4), ("hardware", 0.54), ("workshop", 0.6), ("bar", 0.69), ("diner", 0.75), ("station", 0.85), ("office", 1.0)];

    /// <summary>Chance a house's back yard is fenced (planks, a gate in it).</summary>
    public const double FENCE_CHANCE = 0.35;

    public static LotLayout makeLayout(double seed, Lot lot)
    {
        Func<double, double> r = (k) => Hash.hashRoll(seed, lot.x, lot.y, 0x9100 + k);
        var fr = frame(lot);
        int F = (int)fr.F, D = (int)fr.D;
        LotLayout @out = new LotLayout { buildings = [], trees = [], decor = [] };
        var marks = new string[F * D];
        Func<double, double, string> baseYard = (_, _) => "grass";
        var mirror = r(81) < 0.5;
        int along(int w, int margin) => margin + (int)Math.Floor(r(80) * Math.Max(1, F - w - 2 * margin + 1));
        void decor(double u, double v, string look) => @out.decor.push(Js.With(Js.CopyAs<LotLayoutDecorItem>(lotToMap(lot, u, v)), s => { s.look = look; }));
        switch (lot.zone)
        {
            case "residential":
                {
                    int setback = 2 + (r(90) < 0.5 ? 0 : 1);
                    // Some deep lots keep a long back yard (room for a shed): a smaller house.
                    int maxH = r(97) < 0.35 && D - setback - 6 >= 7 ? D - setback - 6 : D - setback - 2;
                    var house = Buildings.housePlan(r, new HouseOptions { maxW = F - 2, maxH = maxH, garage = true, where = "town" });
                    int u0 = along(house.w, 1);
                    var b = setIn(lot, house, u0, setback, mirror);
                    @out.buildings.push(b);
                    shutSides(lot, b, u0, F);
                    approach(lot, b, marks, F, D, r(95) < 0.5, @out);
                    int vb = setback + house.h;
                    if (r(94) < FENCE_CHANCE) fence(lot, u0, u0 + house.w - 1, vb, F, D, r(96), @out);
                    if (D - vb >= 5 && r(91) < 0.5)
                    {
                        var shed = Buildings.shedPlan((k) => r(200 + k));
                        @out.buildings.push(setIn(lot, shed, r(92) < 0.5 ? 1 : F - 1 - shed.w, D - 1 - shed.h, false));
                    }
                    var garden = D - vb >= 4 && r(93) < 0.35;
                    baseYard = (u, v) => garden && v >= D - 3 && v < D - 1 && u > 1 && u < F - 2 ? (v % 2 == 0 ? "field" : "dirt") : "grass";
                    break;
                }
            case "commercial":
                {
                    var pick = r(0);
                    string kind = TRADES.Find(t => pick < t.upTo).kind ?? "office";
                    int setback = kind == "station" ? 4 : kind == "workshop" ? 3 : 1;
                    var shop = Buildings.shopPlan(r, kind, F - 2, D - setback - 2);
                    int u0 = along(shop.w, 1);
                    var b = setIn(lot, shop, u0, setback, mirror);
                    @out.buildings.push(b);
                    shutSides(lot, b, u0, F);
                    string front = kind == "workshop" ? "concrete" : "paving";
                    baseYard = (_u, v) => v < setback ? front : "grass";
                    if (kind == "station")
                    {
                        decor(F / 3, 1, "pump");
                        decor(2 * F / 3, 1, "pump");
                    }
                    break;
                }
            case "civic":
                {
                    List<string> kinds = ["police", "clinic", "school", "church", "office"];
                    var kind = kinds[Buildings.pick(r(0), kinds.Count)];
                    var plan = kind == "church" ? Buildings.churchPlan(r, F - 4, D - 4) : Buildings.civicPlan(r, kind, F - 4, D - 4);
                    int u0 = along(plan.w, 2);
                    var b = setIn(lot, plan, u0, 2, mirror);
                    @out.buildings.push(b);
                    shutSides(lot, b, u0, F);
                    baseYard = (_, _) => "paving";
                    for (var i = 0; i < 3; i++) decor(1 + Math.Floor(r(70 + i) * (F - 2)), 0, "bench");
                    break;
                }
            case "industrial":
                {
                    var plan = Buildings.warehousePlan(r, r(2) < 0.6 ? "warehouse" : "factory", F - 2, D - 5);
                    int u0 = along(plan.w, 1);
                    var b = setIn(lot, plan, u0, 3, mirror);
                    @out.buildings.push(b);
                    shutSides(lot, b, u0, F);
                    baseYard = (_, _) => "concrete";
                    break;
                }
            case "cabin":
                {
                    var plan = Buildings.housePlan(r, new HouseOptions { maxW = F - 2, maxH = D - 4, garage = false, where = "wild" });
                    int u0 = along(plan.w, 1);
                    var b = setIn(lot, plan, u0, 2, mirror);
                    @out.buildings.push(b);
                    shutSides(lot, b, u0, F);
                    approach(lot, b, marks, F, D, r(95) < 0.6, @out);
                    break;
                }
            case "farm":
            case "field":
                {
                    baseYard = farmLayout(seed, lot, r, F, D, @out, marks);
                    break;
                }
        }
        var yardOf = baseYard;
        @out.yard = (u, v) =>
        {
            int iu = (int)u, iv = (int)v;
            return iu >= 0 && iv >= 0 && iu < F && iv < D && marks[iv * F + iu] != null ? marks[iv * F + iu] : yardOf(u, v);
        };
        return @out;
    }

    /// <summary>Leaves out windows on a side wall too near the lot's edge: the building's plan-left and plan-right sides
    /// map to the lot's low or high u by how it's turned and mirrored.</summary>
    /// <summary>A map tile's (u, v) in the lot's frame, as integers.</summary>
    public static (int u, int v) uv(Lot lot, Point m) { var l = mapToLot(lot, m.x, m.y); return ((int)l.u, (int)l.v); }

    static void shutSides(Lot lot, Placement b, int u0, int F)
    {
        var p = b.plan;
        int uL = uv(lot, planToMap(b, 0, 0)).u, uR = uv(lot, planToMap(b, p.w - 1, 0)).u;
        int low = u0, high = F - (u0 + p.w);
        Buildings.shutSides(p, uL < uR ? low : high, uL < uR ? high : low);
    }

    /// <summary>A paved path from the street to the front door, maybe a porch of boards either side of the step (and a
    /// bench on it); a concrete driveway from the street to a garage's wide door, and a car's place on it.</summary>
    static void approach(Lot lot, Placement b, string[] marks, int F, int D, bool porch, LotLayout @out)
    {
        var p = b.plan;
        var floor = new bool[F * D];
        for (int y = 0; y < p.h; y++)
            for (int x = 0; x < p.w; x++)
            {
                if (Buildings.cellAt(p, x, y) == Buildings.OUTSIDE) continue;
                var m = planToMap(b, x, y);
                var l = uv(lot, m);
                if (l.u >= 0 && l.v >= 0 && l.u < F && l.v < D) floor[l.v * F + l.u] = true;
            }
        void mark(int u, int v, string g) { if (u >= 0 && v >= 0 && u < F && v < D && !floor[v * F + u]) marks[v * F + u] = g; }
        (int u, int v) step(PlanEdge e) => uv(lot, planToMap(b, e.bx, e.by));
        if (p.front != null)
        {
            var s = step(p.front);
            for (int v = 0; v <= s.v; v++) mark(s.u, v, "paving");
            if (porch)
            {
                for (int du = -1; du <= 1; du++) mark(s.u + du, s.v, "boards");
                if (s.v > 0 && s.v - 1 >= 0) for (int du = -1; du <= 1; du++) if (du != 0) mark(s.u + du, s.v - 1, "boards");
                var bench = lotToMap(lot, s.u + 1, s.v);
                if (s.u + 1 < F && !floor[s.v * F + s.u + 1]) @out.decor.push(new LotLayoutDecorItem { x = bench.x, y = bench.y, look = "bench" });
            }
        }
        var drive = new List<(int u, int v)>();
        foreach (var e in p.edges)
        {
            if (!e.door || Buildings.cellAt(p, e.bx, e.by) != Buildings.OUTSIDE || e.by != e.ay + 1) continue;
            int room = Buildings.roomAt(p, e.ax, e.ay);
            if (room < 0 || p.rooms[room] != "garage") continue;
            drive.Add(step(e));
        }
        if (drive.Count == 0) return;
        foreach (var s in drive) for (int v = 0; v <= s.v; v++) mark(s.u, v, "concrete");
        var mid = drive[drive.Count / 2];
        @out.drive = lotToMap(lot, mid.u, Math.Max(0, mid.v - 1));
        @out.driveTo = lotToMap(lot, mid.u, Math.Max(0, mid.v - 1) - 1);
    }

    /// <summary>A back yard fenced in planks: along the lot's back and sides behind the house, and across from the
    /// house's back corners to the lot's sides, a gate left in one of those.</summary>
    static void fence(Lot lot, int uA, int uB, int vb, int F, int D, double roll, LotLayout @out)
    {
        void edge(int u, int v, int u2, int v2)
        {
            var a = lotToMap(lot, u, v);
            var b = lotToMap(lot, u2, v2);
            @out.fences.push(new PlanEdge { ax = a.x, ay = a.y, bx = b.x, by = b.y });
        }
        if (vb >= D - 1) return;
        for (int u = 0; u < F; u++) edge(u, D - 1, u, D);
        for (int v = vb; v < D; v++) { edge(0, v, -1, v); edge(F - 1, v, F, v); }
        bool gateLeft = roll < 0.5 ? uA > 0 : uB >= F - 1;
        for (int u = 0; u < uA; u++) if (!(gateLeft && u == uA - 1)) edge(u, vb - 1, u, vb);
        for (int u = uB + 1; u < F; u++) if (!(!gateLeft && u == uB + 1)) edge(u, vb - 1, u, vb);
    }

    /// <summary>A farm: the farmstead by the road (house, barn, a silo, bales, a tractor), crops behind.</summary>
    public static Func<double, double, string> farmLayout(double seed, Lot lot, Func<double, double> r, int F, int D, LotLayout @out, string[] marks)
    {
        var stead = lot.zone == "farm";
        var yardDepth = stead ? 13 : 0;
        var use = r(10);
        var rowsAlong = r(11) < 0.5;
        string crop(double u, double v)
        {
            if (v < yardDepth) return "dirt";
            if (use < 0.7) return (((rowsAlong ? v : u) % 2) == 0) ? "field" : "dirt";
            return "grass";
        }
        List<bool> sides = [r(20) < 0.6, r(21) < 0.6, r(22) < 0.6];
        for (var v = yardDepth; v < D; v++)
            for (var u = 0; u < F; u++)
            {
                var edge = (sides[0] && u == 0) || (sides[1] && u == F - 1) || (sides[2] && v == D - 1);
                var gate = v == yardDepth && Math.Abs(u - F / 2) <= 1;
                if (edge && !gate && Hash.hashRoll(seed, lot.x + u, lot.y + v, 0x9200) < 0.75) @out.trees.push(lotToMap(lot, u, v));
            }
        if (use >= 0.7 && use < 0.85)
        {
            for (var v = yardDepth + 2; v < D - 1; v += 3)
                for (var u = 2; u < F - 1; u += 3) @out.trees.push(lotToMap(lot, u, v));
        }
        else if (use >= 0.85)
        {
            for (var i = 0; i < 3; i++)
                @out.decor.push(Js.With(Js.CopyAs<LotLayoutDecorItem>(lotToMap(lot, 2 + Math.Floor(r(40 + i) * (F - 4)), yardDepth + 2 + Math.Floor(r(50 + i) * Math.Max(1, D - yardDepth - 4)))), s => { s.look = "hay"; }));
        }
        if (!stead) return crop;
        var house = Buildings.housePlan((k) => r(300 + k), new HouseOptions { maxW = 12, maxH = 10, garage = false, where = "farm" });
        var barn = Buildings.barnPlan((k) => r(400 + k), 12, 10);
        var leftHouse = r(12) < 0.5;
        Placement hp;
        if (house.w + barn.w + 4 <= F)
        {
            int hu = leftHouse ? 1 : F - 1 - house.w;
            int bu = leftHouse ? F - 1 - barn.w : 1;
            @out.buildings.push(hp = setIn(lot, house, hu, 1, r(13) < 0.5));
            @out.buildings.push(setIn(lot, barn, bu, 1, r(14) < 0.5));
        }
        else @out.buildings.push(hp = setIn(lot, house, Math.Max(1, (F - house.w) / 2), 1, r(13) < 0.5));
        approach(lot, hp, marks, F, D, true, @out);
        var mid = F / 2;
        foreach (var (du, dv) in new (int, int)[] { (0, 0), (1, 0), (0, 1), (1, 1) })
            @out.decor.push(Js.With(Js.CopyAs<LotLayoutDecorItem>(lotToMap(lot, mid - 1 + du, 2 + dv)), s => { s.look = "silo"; }));
        @out.decor.push(Js.With(Js.CopyAs<LotLayoutDecorItem>(lotToMap(lot, mid - 1, 6)), s => { s.look = "tractor"; }));
        for (var i = 0; i < 3; i++) @out.decor.push(Js.With(Js.CopyAs<LotLayoutDecorItem>(lotToMap(lot, mid - 2 + i, 9)), s => { s.look = "hay"; }));
        return crop;
    }

    /// <summary>The tile and side ("n" or "w") holding the edge between two neighbouring map tiles.</summary>
    public static (int x, int y, string side) edgeOwner(Point a, Point b)
    {
        if (b.y == a.y - 1) return (a.x, a.y, "n");
        if (b.y == a.y + 1) return (b.x, b.y, "n");
        if (b.x == a.x - 1) return (a.x, a.y, "w");
        return (b.x, b.y, "w");
    }

    /// <summary>Draws the part of a lot inside the rectangle (x0, y0)..(x1, y1) (a chunk).</summary>
    public static void drawLot(SimState state, LotContext ctx, Lot lot, int x0, int y0, int x1, int y1)
    {
        var layout = layoutLot(ctx.seed, lot);
        bool inside(double x, double y) => x >= x0 && y >= y0 && x <= x1 && y <= y1;
        for (var y = Math.Max(y0, lot.y); y <= Math.Min(y1, lot.y + lot.h - 1); y++)
            for (var x = Math.Max(x0, lot.x); x <= Math.Min(x1, lot.x + lot.w - 1); x++)
            {
                var l = mapToLot(lot, x, y);
                Worldgen.ground(state, x, y, layout.yard(l.u, l.v));
            }
        var covered = new JsSet<string>();
        layout.buildings.forEach((b, bi) => drawBuilding(state, ctx, lot, b, bi, inside, covered));
        foreach (var e in layout.fences)
        {
            var (ox, oy, side) = edgeOwner(new Point(e.ax, e.ay), new Point(e.bx, e.by));
            if (inside(ox, oy) && Grids.inBounds(state.grid, ox, oy) && Grids.edgeKind(state.grid, Grids.edgeIndex(state.grid, ox, oy, side)) == Grids.EDGE_NONE)
                Grids.setEdge(state.grid, ox, oy, side, Grids.EDGE_BARRICADE);
        }
        foreach (var t in layout.trees)
            if (inside(t.x, t.y) && !covered.has($"{Js.Str(t.x)},{Js.Str(t.y)}")) FurnitureMod.addFurniture(state, "tree", t.x, t.y);
        foreach (var d in layout.decor)
            if (inside(d.x, d.y) && !covered.has($"{Js.Str(d.x)},{Js.Str(d.y)}") && !Js.T(FurnitureMod.furnitureAt(state, d.x, d.y)))
                FurnitureMod.addFurniture(state, "decor", d.x, d.y).look = d.look;
        parkCar(state, ctx, lot, layout, inside);
    }
    public static void drawLot(SimState state, LotContext ctx, Lot lot, double x0, double y0, double x1, double y1) => drawLot(state, ctx, lot, Js.I(x0), Js.I(y0), Js.I(x1), Js.I(y1));

    /// <summary>Of the lots in town (houses, shops, civic, industry), this many have a car parked: on the driveway where
    /// there is one, else in the street in front.</summary>
    public const double CAR_CHANCE = 0.6;

    /// <summary>A car on the lot's driveway, or in the street in front of a lot in town, some of the time (by hash): a
    /// little fuel, some wear. Only the chunk holding its tile parks it, so it's parked once.</summary>
    public static void parkCar(SimState state, LotContext ctx, Lot lot, LotLayout layout, Func<double, double, bool> inside)
    {
        if (lot.zone != "residential" && lot.zone != "commercial" && lot.zone != "civic" && lot.zone != "industrial") return;
        double r(double k) => Hash.hashRoll(ctx.seed, lot.x, lot.y, 0xca5 + k);
        if (r(0) >= CAR_CHANCE) return;
        Point p, next;
        if (layout.drive != null)
        {
            p = layout.drive;
            next = layout.driveTo;
        }
        else
        {
            var w = frame(lot).F;
            var u = 1 + Math.Floor(r(1) * Math.Max(1, w - 2));
            p = lotToMap(lot, u, -2);
            next = lotToMap(lot, u + 1, -2);
        }
        // Along the street or the drive, either way round; only where the whole car fits (its body, not its tile).
        var heading = Vehicles.headingOf(next.x - p.x, next.y - p.y) + (r(2) < 0.5 ? 0 : 16);
        if (!inside(p.x, p.y) || !Vehicles.parkable(state, p, heading) || Js.T(FurnitureMod.furnitureAt(state, p.x, p.y))) return;
        Vehicles.addVehicle(state, p.x, p.y, heading, Math.Floor(r(3) * 0.6 * Vehicles.FUEL_MAX), Math.Floor((0.4 + r(4) * 0.6) * Vehicles.VEHICLE_HEALTH));
    }

    /// <summary>Of the houses beyond the start's calm, this many are boarded up: every window boarded, the doors shut.</summary>
    public const double BOARDED_CHANCE = 0.06;

    public static void drawBuilding(SimState state, LotContext ctx, Lot lot, Placement b, double bi, Func<double, double, bool> inside, JsSet<string> covered)
    {
        var plan = b.plan;
        double r(double k) => Hash.hashRoll(ctx.seed, lot.x, lot.y, 0x9300 + bi * 1000 + k);
        var decay = Difficulty.ruinDecay(state);
        var dist = ctx.distance(b.ox, b.oy);
        bool boarded = plan.kind == "house" && dist >= ctx.calm && r(500) < BOARDED_CHANCE;
        List<Point> floors = [];
        for (var y = 0; y < plan.h; y++)
            for (var x = 0; x < plan.w; x++)
            {
                if (Buildings.cellAt(plan, x, y) != Buildings.FLOOR) continue;
                var w = planToMap(b, x, y);
                covered.add($"{Js.Str(w.x)},{Js.Str(w.y)}");
                floors.push(w);
                if (!inside(w.x, w.y)) continue;
                Worldgen.ground(state, w.x, w.y, plan.ground[y * plan.w + x] ?? "boards");
                Grids.setTile(state.grid, w.x, w.y, "floor");
            }
        foreach (var e in plan.edges)
        {
            var (ox, oy, side) = edgeOwner(planToMap(b, e.ax, e.ay), planToMap(b, e.bx, e.by));
            if (!inside(ox, oy)) continue;
            var fall = Hash.hashRoll(ctx.seed, ox, oy, side == "n" ? 23 : 29);
            var outer = Buildings.cellAt(plan, e.bx, e.by) == Buildings.OUTSIDE;
            if (e.window == true)
            {
                Grids.setEdge(state.grid, ox, oy, side, boarded ? Grids.EDGE_WINDOW : fall < 0.3 ? Grids.EDGE_WINDOW_BROKEN : Grids.EDGE_WINDOW);
                if (boarded || fall > 0.92) state.grid.edgeBoarded.push(Grids.edgeIndex(state.grid, ox, oy, side));
                continue;
            }
            if (e.door)
            {
                if (fall < 0.08 && !boarded) continue;
                Grids.setEdge(state.grid, ox, oy, side, Grids.EDGE_DOOR);
                if (fall > 0.8 && !boarded) state.grid.edgeOpen.push(Grids.edgeIndex(state.grid, ox, oy, side));
            }
            else if (fall > (outer ? 0.08 : 0.04) + decay)
                Grids.setEdge(state.grid, ox, oy, side, Grids.EDGE_WALL);
        }
        plan.furniture.forEach((f, i) =>
        {
            var w = planToMap(b, f.x, f.y);
            if (!inside(w.x, w.y)) return;
            if (Buildings.isContainerLook(f.look))
            {
                var c = FurnitureMod.addFurniture(state, "crate", w.x, w.y);
                c.look = f.look;
                c.table = f.table;
                var n = JsMath.min(Js.At(FurnitureMod.LOOK_CAPACITY, f.look, double.PositiveInfinity), 1 + Math.Floor(r(100 + i) * Math.Max(1, Math.Floor(ctx.lootAt(w.x, w.y) / 3))));
                var searched = ctx.distance(w.x, w.y) < 80 && r(200 + i) < 0.25;
                c.loot = searched ? 0 : Difficulty.crateLoot(state, n) * (Climate.isRadioactive(state, w.x, w.y) ? 2 : 1);
            }
            else
            {
                var d_2 = FurnitureMod.addFurniture(state, "decor", w.x, w.y);
                d_2.look = f.look;
                d_2.loot = Js.Q(Js.Get(TimelineMod.TANK_WATER, f.look), 0);
            }
        });
        if (dist < ctx.calm || floors.length == 0) return;
        var sleepers = Difficulty.zombieScale(state, Math.Floor(r(300) * (plan.sleepers + 1 + Math.Floor(dist / 60))));
        for (var k = 0; k < sleepers; k++)
        {
            var p = floors[(int)Math.Floor(r(310 + k) * floors.length)];
            if (inside(p.x, p.y)) Dormant.add(state, new() { id = $"d{Js.Str(state.nextId++)}", x = p.x, y = p.y });
        }
    }
}
