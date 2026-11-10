// Ported from tests/worldgen.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class WorldgenTest
{

    public const int SIZE = 128;

    /// <summary>terrain</summary>
    public class D001_terrain
    {

        [Fact(DisplayName = "terrain > water blocks walking, building, and zombies")]
        public void T002_water_blocks_walking_building_and_zombies()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.%.."] });
            expect(Pathfinding.findPath(state.grid, new Point { x = Js.I(0), y = Js.I(0) }, new Point { x = Js.I(4), y = Js.I(0) })).toBeNull();
            expect(Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(2), y = Js.I(0) }).ok).toBe(false);
        }

        [Fact(DisplayName = "terrain > a pocket of floor that touches water, or is huge, is outdoors, not a room")]
        public void T003_a_pocket_of_floor_that_touches_water_or_is_huge_is_outdoors_()
        {
            var pond = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["#####", "#..%#", "#####"] });
            expect(Rooms.roomAt(pond, 1, 1)).toBeNull();
            var side = JsMath.ceil(JsMath.sqrt(Rooms.MAX_ROOM_TILES)) + 1;
            var big = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["#".repeat(side + 2), .. Js.ArrayFrom(side, (int _i) => $"#{Js.Str(".".repeat(side))}#"), "#".repeat(side + 2)] });
            expect(Rooms.roomAt(big, 1, 1)).toBeNull();
            var small = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["####", "#..#", "####"] });
            expect(Rooms.roomAt(small, 1, 1)).not.toBeNull();
        }
    }

    /// <summary>world generation</summary>
    public class D004_world_generation
    {

        public static readonly SimState world = Worldgen.createWorld(7, SIZE);

        public static int count(Func<string, bool> pred)
        {
            return Grids.allTiles(world.grid).filter(pred).length;
        }

        [Fact(DisplayName = "world generation > is the same world every time for a seed, and a different one for another seed")]
        public void T005_is_the_same_world_every_time_for_a_seed_and_a_different_one_()
        {
            expect(Js.Json_stringify(Worldgen.createWorld(7, SIZE))).toBe(Js.Json_stringify(world));
            expect(Grids.allTiles(Worldgen.createWorld(8, SIZE).grid).join()).not.toBe(Grids.allTiles(world.grid).join());
        }

        [Fact(DisplayName = "world generation > generates any chunk the same whatever order the chunks come in")]
        public void T006_generates_any_chunk_the_same_whatever_order_the_chunks_come_()
        {
            var l = Worldgen.layout(7, SIZE);
            var forward = States.createState(new NewStateOptions { seed = 7, width = Js.I(SIZE), height = Js.I(SIZE) });
            var backward = States.createState(new NewStateOptions { seed = 7, width = Js.I(SIZE), height = Js.I(SIZE) });
            var n = ((double)(SIZE) / (Worldgen.CHUNK));
            for (var i = 0; i < ((double)(n) * (n)); i++)
            {
                Worldgen.generateChunk(forward, l, i % n, JsMath.floor(((double)(i) / (n))));
            }
            for (var i = ((double)(n) * (n)) - 1; i >= 0; i--)
            {
                Worldgen.generateChunk(backward, l, i % n, JsMath.floor(((double)(i) / (n))));
            }
            expect(Grids.allTiles(backward.grid)).toEqual(Grids.allTiles(forward.grid));
            expect(Grids.allGround(backward.grid)).toEqual(Grids.allGround(forward.grid));
            List<string> things(SimState s)
            {
                return s.furniture.map((f) => $"{Js.Str(f.kind)}@{Js.Str(f.x)},{Js.Str(f.y)}:{Js.Str(f.loot)}").sort();
            }
            expect(things(backward)).toEqual(things(forward));
        }

        [Fact(DisplayName = "world generation > has roads, ruins with crates, forest, and water")]
        public void T007_has_roads_ruins_with_crates_forest_and_water()
        {
            expect(Grids.allGround(world.grid).filter((g) => g == 2).length).toBeGreaterThan(SIZE);
            expect(count((t) => t == "water")).toBeGreaterThan(SIZE);
            expect(Grids.allEdges(world.grid).filter((e) => e == 1).length).toBeGreaterThan(100);
            expect(world.furniture.filter((f) => f.kind == "crate").length).toBeGreaterThan(5);
            expect(world.furniture.filter((f) => f.kind == "tree").length).toBeGreaterThan(50);
        }

        [Fact(DisplayName = "world generation > starts with a calm, revealed clearing: three survivors, a hut with three beds, wood and food")]
        public void T008_starts_with_a_calm_revealed_clearing_three_survivors_a_hut_w()
        {
            var __d0 = Worldgen.layout(7, SIZE).start;
            var sx = __d0.x;
            var sy = __d0.y;
            expect(world.survivors).toHaveLength(3);
            expect(world.furniture.filter((f) => f.kind == "bed")).toHaveLength(3);
            expect(world.items.filter((i) => i.kind == "wood")).toHaveLength(12);
            for (var y = (sy - NatureMod.START_CLEAR_RADIUS) + 2; y < ((sy + NatureMod.START_CLEAR_RADIUS) - 2); y++)
            {
                for (var x = (sx - NatureMod.START_CLEAR_RADIUS) + 2; x < ((sx + NatureMod.START_CLEAR_RADIUS) - 2); x++)
                {
                    expect((object?)(Grids.tileAt(world.grid, Grids.tileIndex(world.grid, x, y)))).not.toBe("water");
                }
            }
            expect(Js.Missing(Grids.allExplored(world.grid), Grids.tileIndex(world.grid, sx, sy)) ? null : (object?)(Grids.exploredAt(world.grid, Grids.tileIndex(world.grid, sx, sy)))).toBe(1);
            expect(Js.Missing(Grids.allExplored(world.grid), 0) ? null : (object?)(Grids.exploredAt(world.grid, 0))).toBe(0);
        }

        [Fact(DisplayName = "world generation > crates further out hold more")]
        public void T009_crates_further_out_hold_more()
        {
            expect(Worldgen.lootFor(0)).toBeLessThan(Worldgen.lootFor(100));
            expect(Worldgen.lootFor(1000)).toBe(Worldgen.lootFor(10000));
        }

        [Fact(DisplayName = "world generation > a fresh world is consistent and runs")]
        public void T010_a_fresh_world_is_consistent_and_runs()
        {
            var state = Worldgen.createWorld(3, SIZE);
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
            for (var i = 0; i < 300; i++)
            {
                Tick.tick(state);
            }
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }
    }

    /// <summary>world sizes</summary>
    public class D011_world_sizes
    {

        [Fact(DisplayName = "world sizes > every offered size generates a consistent, runnable world")]
        public void T012_every_offered_size_generates_a_consistent_runnable_world()
        {
            foreach (var size in Js.Iter(Worldgen.WORLD_SIZES.filter((n) => n <= 256)))
            {
                var state = Worldgen.createWorld(11, size);
                expect(state.grid.width).toBe(size);
                expect(state.survivors).toHaveLength(3);
                expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
                for (var i = 0; i < 50; i++)
                {
                    Tick.tick(state);
                }
                expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
            }
        }
    }

    /// <summary>lazy generation</summary>
    public class D013_lazy_generation
    {

        [Fact(DisplayName = "lazy generation > a new world generates only the chunks round the start")]
        public void T014_a_new_world_generates_only_the_chunks_round_the_start()
        {
            var world = Worldgen.createWorld(9, 512);
            var n = ((double)(512) / (Worldgen.CHUNK));
            expect(Js.NN(world.world).generated.filter((g) => g == 1)).toHaveLength(JsMath.pow((((double)(2) * (Worldgen.GENERATE_RADIUS)) + 1), 2));
            expect(Js.NN(world.world).generated.length).toBe(((double)(n) * (n)));
            expect((object?)(Grids.tileAt(world.grid, 0))).toBe("floor");
        }

        [Fact(DisplayName = "lazy generation > chunks appear ahead of a survivor as they walk, the same as if made up front")]
        public void T015_chunks_appear_ahead_of_a_survivor_as_they_walk_the_same_as_i()
        {
            var lazy = Worldgen.createWorld(9, 256);
            lazy.settlements = [];
            var s = Js.NN(Js.Get(lazy.survivors, 0));
            var before = Js.NN(lazy.world).generated.filter((g) => g == 1).length;
            s.pos = new Point { x = Js.I(250), y = Js.I(128) };
            Tick.tick(lazy);
            expect(Js.NN(lazy.world).generated.filter((g) => g == 1).length).toBeGreaterThan(before);
            var alone = States.createState(new NewStateOptions { seed = 9, width = Js.I(256), height = Js.I(256) });
            Worldgen.generateChunk(alone, Worldgen.layout(9, 256), 7, 4);
            for (var y = ((double)(4) * (Worldgen.CHUNK)); y < ((double)(5) * (Worldgen.CHUNK)); y++)
            {
                for (var x = ((double)(7) * (Worldgen.CHUNK)); x < ((double)(8) * (Worldgen.CHUNK)); x++)
                {
                    expect((object?)(Grids.tileAt(lazy.grid, ((double)(y) * (256)) + x))).toBe(Grids.tileAt(alone.grid, ((double)(y) * (256)) + x));
                }
            }
            expect(Invariants.checkInvariants(lazy)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "lazy generation > hand-made maps have no world to generate")]
        public void T016_hand_made_maps_have_no_world_to_generate()
        {
            expect(ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.."] }).world).toBeNull();
        }
    }
}
