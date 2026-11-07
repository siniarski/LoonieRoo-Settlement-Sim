// Ported from tests/rng.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class RngTest
{

    /// <summary>rng</summary>
    public class D001_rng
    {

        [Fact(DisplayName = "rng > produces the same sequence for the same seed")]
        public void T002_produces_the_same_sequence_for_the_same_seed()
        {
            RngHolder a = new RngHolder { rng = Rng.seedRng(42) };
            RngHolder b = new RngHolder { rng = Rng.seedRng(42) };
            var seqA = Js.ArrayFrom(20, (int _i) => Rng.random(a));
            var seqB = Js.ArrayFrom(20, (int _i) => Rng.random(b));
            expect(seqA).toEqual(seqB);
        }

        [Fact(DisplayName = "rng > produces different sequences for different seeds")]
        public void T003_produces_different_sequences_for_different_seeds()
        {
            RngHolder a = new RngHolder { rng = Rng.seedRng(1) };
            RngHolder b = new RngHolder { rng = Rng.seedRng(2) };
            expect(Rng.random(a)).not.toBe(Rng.random(b));
        }

        [Fact(DisplayName = "rng > is pure at the step level")]
        public void T004_is_pure_at_the_step_level()
        {
            expect(Rng.mulberry32(123)).toEqual(Rng.mulberry32(123));
        }

        [Fact(DisplayName = "rng > keeps state a uint32 and values in [0, 1)")]
        public void T005_keeps_state_a_uint32_and_values_in_0_1()
        {
            RngHolder holder = new RngHolder { rng = Rng.seedRng(-7) };
            for (var i = 0; i < 1000; i++)
            {
                var v = Rng.random(holder);
                expect(v).toBeGreaterThanOrEqual(0);
                expect(v).toBeLessThan(1);
                expect(JsNumber.isInteger(holder.rng)).toBe(true);
                expect(holder.rng).toBeGreaterThanOrEqual(0);
                expect(holder.rng).toBeLessThanOrEqual(4294967295.0);
            }
        }

        [Fact(DisplayName = "rng > resumes identically after a JSON round trip")]
        public void T006_resumes_identically_after_a_JSON_round_trip()
        {
            RngHolder holder = new RngHolder { rng = Rng.seedRng(99) };
            Rng.random(holder);
            Rng.random(holder);
            var restored = Js.DeepClone(holder);
            expect(Rng.random(restored)).toBe(Rng.random(holder));
        }

        [Fact(DisplayName = "rng > randomInt stays in range and covers it")]
        public void T007_randomInt_stays_in_range_and_covers_it()
        {
            RngHolder holder = new RngHolder { rng = Rng.seedRng(5) };
            var seen = new JsSet<double>();
            for (var i = 0; i < 500; i++)
            {
                var n = Rng.randomInt(holder, 3, 7);
                expect(n).toBeGreaterThanOrEqual(3);
                expect(n).toBeLessThan(7);
                seen.add(n);
            }
            expect(Js.ListOf(seen).sort()).toEqual(Js.Arr([3, 4, 5, 6]));
        }

        [Fact(DisplayName = "rng > randomInt rejects empty ranges")]
        public void T008_randomInt_rejects_empty_ranges()
        {
            expect(() => Rng.randomInt(new RngHolder { rng = 1 }, 4, 4)).toThrow();
        }

        [Fact(DisplayName = "rng > pick returns an element and rejects empty arrays")]
        public void T009_pick_returns_an_element_and_rejects_empty_arrays()
        {
            RngHolder holder = new RngHolder { rng = Rng.seedRng(8) };
            expect(Js.Arr(["a", "b", "c"])).toContain(Rng.pick(holder, ["a", "b", "c"]));
            expect(() => Rng.pick<string>(holder, [])).toThrow();
        }
    }
}
