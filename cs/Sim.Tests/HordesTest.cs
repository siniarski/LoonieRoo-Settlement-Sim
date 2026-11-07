// Ported from tests/hordes.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class HordesTest
{

    public static List<string> field(int w = 60, int h = 40)
    {
        return Js.ArrayFrom(h, (int _i) => ".".repeat(w));
    }
    public static List<string> field(double w, double h = 40) => field(Js.I(w), Js.I(h));

    public static void quiet(SimState state)
    {
        foreach (var s in Js.Iter(state.survivors))
        {
            foreach (var k in Js.Iter(Js.ObjectKeys(s.priorities)))
            {
                s.priorities[Js.Idx(k)] = 0;
            }
        }
    }

    public static /*TODO return type*/object noon(SimState state)
    {
        return state.tick = Js.I(JsMath.floor(((double)(Time.DAY_LENGTH) / (2))));
    }

    /// <summary>A survivor standing still out of their notice (past AGGRO_RADIUS) but near enough that hordes stay real.</summary>
    public static void watcher(SimState state, int x, int y)
    {
        quiet(state);
        var s = Js.NN(Js.Get(state.survivors, 0));
        s.pos = new Point { x = Js.I(x), y = Js.I(y) };
        s.at = Motion.centre(s.pos);
    }
    public static void watcher(SimState state, double x, double y) => watcher(state, Js.I(x), Js.I(y));

    public static List<string> withSurvivor(List<string> map)
    {
        return map.map((r, y) => (y == 0) ? ("s" + r.slice(1)) : r);
    }

    public static List<Zombie> crowd(SimState state, int x, int y, double n, string kind = "herd")
    {
        List<Zombie> @out = [];
        for (var i = 0; i < n; i++)
        {
            @out.push(Zombies.addZombie(state, x + (i % 5), y + JsMath.floor(((double)(i) / (5))), 200, kind));
        }
        return @out;
    }
    public static List<Zombie> crowd(SimState state, double x, double y, double n, string kind = "herd") => crowd(state, Js.I(x), Js.I(y), n, kind);

    /// <summary>hordes</summary>
    public class D001_hordes
    {

        [Fact(DisplayName = "hordes > a night wave comes as one horde and goes for the weakest way in: the door, not the wall")]
        public void T002_a_night_wave_comes_as_one_horde_and_goes_for_the_weakest_way()
        {
            var map = field(40, 20).map((r, y) =>
        {
            if ((y < 6) || (y > 12))
            {
                return r;
            }
            var row = r.split("");
            for (var x = 18; x <= 24; x++)
            {
                if ((((y == 6) || (y == 12)) || (x == 18)) || (x == 24))
                {
                    row[Js.Idx(x)] = "W";
                }
            }
            if (y == 9)
            {
                row[Js.Idx(24)] = "D";
            }
            if (y == 9)
            {
                row[Js.Idx(21)] = "s";
            }
            return row.join("");
        }).map((r) => r.replace(new JsRegExp("W", "g"), "#"));
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map });
            quiet(state);
            while (!Time.isNight(state.tick))
            {
                state.tick = Js.I(state.tick + 10);
            }
            var wave = crowd(state, 2, 8, 5, "night");
            var h = Js.NN(Hordes.formHorde(state, wave, null, true));
            expect(h).not.toBeNull();
            expect(Js.Set(wave.map((z) => z.hordeId))).toEqual(Js.Set([h.id]));
            expect(h.mode).toBe("hunt");
            var doorHit = false;
            var wallHit = false;
            for (var t = 0; (t < 1500) && !doorHit; t++)
            {
                Tick.tick(state);
                if (Js.NN(Js.Get(state.grid.damage, ((double)(9) * (40)) + 24)) > 0)
                {
                    doorHit = true;
                }
                for (var y = 6; y <= 12; y++)
                {
                    if (Js.NN(Js.Get(state.grid.damage, ((double)(y) * (40)) + 18)) > 0)
                    {
                        wallHit = true;
                    }
                }
            }
            expect(doorHit).toBe(true);
            expect(wallHit).toBe(false);
        }

        [Fact(DisplayName = "hordes > two hordes that meet become one; a loner nearby falls in")]
        public void T003_two_hordes_that_meet_become_one_a_loner_nearby_falls_in()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = withSurvivor(field()) });
            noon(state);
            watcher(state, 38, 25);
            var a = Js.NN(Hordes.formHorde(state, crowd(state, 10, 10, 4), new Point { x = Js.I(59), y = Js.I(10) }, false));
            var b = Js.NN(Hordes.formHorde(state, crowd(state, 14, 12, 3), new Point { x = Js.I(59), y = Js.I(10) }, false));
            var loner = Zombies.addZombie(state, 12, 15, 200, "static");
            for (var t = 0; t < 60; t++)
            {
                Tick.tick(state);
            }
            expect(Hordes.hordesOf(state)).toHaveLength(1);
            var one = Js.NN(Js.Get(Hordes.hordesOf(state), 0));
            expect(one.id).toBe(a.id);
            expect(Hordes.getHorde(state, b.id)).toBeUndefined();
            expect(loner.hordeId).toBe(one.id);
            expect(Hordes.membersOf(state, one)).toHaveLength(8);
        }

        [Fact(DisplayName = "hordes > one left far behind drops out, a loner")]
        public void T004_one_left_far_behind_drops_out_a_loner()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = withSurvivor(field(80, 20)) });
            noon(state);
            watcher(state, 60, 18);
            var zs = crowd(state, 50, 5, 4);
            var h = Js.NN(Hordes.formHorde(state, zs, new Point { x = Js.I(79), y = Js.I(5) }, false));
            var lag = Js.NN(Js.Get(zs, 0));
            lag.pos = new Point { x = Js.I((50 - Hordes.STRAY_RADIUS) - 3), y = Js.I(5) };
            lag.at = Motion.centre(lag.pos);
            lag.stuckUntil = 10000;
            for (var t = 0; t < 60; t++)
            {
                Tick.tick(state);
            }
            expect(lag.hordeId ?? null).toBeNull();
            expect(Hordes.membersOf(state, h)).toHaveLength(3);
        }

        [Fact(DisplayName = "hordes > a big horde now and then loses a small part off its back, which heads off on its own")]
        public void T005_a_big_horde_now_and_then_loses_a_small_part_off_its_back_whi()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = withSurvivor(field(120, 120)) });
            noon(state);
            watcher(state, 42, 75);
            var big = Js.NN(Hordes.formHorde(state, crowd(state, 40, 40, 30), new Point { x = Js.I(42), y = Js.I(42) }, false));
            expect(Hordes.membersOf(state, big).length).toBeGreaterThanOrEqual(Hordes.SHED_MIN);
            var shed = false;
            for (var t = 0; (t < ((double)(24) * (Time.TICKS_PER_HOUR))) && !shed; t++)
            {
                Tick.tick(state);
                shed = state.events.some((e) => e.message.includes("broke away"));
            }
            expect(shed).toBe(true);
            var left = Hordes.membersOf(state, big).length;
            expect(left).toBeLessThan(30);
            expect(left).toBeGreaterThan(((double)(30) * (0.8)));
        }

        [Fact(DisplayName = "hordes > a roaming horde with nobody near becomes a herd marker again, and the count holds")]
        public void T006_a_roaming_horde_with_nobody_near_becomes_a_herd_marker_again()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = withSurvivor(field(200, 20)) });
            noon(state);
            var s = Js.Get(state.survivors, 0);
            if (Js.T(s))
            {
                s.pos = new Point { x = Js.I(1), y = Js.I(1) };
                s.at = Motion.centre(s.pos);
            }
            quiet(state);
            Hordes.formHorde(state, crowd(state, (1 + Hordes.demoteRadius()) + 20, 5, 6), new Point { x = Js.I(199), y = Js.I(5) }, false);
            var herdsBefore = state.herds.length;
            for (var t = 0; t < 60; t++)
            {
                Tick.tick(state);
            }
            expect(Hordes.hordesOf(state)).toHaveLength(0);
            expect(state.zombies).toHaveLength(0);
            expect(state.herds).toHaveLength(herdsBefore + 1);
            expect(Js.NN(state.herds.at(-1)).size).toBe(6);
        }

        [Fact(DisplayName = "hordes > a noise in earshot of part of a roaming horde draws that part off")]
        public void T007_a_noise_in_earshot_of_part_of_a_roaming_horde_draws_that_par()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = field(120, 40) });
            noon(state);
            state.survivors.length = 0;
            List<Zombie> zs = [.. crowd(state, 10, 10, 5), .. crowd(state, 30, 10, 5)];
            var h = Js.NN(Hordes.formHorde(state, zs, new Point { x = Js.I(119), y = Js.I(10) }, false));
            Noise.makeNoise(state, new Point { x = Js.I(12), y = Js.I(30) }, "fighting");
            expect(Hordes.hordesOf(state)).toHaveLength(1);
            Noise.makeNoise(state, new Point { x = Js.I(5), y = Js.I(25) }, "gunfire");
            expect(Hordes.hordesOf(state)).toHaveLength(2);
            expect(Hordes.membersOf(state, h)).toHaveLength(5);
        }

        [Fact(DisplayName = "hordes > a game saved mid-horde plays on exactly as the one that was saved")]
        public void T008_a_game_saved_mid_horde_plays_on_exactly_as_the_one_that_was_()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 2, map = field(60, 30).map((r, y) => (y == 15) ? ((r.slice(0, 50) + "s") + r.slice(51)) : r) });
            quiet(state);
            Hordes.formHorde(state, crowd(state, 3, 3, 8, "night"), null, true);
            for (var t = 0; t < 40; t++)
            {
                Tick.tick(state);
            }
            var loaded = Savefile.loadFile(Savefile.toSaveFile(state));
            expect(loaded.ok).toBe(true);
            if (!loaded.ok)
            {
                return;
            }
            var copy = loaded.state;
            for (var t = 0; t < 200; t++)
            {
                Tick.tick(state);
                Tick.tick(copy);
            }
            expect(Js.Json_stringify(Js.ObjFrom(copy, ("commandLog", (object?)(Js.Arr([])))))).toBe(Js.Json_stringify(Js.ObjFrom(state, ("commandLog", (object?)(Js.Arr([]))))));
        }
    }

    /// <summary>hordes on the move</summary>
    public class D009_hordes_on_the_move
    {

        [Fact(DisplayName = "hordes on the move > a night wave crosses open ground to its prey and attacks")]
        public void T010_a_night_wave_crosses_open_ground_to_its_prey_and_attacks()
        {
            var map = Js.ArrayFrom(20, (int _i) => ".".repeat(60)).map((r, y) => (y == 10) ? ((r.slice(0, 50) + "s") + r.slice(51)) : r);
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map });
            quiet(state);
            while (!Time.isNight(state.tick))
            {
                state.tick = Js.I(state.tick + 10);
            }
            var wave = crowd(state, 2, 10, 5, "night");
            Hordes.formHorde(state, wave, null, true);
            for (var t = 0; (t < 600) && (Js.NN(Js.Get(state.survivors, 0)).health == 1000); t++)
            {
                Tick.tick(state);
            }
            expect(JsMath.min(wave.map((z) => JsMath.abs(z.pos.x - 50)))).toBeLessThanOrEqual(2);
            expect(Js.NN(Js.Get(state.survivors, 0)).health).toBeLessThan(1000);
        }
    }
}
