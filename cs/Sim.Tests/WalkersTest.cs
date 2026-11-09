// Animals and dogs as free movers (Walkers.cs): straight at their goals at any angle, round walls by
// A* only where the straight way is blocked, never inside a wall, and deterministic.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class WalkersTest
{
    /// <summary>An open map with a wall down the middle (x = 10, rows 2-17), and nobody on it.</summary>
    static SimState walled()
    {
        var rows = Js.ArrayFrom(20, (int y) => ((y >= 2) && (y <= 17)) ? (".........." + "#" + ".........") : "....................");
        var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = rows });
        state.settings = Js.With(Js.CopyAs<Settings>(state.settings), __s0 => { __s0.zombies = 0; });
        return state;
    }

    /// <summary>The animals alone, n ticks (as WildlifeTest's), checking each one stands clear of walls on open floor every tick.</summary>
    static void run(SimState state, int n)
    {
        for (var k = 0; k < n; k++)
        {
            state.tick = Js.I(state.tick + 1);
            Animals.updateAnimals(state);
            foreach (var a in state.animals)
            {
                expect(Motion.fits(state.grid, a.at, Pathfinding.ANIMAL), DebugText.dumpText(state)).toBe(true);
                expect(Space.samePoint(a.pos, Motion.tileOf(a.at))).toBe(true);
            }
        }
    }

    public class D001_walkers
    {
        [Fact(DisplayName = "walkers > an animal walks round a wall to its goal, straight where it can")]
        public void T001_round_a_wall()
        {
            var state = walled();
            var a = Animals.addAnimal(state, "bear", 6, 10);
            var goal = Motion.centre(new Point(14, 10));
            var budget = new WalkBudget { plans = 1 };
            var steps = 0;
            var length = 0.0;
            while (!Walkers.walkTo(state, a, goal, Animals.speedOf(a), Pathfinding.ANIMAL, budget) && steps < 400)
            {
                var was = a.at;
                state.tick = Js.I(state.tick + 1);
                budget.plans = 1;
                steps++;
                expect(Motion.fits(state.grid, a.at, Pathfinding.ANIMAL)).toBe(true);
                length += JsMath.sqrt(Motion.distance2(was, a.at));
            }
            expect(Motion.distance2(a.at, goal)).toBeLessThanOrEqual((double)Walkers.ARRIVED * Walkers.ARRIVED);
            // Round the wall's end (y = 1 or 18): at most 4 + 8.5 + 8.5 tiles each way. A tile-by-tile walk is 4 + 9 + 4 + 9 = 26.
            expect(length / Motion.SUB).toBeLessThan(22);
        }

        [Fact(DisplayName = "walkers > a rabbit flees off the grid's lines, straight away from someone at an angle")]
        public void T002_flees_off_grid()
        {
            var state = WildlifeTest.wild();
            var s = Js.NN(Js.Get(state.survivors, 0));
            // The rabbit 3 tiles east and 1 north of them: away is about 18 degrees north of east.
            var r = Animals.addAnimal(state, "rabbit", s.pos.x + 3, s.pos.y - 1);
            var from = r.at;
            run(state, 6);
            double dx = r.at.x - from.x;
            double dy = r.at.y - from.y;
            expect(dx).toBeGreaterThan(Motion.SUB);
            expect(dy).toBeLessThan(0);
            // Not along a row, a column or a diagonal: north of east by between 10 and 30 degrees (tan 10 = 0.18, tan 30 = 0.58).
            expect(-dy / dx).toBeGreaterThan(0.18);
            expect(-dy / dx).toBeLessThan(0.58);
            // And not stopped on a tile's centre.
            expect(Space.samePoint(r.at, Motion.centre(r.pos))).toBe(false);
        }

        [Fact(DisplayName = "walkers > animals about walls and buildings never end up in one, and a dog keeps to its person")]
        public void T003_never_in_a_wall()
        {
            var state = walled();
            Commands.applyCommand(state, new Command { type = "spawnSurvivor", x = 14, y = 10 });
            var s = Js.NN(Js.Get(state.survivors, 0));
            var wolf = Animals.addAnimal(state, "wolf", 3, 3);
            wolf.hunger = Animals.WOLF_HUNGRY;
            Animals.addAnimal(state, "wolf", 4, 3, wolf.id);
            Animals.addAnimal(state, "rabbit", 7, 12);
            Animals.addAnimal(state, "rabbit", 2, 16);
            Animals.addDeer(state, 16, 4);
            Animals.addAnimal(state, "bear", 17, 17);
            var dog = Dogs.addDog(state, 5, 10);
            for (var k = 0; k < 1500; k++)
            {
                Tick.tick(state);
                foreach (var a in state.animals)
                {
                    expect(Motion.fits(state.grid, a.at, Pathfinding.ANIMAL), DebugText.dumpText(state)).toBe(true);
                    expect(Space.samePoint(a.pos, Motion.tileOf(a.at))).toBe(true);
                }
                foreach (var d in state.dogs)
                {
                    expect(Motion.fits(state.grid, d.at, Pathfinding.SURVIVOR), DebugText.dumpText(state)).toBe(true);
                    expect(Space.samePoint(d.pos, Motion.tileOf(d.at))).toBe(true);
                }
            }
            // The dog came round the wall to them.
            if (state.dogs.includes(dog) && state.survivors.includes(s))
            {
                expect(Space.manhattan(dog.pos, s.pos)).toBeLessThanOrEqual(Dogs.DOG_FOLLOW + 1);
            }
        }

        [Fact(DisplayName = "walkers > the same start walks the same way (deterministic, and through a save)")]
        public void T004_deterministic()
        {
            SimState start()
            {
                var state = WildlifeTest.wild();
                var s = Js.NN(Js.Get(state.survivors, 0));
                var lead = Animals.addAnimal(state, "wolf", s.pos.x + 9, s.pos.y + 2);
                lead.hunger = Animals.WOLF_STARVING;
                Animals.addAnimal(state, "wolf", s.pos.x + 10, s.pos.y + 3, lead.id);
                Animals.addAnimal(state, "rabbit", s.pos.x - 5, s.pos.y + 1);
                Animals.addDeer(state, s.pos.x - 6, s.pos.y - 6);
                Dogs.addDog(state, s.pos.x + 1, s.pos.y + 1);
                return state;
            }
            var a = start();
            var b = start();
            Tick.step(a, 300);
            Tick.step(b, 150);
            b = StateJson.Clone(b);
            Tick.step(b, 150);
            expect(StateJson.Write(a) == StateJson.Write(b), string.Join("\n", StateJson.Diff(System.Text.Json.Nodes.JsonNode.Parse(StateJson.Write(a)), System.Text.Json.Nodes.JsonNode.Parse(StateJson.Write(b))))).toBe(true);
            // And they did go somewhere, off the tile centres.
            expect(a.animals.some((x) => !Space.samePoint(x.at, Motion.centre(x.pos)))).toBe(true);
        }
    }
}
