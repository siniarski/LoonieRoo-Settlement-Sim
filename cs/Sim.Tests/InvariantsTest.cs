// Ported from tests/invariants.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class InvariantsTest
{

    public static readonly List<string> MAP = ["........................", ".######.....######..www.", ".#b.,.#.....#....#..www.", ".#.s..D.....D..s.#..www.", ".#....#..,..#....#......", ".######.....###D##..++..", "..........s.........d...", "....,...~~~.....,.......", "..www......ff..mm..c.c..", "...................c..k.", "..####D####.............", "..#...t...#.............", "..#########............."];

    /// <summary>Holding a goal while standing still is fine for one tick (the claim tick).</summary>
    public const int MAX_TICKS_WITH_GOAL_BUT_STILL = 1;

    public const int MAX_IDLE_WITH_DOABLE_JOB = 20;

    public const int MAX_TICKS_ON_ONE_JOB = 600;

    public const int TICKS = 4000;

    public static Command? randomCommand(SimState state, RngHolder driver)
    {
        var roll = Rng.randomInt(driver, 0, 1000);
        var x = Rng.randomInt(driver, 0, state.grid.width);
        var y = Rng.randomInt(driver, 0, state.grid.height);
        if (state.survivors.length == 0)
        {
            return new Command { type = "spawnSurvivor", x = Js.I(x), y = Js.I(y) };
        }
        var survivor = Js.NN(Js.Get(state.survivors, Rng.randomInt(driver, 0, state.survivors.length)));
        if (roll < 15)
        {
            return new Command { type = "designateBuild", x = Js.I(x), y = Js.I(y), structure = "wall" };
        }
        if (roll < 22)
        {
            return new Command { type = "designateBuild", x = Js.I(x), y = Js.I(y), structure = "door" };
        }
        if (roll < 25)
        {
            return new Command { type = "designateBuild", x = Js.I(x), y = Js.I(y), structure = "bed" };
        }
        if (roll < 31)
        {
            return new Command { type = "designateRemove", x = Js.I(x), y = Js.I(y) };
        }
        if (roll < 34)
        {
            return new Command { type = "cancelDesignation", x = Js.I(x), y = Js.I(y) };
        }
        if (roll < 36)
        {
            return new Command { type = "designateRoom", x = Js.I(x), y = Js.I(y), purpose = (roll < 35) ? "storage" : "housing" };
        }
        if (roll < 38)
        {
            return new Command { type = "moveSurvivor", survivorId = survivor.id, x = Js.I(x), y = Js.I(y) };
        }
        if (roll < 45)
        {
            var jobType = Js.NN(Js.Get(JobTypes.JOB_TYPES, Rng.randomInt(driver, 0, JobTypes.JOB_TYPES.length)));
            return new Command { type = "setPriority", survivorId = survivor.id, jobType = jobType, priority = Rng.randomInt(driver, 0, 5) };
        }
        if (roll < 50)
        {
            return new Command { type = "placeWall", x = Js.I(x), y = Js.I(y) };
        }
        if (roll < 54)
        {
            return new Command { type = "clearTile", x = Js.I(x), y = Js.I(y) };
        }
        if (roll < 57)
        {
            return new Command { type = "setRest", survivorId = survivor.id, rest = Rng.randomInt(driver, 0, 2001) };
        }
        if (roll < 60)
        {
            return new Command { type = "setSatiety", survivorId = survivor.id, satiety = Rng.randomInt(driver, 0, 3001) };
        }
        if (roll < 62)
        {
            return new Command { type = "spawnFood", x = Js.I(x), y = Js.I(y) };
        }
        if (roll < 70)
        {
            return new Command { type = "designateScavenge", x = Js.I(x), y = Js.I(y) };
        }
        if (roll < 72)
        {
            return new Command { type = "setHealth", survivorId = survivor.id, health = Rng.randomInt(driver, 0, 4) };
        }
        if (roll < 73)
        {
            return new Command { type = "spawnSurvivor", x = Js.I(x), y = Js.I(y) };
        }
        if (roll < 76)
        {
            return new Command { type = "spawnZombie", x = Js.I(x), y = Js.I(y) };
        }
        if (roll < 82)
        {
            return new Command { type = "designateFarm", x = Js.I(x), y = Js.I(y) };
        }
        if (roll < 85)
        {
            return new Command { type = "setHealth", survivorId = survivor.id, health = Rng.randomInt(driver, 150, 700) };
        }
        if ((roll < 90) && (state.wanderers.length > 0))
        {
            var w = Js.NN(Js.Get(state.wanderers, roll % state.wanderers.length));
            return new Command { type = (roll < 88) ? "acceptWanderer" : "dismissWanderer", wandererId = w.id };
        }
        if ((roll >= 90) && (roll < 94))
        {
            return new Command { type = "spawnWeapon", x = Js.I(x), y = Js.I(y) };
        }
        if ((roll >= 94) && (roll < 96))
        {
            return new Command { type = "designateRoom", x = Js.I(x), y = Js.I(y), purpose = "armory" };
        }
        if ((roll >= 96) && (roll < 100))
        {
            return new Command { type = "designateBuild", x = Js.I(x), y = Js.I(y), structure = "table" };
        }
        if ((roll >= 102) && (roll < 105))
        {
            return new Command { type = "designateBuild", x = Js.I(x), y = Js.I(y), structure = "workbench" };
        }
        if ((roll >= 105) && (roll < 108))
        {
            return new Command { type = "setCraftTarget", kind = (roll < 106) ? "weapon" : "bandage", count = roll % 4 };
        }
        if (((roll >= 111) && (roll < 115)) && Breach.isStructure(Grids.getTile(state.grid, x, y)))
        {
            return new Command { type = "damageStructure", x = Js.I(x), y = Js.I(y), amount = ((double)(40) * ((1 + (roll % 5)))) };
        }
        if ((roll >= 115) && (roll < 118))
        {
            return new Command { type = "designateExplore", x0 = Js.I(12), y0 = Js.I(10), x1 = Js.I(23), y1 = Js.I(12) };
        }
        if ((roll >= 108) && (roll < 111))
        {
            return new Command { type = "designateRoom", x = Js.I(14), y = Js.I(3), purpose = (roll < 110) ? "recroom" : "storage" };
        }
        if ((roll >= 100) && (roll < 102))
        {
            var tables = state.furniture.filter((f) => f.kind == "table");
            var at = (tables.length > 0) ? Js.NN(Js.Get(tables, roll % tables.length)) : new Point { x = Js.I(x), y = Js.I(y) };
            return new Command { type = "designateRoom", x = Js.I(at.x), y = Js.I(at.y), purpose = "messhall" };
        }
        return null;
    }

    /// <summary>stuck-state invariant</summary>
    public class D001_stuck_state_invariant
    {

        /// <summary>Outcomes seen across all seeds (tests in a file run in order).</summary>
        public static readonly JsSet<string> notesSeen = new JsSet<string>();

        public static double deaths = 0;

        /// <summary>Wanderer outcomes seen across all seeds.</summary>
        public static readonly JsSet<string> wandererEvents = new JsSet<string>();

        public static double mealsAtTables = 0;

        [Theory(DisplayName = "stuck-state invariant > holds under random designations, orders, and wall edits (seed ...)")]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void T002_holds_under_random_designations_orders_and_wall_edits_seed(double seed)
        {
            var initial = ScenarioMod.loadScenario(new Scenario { seed = seed, map = MAP, rooms = [new() { x = Js.I(3), y = Js.I(3), purpose = "housing" }, new() { x = Js.I(14), y = Js.I(3), purpose = "storage" }, new() { x = Js.I(3), y = Js.I(11), purpose = "messhall" }] });
            initial.craftTargets = new() { ["weapon"] = 1, ["bandage"] = 2, ["medicine"] = 0, ["ammo"] = 0, ["gun"] = 0, ["coat"] = 0, ["armour"] = 0, ["boots"] = 0, ["cart"] = 0, ["axe"] = 0, ["hammer"] = 0, ["medkit"] = 0 };
            for (var y = 10; y <= 12; y++)
            {
                for (var x = 12; x <= 23; x++)
                {
                    Grids.setExplored(initial.grid, ((double)(y) * (initial.grid.width)) + x, 0);
                }
            }
            initial.tick = Js.I(((double)(12) * (200)));
            var state = States.cloneState(initial);
            RngHolder driver = new RngHolder { rng = Rng.seedRng(((double)(seed) * (7919))) };
            var stillWithGoal = new JsMap<string, double>();
            var before = new JsMap<string, TestBefore>();
            var idleWithWork = new JsMap<string, double>();
            var onJob = new JsMap<string, TestOnJob>();
            for (var t = 0; t < TICKS; t++)
            {
                var cmd = randomCommand(state, driver);
                var ordered = ((cmd?.type == "moveSurvivor") && Commands.applyCommand(state, cmd).ok) ? cmd.survivorId : null;
                if (Js.T(cmd) && (cmd.type != "moveSurvivor"))
                {
                    Commands.applyCommand(state, cmd);
                }
                var versionBefore = state.walkabilityVersion;
                Tick.tick(state);
                var pathCut = state.walkabilityVersion != versionBefore;
                var problems = Invariants.checkInvariants(state);
                expect(problems, $"tick {Js.Str(state.tick)}\n{Js.Str(DebugText.dumpText(state))}").toEqual(Js.Arr([]));
                foreach (var s in Js.Iter(state.survivors))
                {
                    var where = $"{Js.Str(s.name)} at tick {Js.Str(state.tick)}: {Js.Str(s.note)}\n{Js.Str(DebugText.dumpText(state))}";
                    if (Js.T(s.note))
                    {
                        notesSeen.add(Js.NN(Js.Get(s.note.split(" "), 0)));
                    }
                    if (s.note == "ate a meal at a table")
                    {
                        mealsAtTables += 1;
                    }
                    var prev = before.get(s.id);
                    var needs = $"{Js.Str(Js.T(s.sleep) ? Js.Str("sleep") : Js.Str(""))}{Js.Str(Js.T(s.meal) ? Js.Str("meal") : Js.Str(""))}{Js.Str(Js.T(s.treatment) ? Js.Str("treat") : Js.Str(""))}{Js.Str(Js.T(s.arming) ? Js.Str("arm") : Js.Str(""))}";
                    var movedAside = ((prev != null) && !prev.moving) && (prev.pos != $"{Js.Str(s.pos.x)},{Js.Str(s.pos.y)}");
                    var needKickedIn = ((prev != null) && (prev.needs != needs)) && (needs != "");
                    var jobSwitched = (((prev != null) && (prev.jobId != null)) && (s.jobId != null)) && (prev.jobId != s.jobId);
                    before.set(s.id, new() { pos = $"{Js.Str(s.pos.x)},{Js.Str(s.pos.y)}", moving = s.move != null, needs = needs, jobId = s.jobId });
                    var excused = (((Js.T((Js.T((s.id == ordered) || Js.T(movedAside))) || Js.T(needKickedIn))) || pathCut) is var __or0 && Js.T(__or0) ? __or0 : jobSwitched);
                    var still = ((Js.T(s.goal) && !Js.T(s.move)) && !Js.T(excused)) ? ((Js.Q(stillWithGoal.get(s.id), 0)) + 1) : 0;
                    stillWithGoal.set(s.id, still);
                    expect(still, $"stuck with goal {Js.Str(Js.Json_stringify(s.goal))}: {Js.Str(where)}").toBeLessThanOrEqual(MAX_TICKS_WITH_GOAL_BUT_STILL);
                    var idle = ((((((!Js.T(s.jobId) && !Js.T(s.goal)) && !Js.T(s.move)) && !Js.T(s.sleep)) && !Js.T(s.meal)) && !Js.T(s.arming)) && !Js.T(s.treatment)) && !Js.T(s.breakdown);
                    var idleTicks = (Js.T(Js.T(idle) && Js.T(JobChoice.chooseJob(state, s)))) ? ((Js.Q(idleWithWork.get(s.id), 0)) + 1) : 0;
                    idleWithWork.set(s.id, idleTicks);
                    expect(idleTicks, $"idle with doable work: {Js.Str(where)}").toBeLessThanOrEqual(MAX_IDLE_WITH_DOABLE_JOB);
                    var current = onJob.get(s.id);
                    if (!Js.T(s.jobId))
                    {
                        onJob.delete(s.id);
                    }
                    else if (current?.id != s.jobId)
                    {
                        onJob.set(s.id, new() { id = s.jobId, since = state.tick });
                    }
                    else
                    {
                        expect(state.tick - current.since, $"on one job too long: {Js.Str(where)}").toBeLessThanOrEqual(MAX_TICKS_ON_ONE_JOB);
                    }
                }
            }
            foreach (var e in Js.Iter(state.events))
            {
                foreach (var outcome in Js.Iter((List<string>)["a wanderer", "joined", "turned away", "gave up", "walled out"]))
                {
                    if (e.message.includes(outcome))
                    {
                        wandererEvents.add(outcome);
                    }
                }
            }
            deaths += state.events.filter((e) => e.message.includes(" died of ")).length;
            var replayed = Replay.replay(initial, state.commandLog, state.tick);
            expect(Js.Json_stringify(replayed)).toBe(Js.Json_stringify(state));
        }

        [Fact(DisplayName = "stuck-state invariant > saw survivors die along the way (death handling is exercised)")]
        public void T003_saw_survivors_die_along_the_way_death_handling_is_exercised()
        {
            expect(deaths).toBeGreaterThan(0);
        }

        [Fact(DisplayName = "stuck-state invariant > saw meals carried to a mess hall table")]
        public void T004_saw_meals_carried_to_a_mess_hall_table()
        {
            expect(mealsAtTables).toBeGreaterThan(0);
        }

        [Fact(DisplayName = "stuck-state invariant > saw wanderers arrive, join, and get turned away")]
        public void T005_saw_wanderers_arrive_join_and_get_turned_away()
        {
            expect(Js.ListOf(wandererEvents).sort()).toEqual(Vitest.arrayContaining(Js.Arr(["a wanderer", "joined", "turned away"])));
        }

        [Fact(DisplayName = "stuck-state invariant > exercised every outcome across the seeds (guards against a vacuous run)")]
        public void T006_exercised_every_outcome_across_the_seeds_guards_against_a_va()
        {
            List<string> expected = ["took", "picked", "built", "removed", "delivered", "stored", "cleaned", "arrived", "going", "asleep", "no", "woke", "eating", "ate", "scavenged", "attacked", "killed", "sowed", "harvested", "treated", "armed", "repaired", "crafted", "brought", "relaxing", "having"];
            expect(expected.filter((n) => !notesSeen.has(n)), $"seen: {Js.Str(Js.ListOf(notesSeen).join(", "))}").toEqual(Js.Arr([]));
        }
    }
}
