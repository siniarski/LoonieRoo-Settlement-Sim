// Ported from tests/weapons.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class WeaponsTest
{

    /// <summary>23:00 on day 1, just past a spawn boundary.</summary>
    public static readonly double NIGHT = ((double)(17) * (Time.TICKS_PER_HOUR)) + 1;

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

    public static SimState setup(List<string> map, JsRecord<double>? jobs = null, List<ScenarioRoomsItem>? rooms = null)
    {
        jobs ??= new() {  };
        rooms ??= new();
        var state = ScenarioMod.loadScenario(new Scenario { seed = 4, map = map, rooms = rooms });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = Js.RecordFrom(JobTypes.NO_JOBS, __r0 => { Js.MergeInto(__r0, jobs); });
        }
        return state;
    }

    public static List<Item> weapons(SimState state)
    {
        return state.items.filter((i) => i.kind == "weapon");
    }

    /// <summary>Lets a guard pick up the weapon on the map by day.</summary>
    public static void arm(SimState state, Survivor s)
    {
        runUntil(state, (_) => s.weapon != null);
    }

    /// <summary>Ticks for a guard to kill a zombie standing next to them, at night.</summary>
    public static double timeToKill(bool armed)
    {
        var state = setup(["s/.", "..."], new() { ["guard"] = 3 });
        var s = Js.NN(Js.Get(state.survivors, 0));
        if (armed)
        {
            arm(state, s);
        }
        else
        {
            state.items = [];
        }
        state.tick = Js.I(NIGHT);
        Zombies.addZombie(state, s.pos.x, s.pos.y + 1);
        return runUntil(state, (_) => state.zombies.length == 0);
    }

    /// <summary>weapons</summary>
    public class D001_weapons
    {

        [Fact(DisplayName = "weapons > a guard picks up a free weapon between jobs and carries it")]
        public void T002_a_guard_picks_up_a_free_weapon_between_jobs_and_carries_it()
        {
            var state = setup(["s...../"], new() { ["guard"] = 3 });
            var s = Js.NN(Js.Get(state.survivors, 0));
            Tick.tick(state);
            expect(s.note).toBe("going for a weapon at (6, 0)");
            arm(state, s);
            var w = (weapons(state))[0];
            expect(s.weapon).toBe(Js.NN(w).id);
            expect(w).toMatchObject(Js.Obj(("pos", (object?)(null)), ("carriedBy", (object?)(s.id)), ("reservedBy", (object?)(null))));
            expect(s.note).toBe("armed with a weapon");
            expect(s.carrying).toBeNull();
        }

        [Fact(DisplayName = "weapons > survivors who never guard leave weapons alone")]
        public void T003_survivors_who_never_guard_leave_weapons_alone()
        {
            var state = setup(["s...../"], new() { ["build"] = 3, ["haul"] = 3 });
            for (var i = 0; i < 60; i++)
            {
                Tick.tick(state);
            }
            expect(Js.NN(Js.Get(state.survivors, 0)).weapon).toBeNull();
            expect(Js.NN(Js.Get(weapons(state), 0)).reservedBy).toBeNull();
        }

        [Fact(DisplayName = "weapons > two guards never go for the same weapon")]
        public void T004_two_guards_never_go_for_the_same_weapon()
        {
            var state = setup(["s..../...s"], new() { ["guard"] = 3 });
            Tick.tick(state);
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
            expect(state.survivors.filter((s) => s.arming != null)).toHaveLength(1);
            runUntil(state, (_) => state.survivors.some((s) => Js.T(s.weapon)));
            expect(state.survivors.filter((s) => Js.T(s.weapon))).toHaveLength(1);
        }

        [Fact(DisplayName = "weapons > an armed guard kills a zombie in half the blows")]
        public void T005_an_armed_guard_kills_a_zombie_in_half_the_blows()
        {
            var unarmed = timeToKill(false);
            var armed = timeToKill(true);
            expect(armed).toBeLessThan(unarmed);
            expect(unarmed - armed).toBeGreaterThanOrEqual(19);
        }

        [Fact(DisplayName = "weapons > a weapon breaks after ... kills")]
        public void T006_a_weapon_breaks_after_kills()
        {
            var state = setup(["s/.", "..."], new() { ["guard"] = 3 });
            var s = Js.NN(Js.Get(state.survivors, 0));
            arm(state, s);
            Js.NN(Js.Get(weapons(state), 0)).wear = Weapons.specOf(Js.Get(weapons(state), 0)).durability - 1;
            state.tick = Js.I(NIGHT);
            Zombies.addZombie(state, s.pos.x, s.pos.y + 1);
            runUntil(state, (_) => state.zombies.length == 0);
            expect(s.weapon).toBeNull();
            expect(weapons(state)).toEqual(Js.Arr([]));
            expect(state.events.map((e) => e.message)).toContain($"{Js.Str(s.name)}'s weapon broke");
        }

        [Fact(DisplayName = "weapons > a weapon wears by one per armed kill")]
        public void T007_a_weapon_wears_by_one_per_armed_kill()
        {
            var state = setup(["s/.", "..."], new() { ["guard"] = 3 });
            var s = Js.NN(Js.Get(state.survivors, 0));
            arm(state, s);
            state.tick = Js.I(NIGHT);
            Zombies.addZombie(state, s.pos.x, s.pos.y + 1);
            runUntil(state, (_) => state.zombies.length == 0);
            expect(Js.NN(Js.Get(weapons(state), 0)).wear).toBe(1);
        }

        [Fact(DisplayName = "weapons > a dead survivor's weapon falls where they died")]
        public void T008_a_dead_survivor_s_weapon_falls_where_they_died()
        {
            var state = setup(["s/...", "....."], new() { ["guard"] = 3 });
            var s = Js.NN(Js.Get(state.survivors, 0));
            arm(state, s);
            var at = Js.Clone(s.pos);
            Needs.killSurvivor(state, s, "a test");
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
            expect(Js.Missing(weapons(state), 0) ? null : (object?)(Js.Get(weapons(state), 0))).toMatchObject(Js.Obj(("carriedBy", (object?)(null)), ("pos", (object?)(at))));
        }

        [Fact(DisplayName = "weapons > a direct order interrupts fetching a weapon and frees it")]
        public void T009_a_direct_order_interrupts_fetching_a_weapon_and_frees_it()
        {
            var state = setup(["s...../", "......."], new() { ["guard"] = 3 });
            var s = Js.NN(Js.Get(state.survivors, 0));
            Tick.tick(state);
            expect(s.arming).not.toBeNull();
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = s.id, x = Js.I(0), y = Js.I(1) });
            expect(s.arming).toBeNull();
            expect(Js.NN(Js.Get(weapons(state), 0)).reservedBy).toBeNull();
        }

        [Fact(DisplayName = "weapons > getting hungry drops the weapon errand for a meal")]
        public void T010_getting_hungry_drops_the_weapon_errand_for_a_meal()
        {
            var state = setup(["f.s...../"], new() { ["guard"] = 3 });
            var s = Js.NN(Js.Get(state.survivors, 0));
            Tick.tick(state);
            expect(s.arming).not.toBeNull();
            Commands.applyCommand(state, new Command { type = "setSatiety", survivorId = s.id, satiety = 0 });
            runUntil(state, (_) => s.meal != null);
            expect(s.arming).toBeNull();
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }
    }

    /// <summary>armory</summary>
    public class D011_armory
    {

        public static readonly List<string> MAP = ["#####.#####", "#...#.#...#", "#...D.D...#", "#...#.#...#", "#####.#####", "s..../.f..."];

        public static readonly ScenarioRoomsItem STORAGE = new ScenarioRoomsItem { x = Js.I(2), y = Js.I(2), purpose = "storage" };

        public static readonly ScenarioRoomsItem ARMORY = new ScenarioRoomsItem { x = Js.I(8), y = Js.I(2), purpose = "armory" };

        public static string roomOf(int x)
        {
            return (x < 5) ? "storage" : "armory";
        }
        public static string roomOf(double x) => roomOf(Js.I(x));

        [Fact(DisplayName = "armory > weapons are hauled to the armory, everything else to storage")]
        public void T012_weapons_are_hauled_to_the_armory_everything_else_to_storage()
        {
            var state = setup(MAP, new() { ["haul"] = 3 }, [STORAGE, ARMORY]);
            runUntil(state, (_) => (state.jobs.length == 0) && state.items.every((i) => Js.T(i.pos) && (i.pos.y < 4)), 800);
            var where = Js.Object_fromEntries(state.items.map((i) => (i.kind, roomOf(Js.NN(i.pos).x))));
            expect(where).toEqual(Js.Obj(("weapon", (object?)("armory")), ("food", (object?)("storage"))));
        }

        [Fact(DisplayName = "armory > without an armory, weapons go to storage")]
        public void T013_without_an_armory_weapons_go_to_storage()
        {
            var state = setup(MAP, new() { ["haul"] = 3 }, [STORAGE]);
            runUntil(state, (_) => (state.jobs.length == 0) && state.items.every((i) => Js.T(i.pos) && (i.pos.y < 4)), 800);
            expect(state.items.map((i) => roomOf(Js.NN(i.pos).x))).toEqual(Js.Arr(["storage", "storage"]));
        }

        [Fact(DisplayName = "armory > once an armory is complete, weapons kept in storage move to it")]
        public void T014_once_an_armory_is_complete_weapons_kept_in_storage_move_to_i()
        {
            var state = setup(MAP, new() { ["haul"] = 3 }, [STORAGE]);
            runUntil(state, (_) => (state.jobs.length == 0) && state.items.every((i) => Js.T(i.pos) && (i.pos.y < 4)), 800);
            Commands.applyCommand(state, Js.With(Js.CopyAs<Command>(ARMORY), __s1 => { __s1.type = "designateRoom"; }));
            runUntil(state, (_) => (state.jobs.length == 0) && (roomOf(Js.NN(Js.NN(Js.Get(weapons(state), 0)).pos).x) == "armory"), 800);
            expect(Js.NN(Js.NN(state.items.find((i) => i.kind == "food")).pos).x).toBeLessThan(5);
        }

        [Fact(DisplayName = "armory > an armory needs walls, a door, and 4 tiles; a cramped one is ignored")]
        public void T015_an_armory_needs_walls_a_door_and_4_tiles_a_cramped_one_is_ig()
        {
            List<string> map = ["###.#####", "#.D.#...#", "###.D...#", "....#####", "s../....."];
            var state = setup(map, new() { ["haul"] = 3 }, [new() { x = Js.I(1), y = Js.I(1), purpose = "armory" }, new() { x = Js.I(6), y = Js.I(1), purpose = "storage" }]);
            var status = Rooms.designationStatus(state, Js.NN(Js.Get(state.roomDesignations, 0)));
            expect(status.complete).toBe(false);
            expect(status.requirements.filter((r) => !r.met).map((r) => r.label)).toEqual(Js.Arr(["at least 4 tiles"]));
            runUntil(state, (_) => (state.jobs.length == 0) && (Js.NN(Js.NN(Js.Get(weapons(state), 0)).pos).y < 3), 800);
            expect(Js.NN(Js.NN(Js.Get(weapons(state), 0)).pos).x).toBeGreaterThan(4);
        }
    }
}
