// Ported from src/sim/moodlets.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Moodlets: thoughts (mood.ts) that do something beyond mood, while they last.
//   faster    well rested (woke in a bed, rested): work a bit faster, swing sooner
//   harder    blood up (just made a kill): hit a fifth harder
//   slower    shaken (just got hit): swing later
//   slowWalk  out in the cold: a tick slower a tile
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Moodlets
{

    public static readonly JsRecord<string> BOOSTS = new() { ["wellRested"] = "faster", ["bloodUp"] = "harder", ["shaken"] = "slower", ["cold"] = "slowWalk" };

    /// <summary>Blood up: damage, in percent.</summary>
    public const int HARDER_PCT = 120;

    /// <summary>Ticks off (well rested) or onto (shaken) the wait between swings.</summary>
    public const int SWING_SOONER = 2;

    public const int SWING_LATER = 3;

    /// <summary>Does one of this survivor's current thoughts give this boost? (Thoughts past their time are dropped each tick.)</summary>
    public static bool boosted(Survivor s, string boost)
    {
        return s.thoughts.some((t) => Js.Get(BOOSTS, t.kind) == boost);
    }

    /// <summary>A blow's damage after moodlets.</summary>
    public static double boostDamage(Survivor s, double damage)
    {
        return boosted(s, "harder") ? JsMath.round(((double)(((double)(damage) * (HARDER_PCT))) / (100))) : damage;
    }

    /// <summary>Ticks between swings after moodlets.</summary>
    public static double swingWait(Survivor s, double @base)
    {
        return (@base - (boosted(s, "faster") ? SWING_SOONER : 0)) + (boosted(s, "slower") ? SWING_LATER : 0);
    }
}
