// Ported from tests/crafting.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class CraftingTest
{

    public static double runUntil(SimState state, Func<SimState, bool> done, double max = 1500)
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
        throw new Exception($"condition not met after {Js.Str(max)} ticks\n{Js.Str(DebugText.dumpText(state))}\n{Js.Str(state.survivors.map((s) => s.note).join("\n"))}");
    }

    public static SimState setup(List<string> map, JsRecord<double>? jobs = null)
    {
        jobs ??= new() { ["craft"] = 3 };
        var state = ScenarioMod.loadScenario(new Scenario { seed = 8, map = map });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = Js.RecordFrom(JobTypes.NO_JOBS, __r0 => { Js.MergeInto(__r0, jobs); });
        }
        return state;
    }

    public static CommandResult target(SimState state, string kind, double count)
    {
        return Commands.applyCommand(state, new Command { type = "setCraftTarget", kind = kind, count = count });
    }

    public static int count(SimState state, string kind)
    {
        return state.items.filter((i) => i.kind == kind).length;
    }

    /// <summary>crafting</summary>
    public class D001_crafting
    {

        [Fact(DisplayName = "crafting > no target, no craft jobs")]
        public void T002_no_target_no_craft_jobs()
        {
            var state = setup(["s.k.ww"]);
            Tick.tick(state);
            expect(state.jobs).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "crafting > a crafter carries wood to the bench piece by piece, then makes a weapon")]
        public void T003_a_crafter_carries_wood_to_the_bench_piece_by_piece_then_make()
        {
            var state = setup(["s.k.ww"]);
            target(state, "plank", 1);
            Tick.tick(state);
            expect(state.jobs).toMatchObject(Js.Arr([Js.Obj(("type", (object?)("craft")), ("x", (object?)(2)), ("y", (object?)(0)))]));
            var bench = Js.NN(Js.Get(state.furniture, 0));
            runUntil(state, (_) => bench.stored == 1);
            expect(Js.NN(Js.Get(state.survivors, 0)).note).toBe("brought wood to the workbench (1/2)");
            runUntil(state, (_) => Js.NN(Js.Get(state.survivors, 0)).note == "crafted a plank at (2, 0)");
            expect(count(state, "plank")).toBe(1);
            expect(count(state, "wood")).toBe(0);
            expect(bench.stored).toBe(0);
        }

        [Fact(DisplayName = "crafting > stops at the target and makes what is short, in order")]
        public void T004_stops_at_the_target_and_makes_what_is_short_in_order()
        {
            var state = setup(["s.k.wwww", "........"]);
            target(state, "plank", 1);
            target(state, "bandage", 2);
            runUntil(state, (_) => Crafting.wantedCraft(state) == null, 3000);
            expect(Js.Arr([count(state, "plank"), count(state, "bandage"), count(state, "wood")])).toEqual(Js.Arr([1, 2, 0]));
            for (var i = 0; i < 50; i++)
            {
                Tick.tick(state);
            }
            expect(state.jobs).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "crafting > wielded weapons don't count as stock")]
        public void T005_wielded_weapons_don_t_count_as_stock()
        {
            var state = setup(["s.k.", "...."], new() { ["guard"] = 3 });
            Items.spawnItem(state, "plank", 1, 0);
            runUntil(state, (_) => Js.NN(Js.Get(state.survivors, 0)).weapon != null);
            expect(Crafting.craftStock(state, "plank")).toBe(0);
            target(state, "plank", 1);
            expect(Crafting.wantedCraft(state)).toBe("plank");
        }

        [Fact(DisplayName = "crafting > with no wood the job waits, unclaimed, and nobody gets stuck")]
        public void T006_with_no_wood_the_job_waits_unclaimed_and_nobody_gets_stuck()
        {
            var state = setup(["s.k..."]);
            target(state, "bandage", 1);
            for (var i = 0; i < 100; i++)
            {
                Tick.tick(state);
            }
            expect(state.jobs).toMatchObject(Js.Arr([Js.Obj(("type", (object?)("craft")), ("reservedBy", (object?)(null)))]));
            state.items.push(new() { id = "i900", kind = "wood", pos = new Point { x = Js.I(5), y = Js.I(0) }, carriedBy = null, reservedBy = null, wear = 0, tag = null });
            runUntil(state, (_) => count(state, "bandage") == 1);
        }

        [Fact(DisplayName = "crafting > removing a bench gives its stored wood back and frees the crafter")]
        public void T007_removing_a_bench_gives_its_stored_wood_back_and_frees_the_cr()
        {
            var state = setup(["s.k.ww", "......"]);
            target(state, "plank", 1);
            var bench = Js.NN(Js.Get(state.furniture, 0));
            runUntil(state, (_) => bench.stored == 1);
            Commands.applyCommand(state, new Command { type = "clearTile", x = Js.I(2), y = Js.I(0) });
            Tick.tick(state);
            expect(state.furniture).toEqual(Js.Arr([]));
            expect(count(state, "wood")).toBe(2);
            expect(Js.NN(Js.Get(state.survivors, 0)).jobId).toBeNull();
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "crafting > lowering the target mid-craft releases the crafter; the wood stays in the bench")]
        public void T008_lowering_the_target_mid_craft_releases_the_crafter_the_wood_()
        {
            var state = setup(["s.k.ww"]);
            target(state, "plank", 1);
            var bench = Js.NN(Js.Get(state.furniture, 0));
            runUntil(state, (_) => bench.stored == 1);
            target(state, "plank", 0);
            Tick.tick(state);
            expect(state.jobs).toEqual(Js.Arr([]));
            expect(Js.NN(Js.Get(state.survivors, 0)).jobId).toBeNull();
            expect(bench.stored).toBe(1);
        }

        [Fact(DisplayName = "crafting > targets are validated")]
        public void T009_targets_are_validated()
        {
            var state = setup(["s.k"]);
            expect(target(state, "plank", -1).ok).toBe(false);
            expect(target(state, "plank", 21).ok).toBe(false);
            expect(target(state, "plank", 2.5).ok).toBe(false);
            expect(target(state, "plank", 3).ok).toBe(true);
        }

        [Fact(DisplayName = "crafting > survivors build workbenches from wood")]
        public void T010_survivors_build_workbenches_from_wood()
        {
            var state = setup(["K..", "s.w"], new() { ["build"] = 3, ["haul"] = 3 });
            runUntil(state, (_) => state.furniture.some((f) => f.kind == "workbench"));
        }
    }
}
