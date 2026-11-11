// Ported from tests/towns.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class TownsTest
{

    public static Func<double, double> rolls(double n)
    {
        return (double k) => Hash.hashRoll(n, k, 24301.0);
    }

    /// <summary>Every plan of every kind, for many rolls.</summary>
    public static List<BuildingsPlan> allPlans()
    {
        List<BuildingsPlan> @out = [];
        for (var n = 0; n < 60; n++)
        {
            var r = rolls(n);
            foreach (var (mw, mh) in new (double, double)[] { (10, 8), (11, 9), (12, 10), (13, 11), (13, 9) })
            {
                @out.push(Buildings.housePlan(r, new HouseOptions { maxW = mw, maxH = mh, garage = true, where = "town" }));
            }
            @out.pushAll([Buildings.housePlan(r, new HouseOptions { maxW = 12, maxH = 10, where = "farm" }), Buildings.housePlan(r, new HouseOptions { maxW = 11, maxH = 9, where = "wild" }), Buildings.housePlan(r, new HouseOptions { maxW = 9, maxH = 9, where = "wild" })]);
            foreach (var kind in Js.Iter((List<string>)["grocery", "pharmacy", "hardware", "workshop", "bar", "diner", "station", "office"]))
            {
                @out.push(Buildings.shopPlan(r, kind, 10 + n % 4, 9 + n % 3));
            }
            foreach (var kind in Js.Iter((List<string>)["police", "clinic", "school", "office"]))
            {
                @out.push(Buildings.civicPlan(r, kind, 16 + n % 7, 14 + n % 7));
            }
            @out.pushAll([Buildings.churchPlan(r, 16 + n % 7, 14 + n % 7), Buildings.warehousePlan(r, "warehouse", 20 + n % 7, 13 + n % 7), Buildings.warehousePlan(r, "factory", 20 + n % 7, 13 + n % 7), Buildings.barnPlan(r, 12, 10), Buildings.shedPlan(r)]);
        }
        return @out;
    }

    /// <summary>The plan's edge between two cells side by side, if any.</summary>
    public static PlanEdge? edgeOf(BuildingsPlan p, int ax, int ay, int bx, int by)
    {
        return p.edges.find((e) => ((((e.ax == ax) && (e.ay == ay)) && (e.bx == bx)) && (e.by == by)) || ((((e.ax == bx) && (e.ay == by)) && (e.bx == ax)) && (e.by == ay)));
    }
    public static PlanEdge? edgeOf(BuildingsPlan p, double ax, double ay, double bx, double by) => edgeOf(p, Js.I(ax), Js.I(ay), Js.I(bx), Js.I(by));

    /// <summary>Floor reachable from outside the plan, in through its outer door edges, crossing no edge wall.</summary>
    public static JsSet<double> reachable(BuildingsPlan p)
    {
        var seen = new JsSet<double>();
        List<List<double>> queue = [];
        foreach (var e in Js.Iter(p.edges))
        {
            if (!e.door || (Buildings.cellAt(p, e.bx, e.by) != Buildings.OUTSIDE))
            {
                continue;
            }
            var i = ((double)(e.ay) * (p.w)) + e.ax;
            if (!seen.has(i))
            {
                seen.add(i);
                queue.push([e.ax, e.ay]);
            }
        }
        while (Js.T(queue.length))
        {
            var (x, y) = Js.NN(queue.shift());
            foreach (var (dx, dy) in new (double, double)[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var c = Buildings.cellAt(p, x + dx, y + dy);
                var i = (((double)((y + dy)) * (p.w)) + x) + dx;
                var edge = edgeOf(p, x, y, x + dx, y + dy);
                if (((c == Buildings.FLOOR) && !seen.has(i)) && (!Js.T(edge) || edge.door))
                {
                    seen.add(i);
                    queue.push([x + dx, y + dy]);
                }
            }
        }
        return seen;
    }

    /// <summary>building plans</summary>
    public class D001_building_plans
    {

        public static readonly List<BuildingsPlan> plans = allPlans();

        [Fact(DisplayName = "building plans > are walled all round: an edge wall or door wherever floor meets the outside; only floor and outside cells")]
        public void T002_are_walled_all_round_an_edge_wall_or_door_wherever_floor_mee()
        {
            foreach (var p in Js.Iter(plans))
            {
                expect(p.cells.filter((c) => (c != Buildings.FLOOR) && (c != Buildings.OUTSIDE)), p.kind).toHaveLength(0);
                for (var y = 0; y < p.h; y++)
                {
                    for (var x = 0; x < p.w; x++)
                    {
                        if (Buildings.cellAt(p, x, y) != Buildings.FLOOR)
                        {
                            continue;
                        }
                        foreach (var (dx, dy) in new (double, double)[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            if (Buildings.cellAt(p, x + dx, y + dy) == Buildings.OUTSIDE)
                            {
                                expect(edgeOf(p, x, y, x + dx, y + dy), $"{Js.Str(p.kind)} ({Js.Str(x)}, {Js.Str(y)})").toBeDefined();
                            }
                        }
                    }
                }
            }
        }

        [Fact(DisplayName = "building plans > every room can be walked into from a door to the outside")]
        public void T003_every_room_can_be_walked_into_from_a_door_to_the_outside()
        {
            foreach (var p in Js.Iter(plans))
            {
                var seen = reachable(p);
                for (var i = 0; i < p.cells.length; i++)
                {
                    if (Js.Get(p.cells, i) == Buildings.FLOOR)
                    {
                        expect(seen.has(i), $"{Js.Str(p.kind)} {Js.Str(p.w)}x{Js.Str(p.h)} cell {Js.Str(i % p.w)},{Js.Str(JsMath.floor(((double)(i) / (p.w))))}").toBe(true);
                    }
                }
            }
        }

        [Fact(DisplayName = "building plans > furniture stands on floor, one to a tile; containers carry a loot table, decor none")]
        public void T004_furniture_stands_on_floor_one_to_a_tile_containers_carry_a_l()
        {
            foreach (var p in Js.Iter(plans))
            {
                var at = new JsSet<string>();
                foreach (var f in Js.Iter(p.furniture))
                {
                    expect(Buildings.cellAt(p, f.x, f.y)).toBe(Buildings.FLOOR);
                    expect(at.has($"{Js.Str(f.x)},{Js.Str(f.y)}")).toBe(false);
                    at.add($"{Js.Str(f.x)},{Js.Str(f.y)}");
                    expect(f.table != null).toBe(Buildings.isContainerLook(f.look));
                }
            }
        }

        [Fact(DisplayName = "building plans > a house has a kitchen, a bathroom and a bedroom, and fits its lot")]
        public void T005_a_house_has_a_kitchen_a_bathroom_and_a_bedroom_and_fits_its_()
        {
            for (var n = 0; n < 60; n++)
            {
                var p = Buildings.housePlan(rolls(n), new HouseOptions { maxW = 13, maxH = 12, garage = true });
                expect(p.w).toBeLessThanOrEqual(13);
                expect(p.h).toBeLessThanOrEqual(12);
                var looks = p.furniture.map((f) => f.look);
                foreach (var look in Js.Iter((List<string>)["icebox", "toilet", "bed", "sofa"]))
                {
                    expect(looks, $"house {Js.Str(n)}").toContain(look);
                }
                expect(p.edges.filter((e) => !e.door).length).toBeGreaterThan(0);
            }
        }

        [Fact(DisplayName = "building plans > each room is fitted out for what it is: a kitchen sink and bin, a bathroom sink, bedside tables")]
        public void T006_each_room_is_fitted_out_for_what_it_is_a_kitchen_sink_and_bi()
        {
            double kitchenSinks = 0;
            double bins = 0;
            double bathSinks = 0;
            double nightstands = 0;
            double washing = 0;
            for (var n = 0; n < 60; n++)
            {
                var p = Buildings.housePlan(rolls(n), new HouseOptions { maxW = 13, maxH = 12, garage = true });
                kitchenSinks += p.furniture.filter((f) => (f.look == "sink") && (f.table == "kitchen")).length;
                bathSinks += p.furniture.filter((f) => (f.look == "sink") && (f.table == "bathroom")).length;
                bins += p.furniture.filter((f) => (f.look == "bin") && (f.table == "trash")).length;
                nightstands += p.furniture.filter((f) => (f.look == "nightstand") && (f.table == "bedroom")).length;
                washing += p.furniture.some((f) => (f.look == "bath") || (f.look == "shower")) ? 1 : 0;
            }
            foreach (var (what, count) in new (string, double)[] { ("kitchen sinks", kitchenSinks), ("bins", bins), ("bathroom sinks", bathSinks), ("nightstands", nightstands), ("a bath or shower", washing) })
            {
                expect(count, what).toBeGreaterThan(40);
            }
        }

        [Fact(DisplayName = "building plans > turns to face any street: a one-to-one map onto the turned footprint")]
        public void T007_turns_to_face_any_street_a_one_to_one_map_onto_the_turned_fo()
        {
            var p = Buildings.housePlan(rolls(3), new HouseOptions { maxW = 13, maxH = 12, garage = true });
            foreach (var facing in Js.Iter((List<string>)["north", "south", "east", "west"]))
            {
                foreach (var mirror in Js.Iter([false, true]))
                {
                    var seen = new JsSet<string>();
                    for (var y = 0; y < p.h; y++)
                    {
                        for (var x = 0; x < p.w; x++)
                        {
                            var t = Lots.transform(p, x, y, facing, mirror);
                            var across = (facing == "north") || (facing == "south");
                            expect(t.x).toBeGreaterThanOrEqual(0);
                            expect(t.x).toBeLessThan(Js.T(across) ? p.w : p.h);
                            expect(t.y).toBeLessThan(Js.T(across) ? p.h : p.w);
                            seen.add($"{Js.Str(t.x)},{Js.Str(t.y)}");
                        }
                    }
                    expect(seen.size).toBe(((double)(p.w) * (p.h)));
                }
            }
        }
    }

    /// <summary>loot by room</summary>
    public class D008_loot_by_room
    {

        [Fact(DisplayName = "loot by room > every table sums to 1, and a container gives only what its table holds")]
        public void T009_every_table_sums_to_1_and_a_container_gives_only_what_its_ta()
        {
            foreach (var (name, table) in Js.Iter(Js.ObjectEntries(Loot.LOOT_TABLES)))
            {
                expect(table.reduce((a, __p0) =>
        {
            var (_, w) = __p0;
            return a + w;
        }, 0.0), name).toBeCloseTo(1, 6);
            }
            var kinds = Js.Set(Loot.LOOT_TABLES["pharmacy"].map((__p1) =>
        {
            var k = __p1.Item1;
            return k;
        }));
            for (var i = 0; i < 100; i++)
            {
                expect(kinds.has(Work.scavengeFind(((double)(i) / (100)), "pharmacy"))).toBe(true);
            }
        }
    }

    /// <summary>towns</summary>
    public class D010_towns
    {

        [Fact(DisplayName = "towns > the plan draws in chunks the size worldgen uses")]
        public void T011_the_plan_draws_in_chunks_the_size_worldgen_uses()
        {
            expect(Worldplan.PLAN_CHUNK).toBe(Worldgen.CHUNK);
        }

        public static readonly List<(string, WorldPlan)> plans = new List<List<double>> { new List<double> { 1, 256 }, new List<double> { 7, 256 }, new List<double> { 5, 512 }, new List<double> { 3, 128 } }.map((__p2) =>
        {
            var (seed, size) = __p2;
            return ($"seed {Js.Str(seed)} size {Js.Str(size)}", Worldplan.planWorld(Js.NN(seed), Js.NN(size), "mainland"));
        });

        [Fact(DisplayName = "towns > towns stand on dry land, apart from each other and clear of the start")]
        public void T012_towns_stand_on_dry_land_apart_from_each_other_and_clear_of_t()
        {
            foreach (var (name, p) in Js.Iter(plans))
            {
                expect(p.towns.length, name).toBeGreaterThan(0);
                var n = p.nature;
                foreach (var t in Js.Iter(p.towns))
                {
                    expect(NatureMod.isWater(n, t.x, t.y), name).toBe(false);
                    expect(JsMath.sqrt((JsMath.pow((t.x - n.start.x), 2)) + (JsMath.pow((t.y - n.start.y), 2))), name).toBeGreaterThan(t.radius + NatureMod.START_CLEAR_RADIUS);
                    foreach (var o in Js.Iter(p.towns))
                    {
                        if (o != t)
                        {
                            expect(JsMath.sqrt((JsMath.pow((t.x - o.x), 2)) + (JsMath.pow((t.y - o.y), 2))), name).toBeGreaterThan(t.radius + o.radius);
                        }
                    }
                }
            }
        }

        /// <summary>Tiles reachable along roads and streets from a point.</summary>
        public static Uint8Array alongRoads(WorldPlan p, Point from)
        {
            var size = p.nature.size;
            var seen = new Uint8Array(((double)(size) * (size)));
            List<double> queue = [((double)(from.y) * (size)) + from.x];
            seen[Js.Idx(Js.NN(Js.Get(queue, 0)))] = 1;
            while (Js.T(queue.length))
            {
                var i = Js.NN(queue.pop());
                var x = i % size;
                var y = JsMath.floor(((double)(i) / (size)));
                foreach (var (dx, dy) in new (double, double)[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) })
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if ((((nx < 0) || (ny < 0)) || (nx >= size)) || (ny >= size))
                    {
                        continue;
                    }
                    var j = ((double)(ny) * (size)) + nx;
                    if (!Js.T(Js.Get(seen, j)) && (Js.Get(p.roadMask, j) != 0))
                    {
                        seen[Js.Idx(j)] = 1;
                        queue.push(j);
                    }
                }
            }
            return seen;
        }

        [Fact(DisplayName = "towns > a street is a parking strip and two lanes of asphalt between sidewalks; a country road is two lanes")]
        public void T012b_streets_and_roads_carry_two_lanes()
        {
            expect(Towns.CARRIAGEWAY).toBeGreaterThanOrEqual(6); // two lanes of 3 m: a pickup is 1.9 m across
            expect(Towns.STREET).toBe(Towns.PARKING + Towns.CARRIAGEWAY);
            foreach (var (name, p) in Js.Iter(plans))
            {
                var size = p.nature.size;
                double at(double x, double y) => ((((x < 0) || (y < 0)) || (x >= size)) || (y >= size)) ? 0 : Js.Get(p.roadMask, ((double)(y) * (size)) + x);
                double walks = 0, walked = 0;
                foreach (var s in Js.Iter(p.streets))
                {
                    // Across the street, at every tile along it.
                    for (var k = 0; k <= (s.eastWest ? (s.x1 - s.x0) : (s.y1 - s.y0)); k++)
                    {
                        double cell(double o) => s.eastWest ? at(s.x0 + k, s.y0 + o) : at(s.x0 + o, s.y0 + k);
                        for (var o = 0; o < Towns.STREET; o++)
                        {
                            expect(cell(o), $"{Js.Str(name)}: street {Js.Str(Js.Json_stringify(s))} at {k}, {o} across").toBe(Worldplan.STREET);
                        }
                        // Beside it: sidewalk (or the asphalt of a street or road joining it; water at a bank).
                        foreach (var o in new[] { -Towns.SIDEWALK, -1, Towns.STREET, (Towns.STREET + Towns.SIDEWALK) - 1 })
                        {
                            walks++;
                            if (cell(o) != 0) walked++;
                        }
                    }
                }
                if (walks > 0) expect(walked / walks, $"{Js.Str(name)}: sidewalks").toBeGreaterThan(0.97);
                // Country roads: the tiles of road across the line at each point outside the streets (a quarter tile a step).
                double narrow = 0, points = 0;
                foreach (var line in Js.Iter(p.roads))
                {
                    for (var i = 2; (i + 3) < line.length; i++)
                    {
                        var a = Js.NN(Js.Get(line, i));
                        var b = Js.NN(Js.Get(line, i + 1));
                        double dx = b.x - a.x, dy = b.y - a.y, len = JsMath.sqrt((dx * dx) + (dy * dy));
                        if ((len < 0.000001) || (at(JsMath.floor(a.x), JsMath.floor(a.y)) != Worldplan.ROAD)) continue;
                        double w = 0;
                        foreach (var sign in new[] { -1, 1 })
                        {
                            for (var d = (sign > 0) ? 0 : 0.25; d < 12; d += 0.25)
                            {
                                if (at(JsMath.floor(a.x - (((dy / len) * d) * sign)), JsMath.floor(a.y + (((dx / len) * d) * sign))) == 0) break;
                                w += 0.25;
                            }
                        }
                        points++;
                        if (w < 4.5) narrow++;
                        expect(w, $"{Js.Str(name)}: road at {a.x},{a.y}").toBeGreaterThanOrEqual(4);
                    }
                }
                expect(points, name).toBeGreaterThan(20);
                expect(narrow / points, $"{Js.Str(name)}: road points under 4.5 tiles across").toBeLessThan(0.05);
            }
        }

        [Fact(DisplayName = "towns > a town's streets all join up, and country roads link every town")]
        public void T013_a_town_s_streets_all_join_up_and_country_roads_link_every_to()
        {
            foreach (var (name, p) in Js.Iter(plans))
            {
                var size = p.nature.size;
                // From the first town's centre square (Towns.anchor: its own point unless that is in a pond).
                var seen = alongRoads(p, Towns.anchor(p.nature, Js.NN(Js.Get(p.towns, 0))));
                foreach (var s in Js.Iter(p.streets))
                {
                    expect(Js.Missing(seen, ((double)(s.y0) * (size)) + s.x0) ? null : (object?)(Js.Get(seen, ((double)(s.y0) * (size)) + s.x0)), $"{Js.Str(name)}: street {Js.Str(Js.Json_stringify(s))}").toBe(1);
                }
                foreach (var t in Js.Iter(p.towns))
                {
                    var c = Towns.anchor(p.nature, t);
                    expect(c != null, $"{Js.Str(name)}: town at {Js.Str(t.x)},{Js.Str(t.y)} has a centre square").toBe(true);
                    expect(Js.Missing(seen, ((double)(c.y) * (size)) + c.x) ? null : (object?)(Js.Get(seen, ((double)(c.y) * (size)) + c.x)), $"{Js.Str(name)}: town at {Js.Str(t.x)},{Js.Str(t.y)}").toBe(1);
                }
            }
        }

        [Fact(DisplayName = "towns > lots sit on dry land off the roads and each other, and every building and its apron fit inside its lot")]
        public void T014_lots_sit_on_dry_land_off_the_roads_and_each_other_and_every_()
        {
            foreach (var (name, p) in Js.Iter(plans))
            {
                var size = p.nature.size;
                var zones = Js.Set(p.lots.map((l) => l.zone));
                // (A 128 map's one town, between the water and the map's edge, has room for a few houses and no more now
                // that a street with its sidewalks is twelve tiles across. Likewise a 256 map's cabins, since its towns grew by
                // a third: they stand in the wild cells between, and a small map has few.)
                foreach (var z in Js.Iter(size <= 128 ? (List<string>)["residential"] : size <= 256 ? (List<string>)["residential", "commercial", "field"] : (List<string>)["residential", "commercial", "field", "cabin"]))
                {
                    expect(zones.has(z), $"{Js.Str(name)}: {Js.Str(z)}").toBe(true);
                }
                foreach (var lot in Js.Iter(p.lots))
                {
                    for (var y = lot.y; y < (lot.y + lot.h); y++)
                    {
                        for (var x = lot.x; x < (lot.x + lot.w); x++)
                        {
                            expect(Js.Missing(p.lotMask, ((double)(y) * (size)) + x) ? null : (object?)(Js.Get(p.lotMask, ((double)(y) * (size)) + x)), name).toBe(lot.id + 1);
                            expect(Js.Missing(p.roadMask, ((double)(y) * (size)) + x) ? null : (object?)(Js.Get(p.roadMask, ((double)(y) * (size)) + x)), name).toBe(0);
                            expect(NatureMod.isWater(p.nature, x, y), name).toBe(false);
                        }
                    }
                    foreach (var b in Js.Iter(Lots.layoutLot(p.nature.seed, lot).buildings))
                    {
                        var across = (b.facing == "north") || (b.facing == "south");
                        var __c3 = Js.T(across); var w = __c3 ? b.plan.w : b.plan.h; var h = __c3 ? b.plan.h : b.plan.w;
                        var msg = $"{Js.Str(name)}: {Js.Str(b.plan.kind)} {Js.Str(w)}x{Js.Str(h)} at {Js.Str(b.ox)},{Js.Str(b.oy)} in {Js.Str(lot.zone)} lot {Js.Str(Js.Json_stringify(lot))}";
                        // The footprint and the one-tile apron round it (Heights levels both) inside the lot.
                        expect(b.ox - 1, msg).toBeGreaterThanOrEqual(lot.x);
                        expect(b.oy - 1, msg).toBeGreaterThanOrEqual(lot.y);
                        expect(b.ox + w + 1, msg).toBeLessThanOrEqual(lot.x + lot.w);
                        expect(b.oy + h + 1, msg).toBeLessThanOrEqual(lot.y + lot.h);
                    }
                }
            }
        }

        [Fact(DisplayName = "towns > every container in a generated world can be walked to from a road")]
        public void T015_every_container_in_a_generated_world_can_be_walked_to_from_a()
        {
            foreach (var seed in Js.Iter((List<double>)[1, 7]))
            {
                var size = 256;
                var state = Worldgen.createWorld(seed, size);
                for (var y = 0; y < size; y += Worldgen.CHUNK)
                {
                    for (var x = 0; x < size; x += Worldgen.CHUNK)
                    {
                        Worldgen.ensureGeneratedAround(state, new Point { x = Js.I(x), y = Js.I(y) });
                    }
                }
                var road = Grids.GROUNDS.indexOf("road");
                var seen = new Uint8Array(((double)(size) * (size)));
                List<double> queue = [];
                for (var i = 0; i < ((double)(size) * (size)); i++)
                {
                    if ((Grids.groundAt(state.grid, i) == road) && (Grids.tileAt(state.grid, i) == "floor"))
                    {
                        seen[Js.Idx(i)] = 1;
                        queue.push(i);
                    }
                }
                while (Js.T(queue.length))
                {
                    var i = Js.NN(queue.pop());
                    var x = i % size;
                    var y = JsMath.floor(((double)(i) / (size)));
                    foreach (var (dx, dy) in new (double, double)[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if ((((nx < 0) || (ny < 0)) || (nx >= size)) || (ny >= size))
                        {
                            continue;
                        }
                        var j = Grids.tileIndex(state.grid, nx, ny);
                        if (!Js.T(Js.Get(seen, j)) && Pathfinding.isPassable(state.grid, nx, ny, "survivor"))
                        {
                            seen[Js.Idx(j)] = 1;
                            queue.push(j);
                        }
                    }
                }
                var containers = state.furniture.filter((f) => (f.kind == "crate") && (f.table != null));
                expect(containers.length).toBeGreaterThan(20);
                var cut = containers.filter((f) => !Js.T(Js.Get(seen, Grids.tileIndex(state.grid, f.x, f.y))));
                foreach (var f in Js.Iter(cut))
                {
                    var nearWater = new List<List<double>> { new List<double> { 1, 0 }, new List<double> { -1, 0 }, new List<double> { 0, 1 }, new List<double> { 0, -1 } }.some((__p4) =>
        {
            var (dx, dy) = __p4;
            return Grids.getTile(state.grid, f.x + ((double)(Js.NN(dx)) * (2)), f.y + ((double)(Js.NN(dy)) * (2))) == "water";
        });
                    expect(Js.T(nearWater) || (cut.length <= ((double)(containers.length) * (0.02))), $"seed {Js.Str(seed)}: ({Js.Str(f.x)}, {Js.Str(f.y)}) {Js.Str(f.look)} cut off").toBe(true);
                }
            }
        }
    }
}
