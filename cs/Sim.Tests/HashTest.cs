// The stateless hash (Hash.cs, rule 11): chances that mustn't draw on the sim RNG roll on it, so its values are part
// of every world that has been generated and every game that will be replayed. Nothing else pinned them: a change to
// hashRoll that happened to pass the other tests would have changed worlds quietly.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class HashTest
{
    [Fact(DisplayName = "hash > hashRoll gives the values it always gave")]
    public void Pinned()
    {
        expect(Hash.hashRoll(0)).toBe(0.49312001722864807);
        expect(Hash.hashRoll(1)).toBe(0.5663235550746322);
        expect(Hash.hashRoll(1, 2)).toBe(0.7753679812885821);
        expect(Hash.hashRoll(12, 4800, 7)).toBe(0.6759061925113201);
        // Numbers go in as JS's ToInt32 has them: a negative one wraps, a fraction is cut, 2^32 is taken off.
        expect(Hash.hashRoll(-1, 3)).toBe(0.2844489347189665);
        expect(Hash.hashRoll(2.7, 2)).toBe(0.8611185308545828);
        expect(Hash.hashRoll(2.7, 2)).toBe(Hash.hashRoll(2, 2));
        expect(Hash.hashRoll(123456789, 987654321)).toBe(0.30855161836370826);
        expect(Hash.hashRoll(4294967301, 1)).toBe(Hash.hashRoll(5, 1));
    }

    [Fact(DisplayName = "hash > hashRoll is in [0, 1), the same each time, and spread out")]
    public void Spread()
    {
        var tenths = new int[10];
        for (int a = 0; a < 200; a++)
        {
            for (int b = 0; b < 50; b++)
            {
                double r = Hash.hashRoll(a, b);
                expect(r >= 0 && r < 1, $"hashRoll({a}, {b}) = {r}").toBe(true);
                expect(Hash.hashRoll(a, b)).toBe(r);
                tenths[(int)(r * 10)]++;
            }
        }
        // 10,000 rolls: about 1,000 in each tenth.
        foreach (int n in tenths) expect(n > 850 && n < 1150, string.Join(", ", tenths)).toBe(true);
        // The order of the numbers matters: (a, b) is not (b, a).
        expect(Hash.hashRoll(3, 4) != Hash.hashRoll(4, 3)).toBe(true);
    }

    [Fact(DisplayName = "hash > idNumber reads an id's digits as one number")]
    public void IdNumber()
    {
        expect(Hash.idNumber("z12")).toBe(12);
        expect(Hash.idNumber("s0")).toBe(0);
        expect(Hash.idNumber("nobody")).toBe(0);
        expect(Hash.idNumber("")).toBe(0);
        // A chunk's own ids ("f3.4.15": chunk 3, 4, the fifteenth made) read as all their digits together.
        expect(Hash.idNumber("f3.4.15")).toBe(3415);
    }
}
