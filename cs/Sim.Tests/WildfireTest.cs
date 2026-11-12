// Wildfire (FireMod, 2026-11-10): fire through grass, undergrowth and trees; what stops it; its bounds; its saves.
// Hand-made maps are all grass ground, so the tests set the ground they mean (roads, forest) and the season (summer).
#nullable disable warnings
using System.Diagnostics;
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class WildfireTest
{

    /// <summary>A summer's day (day 7, noon: the day's weather was rolled at the tick before, so a test sets its own).</summary>
    public static readonly int SUMMER = (6 * 4800) + 1200;

    public static SimState setup(List<string> map, Func<SimState, JsRecord<double>> priorities = null)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 9, map = map, rooms = new() });
        state.tick = SUMMER;
        state.weather = "heatwave"; // fire runs in a heatwave; a clear summer day is the edge of it (dryness is tested on its own)
        foreach (var s in state.survivors)
        {
            s.priorities = Js.RecordFrom(JobTypes.NO_JOBS, __r0 => { if (priorities != null) Js.MergeInto(__r0, priorities(state)); });
        }
        return state;
    }

    /// <summary>Rows of a map `w` wide and `h` high, all floor, with a survivor in the top left corner.</summary>
    public static List<string> open(int w, int h)
    {
        var rows = new List<string>();
        for (var y = 0; y < h; y++) rows.Add((y == 0 ? "s" : ".") + new string('.', w - 1));
        return rows;
    }

    public static void setGround(SimState state, int x, int y, string ground)
    {
        Grids.setGround(state.grid, x, y, Grids.GROUNDS.IndexOf(ground));
    }

    public static void run(SimState state, double n) => TestKit.run(state, n);

    public static bool scarredAt(SimState state, int x, int y) => state.burnt.ContainsKey($"{x},{y}");

    public static int scarsIn(SimState state, int x0, int x1, int y0 = 0, int y1 = 1000)
    {
        var n = 0;
        foreach (var k in state.burnt.Keys)
        {
            var p = k.Split(',');
            int x = int.Parse(p[0]), y = int.Parse(p[1]);
            if ((x >= x0) && (x <= x1) && (y >= y0) && (y <= y1)) n++;
        }
        return n;
    }

    /// <summary>wildfire</summary>
    public class D001_wildfire
    {

        [Fact(DisplayName = "wildfire > fire crosses dry grass and stops at a road")]
        public void T002_fire_crosses_dry_grass_and_stops_at_a_road()
        {
            var state = setup(open(30, 7));
            for (var y = 0; y < 7; y++) { setGround(state, 14, y, "road"); setGround(state, 15, y, "road"); }
            FireMod.ignite(state, 3, 3);
            run(state, 1200);
            expect(state.fires).toHaveLength(0);
            // The grass between the start and the road burnt (most of it: a fire is a chance, not a flood fill)...
            expect(scarsIn(state, 4, 13, 1, 6)).toBeGreaterThan(40);
            // ...and nothing on or past the road did.
            expect(scarsIn(state, 14, 29)).toBe(0);
        }

        [Fact(DisplayName = "wildfire > fire stops at water")]
        public void T003_fire_stops_at_water()
        {
            var map = open(30, 7).Select((r, y) => r.Substring(0, 14) + "%" + r.Substring(15)).ToList();
            var state = setup(map);
            FireMod.ignite(state, 3, 3);
            run(state, 1200);
            expect(state.fires).toHaveLength(0);
            expect(scarsIn(state, 4, 13, 1, 6)).toBeGreaterThan(40);
            expect(scarsIn(state, 14, 29)).toBe(0);
        }

        [Fact(DisplayName = "wildfire > rain puts a grass fire out; the same fire in the dry spreads")]
        public void T004_rain_puts_a_grass_fire_out_the_same_fire_in_the_dry_spreads()
        {
            var dry = setup(open(30, 7));
            FireMod.ignite(dry, 15, 3);
            run(dry, 100);
            expect(dry.burnt.Count).toBeGreaterThan(8);
            var wet = setup(open(30, 7));
            wet.weather = "rain";
            FireMod.ignite(wet, 15, 3);
            run(wet, 30);
            expect(wet.fires).toHaveLength(0);
            expect(wet.burnt.Count).toBeLessThanOrEqual(2);
        }

        [Fact(DisplayName = "wildfire > snow and a spring green-up barely let grass burn")]
        public void T005_snow_and_a_spring_green_up_barely_let_grass_burn()
        {
            var snow = setup(open(30, 7));
            snow.weather = "snow";
            FireMod.ignite(snow, 15, 3);
            run(snow, 400);
            expect(snow.burnt.Count).toBeLessThanOrEqual(2);
            var spring = setup(open(30, 7));
            spring.tick = 1200;
            var summer = setup(open(30, 7));
            spring.weather = summer.weather = "clear";
            expect(FireMod.dryness(spring)).toBeLessThan(FireMod.dryness(summer) * 0.5);
            expect(FireMod.dryness(summer)).toBeGreaterThan(0.39);
            expect(FireMod.dryness(summer)).toBeLessThan(0.41);
            summer.weather = "heatwave";
            expect(FireMod.dryness(summer)).toBeGreaterThan(0.85);
            summer.weather = "rain";
            expect(FireMod.dryness(summer)).toBeLessThan(0.05);
            // Nights are damper than days.
            summer.weather = "clear";
            var noon = FireMod.dryness(summer);
            summer.tick = SUMMER - 1200 - (200 * 8);
            expect(FireMod.dryness(summer)).toBeLessThan(noon * 0.6);
        }

        [Fact(DisplayName = "wildfire > a wood burns and leaves burnt ground, scarred where trees stood")]
        public void T006_a_wood_burns_and_leaves_burnt_ground()
        {
            var rows = open(24, 16).Select((r, y) => new string(r.Select((c, x) => ((c == 's') || ((x * 7 + y * 13) % 10 >= 3)) ? c : 'Y').ToArray())).ToList();
            var state = setup(rows);
            for (var y = 0; y < 16; y++) for (var x = 0; x < 24; x++) setGround(state, x, y, "forest");
            var trees = state.furniture.filter((f) => f.kind == "tree").map((f) => (f.x, f.y)).ToList();
            expect(trees.Count).toBeGreaterThan(80);
            FireMod.ignite(state, trees[40].x, trees[40].y);
            run(state, 3000);
            expect(state.fires).toHaveLength(0);
            var left = state.furniture.filter((f) => f.kind == "tree").length;
            expect(left).toBeLessThan(trees.Count / 2);
            var charred = trees.Count((t) => FireMod.scarOf(state, t.x, t.y, out _, out var kind) && (kind == FireMod.SCAR_TREE));
            expect(charred).toBeGreaterThan(trees.Count / 2);
            // The scar says when.
            FireMod.scarOf(state, trees[40].x, trees[40].y, out var tick, out _);
            expect(tick).toBeGreaterThanOrEqual(SUMMER);
            // Burnt ground doesn't burn again; a tree left standing does, a bare scar does not.
            expect(FireMod.fuelAt(state, trees[40].x, trees[40].y)).toBe(FireMod.Fuel.None);
            // Not every tree went: gaps in the fire, as well as a burn.
            expect(state.events.some((e) => e.message.includes("tree") || e.message.includes("grass"))).toBe(true);
        }

        [Fact(DisplayName = "wildfire > burnt grass doesn't burn again until it has regrown; old scars are forgotten")]
        public void T007_burnt_grass_regrows()
        {
            var state = setup(open(12, 5));
            expect(FireMod.fuelAt(state, 5, 2)).toBe(FireMod.Fuel.Grass);
            FireMod.ignite(state, 5, 2);
            run(state, FireMod.GRASS_BURN + 5);
            expect(scarredAt(state, 5, 2)).toBe(true);
            expect(FireMod.fuelAt(state, 5, 2)).toBe(FireMod.Fuel.None);
            state.tick += (FireMod.SCAR_DAYS + 1) * Time.DAY_LENGTH;
            expect(FireMod.fuelAt(state, 5, 2)).toBe(FireMod.Fuel.Grass);
            // The daily sweep drops it from the record.
            state.tick = ((state.tick / Time.DAY_LENGTH) + 1) * Time.DAY_LENGTH;
            Tick.tick(state);
            expect(state.burnt.Count).toBe(0);
        }

        [Fact(DisplayName = "wildfire > the world's wildfire setting: 0 keeps vegetation from burning, as before")]
        public void T008_wildfire_off()
        {
            var state = setup(open(30, 7));
            state.settings.wildfire = 0;
            FireMod.ignite(state, 15, 3);
            run(state, 600);
            expect(state.fires).toHaveLength(0);
            expect(state.burnt.Count).toBeLessThanOrEqual(1);
            // A lone fire on ground that doesn't burn is the old bare-ground fire.
            expect(FireMod.fuelAt(state, 14, 3)).toBe(FireMod.Fuel.None);
        }

        [Fact(DisplayName = "wildfire > a burning building spills into the yard, but its own floor stays unburnt")]
        public void T009_a_burning_building_spills_into_the_yard()
        {
            var state = setup(["s.........", ".#####....", ".#...#....", ".#...#....", ".#####....", ".........."]);
            expect(FireMod.fuelAt(state, 3, 2)).toBe(FireMod.Fuel.None);
            FireMod.ignite(state, 5, 2);
            run(state, 200);
            expect(state.events.some((e) => e.message.includes("spread outdoors"))).toBe(true);
            expect(scarredAt(state, 6, 2)).toBe(true);
            expect(scarredAt(state, 3, 2)).toBe(false);
        }

        [Fact(DisplayName = "wildfire > a fuelled brazier out in the dry throws a spark; indoors, unfuelled or in winter it does not")]
        public void T010_a_brazier_throws_a_spark()
        {
            // The chance is small (a few in a thousand an hour): scale it up so one hour tells the story.
            var out_ = setup(["s....", ".....", "..X..", ".....", "....."]);
            out_.settings.wildfire = 200;
            Js.NN(Js.Get(FurnitureMod.ofKind(out_, "heater"), 0)).stored = 3;
            run(out_, 400);
            expect(out_.events.some((e) => e.message.includes("A spark from the brazier"))).toBe(true);
            var empty = setup(["s....", ".....", "..X..", ".....", "....."]);
            empty.settings.wildfire = 200;
            run(empty, 400);
            expect(empty.events.some((e) => e.message.includes("spark"))).toBe(false);
            var winter = setup(["s....", ".....", "..X..", ".....", "....."]);
            winter.settings.wildfire = 200;
            winter.tick = 15 * 4800;
            Js.NN(Js.Get(FurnitureMod.ofKind(winter, "heater"), 0)).stored = 3;
            winter.weather = "snow";
            run(winter, 400);
            expect(winter.events.some((e) => e.message.includes("spark"))).toBe(false);
        }

        [Fact(DisplayName = "wildfire > a thousand burning tiles: the checks per tick stay at the budget and the fire count at its cap")]
        public void T011_a_thousand_burning_tiles_stay_within_the_budget()
        {
            var state = setup(open(64, 40));
            var lit = 0;
            for (var y = 1; (y < 40) && (lit < 1200); y++)
                for (var x = 2; (x < 62) && (lit < 1200); x++)
                    if (FireMod.ignite(state, x, y)) lit++;
            expect(state.fires.length).toBeGreaterThan(1000);
            var sw = Stopwatch.StartNew();
            var worstMs = 0.0;
            var worstChecks = 0;
            var worstGrowth = 0;
            for (var t = 0; t < 40; t++)
            {
                var before = state.fires.length;
                var one = Stopwatch.StartNew();
                Tick.tick(state);
                worstMs = Math.Max(worstMs, one.Elapsed.TotalMilliseconds);
                worstChecks = Math.Max(worstChecks, FireMod.lastSpreadChecks);
                worstGrowth = Math.Max(worstGrowth, state.fires.length - before);
                expect(state.fires.length).toBeLessThanOrEqual(FireMod.MAX_FIRES);
            }
            Console.Error.WriteLine($"[wildfire] {lit} lit, 40 ticks in {sw.Elapsed.TotalMilliseconds:0} ms; worst tick {worstMs:0.00} ms, worst checks {worstChecks}, worst growth {worstGrowth}");
            expect(worstChecks).toBeLessThanOrEqual(FireMod.SPREAD_BUDGET);
            // Each check lights at most its four neighbours.
            expect(worstGrowth).toBeLessThanOrEqual(FireMod.SPREAD_BUDGET * 4);
        }

        [Fact(DisplayName = "wildfire > the fire system's own cost at 1500 burning tiles, and with the fire far off")]
        public void T011b_the_cost_of_a_large_fire()
        {
            double Measure(SimState state, int ticks, out double worst)
            {
                var sw = new Stopwatch();
                worst = 0;
                var total = 0.0;
                for (var t = 0; t < ticks; t++)
                {
                    state.tick++;
                    sw.Restart();
                    FireMod.updateFires(state);
                    var ms = sw.Elapsed.TotalMilliseconds;
                    total += ms;
                    worst = Math.Max(worst, ms);
                }
                return total / ticks;
            }
            var near = setup(open(64, 40));
            var lit = 0;
            for (var y = 1; (y < 40) && (lit < 1500); y++)
                for (var x = 2; (x < 62) && (lit < 1500); x++)
                    if (FireMod.ignite(near, x, y)) lit++;
            // Burning grass goes out in 40 ticks: keep it lit by topping up each tick (the cost is what's measured, not the burn).
            Measure(near, 20, out _);
            var refill = 0.0;
            for (var t = 0; t < 100; t++)
            {
                near.tick++;
                foreach (var f in near.fires) f.left = 30;
                for (var y = 1; (y < 40) && (near.fires.length < 1500); y++) for (var x = 2; (x < 62) && (near.fires.length < 1500); x++) FireMod.ignite(near, x, y);
                var sw = Stopwatch.StartNew();
                FireMod.updateFires(near);
                refill += sw.Elapsed.TotalMilliseconds;
            }
            var farState = setup(open(300, 5));
            for (var x = 150; x < 250; x++) for (var y = 1; y < 5; y++) FireMod.ignite(farState, x, y);
            foreach (var f in farState.fires) f.left = 100000;
            var farMean = Measure(farState, 100, out var farWorst);
            Console.Error.WriteLine($"[wildfire] {near.fires.length} burning near: {refill / 100:0.000} ms a tick of the fire system; {farState.fires.length} burning far: {farMean:0.000} ms mean, {farWorst:0.000} ms worst");
            expect(refill / 100).toBeLessThan(5);
            expect(farMean).toBeLessThan(1);
        }

        [Fact(DisplayName = "wildfire > a fire far from everyone doesn't spread or get stepped, and burns itself out in batches")]
        public void T012_a_far_fire_ends_without_being_stepped()
        {
            var state = setup(open(110, 5));
            FireMod.ignite(state, 100, 2);
            // A tree is the long one: three batches.
            FurnitureMod.addFurniture(state, "tree", 101, 2);
            FireMod.ignite(state, 101, 2);
            var worstFires = 0;
            for (var t = 0; t < 500; t++)
            {
                Tick.tick(state);
                worstFires = Math.Max(worstFires, state.fires.length);
                expect(FireMod.lastNearFires).toBe(0);
                expect(FireMod.lastSpreadChecks).toBe(0);
                // The grass goes at the first batch; the tree, which burns longer, on the third (tick 29200).
                if (state.tick == SUMMER + 150) expect(state.fires).toHaveLength(1);
            }
            // It never spread, and it's gone: burnt where it stood.
            expect(worstFires).toBeLessThanOrEqual(2);
            expect(state.fires).toHaveLength(0);
            expect(scarredAt(state, 100, 2)).toBe(true);
            expect(state.furniture.some((f) => f.kind == "tree")).toBe(false);
            expect(state.burnt.Count).toBe(2);
        }

        [Fact(DisplayName = "wildfire > a thousand burning buildings, all on one beat: the budget still holds")]
        public void T012b_a_thousand_burning_walls_on_one_beat()
        {
            var rows = new List<string>();
            for (var y = 0; y < 40; y++) rows.Add(y == 0 ? "s" + new string('#', 63) : new string('#', 64));
            var state = setup(rows);
            var lit = 0;
            for (var y = 1; (y < 40) && (lit < 1200); y++)
                for (var x = 2; (x < 62) && (lit < 1200); x++)
                    if (FireMod.ignite(state, x, y)) lit++;
            expect(state.fires.length).toBeGreaterThan(1000);
            for (var t = 0; t < 45; t++)
            {
                Tick.tick(state);
                expect(FireMod.lastSpreadChecks).toBeLessThanOrEqual(FireMod.SPREAD_BUDGET);
            }
        }

        [Fact(DisplayName = "wildfire > a save taken in the middle of a fire loads and goes on the same way")]
        public void T013_a_save_taken_mid_fire_goes_on_identically()
        {
            var state = setup(open(40, 12));
            FireMod.ignite(state, 20, 6);
            run(state, 70);
            expect(state.fires.length).toBeGreaterThan(5);
            expect(state.burnt.Count).toBeGreaterThan(0);
            var loaded = Savefile.loadFile(Savefile.toSaveFile(state));
            expect(loaded.ok).toBe(true);
            expect(loaded.state.fires.length).toBe(state.fires.length);
            expect(loaded.state.burnt.Count).toBe(state.burnt.Count);
            expect(loaded.state.settings.wildfire).toBe(1);
            for (var t = 0; t < 300; t++)
            {
                Tick.tick(state);
                Tick.tick(loaded.state);
                expect(Js.Json_stringify(Js.ObjFrom(loaded.state, ("commandLog", (object?)(Js.Arr([])))))).toBe(Js.Json_stringify(Js.ObjFrom(state, ("commandLog", (object?)(Js.Arr([]))))));
            }
        }

        [Fact(DisplayName = "wildfire > a save from before wildfire loads: no scars, wildfire on")]
        public void T014_an_older_save_loads()
        {
            var state = setup(open(10, 4));
            var text = Savefile.toSaveFile(state);
            var node = System.Text.Json.Nodes.JsonNode.Parse(text);
            var s = node["state"].AsObject();
            s.Remove("burnt");
            s["settings"].AsObject().Remove("wildfire");
            var result = Savefile.loadFile(node.ToJsonString());
            expect(result.ok).toBe(true);
            expect(result.state.burnt.Count).toBe(0);
            expect(result.state.settings.wildfire).toBe(1);
        }

        [Fact(DisplayName = "wildfire > firefighters defend a house from a spreading grass fire, and nobody gets stuck")]
        public void T015_firefighters_work_a_spreading_fire_without_getting_stuck()
        {
            List<string> map = ["....................", "....................", "..#######...........", "..#.....#...........", "..#..s..#...........", "..#.....#...........", "..###D###...........", "....................", ".s.s................", "...................."];
            var state = setup(map, (_) => new() { ["firefight"] = 3 });
            // Grass fire in the yard, beside the wall.
            FireMod.ignite(state, 10, 4);
            FireMod.ignite(state, 10, 3);
            FireMod.ignite(state, 10, 5);
            var idle = new Dictionary<string, int>();
            var since = new Dictionary<string, (string job, int tick)>();
            var sawJob = false;
            for (var t = 0; t < 2500; t++)
            {
                Tick.tick(state);
                expect(Invariants.checkInvariants(state), DebugText.dumpText(state)).toEqual(Js.Arr([]));
                foreach (var s in state.survivors)
                {
                    sawJob |= (s.note != null) && s.note.StartsWith("put out");
                    var isIdle = (s.jobId == null) && (s.goal == null) && (s.move == null) && (s.sleep == null) && (s.meal == null) && (s.arming == null) && (s.treatment == null) && (s.breakdown == null) && (s.forage == null) && (s.drink == null) && (s.reading == null);
                    var doable = isIdle && (JobChoice.chooseJob(state, s) != null);
                    idle[s.id] = doable ? idle.GetValueOrDefault(s.id) + 1 : 0;
                    expect(idle[s.id], $"{s.name} idle with doable work at tick {state.tick}: job {Js.Json_stringify(JobChoice.chooseJob(state, s)?.job)} note {s.note} leisure {Js.Json_stringify(s.leisure)} seek {s.nextSeekTick} fires {state.fires.length}\n{DebugText.dumpText(state)}").toBeLessThanOrEqual(InvariantsTest.MAX_IDLE_WITH_DOABLE_JOB);
                    if (s.jobId == null) since.Remove(s.id);
                    else if (!since.TryGetValue(s.id, out var cur) || (cur.job != s.jobId)) since[s.id] = (s.jobId, state.tick);
                    else expect(state.tick - cur.tick, $"{s.name} on one job too long\n{DebugText.dumpText(state)}").toBeLessThanOrEqual(InvariantsTest.MAX_TICKS_ON_ONE_JOB);
                }
            }
            expect(state.fires).toHaveLength(0);
            expect(sawJob).toBe(true);
        }
    }
}
