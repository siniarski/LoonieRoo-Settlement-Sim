// What the game picks by a sim id, the same every run and on every machine: a body's hair, a car's model, an item's
// variant, an animal's phase. Drawn only, so it is a stateless hash of the id (FNV-1a), never the sim's RNG (rule 11
// is the sim's version of this). Used as `using static LoonieRoo.Game.IdHash;`. Changing any of these re-dresses the
// world: every pick moves.

namespace LoonieRoo.Game;

public static class IdHash
{
    /// <summary>The 32-bit FNV-1a hash of a string.</summary>
    public static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) h = (h ^ c) * 16777619;
        return h;
    }

    /// <summary>A hash's bits stirred (murmur3's finaliser), so nearby ids pick far apart.</summary>
    public static uint Mix(uint h)
    {
        h ^= h >> 16; h *= 0x85EBCA6B;
        h ^= h >> 13; h *= 0xC2B2AE35;
        h ^= h >> 16;
        return h;
    }

    /// <summary>A number from 0 to 1 for an id and a salt (one salt for each thing picked, so picks are independent).</summary>
    public static double Hash01(string id, int salt)
    {
        uint h = Fnv(id);
        h = (h ^ (uint)salt) * 16777619;
        h ^= h >> 13;
        return (h % 10007) / 10007.0;
    }
}
