// Ported from tests/wildlife.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class WildlifeTest
{

    /// <summary>Open ground, size x size, one survivor in the middle.</summary>
    public static SimState wild(double size = 90)
    {
        var rows = Js.ArrayFrom(size, (int y) => (y == (Js.Shr(size, 1))) ? ((".".repeat(Js.Shr(size, 1)) + "s") + ".".repeat((size - (Js.Shr(size, 1))) - 1)) : ".".repeat(size));
        var state = ScenarioMod.loadScenario(new Scenario { seed = 5, map = rows });
        state.settings = Js.With(Js.CopyAs<Settings>(state.settings), __s0 => { __s0.zombies = 0; });
        state.tick = Js.I(((double)(8) * (Time.TICKS_PER_HOUR)) + 1);
        return state;
    }

    /// <summary>Runs only the animals, n ticks (the rest of the sim left out: nobody walks off).</summary>
    public static void animals(SimState state, double n)
    {
        for (var k = 0; k < n; k++)
        {
            state.tick = Js.I(state.tick + 1);
            Animals.updateAnimals(state);
        }
    }

    /// <summary>wildlife</summary>
    public class D001_wildlife
    {

        [Fact(DisplayName = "wildlife > rabbits turn up out in the open round your people, and not on a small map")]
        public void T002_rabbits_turn_up_out_in_the_open_round_your_people_and_not_on()
        {
            var big = wild();
            animals(big, Time.DAY_LENGTH);
            var rabbits = big.animals.filter((a) => a.kind == "rabbit");
            expect(rabbits.length).toBeGreaterThan(0);
            var small = wild(40);
            animals(small, Time.DAY_LENGTH);
            expect(small.animals.filter((a) => a.kind != "deer")).toHaveLength(0);
        }

        [Fact(DisplayName = "wildlife > a rabbit bolts from someone close")]
        public void T003_a_rabbit_bolts_from_someone_close()
        {
            var state = wild();
            var s = Js.NN(Js.Get(state.survivors, 0));
            var r = Animals.addAnimal(state, "rabbit", s.pos.x + 2, s.pos.y);
            animals(state, 20);
            expect(Space.manhattan(r.pos, s.pos)).toBeGreaterThan(3);
        }

        [Fact(DisplayName = "wildlife > a hungry wolf hunts down a rabbit and is fed")]
        public void T004_a_hungry_wolf_hunts_down_a_rabbit_and_is_fed()
        {
            var state = wild();
            var s = Js.NN(Js.Get(state.survivors, 0));
            var w = Animals.addAnimal(state, "wolf", s.pos.x - 30, s.pos.y);
            w.hunger = Animals.WOLF_HUNGRY;
            var r = Animals.addAnimal(state, "rabbit", s.pos.x - 24, s.pos.y + 4);
            for (var k = 0; (k < 600) && state.animals.includes(r); k++)
            {
                animals(state, 1);
            }
            expect(state.animals.includes(r)).toBe(false);
            expect(w.hunger).toBeLessThan(Animals.WOLF_HUNGRY);
        }

        [Fact(DisplayName = "wildlife > a fed wolf leaves people alone; one too close bites, and is marked for hunting")]
        public void T005_a_fed_wolf_leaves_people_alone_one_too_close_bites_and_is_ma()
        {
            var state = wild();
            var s = Js.NN(Js.Get(state.survivors, 0));
            var far = Animals.addAnimal(state, "wolf", s.pos.x + 10, s.pos.y);
            animals(state, 40);
            expect(s.health).toBe(((double)(100) * (10)));
            state.animals.splice(state.animals.indexOf(far), 1);
            var close = Animals.addAnimal(state, "wolf", s.pos.x + 1, s.pos.y);
            animals(state, 30);
            expect(s.health).toBeLessThan(1000);
            expect(close.hunted).toBe(true);
            expect(1000 - s.health).toBe(Animals.WOLF_DAMAGE);
            expect(Space.manhattan(close.pos, s.pos)).toBeGreaterThan(Animals.WOLF_CLOSE);
        }

        [Fact(DisplayName = "wildlife > a starving wolf comes for people")]
        public void T006_a_starving_wolf_comes_for_people()
        {
            var state = wild();
            var s = Js.NN(Js.Get(state.survivors, 0));
            var w = Animals.addAnimal(state, "wolf", s.pos.x + 10, s.pos.y);
            w.hunger = Animals.WOLF_STARVING;
            animals(state, 120);
            expect(s.health).toBeLessThan(1000);
        }

        [Fact(DisplayName = "wildlife > a bear mauls anyone who comes too close")]
        public void T007_a_bear_mauls_anyone_who_comes_too_close()
        {
            var state = wild();
            var s = Js.NN(Js.Get(state.survivors, 0));
            Animals.addAnimal(state, "bear", s.pos.x + 3, s.pos.y);
            animals(state, 40);
            expect(1000 - s.health).toBeGreaterThanOrEqual(Animals.BEAR_DAMAGE);
        }

        [Fact(DisplayName = "wildlife > hitting a wolf sets its pack on you; brought down it leaves meat")]
        public void T008_hitting_a_wolf_sets_its_pack_on_you_brought_down_it_leaves_m()
        {
            var state = wild();
            var s = Js.NN(Js.Get(state.survivors, 0));
            var lead = Animals.addAnimal(state, "wolf", s.pos.x + 8, s.pos.y);
            var mate = Animals.addAnimal(state, "wolf", s.pos.x + 9, s.pos.y, lead.id);
            expect(Handiwork.hitAnimal(state, s, lead.id, 10)).toBe("hit the wolf");
            expect(lead.angryAt).toBe(s.id);
            expect(mate.angryAt).toBe(s.id);
            var food = state.items.filter((i) => i.kind == "food").length;
            expect(Handiwork.hitAnimal(state, s, lead.id, Animals.HEALTH["wolf"])).toBe("brought it down");
            expect(state.items.filter((i) => i.kind == "food").length - food).toBe(Animals.MEAT["wolf"]);
        }

        [Fact(DisplayName = "wildlife > a starving wolf going for someone is fought off by a colonist on guard (a warning bite only drives it off)")]
        public void T009_a_starving_wolf_going_for_someone_is_fought_off_by_a_colonis()
        {
            var state = wild();
            var s = Js.NN(Js.Get(state.survivors, 0));
            foreach (var k in Js.Iter(Js.ObjectKeys(s.priorities)))
            {
                s.priorities[Js.Idx(k)] = 0;
            }
            var guard = Js.With(Js.Clone(Js.DeepClone(s)), __s1 => { __s1.id = "s900"; __s1.name = "Guard"; __s1.pos = new Point { x = Js.I(s.pos.x - 6), y = Js.I(s.pos.y) }; });
            Js.Object_assign(guard, Motion.placedAt(guard.pos));
            foreach (var k in Js.Iter(Js.ObjectKeys(guard.priorities)))
            {
                guard.priorities[Js.Idx(k)] = (k == "guard") ? 1 : 0;
            }
            state.survivors.push(guard);
            var w = Animals.addAnimal(state, "wolf", s.pos.x + 4, s.pos.y);
            w.hunger = Animals.WOLF_STARVING;
            var fought = false;
            for (var k = 0; (k < 600) && !fought; k++)
            {
                Tick.tick(state);
                fought = !state.animals.includes(w) || ((Js.Q(w.health, 0)) < Animals.HEALTH["wolf"]);
            }
            expect(fought).toBe(true);
        }

        [Fact(DisplayName = "wildlife > animals far from everyone are gone at the hour")]
        public void T010_animals_far_from_everyone_are_gone_at_the_hour()
        {
            var state = wild(200);
            var s = Js.NN(Js.Get(state.survivors, 0));
            var r = Animals.addAnimal(state, "rabbit", JsMath.min(199, (s.pos.x + Animals.WILD_RADIUS) + 5), s.pos.y);
            state.tick = Js.I(((double)(10) * (Time.TICKS_PER_HOUR)) - 1);
            animals(state, 1);
            expect(state.animals.includes(r)).toBe(false);
        }

        [Fact(DisplayName = "wildlife > the whole sim stays sound with wildlife about for a few days")]
        public void T011_the_whole_sim_stays_sound_with_wildlife_about_for_a_few_days()
        {
            var state = wild();
            for (var k = 0; k < ((double)(3) * (Time.DAY_LENGTH)); k++)
            {
                Tick.tick(state);
                if ((k % 200) == 0)
                {
                    expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
                }
            }
        }
    }
}
