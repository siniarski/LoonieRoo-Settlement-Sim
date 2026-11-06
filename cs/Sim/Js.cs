// JavaScript's number semantics where the port needs them exactly (the TypeScript sim is the reference:
// same seed and inputs must give the same state in both). JS numbers are doubles; bitwise operators
// convert to 32-bit integers (ToInt32 / ToUint32) first; Math.imul multiplies as 32-bit integers.
namespace LoonieRoo.Sim;

public static class Js
{
    /// <summary>ECMAScript ToInt32: truncate, wrap modulo 2^32, as signed (what `x | 0` gives).</summary>
    public static int ToInt32(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return 0;
        double t = Math.Truncate(x);
        double m = t % 4294967296.0;
        if (m < 0) m += 4294967296.0;
        return unchecked((int)(uint)m);
    }

    /// <summary>ECMAScript ToUint32 (what `x >>> 0` gives).</summary>
    public static uint ToUint32(double x) => unchecked((uint)ToInt32(x));

    /// <summary>Math.imul: the low 32 bits of the product, as signed.</summary>
    public static int Imul(int a, int b) => unchecked(a * b);

    /// <summary>Math.round: halves round up (towards +infinity), unlike Math.Round's banker's rounding.</summary>
    public static double Round(double x) => Math.Floor(x + 0.5);

    /// <summary>Math.sign.</summary>
    public static int Sign(double x) => x > 0 ? 1 : x < 0 ? -1 : 0;
}
