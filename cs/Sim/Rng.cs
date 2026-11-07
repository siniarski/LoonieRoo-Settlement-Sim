// Seeded RNG (mulberry32), from rng.ts. The whole RNG state is one uint32 held in sim state, so
// save/load and replays restore it exactly. The only randomness the sim may use.
namespace LoonieRoo.Sim;

public interface IRngHolder
{
    /// <summary>The RNG state (a uint32, kept as a double so saves read and write it as the TypeScript sim does).</summary>
    double rng { get; set; }
}

public static partial class Rng
{
    /// <summary>Pure step: a value in [0, 1) and the next RNG state.</summary>
    public static (double value, uint next) mulberry32(uint rngState)
    {
        uint next = unchecked(rngState + 0x6d2b79f5u);
        int t = unchecked((int)next);
        t = Js.Imul(t ^ (int)((uint)t >> 15), t | 1);
        t ^= unchecked(t + Js.Imul(t ^ (int)((uint)t >> 7), t | 61));
        double value = (uint)(t ^ (int)((uint)t >> 14)) / 4294967296.0;
        return (value, next);
    }

    /// <summary>Normalises any integer seed into a valid RNG state.</summary>
    public static uint seedRng(double seed) => Js.ToUint32(seed);

    /// <summary>A double in [0, 1). Advances the holder's RNG.</summary>
    public static double random(IRngHolder holder)
    {
        var (value, next) = mulberry32(Js.ToUint32(holder.rng));
        holder.rng = next;
        return value;
    }

    /// <summary>An integer in [min, maxExclusive). Advances the holder's RNG.</summary>
    public static int randomInt(IRngHolder holder, int min, int maxExclusive)
    {
        if (maxExclusive <= min) throw new InvalidOperationException($"randomInt: empty range [{min}, {maxExclusive})");
        return min + (int)Math.Floor(random(holder) * (maxExclusive - min));
    }

    /// <summary>Picks one element. Advances the holder's RNG.</summary>
    public static T pick<T>(IRngHolder holder, IReadOnlyList<T> items)
    {
        if (items.Count == 0) throw new InvalidOperationException("pick: empty array");
        return items[randomInt(holder, 0, items.Count)];
    }
}
