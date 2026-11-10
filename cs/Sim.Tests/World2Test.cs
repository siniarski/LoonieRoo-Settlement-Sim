// Ported from tests/world2.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class World2Test
{

    public const int SIZE = 256;

    public static void generateAll(SimState state)
    {
        for (var y = 0; y < SIZE; y += Worldgen.CHUNK)
        {
            for (var x = 0; x < SIZE; x += Worldgen.CHUNK)
            {
                Worldgen.ensureGeneratedAround(state, new Point { x = Js.I(x), y = Js.I(y) });
            }
        }
    }

    /// <summary>map types</summary>
    public class D001_map_types
    {

        [Fact(DisplayName = "map types > an island is ringed by sea; the start is dry")]
        public void T002_an_island_is_ringed_by_sea_the_start_is_dry()
        {
            var state = Worldgen.createWorld(11, SIZE, Js.With(Js.CopyAs<Settings>(Difficulty.NORMAL), __s0 => { __s0.map = "island"; }));
            generateAll(state);
            expect(Grids.getTile(state.grid, 0, 0)).toBe("water");
            expect(Grids.getTile(state.grid, SIZE - 1, ((double)(SIZE) / (2)))).toBe("water");
            var s = Js.NN(Js.Get(state.survivors, 0));
            expect(Grids.getTile(state.grid, s.pos.x, s.pos.y)).toBe("floor");
        }

        [Fact(DisplayName = "map types > a quarantine zone is fenced in, with gates where roads cross")]
        public void T003_a_quarantine_zone_is_fenced_in_with_gates_where_roads_cross()
        {
            var l = Worldgen.layout(11, SIZE, "quarantine");
            expect(Worldgen.fenceAt(l, 2, 100)).not.toBeNull();
            expect(Worldgen.fenceAt(l, 10, 100)).toBeNull();
            double gates = 0;
            for (var i = 2; i < (SIZE - 2); i++)
            {
                foreach (var (x, y) in new [] { (2, i), (i, 2), (SIZE - 3, i), (i, SIZE - 3) })
                {
                    if (Worldgen.fenceAt(l, x, y) == "door")
                    {
                        gates++;
                    }
                }
            }
            expect(gates).toBeGreaterThan(0);
            expect(NatureMod.isSea(Worldgen.layout(11, SIZE, "mainland"), 0, 0)).toBe(false);
        }

        [Fact(DisplayName = "map types > an island chain has channels between islands, and roads cross water only on bridges")]
        public void T004_an_island_chain_has_channels_between_islands_and_roads_cross()
        {
            var state = Worldgen.createWorld(11, SIZE, Js.With(Js.CopyAs<Settings>(Difficulty.NORMAL), __s1 => { __s1.map = "islands"; }));
            generateAll(state);
            var plain = Worldgen.createWorld(11, SIZE, Js.With(Js.CopyAs<Settings>(Difficulty.NORMAL), __s2 => { __s2.map = "mainland"; }));
            generateAll(plain);
            double water(SimState s)
            {
                return Grids.allTiles(s.grid).filter((t) => t == "water").length;
            }
            expect(water(state)).toBeGreaterThan(water(plain));
            var l = Worldgen.layout(11, SIZE, "islands");
            for (var i = 0; i < ((double)(SIZE) * (SIZE)); i++)
            {
                if (Js.Get(l.regions.Region(0, 0).roadMask, i) == 0)
                {
                    continue;
                }
                expect((object?)(Grids.tileAt(state.grid, i))).not.toBe("water");
            }
        }
    }

    /// <summary>climate and radiation</summary>
    public class D005_climate_and_radiation
    {

        [Fact(DisplayName = "climate and radiation > the north runs cold, the south hot; a cold region feels autumn nights")]
        public void T006_the_north_runs_cold_the_south_hot_a_cold_region_feels_autumn()
        {
            var state = Worldgen.createWorld(11, SIZE, Difficulty.NORMAL);
            expect(Climate.climateAt(state, ((double)(SIZE) / (2)), 5)).toBe("cold");
            expect(Climate.climateAt(state, ((double)(SIZE) / (2)), SIZE - 5)).toBe("hot");
            state.tick = Js.I(((double)(((double)(2) * (Seasons.SEASON_DAYS))) * (Time.DAY_LENGTH)) + ((double)(16) * (Time.TICKS_PER_HOUR)));
            expect(Climate.regionCold(state, ((double)(SIZE) / (2)), 5)).toBe(true);
            expect(Climate.regionCold(state, ((double)(SIZE) / (2)), ((double)(SIZE) / (2)))).toBe(false);
            expect(Climate.regionCold(state, ((double)(SIZE) / (2)), SIZE - 5)).toBe(false);
        }

        [Fact(DisplayName = "climate and radiation > hot zones only in a nuclear winter, never near the start, and they hurt")]
        public void T007_hot_zones_only_in_a_nuclear_winter_never_near_the_start_and_()
        {
            var plain = Worldgen.createWorld(11, SIZE, Difficulty.NORMAL);
            var winter = Worldgen.createWorld(11, SIZE, Js.With(Js.CopyAs<Settings>(Difficulty.NORMAL), __s3 => { __s3.winter = true; }));
            Point? spot = null;
            for (var y = 0; (y < SIZE) && !Js.T(spot); y += 2)
            {
                for (var x = 0; (x < SIZE) && !Js.T(spot); x += 2)
                {
                    if (Climate.isRadioactive(winter, x, y))
                    {
                        spot = new Point { x = Js.I(x), y = Js.I(y) };
                    }
                }
            }
            expect(spot).not.toBeNull();
            expect(Climate.isRadioactive(plain, Js.NN(spot).x, Js.NN(spot).y)).toBe(false);
            expect(Climate.isRadioactive(winter, ((double)(SIZE) / (2)), ((double)(SIZE) / (2)))).toBe(false);
            var s = Js.NN(Js.Get(winter.survivors, 0));
            s.pos = Js.NN(spot);
            var hp = s.health;
            winter.tick = Js.I(20);
            Tick.tick(winter);
            expect(s.health).toBeLessThan(hp);
            expect(s.thoughts.some((t) => t.kind == "irradiated")).toBe(true);
        }
    }

    /// <summary>escape</summary>
    public class D008_escape
    {

        public static SimState helicopterDown()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.s.s"] });
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = Js.NN(Js.Get(state.survivors, 0)).id });
            state.endgame.helicopter = true;
            state.endgame.won = "evacuation";
            return state;
        }

        [Fact(DisplayName = "escape > send the others: they fly out, you stay, and can call again")]
        public void T009_send_the_others_they_fly_out_you_stay_and_can_call_again()
        {
            var state = helicopterDown();
            expect(Commands.applyCommand(state, new Command { type = "escape", who = "others" })).toMatchObject(Js.Obj(("ok", (object?)(true))));
            expect(state.survivors).toHaveLength(1);
            expect(state.endgame.escaped).toBe(2);
            expect(state.endgame.helicopter).toBe(false);
            expect(state.endgame.evacuation).toBeNull();
        }

        [Fact(DisplayName = "escape > left waiting, it goes at dusk")]
        public void T010_left_waiting_it_goes_at_dusk()
        {
            var state = helicopterDown();
            state.tick = Js.I(((double)(15) * (Time.TICKS_PER_HOUR)));
            Tick.tick(state);
            expect(state.endgame.helicopter).toBe(false);
            expect(state.survivors).toHaveLength(3);
        }

        [Fact(DisplayName = "escape > everyone: a new game+, you alone in a fresh world, with your gear and who you were")]
        public void T011_everyone_a_new_game_you_alone_in_a_fresh_world_with_your_gea()
        {
            var old = Worldgen.createWorld(5, 128, Js.With(Js.CopyAs<Settings>(Difficulty.NORMAL), __s4 => { __s4.start = "solo"; }));
            var you = Js.NN(Js.Get(old.survivors, 0));
            you.xp = new() { ["guard"] = 500 };
            you.traits = ["tough"];
            you.resolve = 77;
            old.endgame.escaped = 4;
            var fresh = Escape.newGamePlus(old, 99);
            var me = Js.NN(Js.Get(fresh.survivors, 0));
            expect(fresh.survivors).toHaveLength(1);
            expect(fresh.control?.survivorId).toBe(me.id);
            expect(me.name).toBe(you.name);
            expect(me.xp).toEqual(Js.Obj(("guard", (object?)(500))));
            expect(me.traits).toEqual(Js.Arr(["tough"]));
            expect(me.resolve).toBe(77);
            expect(me.weapon).not.toBeNull();
            expect(fresh.items.filter((i) => i.carriedBy == me.id)).toHaveLength(1);
            expect(fresh.endgame.escaped).toBe(4);
            expect(Js.NN(fresh.world).seed).toBe(99);
        }
    }
}
