// Ported from tests/lookup.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class LookupTest
{

    /// <summary>findById</summary>
    public class D001_findById
    {

        [Fact(DisplayName = "findById > always answers as list.find would, through pushes, splices and lookups of ids long gone")]
        public void T002_always_answers_as_list_find_would_through_pushes_splices_and()
        {
            List<IHasId> list = [];
            double next = 0;
            for (var round = 0; round < 3000; round++)
            {
                var r = Hash.hashRoll(round, 1);
                if (r < 0.4)
                {
                    list.push(new Item { id = $"x{Js.Str(next++)}" });
                }
                else if ((r < 0.6) && (list.length > 0))
                {
                    list.splice((int)JsMath.floor(Hash.hashRoll(round, 2) * list.length), 1);
                }
                var id = $"x{Js.Str(JsMath.floor(((double)(Hash.hashRoll(round, 3)) * ((next + 2)))))}";
                expect(Lookup.findById(list, id)).toBe(list.find((x) => x.id == id));
            }
        }
    }
}
