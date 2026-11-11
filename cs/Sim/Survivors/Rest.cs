// Ported from src/sim/rest.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Rest constants (Needs sleeps by them).
// All values are integers so the sim stays exactly deterministic.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Rest
{

    public const int REST_MAX = 2000;

    /// <summary>Below this a survivor stops working and goes to sleep.</summary>
    public const int TIRED = 500;

    /// <summary>At night survivors turn in much earlier (below 75%), so sleep follows the clock.</summary>
    public const int NIGHT_TIRED = 1500;

    /// <summary>Rest lost per tick while awake (2000 → 500 takes 1500 ticks, 2.5 min at 1x).</summary>
    public const int REST_DRAIN = 1;

    /// <summary>Rest gained per tick asleep in a bed / on the floor.</summary>
    public const int BED_REGEN = 4;

    public const int FLOOR_REGEN = 2;

    /// <summary>Survivors start staggered by index (100%, 85%, 70%, 55%, then repeat) so they don't all tire together.</summary>
    public static double initialRest(int index)
    {
        return REST_MAX - JsMath.floor(((double)(((double)(((double)(REST_MAX) * (15))) * ((index % 4)))) / (100)));
    }
    public static double initialRest(double index) => initialRest(Js.I(index));
}
