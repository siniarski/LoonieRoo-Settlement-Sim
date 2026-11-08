// Ported from tests/control.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class ControlTest
{

    public static readonly string WELL = Js.NN((ScenarioMod.ENTITY_CHARS)["well"]);

    public static void run(SimState state, double ticks)
    {
        for (var t = 0; t < ticks; t++)
        {
            Tick.tick(state);
            expect(Invariants.checkInvariants(state), DebugText.dumpText(state)).toEqual(Js.Arr([]));
        }
    }

    public static CommandResult cmd(SimState state, Command c)
    {
        var r = Commands.applyCommand(state, c);
        expect(r, Js.Json_stringify(c)).toMatchObject(Js.Obj(("ok", (object?)(true))));
        return r;
    }

    /// <summary>direct control</summary>
    public class D001_direct_control
    {

        [Fact(DisplayName = "direct control > taking over a survivor stops their work; handing back lets them pick it up again")]
        public void T002_taking_over_a_survivor_stops_their_work_handing_back_lets_th()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["sw...", "..+.."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            run(state, 2);
            expect(s.jobId).not.toBeNull();
            cmd(state, new Command { type = "takeControl", survivorId = s.id });
            expect(s.jobId).toBeNull();
            run(state, 50);
            expect(s.jobId).toBeNull();
            expect(state.blueprints).toHaveLength(1);
            cmd(state, new Command { type = "takeControl", survivorId = null });
            run(state, 5);
            expect(s.jobId).not.toBeNull();
        }

        [Fact(DisplayName = "direct control > a held direction moves you smoothly, a tile every TICKS_PER_TILE ticks, and stops when let go")]
        public void T003_a_held_direction_moves_you_smoothly_a_tile_every_TICKS_PER_T()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s......."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            cmd(state, new Command { type = "takeControl", survivorId = s.id });
            cmd(state, new Command { type = "steer", dx = 1, dy = 0 });
            run(state, 1);
            expect(s.at.x).toBe(Motion.centre(new Point { x = Js.I(0), y = Js.I(0) }).x + ((double)(Motion.SUB) / (Survivors.TICKS_PER_TILE)));
            run(state, ((double)(Survivors.TICKS_PER_TILE) * (3)) - 1);
            expect(s.at.x).toBe(Motion.centre(new Point { x = Js.I(3), y = Js.I(0) }).x);
            expect(s.pos).toEqual(Js.Obj(("x", (object?)(3)), ("y", (object?)(0))));
            cmd(state, new Command { type = "steer", dx = 0, dy = 0 });
            var stopped = s.at.x;
            run(state, 5);
            expect(s.at.x).toBe(stopped);
            expect(s.move).toBeNull();
        }

        [Fact(DisplayName = "direct control > any direction is the same speed; walls stop you, and you slide along them")]
        public void T004_any_direction_is_the_same_speed_walls_stop_you_and_you_slide()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [".#....", ".#....", "s#....", "......"] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            cmd(state, new Command { type = "takeControl", survivorId = s.id });
            s.pos = new Point { x = Js.I(3), y = Js.I(1) };
            s.at = Motion.centre(s.pos);
            var start = Js.Clone(s.at);
            cmd(state, new Command { type = "steer", dx = 1, dy = 1 });
            run(state, 2);
            var moved = JsMath.sqrt((JsMath.pow((s.at.x - start.x), 2)) + (JsMath.pow((s.at.y - start.y), 2)));
            expect(JsMath.abs(moved - ((double)(((double)(2) * (Motion.SUB))) / (Survivors.TICKS_PER_TILE)))).toBeLessThan(2);
            s.at = Motion.centre(new Point { x = Js.I(0), y = Js.I(2) });
            s.pos = new Point { x = Js.I(0), y = Js.I(2) };
            cmd(state, new Command { type = "steer", dx = 1, dy = -1 });
            run(state, 12);
            expect(s.pos.x).toBe(0);
            expect(s.at.x).toBeLessThanOrEqual(Motion.SUB - Motion.BODY_RADIUS);
            expect(s.pos.y).toBe(0);
        }

        [Fact(DisplayName = "direct control > steer takes any direction (first person): the same speed, the facing the nearest of eight")]
        public void T004b_steer_takes_any_direction()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["......", "......", "..s...", "......", "......"] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            cmd(state, new Command { type = "takeControl", survivorId = s.id });
            var start = Js.Clone(s.at);
            cmd(state, new Command { type = "steer", dx = 59, dy = -24 });
            run(state, 2);
            var moved = JsMath.sqrt((JsMath.pow((s.at.x - start.x), 2)) + (JsMath.pow((s.at.y - start.y), 2)));
            expect(JsMath.abs(moved - ((double)(((double)(2) * (Motion.SUB))) / (Survivors.TICKS_PER_TILE)))).toBeLessThan(2);
            // East a bit north: more east than north, and some north.
            expect(s.at.x - start.x).toBeGreaterThan(start.y - s.at.y);
            expect(start.y - s.at.y).toBeGreaterThan(0);
            expect(Js.NN(state.control).facing).toEqual(Js.Obj(("x", (object?)(1)), ("y", (object?)(0))));
            expect(Commands.applyCommand(state, new Command { type = "steer", dx = ControlMod.AIM_MAX + 1, dy = 0 }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "steer", dx = 0.5, dy = 0 }).ok).toBe(false);
        }

        [Fact(DisplayName = "direct control > swinging at a zombie next to you hits it, and not faster than a guard could")]
        public void T005_swinging_at_a_zombie_next_to_you_hits_it_and_not_faster_than()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s."] });
            state.zombies = [];
            Commands.applyCommand(state, new Command { type = "spawnZombie", x = Js.I(1), y = Js.I(0) });
            var z = Js.NN(Js.Get(state.zombies, 0));
            z.kind = "static";
            var s = Js.NN(Js.Get(state.survivors, 0));
            cmd(state, new Command { type = "takeControl", survivorId = s.id });
            var before = z.health;
            cmd(state, new Command { type = "swing", x = Js.I(1), y = Js.I(0) });
            expect(z.health).toBeLessThan(before);
            expect(Commands.applyCommand(state, new Command { type = "swing", x = Js.I(1), y = Js.I(0) }).ok).toBe(false);
        }

        [Fact(DisplayName = "direct control > use: picks up a weapon into the weapon slot, eats food (taking time), searches a crate")]
        public void T006_use_picks_up_a_weapon_into_the_weapon_slot_eats_food_taking_()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["/sf", ".c."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            cmd(state, new Command { type = "takeControl", survivorId = s.id });
            cmd(state, new Command { type = "use", x = Js.I(0), y = Js.I(0) });
            expect(s.weapon).not.toBeNull();
            s.satiety = 100;
            cmd(state, new Command { type = "use", x = Js.I(2), y = Js.I(0) });
            run(state, 5);
            expect(s.satiety).toBeLessThan(200);
            run(state, 40);
            expect(s.satiety).toBeGreaterThan(1000);
            expect(s.satiety).toBeLessThanOrEqual(Food.SATIETY_MAX);
            var items = state.items.length;
            var crate = Js.NN(state.furniture.find((f) => f.kind == "crate"));
            var loot = crate.loot;
            cmd(state, new Command { type = "use", x = Js.I(1), y = Js.I(1) });
            run(state, 60);
            expect(state.items.length).toBe(items + loot);
            expect(state.items.filter((i) => i.inside == crate.id)).toHaveLength(loot);
            expect(crate.loot).toBe(0);
        }

        [Fact(DisplayName = "direct control > use: drink at a well, sleep in a bed until rested, wake when hurt")]
        public void T007_use_drink_at_a_well_sleep_in_a_bed_until_rested_wake_when_hu()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [$"s{Js.Str(WELL)}.", "b.."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            cmd(state, new Command { type = "takeControl", survivorId = s.id });
            s.hydration = 10;
            cmd(state, new Command { type = "use", x = Js.I(1), y = Js.I(0) });
            run(state, 30);
            expect(s.hydration).toBeGreaterThan(Water.HYDRATION_MAX - 40);
            s.rest = 100;
            cmd(state, new Command { type = "use", x = Js.I(0), y = Js.I(1) });
            run(state, 6);
            expect(s.pos).toEqual(Js.Obj(("x", (object?)(0)), ("y", (object?)(1))));
            cmd(state, new Command { type = "use", x = Js.I(0), y = Js.I(1) });
            run(state, 100);
            expect(s.rest).toBeGreaterThan(400);
            s.hurtTick = state.tick;
            Tick.tick(state);
            expect(Js.NN(state.control).busy).toBeNull();
        }

        [Fact(DisplayName = "direct control > use: medicine you carry cures your infection; one on the ground you need is used, not picked up")]
        public void T008_use_medicine_you_carry_cures_your_infection_one_on_the_groun()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["sm."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            cmd(state, new Command { type = "takeControl", survivorId = s.id });
            s.health = 500;
            cmd(state, new Command { type = "use", x = Js.I(1), y = Js.I(0) });
            run(state, 40);
            expect(s.health).toBeGreaterThan(500);
            expect(state.items.filter((i) => i.kind == "bandage")).toEqual(Js.Arr([]));
            s.infection = 100;
            var med = Items.spawnItem(state, "medicine", 0, 0);
            med.pos = null;
            med.carriedBy = s.id;
            s.carrying = med.id;
            cmd(state, new Command { type = "use", x = Js.I(0), y = Js.I(0) });
            run(state, 40);
            expect(s.infection).toBeNull();
            expect(s.carrying).toBeNull();
        }

        [Fact(DisplayName = "direct control > moving interrupts a search")]
        public void T009_moving_interrupts_a_search()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["sc..", "...."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            cmd(state, new Command { type = "takeControl", survivorId = s.id });
            cmd(state, new Command { type = "use", x = Js.I(1), y = Js.I(0) });
            run(state, 10);
            cmd(state, new Command { type = "steer", dx = 0, dy = 1 });
            run(state, 60);
            expect(Js.NN(state.control).busy).toBeNull();
            expect(state.items).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "direct control > control ends when the survivor dies")]
        public void T010_control_ends_when_the_survivor_dies()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.", ".."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            cmd(state, new Command { type = "takeControl", survivorId = s.id });
            s.health = 0;
            Tick.tick(state);
            expect(state.control).toBeNull();
        }

        [Fact(DisplayName = "direct control > replays exactly")]
        public void T011_replays_exactly()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = ["s.....", ".c..f.", "......"] });
            var start = States.cloneState(state);
            var s = Js.NN(Js.Get(state.survivors, 0));
            cmd(state, new Command { type = "takeControl", survivorId = s.id });
            cmd(state, new Command { type = "steer", dx = 1, dy = 1 });
            run(state, 9);
            cmd(state, new Command { type = "steer", dx = 0, dy = 0 });
            run(state, 6);
            Commands.applyCommand(state, new Command { type = "use", x = Js.I(1), y = Js.I(1) });
            run(state, 50);
            expect(Replay.replay(start, state.commandLog, state.tick)).toEqual(state);
        }
    }
}
