// Ported from tests/infection.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class InfectionTest
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
        throw new Exception($"condition not met after {Js.Str(max)} ticks\n{Js.Str(DebugText.dumpText(state))}\n{Js.Str(state.survivors.map((s) => s.note).join("\n"))}");
    }

    public static SimState setup(List<string> map, JsRecord<double>? jobs = null, List<ScenarioRoomsItem>? rooms = null)
    {
        jobs ??= new() {  };
        rooms ??= new();
        var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = map, rooms = rooms });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = Js.RecordFrom(JobTypes.NO_JOBS, __r0 => { Js.MergeInto(__r0, jobs); });
        }
        return state;
    }

    /// <summary>infection</summary>
    public class D001_infection
    {

        [Fact(DisplayName = "infection > a bite infects about ...% of the time, decided by a hash (the same bite, the same answer)")]
        public void T002_a_bite_infects_about_of_the_time_decided_by_a_hash_the_same_()
        {
            var state = setup(["s"]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            double infected = 0;
            for (var t = 0; t < 1000; t++)
            {
                s.infection = null;
                state.tick = Js.I(t);
                Infection.maybeInfect(state, s, "z7");
                if (s.infection != null)
                {
                    infected += 1;
                }
            }
            expect(infected).toBeGreaterThan(((double)(((double)(1000) * (Infection.INFECTION_CHANCE))) * (0.6)));
            expect(infected).toBeLessThan(((double)(((double)(1000) * (Infection.INFECTION_CHANCE))) * (1.4)));
        }

        [Fact(DisplayName = "infection > untreated, it kills in a day and the survivor gets up as a zombie")]
        public void T003_untreated_it_kills_in_a_day_and_the_survivor_gets_up_as_a_zo()
        {
            var state = setup(["s....", "....."]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.infection = Infection.INFECTION_TURN - 3;
            for (var i = 0; i < 5; i++)
            {
                Tick.tick(state);
            }
            expect(state.survivors).toEqual(Js.Arr([]));
            expect(state.events.map((e) => e.message)).toContain($"{Js.Str(s.name)} died of the infection");
            expect(state.zombies).toMatchObject(Js.Arr([Js.Obj(("pos", (object?)(s.pos)), ("kind", (object?)("static")))]));
        }

        [Fact(DisplayName = "infection > the infected fetch medicine and are cured")]
        public void T004_the_infected_fetch_medicine_and_are_cured()
        {
            var state = setup(["s...!"]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.infection = 100;
            runUntil(state, (_) => s.note == "took medicine for the infection");
            expect(s.infection).toBeNull();
            expect(state.items).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "infection > resting in an infirmary bed slows it by half")]
        public void T005_resting_in_an_infirmary_bed_slows_it_by_half()
        {
            double spread(List<ScenarioRoomsItem> rooms)
            {
                var state = setup(["####", "#b.#", "#..#", "##D#", ".s.."], new() {  }, rooms);
                var s = Js.NN(Js.Get(state.survivors, 0));
                Commands.applyCommand(state, new Command { type = "setRest", survivorId = s.id, rest = 0 });
                s.infection = 0;
                runUntil(state, (_) => s.sleep?.asleep == true);
                var at = Js.NN(s.infection);
                for (var i = 0; i < 100; i++)
                {
                    Tick.tick(state);
                }
                return Js.NN(s.infection) - at;
            }
            expect(spread([])).toBe(100);
            expect(spread([new() { x = Js.I(1), y = Js.I(2), purpose = "infirmary" }])).toBe(50);
        }

        [Fact(DisplayName = "infection > a doctor brings medicine to an infected patient in bed")]
        public void T006_a_doctor_brings_medicine_to_an_infected_patient_in_bed()
        {
            var state = setup(["s.b...", "......", "s....."]);
            var (ash, bea) = state.survivors;
            bea.priorities = new(JobTypes.NO_JOBS) { ["doctor"] = 3 };
            Commands.applyCommand(state, new Command { type = "setRest", survivorId = bea.id, rest = 2000 });
            Commands.applyCommand(state, new Command { type = "setRest", survivorId = ash.id, rest = 0 });
            runUntil(state, (_) => ash.sleep?.asleep == true);
            ash.infection = 50;
            state.items.push(new() { id = "i900", kind = "medicine", pos = new Point { x = Js.I(5), y = Js.I(2) }, carriedBy = null, reservedBy = null, wear = 0, tag = null });
            runUntil(state, (_) => bea.note == $"cured {Js.Str(ash.name)}'s infection");
            expect(ash.infection).toBeNull();
        }
    }

    /// <summary>workshop and medicine</summary>
    public class D007_workshop_and_medicine
    {

        public static readonly List<string> MAP = ["#####", "#.k.#", "#...#", "##D##", "s.www", "..ww."];

        [Fact(DisplayName = "workshop and medicine > any workbench makes medicine (balance pass 3); ammo needs a workshop as well as gunsmithing")]
        public void T008_any_workbench_makes_medicine_balance_pass_3_ammo_needs_a_wor()
        {
            var plain = setup(MAP, new() { ["craft"] = 3 });
            Commands.applyCommand(plain, new Command { type = "setCraftTarget", kind = "medicine", count = 1 });
            runUntil(plain, (_) => plain.items.some((i) => i.kind == "medicine"), 1500);
            var ammo = setup(MAP, new() { ["craft"] = 3 });
            ammo.knowledge = ["gunsmithing"];
            Commands.applyCommand(ammo, new Command { type = "setCraftTarget", kind = "ammo", count = 1 });
            for (var i = 0; i < 200; i++)
            {
                Tick.tick(ammo);
            }
            expect(ammo.jobs.filter((j) => j.type == "craft")).toEqual(Js.Arr([]));
            var shop = setup(MAP, new() { ["craft"] = 3 }, [new() { x = Js.I(1), y = Js.I(1), purpose = "workshop" }]);
            shop.knowledge = ["gunsmithing"];
            Commands.applyCommand(shop, new Command { type = "setCraftTarget", kind = "ammo", count = 1 });
            runUntil(shop, (_) => shop.items.some((i) => i.kind == "ammo"), 1500);
        }

        [Fact(DisplayName = "workshop and medicine > a workshop bench works faster")]
        public void T009_a_workshop_bench_works_faster()
        {
            double time(List<ScenarioRoomsItem> rooms)
            {
                var state = setup(MAP, new() { ["craft"] = 3 }, rooms);
                Commands.applyCommand(state, new Command { type = "setCraftTarget", kind = "bandage", count = 1 });
                var bench = Js.NN(Js.Get(state.furniture, 0));
                runUntil(state, (_) => bench.stored == 1, 1500);
                return runUntil(state, (_) => state.items.some((i) => i.kind == "bandage"), 1500);
            }
            expect(time([new() { x = Js.I(1), y = Js.I(1), purpose = "workshop" }])).toBeLessThan(time([]));
        }
    }
}
