// A stateless roll in [0, 1) from a few numbers: for chances that mustn't draw on the sim RNG,
// so adding one doesn't shift every other random outcome. Bit for bit the TypeScript hashRoll.
namespace LoonieRoo.Sim;

public static class Hash
{
    public static double hashRoll(params double[] nums)
    {
        int h = unchecked((int)0x811c9dc5u);
        foreach (double n in nums)
        {
            h = Js.Imul(h ^ Js.ToInt32(n), 0x01000193);
            h = Js.Imul(h ^ (int)((uint)h >> 15), 0x2c1b3c6d);
        }
        h = Js.Imul(h ^ (int)((uint)h >> 13), 0x297a2d39);
        return (uint)(h ^ (int)((uint)h >> 16)) / 4294967296.0;
    }

    /// <summary>The number in an id like "z12": its digits, read as one number (0 if none). A double, as in JS: ids can run past int.</summary>
    public static double idNumber(string id)
    {
        var digits = new System.Text.StringBuilder();
        foreach (char c in id) if (c >= '0' && c <= '9') digits.Append(c);
        if (digits.Length == 0) return 0;
        return double.Parse(digits.ToString(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
