// Ported from tests/furniture-index.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class FurnitureIndexTest
{

    /// <summary>the furniture index, kept up to date in place</summary>
    public class D001_the_furniture_index_kept_up_to_date_in_place
    {

        [Fact(DisplayName = "the furniture index, kept up to date in place > answers as an index built afresh would, through adds, removes and marks")]
        public void T002_answers_as_an_index_built_afresh_would_through_adds_removes_()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = Js.ArrayFrom(12, (int _i) => ".".repeat(12)) });
            List<string> kinds = ["crate", "tree", "bed", "trap"];
            FurnitureMod.furnitureAt(state, 0, 0);
            for (var round = 0; round < 400; round++)
            {
                var roll = Hash.hashRoll(round, 1);
                if ((roll < 0.55) || (state.furniture.length == 0))
                {
                    FurnitureMod.addFurniture(state, Js.NN(Js.Get(kinds, JsMath.floor(((double)(Hash.hashRoll(round, 2)) * (kinds.length))))), JsMath.floor(((double)(Hash.hashRoll(round, 3)) * (12))), JsMath.floor(((double)(Hash.hashRoll(round, 4)) * (12))));
                }
                else if (roll < 0.85)
                {
                    FurnitureMod.removeFurniture(state, Js.NN(Js.Get(state.furniture, JsMath.floor(((double)(Hash.hashRoll(round, 5)) * (state.furniture.length))))));
                }
                else
                {
                    var f = Js.NN(Js.Get(state.furniture, JsMath.floor(((double)(Hash.hashRoll(round, 6)) * (state.furniture.length)))));
                    FurnitureMod.setScavenge(state, f, !f.scavenge);
                }
                if ((round % 20) != 0)
                {
                    continue;
                }
                var fresh = Js.With(Js.Clone(state), __s0 => { __s0.furniture = (state.furniture).ToList(); });
                for (var y = 0; y < 12; y++)
                {
                    for (var x = 0; x < 12; x++)
                    {
                        expect(FurnitureMod.furnitureAt(state, x, y), $"{Js.Str(x)},{Js.Str(y)} round {Js.Str(round)}").toBe(FurnitureMod.furnitureAt(fresh, x, y));
                    }
                }
                foreach (var k in Js.Iter(kinds))
                {
                    expect(Js.Arr([.. FurnitureMod.ofKind(state, k)])).toEqual(Js.Arr([.. FurnitureMod.ofKind(fresh, k)]));
                }
                expect(Js.Arr([.. FurnitureMod.markedForScavenge(state)])).toEqual(Js.Arr([.. FurnitureMod.markedForScavenge(fresh)]));
                foreach (var f in Js.Iter(fresh.furniture))
                {
                    expect(FurnitureMod.getFurniture(state, f.id)).toBe(f);
                }
            }
        }
    }
}
