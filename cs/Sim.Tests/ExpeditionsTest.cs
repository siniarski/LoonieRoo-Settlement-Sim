// Ported from tests/expeditions.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class ExpeditionsTest
{

    /// <summary>A colony at the left, a ruin of two crates (5 finds each) 30 tiles east, marked as found.</summary>
    public static SimState setup(double sleepers = 0)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 4, map = [("s.s" + ".".repeat(27)) + "c.c", "b.b" + ".".repeat(30)] });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = new(JobTypes.NO_JOBS) {  };
        }
        state.pointsOfInterest.push(new() { id = "p1", x = Js.I(30), y = Js.I(0), label = "ruin with crates" });
        for (var i = 0; i < sleepers; i++)
        {
            state.dormant.push(new() { id = $"d{Js.Str(i)}", x = Js.I(31), y = Js.I(1) });
        }
        return state;
    }

    public static CommandResult send(SimState state, List<string> ids)
    {
        return Commands.applyCommand(state, new Command { type = "sendExpedition", survivorIds = ids, poiId = "p1" });
    }

    /// <summary>expeditions</summary>
    public class D001_expeditions
    {

        [Fact(DisplayName = "expeditions > take survivors off the map, and bring them back with what the ruin held")]
        public void T002_take_survivors_off_the_map_and_bring_them_back_with_what_the()
        {
            var state = setup();
            var ash = Js.NN(Js.Get(state.survivors, 0));
            Js.NN(state.furniture.find((f) => f.kind == "bed")).owner = ash.id;
            expect(send(state, [ash.id])).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(true))));
            expect(state.survivors.map((s) => s.name)).not.toContain(ash.name);
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
            var due = Js.NN(Js.Get(state.expeditions, 0)).returnTick;
            expect(due - state.tick).toBe(((double)(((double)(2) * (30))) * (Survivors.TICKS_PER_TILE)) + Expeditions.SEARCH_TICKS);
            while (state.tick < due)
            {
                Tick.tick(state);
            }
            Tick.tick(state);
            expect(state.survivors.map((s) => s.name)).toContain(ash.name);
            expect(ash.pos).toEqual(Js.Obj(("x", (object?)(0)), ("y", (object?)(0))));
            expect(state.items).toHaveLength(Expeditions.CARRY);
            expect(state.furniture.filter((f) => f.kind == "crate").reduce((a, f) => a + f.loot, 0.0)).toBe(10 - Expeditions.CARRY);
            expect(state.pointsOfInterest).toEqual(Js.Arr([]));
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "expeditions > a ruin full of sleepers is dangerous; its sleepers are cleared either way")]
        public void T003_a_ruin_full_of_sleepers_is_dangerous_its_sleepers_are_cleare()
        {
            var outcomes = new JsSet<string>();
            for (var seed = 1; seed <= 12; seed++)
            {
                var state = setup(8);
                state.rng = ((double)(seed) * (7777));
                var (a, b) = state.survivors;
                send(state, [Js.NN(a).id, Js.NN(b).id]);
                var due = Js.NN(Js.Get(state.expeditions, 0)).returnTick;
                while (state.tick <= due)
                {
                    Tick.tick(state);
                }
                expect(state.dormant).toEqual(Js.Arr([]));
                expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
                var back = state.survivors.length;
                var hurt = state.survivors.some((s) => s.health < 1000);
                outcomes.add((back < 2) ? "loss" : (hurt ? "hurt" : "fine"));
            }
            expect(outcomes.has("loss") || outcomes.has("hurt")).toBe(true);
        }

        [Fact(DisplayName = "expeditions > refuses nobody, strangers, or an unknown place")]
        public void T004_refuses_nobody_strangers_or_an_unknown_place()
        {
            var state = setup();
            expect(send(state, []).ok).toBe(false);
            expect(send(state, ["s999"]).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "sendExpedition", survivorIds = [Js.NN(Js.Get(state.survivors, 0)).id], poiId = "nope" }).ok).toBe(false);
        }
    }
}
