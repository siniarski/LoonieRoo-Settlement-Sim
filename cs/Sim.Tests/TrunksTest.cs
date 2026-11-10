// Tree trunks are solid to free movers (Trunks.cs, Motion.slide with the state), to tile walkers' bodies
// (Motion.syncStep with the state) and to cars (Vehicles.move).
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class TrunksTest
{
    static readonly double REACH = Motion.BODY_RADIUS + Trunks.RADIUS;

    static double clearance(Point at, Point trunk) => JsMath.sqrt(Motion.distance2(at, trunk)) - REACH;

    /// <summary>Walk a body from `from` by (dx, dy) per tick for n ticks, sliding; the closest it came to the trunk (negative: inside it).</summary>
    static (Point at, double closest) walk(SimState state, Point from, int dx, int dy, int n, Point trunk)
    {
        var at = from;
        var closest = double.PositiveInfinity;
        for (var i = 0; i < n; i++)
        {
            at = Motion.slide(state, at, dx, dy, Pathfinding.FREE);
            closest = JsMath.min(closest, clearance(at, trunk));
        }
        return (at, closest);
    }

    [Fact(DisplayName = "trunks > the trunk stands within JITTER of its tile's centre, the same every time")]
    public void within_jitter()
    {
        for (var x = -20; x < 20; x++)
            for (var y = 0; y < 20; y++)
            {
                var c = Trunks.trunkAt(x, y);
                expect(JsMath.abs(c.x - Motion.centre(new Point { x = x, y = y }).x)).toBeLessThanOrEqual(Trunks.JITTER);
                expect(JsMath.abs(c.y - Motion.centre(new Point { x = x, y = y }).y)).toBeLessThanOrEqual(Trunks.JITTER);
                expect(Trunks.trunkAt(x, y).x).toBe(c.x);
            }
    }

    [Fact(DisplayName = "trunks > walking straight into a tree never enters the trunk, and a glancing walk slides round it and on")]
    public void walk_into_a_tree()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [".......", ".......", "...Y...", ".......", "......."] });
        var trunk = Trunks.trunkAt(3, 2);
        // Dead on at the trunk's centre, from two tiles west.
        var (at, closest) = walk(state, new Point { x = trunk.x - 512, y = trunk.y }, 20, 0, 60, trunk);
        expect(closest).toBeGreaterThanOrEqual(-1);
        // A glancing walk, a third of the way off its centre: round it and out the far side, near the line it was on.
        (at, closest) = walk(state, new Point { x = trunk.x - 512, y = trunk.y + 40 }, 20, 0, 60, trunk);
        expect(closest).toBeGreaterThanOrEqual(-1);
        expect(at.x).toBeGreaterThan(trunk.x + 256);
        expect(JsMath.abs(at.y - trunk.y)).toBeLessThanOrEqual(REACH + 2);
        // Without the state (walls only) it walks straight through, as before.
        var through = Motion.slide(state.grid, new Point { x = trunk.x - 512, y = trunk.y }, 1024, 0, Pathfinding.FREE);
        expect(through.y).toBe(trunk.y);
    }

    [Fact(DisplayName = "trunks > you pass between two trees with a tile between them")]
    public void between_two_trees()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [".....", ".....", ".Y.Y.", ".....", "....."] });
        var start = Motion.centre(new Point { x = 2, y = 4 });
        var at = start;
        for (var i = 0; i < 60; i++)
        {
            at = Motion.slide(state, at, 0, -20, Pathfinding.FREE);
        }
        expect(Motion.tileOf(at).y).toBe(0);
        expect(clearance(at, Trunks.trunkAt(1, 2))).toBeGreaterThanOrEqual(-1);
    }

    [Fact(DisplayName = "trunks > you, steering into a tree, stop at its trunk; with the tree felled you walk on")]
    public void you_steer_into_a_tree()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s..Y...", "......."] });
        var you = Js.NN(Js.Get(state.survivors, 0));
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
        you.at = new Point { x = 128, y = Trunks.trunkAt(3, 0).y };
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
        var trunk = Trunks.trunkAt(3, 0);
        var closest = double.PositiveInfinity;
        for (var t = 0; t < 60; t++)
        {
            Tick.tick(state);
            closest = JsMath.min(closest, clearance(you.at, trunk));
        }
        expect(closest).toBeGreaterThanOrEqual(-1);
        FurnitureMod.removeFurniture(state, Js.NN(FurnitureMod.furnitureAt(state, 3, 0)));
        for (var t = 0; t < 80; t++)
        {
            Tick.tick(state);
        }
        expect(you.pos.x).toBeGreaterThanOrEqual(4);
    }

    [Fact(DisplayName = "trunks > a zombie walking its path through a wood gets where it's going")]
    public void a_zombie_through_a_wood()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [
            "s.........",
            ".Y.Y.Y.Y..",
            "..Y.Y.Y.Y.",
            ".Y.Y.Y.Y..",
            "..Y.Y.Y.Y.",
            "..........",
        ] });
        var you = Js.NN(Js.Get(state.survivors, 0));
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
        Commands.applyCommand(state, new Command { type = "spawnZombie", x = 9, y = 5 });
        var z = Js.NN(Js.Get(state.zombies, 0));
        z.kind = "static";
        var full = you.health;
        for (var t = 0; t < 600 && you.health >= full; t++)
        {
            Tick.tick(state);
            foreach (var f in Js.Iter(state.furniture))
                if (f.kind == "tree") expect(clearance(z.at, Trunks.trunkAt(f.x, f.y))).toBeGreaterThanOrEqual(-1);
        }
        expect(you.health).toBeLessThan(full);
    }

    // ---- Tile walkers: colonists, survivors on tile paths (the tile path stays; `at` goes round the trunk) ----

    static void quiet(SimState state)
    {
        foreach (var s in Js.Iter(state.survivors))
            foreach (var k in Js.Iter(Js.ObjectKeys(s.priorities)))
                s.priorities[Js.Idx(k)] = 0;
    }

    static double bodyClearance(SimState state, Body b)
    {
        var closest = double.PositiveInfinity;
        foreach (var f in Js.Iter(state.furniture))
            if (f.kind == "tree") closest = JsMath.min(closest, clearance(b.at, Trunks.trunkAt(f.x, f.y)));
        return closest;
    }

    /// <summary>Order the first survivor to (tx, ty) and run until they're there and still. Returns (arrival tick, how far `at` strayed off the row's line, whether they stood on a tile with a tree, the nearest they came to a trunk).</summary>
    static (int arrived, double strayed, bool crossed, double closest) walkOrder(List<string> map, int tx, int ty)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map });
        quiet(state);
        var s = Js.NN(Js.Get(state.survivors, 0));
        var row = s.pos.y * Motion.SUB + Motion.SUB / 2;
        Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = s.id, x = tx, y = ty });
        double strayed = 0, closest = double.PositiveInfinity;
        var crossed = false;
        for (var t = 0; t < 200; t++)
        {
            Tick.tick(state);
            strayed = JsMath.max(strayed, JsMath.abs(s.at.y - row));
            closest = JsMath.min(closest, bodyClearance(state, s));
            if (Trunks.treeAt(state, s.pos.x, s.pos.y)) crossed = true;
            if (s.pos.x == tx && s.pos.y == ty && s.move == null) return (t, strayed, crossed, closest);
        }
        return (-1, strayed, crossed, closest);
    }

    [Fact(DisplayName = "trunks > a colonist's tile path through a wood never puts their body in a trunk, and they arrive when they would have")]
    public void a_colonist_walks_round_trunks()
    {
        var withTrees = walkOrder([".........", ".........", "s..Y.Y...", ".........", "........."], 8, 2);
        var without = walkOrder([".........", ".........", "s........", ".........", "........."], 8, 2);
        expect(withTrees.arrived).toBeGreaterThan(0);
        expect(withTrees.arrived).toBe(without.arrived);
        expect(withTrees.crossed).toBe(true);
        expect(withTrees.closest).toBeGreaterThanOrEqual(-1);
        expect(withTrees.strayed).toBeGreaterThan(40);   // really went round, not along the line
        expect(without.strayed).toBe(0);
        // The same with the walk ending on a tree's tile: they stop beside the trunk, not in it.
        var onTree = walkOrder([".........", ".........", "s..Y.....", ".........", "........."], 3, 2);
        expect(onTree.arrived).toBeGreaterThan(0);
        expect(onTree.closest).toBeGreaterThanOrEqual(-1);
    }

    [Fact(DisplayName = "trunks > a colonist standing on a tree's tile is beside the trunk, and with the tree felled they stand on the tile's centre")]
    public void a_colonist_standing_on_a_tree_tile()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["......", "..s...", "......"] });
        quiet(state);
        var s = Js.NN(Js.Get(state.survivors, 0));
        var tree = FurnitureMod.addFurniture(state, "tree", 2, 1);
        Tick.tick(state);
        expect(bodyClearance(state, s)).toBeGreaterThanOrEqual(-1);
        expect(Motion.distance2(s.at, Motion.centre(s.pos))).toBeGreaterThan(0);
        FurnitureMod.removeFurniture(state, tree);
        Tick.tick(state);
        expect(s.at.x).toBe(Motion.centre(s.pos).x);
        expect(s.at.y).toBe(Motion.centre(s.pos).y);
    }

    [Fact(DisplayName = "trunks > a hauling job through trees completes, and the hauler never walks through a trunk")]
    public void hauling_through_trees()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [
            "............",
            "s.Y..w..Y...",
            "............",
        ] });
        foreach (var s in Js.Iter(state.survivors)) Js.Object_assign(s.priorities, new JsRecord<double> { ["clean"] = 0 });
        Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(11), y = Js.I(1), structure = "wall" });
        var hauler = Js.NN(Js.Get(state.survivors, 0));
        var closest = double.PositiveInfinity;
        Ported.JobsTest.runUntil(state, (st) =>
        {
            closest = JsMath.min(closest, bodyClearance(st, hauler));
            return st.blueprints.length == 0;
        });
        expect(Grids.getTile(state.grid, 11, 1)).toBe("wall");
        expect(closest).toBeGreaterThanOrEqual(-1);
    }

    [Fact(DisplayName = "trunks > in a wooded scenario nobody gets stuck (the stuck-state invariants hold) and no colonist is in a trunk")]
    public void stuck_state_invariants_in_a_wood()
    {
        // A wood (a tree where a hash says so), three colonists, wood to haul, a wall to build.
        var map = new List<string>();
        for (var y = 0; y < 18; y++)
        {
            var row = "";
            for (var x = 0; x < 30; x++)
            {
                if (x == 2 && y == 2) row += "s";
                else if (x == 4 && y == 3) row += "s";
                else if (x == 3 && y == 5) row += "s";
                else if (x == 8 && y == 4) row += "w";
                else if (x == 9 && y == 6) row += "w";
                else if (Trunks.hash(x, y, 77) % 100 < 22) row += "Y";
                else row += ".";
            }
            map.Add(row);
        }
        var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = map });
        foreach (var x in new[] { 15, 16, 17 })
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(x), y = Js.I(8), structure = "wall" });
        var jobSince = new JsMap<string, double>();
        var jobId = new JsMap<string, string>();
        var idle = new JsMap<string, double>();
        var inTrunk = 0;
        for (var t = 0; t < 1500; t++)
        {
            Tick.tick(state);
            expect(Invariants.checkInvariants(state), DebugText.dumpText(state)).toEqual(Js.Arr([]));
            foreach (var s in Js.Iter(state.survivors))
            {
                if (bodyClearance(state, s) < -1) inTrunk++;
                if (!Js.T(s.jobId)) jobId.delete(s.id);
                else if (jobId.get(s.id) != s.jobId) { jobId.set(s.id, s.jobId); jobSince.set(s.id, state.tick); }
                else expect(state.tick - jobSince.get(s.id), $"{s.name} on one job too long\n{DebugText.dumpText(state)}").toBeLessThanOrEqual(Ported.InvariantsTest.MAX_TICKS_ON_ONE_JOB);
                var idleNow = !Js.T(s.jobId) && !Js.T(s.goal) && !Js.T(s.move) && !Js.T(s.sleep) && !Js.T(s.meal) && !Js.T(s.arming) && !Js.T(s.treatment) && !Js.T(s.breakdown);
                var ticks = (idleNow && Js.T(JobChoice.chooseJob(state, s))) ? (Js.Q(idle.get(s.id), 0)) + 1 : 0;
                idle.set(s.id, ticks);
                expect(ticks, $"{s.name} idle with doable work\n{DebugText.dumpText(state)}").toBeLessThanOrEqual(Ported.InvariantsTest.MAX_IDLE_WITH_DOABLE_JOB);
            }
        }
        expect(inTrunk).toBe(0);
        expect(state.blueprints.length).toBe(0);
    }

    [Fact(DisplayName = "trunks > a zombie chasing a colonist through trees still reaches them, standing on a tree's tile or not")]
    public void a_zombie_reaches_a_colonist_in_a_wood()
    {
        foreach (var onTree in new[] { false, true })
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [
                "s.........",
                ".Y.Y.Y.Y..",
                "..Y.Y.Y.Y.",
                ".Y.Y.Y.Y..",
                "..Y.Y.Y.Y.",
                "..........",
            ] });
            quiet(state);
            var s = Js.NN(Js.Get(state.survivors, 0));
            // Somewhere in the middle of the wood: on a tree's tile, or on a clear one beside it.
            Motion.place(s, onTree ? new Point { x = 4, y = 2 } : new Point { x = 5, y = 2 });
            Commands.applyCommand(state, new Command { type = "spawnZombie", x = 9, y = 5 });
            var z = Js.NN(Js.Get(state.zombies, 0));
            z.kind = "static";
            var full = s.health;
            for (var t = 0; t < 800 && s.health >= full; t++)
            {
                Tick.tick(state);
                expect(clearance(z.at, Trunks.trunkAt(4, 2))).toBeGreaterThanOrEqual(-1);
            }
            expect(s.health, $"onTree={onTree}\n{DebugText.dumpText(state)}").toBeLessThan(full);
        }
    }

    // ---- Cars ----

    static (SimState state, Survivor s, Vehicle car) carField(int treeX)
    {
        var map = new List<string>();
        for (var y = 0; y < 7; y++) map.Add(y == 3 ? "..s" + new string('.', 27) : new string('.', 30));
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map });
        var s = Js.NN(Js.Get(state.survivors, 0));
        quiet(state);
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        if (treeX >= 0) FurnitureMod.addFurniture(state, "tree", treeX, 3);
        var car = Vehicles.addVehicle(state, 3, 3);
        Commands.applyCommand(state, new Command { type = "use", x = Js.I(3), y = Js.I(3) });
        for (var i = 0; i < Vehicles.GET_IN_TICKS; i++) Tick.tick(state);
        return (state, s, car);
    }

    [Fact(DisplayName = "trunks > a car driven at a tree stops against the trunk, and can reverse away")]
    public void a_car_stops_at_a_tree()
    {
        var (state, s, car) = carField(12);
        expect(Vehicles.vehicleOf(state, s)).toBe(car);
        var trunk = Trunks.trunkAt(12, 3);
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = -1 });
        var closest = double.PositiveInfinity;
        for (var t = 0; t < 120; t++)
        {
            Tick.tick(state);
            closest = JsMath.min(closest, JsMath.sqrt(Motion.distance2(car.at, trunk)) - (Vehicles.VEHICLE_RADIUS + Trunks.RADIUS));
        }
        expect(closest).toBeGreaterThanOrEqual(-1);   // the circle never entered the trunk
        expect(car.at.x).toBeLessThan(trunk.x);       // and it didn't get past the tree
        expect(car.speed).toBe(0);
        // Reverse away.
        var stopped = car.at.x;
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 1 });
        for (var t = 0; t < 30; t++) Tick.tick(state);
        expect(car.at.x).toBeLessThan(stopped - Motion.SUB / 2);
        expect(car.speed).toBeLessThan(0);
    }

    [Fact(DisplayName = "trunks > a car passes a tree two tiles off its line")]
    public void a_car_goes_past_clear_trees()
    {
        var (state, s, car) = carField(-1);
        FurnitureMod.addFurniture(state, "tree", 12, 1);
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = -1 });
        for (var t = 0; t < 120; t++) Tick.tick(state);
        expect(car.pos.x).toBeGreaterThan(14);
    }
}
