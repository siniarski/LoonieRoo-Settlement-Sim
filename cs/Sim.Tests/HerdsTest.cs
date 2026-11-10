// Ported from tests/herds.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class HerdsTest
{

    public static double runUntil(SimState state, Func<SimState, bool> done, double max = 2000)
    {
        for (var t = 0; t < max; t++)
        {
            if (Js.T(done(state)))
            {
                return t;
            }
            Tick.tick(state);
            expect(Invariants.checkInvariants(state), DebugText.dumpText(state)).toEqual(Js.Arr([]));
        }
        throw new Exception($"condition not met after {Js.Str(max)} ticks\n{Js.Str(DebugText.dumpText(state))}");
    }

    /// <summary>Open ground, one survivor at the left, doing nothing. Midday, so no night arrivals.</summary>
    public static SimState field(int width = 80, int height = 9)
    {
        var map = Js.ArrayFrom(height, (int y) => (y == 4) ? ("s" + ".".repeat(width - 1)) : ".".repeat(width));
        var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = map });
        Js.NN(Js.Get(state.survivors, 0)).priorities = new(JobTypes.NO_JOBS) {  };
        state.tick = Js.I(((double)(6) * (Time.TICKS_PER_HOUR)));
        return state;
    }
    public static SimState field(double width, double height = 9) => field(Js.I(width), Js.I(height));

    /// <summary>dormant zombies</summary>
    public class D001_dormant_zombies
    {

        [Fact(DisplayName = "dormant zombies > stay as data until a survivor comes within ..., then get up")]
        public void T002_stay_as_data_until_a_survivor_comes_within_then_get_up()
        {
            var state = field();
            state.dormant.push(new() { id = "d1", x = Js.I(30), y = Js.I(4) });
            for (var i = 0; i < 50; i++)
            {
                Tick.tick(state);
            }
            expect(state.zombies).toEqual(Js.Arr([]));
            Js.NN(Js.Get(state.survivors, 0)).pos = new Point { x = Js.I(30 - Herds.WAKE_RADIUS), y = Js.I(4) };
            for (var i = 0; i < 10; i++)
            {
                Tick.tick(state);
            }
            expect(state.dormant).toEqual(Js.Arr([]));
            expect(state.zombies).toMatchObject(Js.Arr([Js.Obj(("kind", (object?)("static")))]));
        }

        [Fact(DisplayName = "dormant zombies > once awake, only go for survivors they see or hear, and stay out in the day")]
        public void T003_once_awake_only_go_for_survivors_they_see_or_hear_and_stay_o()
        {
            var state = field();
            var z = Zombies.addZombie(state, 60, 4, 200, "static");
            for (var i = 0; i < 60; i++)
            {
                Tick.tick(state);
            }
            expect(z.targetId).toBeNull();
            Js.NN(Js.Get(state.survivors, 0)).pos = new Point { x = Js.I((z.pos.x - Sight.ZOMBIE_SIGHT) - 4), y = Js.I(z.pos.y) };
            for (var i = 0; i < 20; i++)
            {
                Tick.tick(state);
            }
            expect(z.targetId).toBeNull();
            Js.NN(Js.Get(state.survivors, 0)).pos = new Point { x = Js.I((z.pos.x - Sight.ZOMBIE_SIGHT) + 2), y = Js.I(z.pos.y) };
            for (var i = 0; i < 20; i++)
            {
                Tick.tick(state);
            }
            expect(z.targetId).toBe(Js.NN(Js.Get(state.survivors, 0)).id);
        }

        [Fact(DisplayName = "dormant zombies > a new world sleeps zombies in ruins, none near the start")]
        public void T004_a_new_world_sleeps_zombies_in_ruins_none_near_the_start()
        {
            var world = Worldgen.createWorld(7, 128);
            expect(world.dormant.length).toBeGreaterThan(0);
            var start = Js.NN(Js.Get(world.survivors, 0)).pos;
            foreach (var d in Js.Iter(world.dormant))
            {
                expect(JsMath.hypot(d.x - start.x, d.y - start.y)).toBeGreaterThan(NatureMod.START_CLEAR_RADIUS + 16);
            }
        }

        [Fact(DisplayName = "dormant zombies > a new world has the dead up and about round the start from the first tick, outside its calm")]
        public void T005_a_new_world_has_strays_awake_round_the_start()
        {
            for (var seed = 1; seed <= 4; seed++)
            {
                var world = Worldgen.createWorld(seed, 128);
                var start = Js.NN(Js.Get(world.survivors, 0)).pos;
                expect(world.zombies.length).toBe(Difficulty.zombieScale(world, Worldgen.START_STRAYS));
                foreach (var z in Js.Iter(world.zombies))
                {
                    expect(z.kind).toBe("static");
                    var r = JsMath.sqrt(JsMath.pow(z.pos.x - start.x, 2) + JsMath.pow(z.pos.y - start.y, 2));
                    expect(r).toBeGreaterThanOrEqual(Worldgen.START_STRAY_NEAR);
                    expect(r).toBeLessThan(Worldgen.START_STRAY_FAR + 12);
                }
                var harder = Worldgen.createWorld(seed, 128, Js.Get(Difficulty.PRESETS, "apocalypse").settings);
                expect(harder.zombies.length).toBeGreaterThan(world.zombies.length);
            }
        }

        [Fact(DisplayName = "dormant zombies > the dead lie out in the open too (roads, fields), drawn the same whatever order chunks come in")]
        public void T006_strays_in_the_open_are_stateless()
        {
            var a = Worldgen.createWorld(3, 128);
            var start = Js.NN(Js.Get(a.survivors, 0)).pos;
            var l = Worldgen.layout(3, 128);
            var open = a.dormant.filter((d) => !Worldgen.onLot(l, d.x, d.y));
            expect(open.length).toBeGreaterThan(0);
            foreach (var d in Js.Iter(open))
            {
                expect(JsMath.sqrt(JsMath.pow(d.x - start.x, 2) + JsMath.pow(d.y - start.y, 2))).toBeGreaterThanOrEqual(Worldgen.STRAY_CALM);
            }
            // The same chunk drawn into another world, alone: the same strays in it.
            var b = States.createState(new NewStateOptions { seed = 3, width = 128, height = 128 });
            b.settings = Js.CopyAs<Settings>(a.settings);
            var cx = JsMath.floor((double)(open[0].x) / Worldgen.CHUNK);
            var cy = JsMath.floor((double)(open[0].y) / Worldgen.CHUNK);
            Worldgen.generateChunk(b, l, cx, cy);
            string key(DormantZombie d) => $"{d.x},{d.y}";
            bool inChunk(DormantZombie d) => (JsMath.floor((double)(d.x) / Worldgen.CHUNK) == cx) && (JsMath.floor((double)(d.y) / Worldgen.CHUNK) == cy) && !Worldgen.onLot(l, d.x, d.y);
            expect(b.dormant.filter(inChunk).map(key).join(" ")).toBe(a.dormant.filter(inChunk).map(key).join(" "));
        }
    }

    /// <summary>herds</summary>
    public class D005_herds
    {

        [Fact(DisplayName = "herds > cross the map as a marker, a tile every few ticks, and leave at the far side")]
        public void T006_cross_the_map_as_a_marker_a_tile_every_few_ticks_and_leave_a()
        {
            var state = field(40, 60);
            Js.NN(Js.Get(state.survivors, 0)).pos = new Point { x = Js.I(0), y = Js.I(0) };
            state.herds.push(new() { id = "h1", x = Js.I(39), y = Js.I(59), to = new Point { x = Js.I(39), y = Js.I(30) }, size = 4, nextStepTick = state.tick, heard = false });
            for (var i = 0; i < ((double)(10) * (Herds.HERD_TICKS_PER_TILE)); i++)
            {
                Tick.tick(state);
            }
            expect(Js.NN(Js.Get(state.herds, 0)).y).toBe(59 - 10);
            runUntil(state, (_) => state.herds.length == 0, ((double)(40) * (Herds.HERD_TICKS_PER_TILE)));
            expect(state.zombies).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "herds > break up into real zombies within ... of a survivor, and walk on where the herd was going")]
        public void T007_break_up_into_real_zombies_within_of_a_survivor_and_walk_on_()
        {
            var state = field(120, 9);
            Js.NN(Js.Get(state.survivors, 0)).pos = new Point { x = Js.I(0), y = Js.I(0) };
            state.herds.push(new() { id = "h1", x = Js.I(60), y = Js.I(8), to = new Point { x = Js.I(119), y = Js.I(8) }, size = 4, nextStepTick = state.tick, heard = false });
            Js.NN(Js.Get(state.survivors, 0)).pos = new Point { x = Js.I(60 - Herds.MATERIALIZE_RADIUS), y = Js.I(8) };
            Tick.tick(state);
            expect(state.herds).toEqual(Js.Arr([]));
            expect(state.zombies).toHaveLength(4);
            expect(state.zombies.every((z) => (z.kind == "herd") && (z.heading?.x == 119))).toBe(true);
            Js.NN(Js.Get(state.survivors, 0)).pos = new Point { x = Js.I(0), y = Js.I(0) };
            runUntil(state, (_) => state.zombies.length == 0, 1500);
        }

        [Fact(DisplayName = "herds > set out from day 2 (sim RNG: the same seed, the same herd)")]
        public void T008_set_out_from_day_2_sim_RNG_the_same_seed_the_same_herd()
        {
            /*TODO return type*/object herd()
            {
                var state = field(80, 80);
                state.tick = Js.I((Time.DAY_LENGTH + ((double)(2) * (Time.TICKS_PER_HOUR))) - 1);
                for (var day = 0; (day < 4) && (state.herds.length == 0); day++)
                {
                    state.tick = Js.I((((double)(Time.DAY_LENGTH) * ((day + 1))) + ((double)(2) * (Time.TICKS_PER_HOUR))) - 1);
                    Tick.tick(state);
                    Tick.tick(state);
                }
                return Js.Json_stringify(state.herds);
            }
            expect(herd()).not.toBe("[]");
            expect(herd()).toBe(herd());
        }
    }

    /// <summary>hearing a herd coming</summary>
    public class D009_hearing_a_herd_coming
    {

        [Fact(DisplayName = "hearing a herd coming > warns once, with a direction, when it comes within ...")]
        public void T010_warns_once_with_a_direction_when_it_comes_within()
        {
            var state = field(120, 9);
            Js.NN(Js.Get(state.survivors, 0)).pos = new Point { x = Js.I(10), y = Js.I(4) };
            state.herds.push(new() { id = "h1", x = Js.I(119), y = Js.I(4), to = new Point { x = Js.I(0), y = Js.I(4) }, size = 5, nextStepTick = state.tick, heard = false });
            runUntil(state, (_) => Js.Get(state.herds, 0)?.heard == true, 2000);
            expect(Js.NN(Js.Get(state.herds, 0)).x).toBe(10 + Herds.WARN_RADIUS);
            expect(Js.NN(state.events.at(-1)).message).toBe($"A herd of 5 is coming from the east ({Js.Str(10 + Herds.WARN_RADIUS)}, 4)");
            int warnings()
            {
                return state.events.filter((e) => e.message.startsWith("A herd of 5 is coming")).length;
            }
            runUntil(state, (_) => state.herds.length == 0, 2000);
            expect(warnings()).toBe(1);
        }

        [Fact(DisplayName = "hearing a herd coming > names directions the way a compass does")]
        public void T011_names_directions_the_way_a_compass_does()
        {
            expect(Js.Arr([Herds.direction(10, 0), Herds.direction(0, -10), Herds.direction(-10, 10), Herds.direction(3, -10), Herds.direction(0, 0)])).toEqual(Js.Arr(["east", "north", "south-west", "north", "nearby"]));
        }
    }

    /// <summary>night zombies on a dark map</summary>
    public class D012_night_zombies_on_a_dark_map
    {

        [Fact(DisplayName = "night zombies on a dark map > arrive in the dark past explored ground, and leave at dawn while others stay")]
        public void T013_arrive_in_the_dark_past_explored_ground_and_leave_at_dawn_wh()
        {
            var state = field(80, 80);
            Grids.fillExplored(state.grid, 0);
            Js.NN(Js.Get(state.survivors, 0)).pos = new Point { x = Js.I(40), y = Js.I(40) };
            Tick.tick(state);
            Zombies.addZombie(state, 0, 0, 200, "static");
            state.tick = Js.I(((double)(15) * (Time.TICKS_PER_HOUR)));
            runUntil(state, (_) => state.zombies.some((z_1) => z_1.kind == "night"), 3000);
            var z = Js.NN(state.zombies.find((zz) => zz.kind == "night"));
            expect(Fog.isExplored(state, z.pos.x, z.pos.y)).toBe(false);
            expect(JsMath.abs(z.pos.x - 40) + JsMath.abs(z.pos.y - 40)).toBeGreaterThanOrEqual(Zombies.FRONTIER_MIN - 8);
            runUntil(state, (_) => !state.zombies.some((zz) => zz.kind == "night"), Time.DAY_LENGTH);
            expect(state.zombies.some((zz) => zz.kind == "static")).toBe(true);
        }
    }
}
