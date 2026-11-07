// Ported from tests/runs.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class RunsTest
{

    public static SimState world()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = Js.ArrayFrom(60, (int y) => ((y == 10) ? "s" : ".") + ".".repeat(79)) });
        return state;
    }

    public static ScavRun run(SimState state, ScavRun? over = null)
    {
        over ??= new();
        var n = Js.NN(Js.Get(state.settlements, 0));
        ScavRun r = Js.With(new ScavRun { id = "sr1", from = n.id, x = Js.I(n.x), y = Js.I(n.y), home = new Point { x = Js.I(n.x), y = Js.I(n.y) }, ruin = new Point { x = Js.I(n.x + 10), y = Js.I(n.y) }, people = 3, phase = "out", untilTick = 0, nextStepTick = state.tick + 1, food = 0, met = false }, __s0 => Js.AssignSet(__s0, over));
        state.runs = [r];
        return r;
    }

    /// <summary>Only the runs, n ticks.</summary>
    public static void runs(SimState state, double n)
    {
        for (var k = 0; k < n; k++)
        {
            state.tick = Js.I(state.tick + 1);
            Runs.updateRuns(state);
        }
    }

    /// <summary>neighbours' scavenging runs</summary>
    public class D001_neighbours_scavenging_runs
    {

        [Fact(DisplayName = "neighbours' scavenging runs > a hungry settlement sends people out at dawn now and then")]
        public void T002_a_hungry_settlement_sends_people_out_at_dawn_now_and_then()
        {
            var state = world();
            var n = Settlements.newSettlement(state, "Hollow", 40, 30, 10, 0, 1);
            state.settlements = [n];
            var sent = false;
            for (var d = 1; (d <= 20) && !sent; d++)
            {
                state.tick = Js.I(((double)(d) * (Time.DAY_LENGTH)) - 1);
                runs(state, 1);
                sent = Runs.runsOf(state).length > 0;
            }
            expect(sent).toBe(true);
            expect(Js.NN(Js.Get(Runs.runsOf(state), 0)).people).toBe(Runs.RUN_PARTY);
            expect(n.people).toBe(10 - Runs.RUN_PARTY);
        }

        [Fact(DisplayName = "neighbours' scavenging runs > out to the ruin, a search, and home with food")]
        public void T003_out_to_the_ruin_a_search_and_home_with_food()
        {
            var state = world();
            var n = Settlements.newSettlement(state, "Hollow", 40, 30, 7, 0, 1);
            state.settlements = [n];
            state.settings = Js.With(Js.CopyAs<Settings>(state.settings), __s1 => { __s1.zombies = 0; });
            n.people -= 3;
            var r = run(state);
            runs(state, ((double)(20) * (Time.TICKS_PER_HOUR)));
            expect(Runs.runsOf(state)).toHaveLength(0);
            expect(n.people).toBe(7);
            expect(n.food).toBe(((double)(3) * (Runs.RUN_FIND)));
            expect(r.phase).toBe("back");
        }

        [Fact(DisplayName = "neighbours' scavenging runs > caught by the dead to the last: bodies, their food, and a mark on your map")]
        public void T004_caught_by_the_dead_to_the_last_bodies_their_food_and_a_mark_()
        {
            var state = world();
            var n = Settlements.newSettlement(state, "Hollow", 40, 30, 7, 0, 1);
            state.settlements = [n];
            state.settings = Js.With(Js.CopyAs<Settings>(state.settings), __s2 => { __s2.zombies = 1000000000.0; });
            state.harsh = true;
            run(state, new ScavRun { phase = "search", untilTick = 1000000000.0, x = Js.I(50), y = Js.I(30), food = 4 });
            state.tick = Js.I(Time.TICKS_PER_HOUR - 1);
            for (var h = 0; (h < 4) && (Runs.runsOf(state).length > 0); h++)
            {
                runs(state, Time.TICKS_PER_HOUR);
            }
            expect(Runs.runsOf(state)).toHaveLength(0);
            expect(state.pointsOfInterest.some((p) => p.label == Runs.DEAD_LABEL)).toBe(true);
            expect(state.items.filter((i) => i.kind == "corpse")).toHaveLength(3);
            expect(state.items.filter((i) => i.kind == "food")).toHaveLength(4);
        }

        [Fact(DisplayName = "neighbours' scavenging runs > meeting one of yours: they think better of you and say where they are going")]
        public void T005_meeting_one_of_yours_they_think_better_of_you_and_say_where_()
        {
            var state = world();
            var s = Js.NN(Js.Get(state.survivors, 0));
            var n = Settlements.newSettlement(state, "Hollow", s.pos.x + 2, s.pos.y, 7, 0, 1);
            state.settlements = [n];
            state.settings = Js.With(Js.CopyAs<Settings>(state.settings), __s3 => { __s3.zombies = 0; });
            var rep = n.reputation;
            var r = run(state);
            runs(state, 2);
            expect(r.met).toBe(true);
            expect(n.reputation).toBe(rep + 3);
            expect(state.pointsOfInterest.some((p) => (p.x == r.ruin.x) && (p.y == r.ruin.y))).toBe(true);
        }
    }

    /// <summary>the truck on the road</summary>
    public class D006_the_truck_on_the_road
    {

        public static WithTruckResult withTruck()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 8, map = ["s" + ".".repeat(59), ".".repeat(60)] });
            state.settings = Js.With(Js.CopyAs<Settings>(state.settings), __s4 => { __s4.zombies = 0; });
            state.pointsOfInterest.push(new() { id = "poi1", x = Js.I(59), y = Js.I(0), label = "ruin" });
            var v = Vehicles.addVehicle(state, 2, 1, 0, kind: "truck");
            Fog.revealAround(state, new Point { x = Js.I(2), y = Js.I(1) }, 3);
            return new WithTruckResult { state = state, v = v };
        }

        [Fact(DisplayName = "the truck on the road > drives out, waits, and comes back; nobody else takes it meanwhile")]
        public void T007_drives_out_waits_and_comes_back_nobody_else_takes_it_meanwhi()
        {
            var __d5 = withTruck();
            var state = __d5.state;
            var v = __d5.v;
            var s = Js.NN(Js.Get(state.survivors, 0));
            expect(Commands.applyCommand(state, new Command { type = "sendExpedition", survivorIds = [s.id], poiId = "poi1" }).ok).toBe(true);
            var x = Js.NN(Js.Get(state.expeditions, 0));
            expect(x.truckId).toBe(v.id);
            expect(v.driverId).toBe($"x:{Js.Str(x.id)}");
            double furthest = 0;
            while ((state.expeditions.length > 0) && (state.tick < ((double)(Time.DAY_LENGTH) * (2))))
            {
                Tick.tick(state);
                furthest = JsMath.max(furthest, v.pos.x);
            }
            expect(furthest).toBeGreaterThanOrEqual(55);
            expect(v.driverId).toBeNull();
            expect(JsMath.abs(v.pos.x - x.home.x) + JsMath.abs(v.pos.y - x.home.y)).toBeLessThanOrEqual(3);
            expect(Vehicles.vehiclesOf(state)).toHaveLength(1);
        }

        [Fact(DisplayName = "the truck on the road > caught on the road with nobody left, the truck stands where it stopped")]
        public void T008_caught_on_the_road_with_nobody_left_the_truck_stands_where_i()
        {
            var __d6 = withTruck();
            var state = __d6.state;
            var v = __d6.v;
            state.settings = Js.With(Js.CopyAs<Settings>(state.settings), __s7 => { __s7.zombies = 1000000000.0; });
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "sendExpedition", survivorIds = [s.id], poiId = "poi1" });
            while ((state.expeditions.length > 0) && (state.tick < Time.DAY_LENGTH))
            {
                Tick.tick(state);
            }
            expect(state.expeditions).toHaveLength(0);
            expect(v.driverId).toBeNull();
            expect(state.events.some((e) => e.message.includes("truck stands empty"))).toBe(true);
        }
    }
}
