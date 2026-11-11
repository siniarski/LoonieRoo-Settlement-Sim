// Ported from src/sim/food.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Hunger constants (Needs eats by them; Wellbeing, Alerts and the character sheet read them).
// All values are integers so the sim stays exactly deterministic.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Food
{

    public const int SATIETY_MAX = 3000;

    /// <summary>Below this a survivor stops working and goes to eat.</summary>
    public const int HUNGRY = 750;

    /// <summary>Satiety lost per tick, awake or asleep (3000 → 750 takes 2250 ticks, 3.75 min at 1x).</summary>
    public const int SATIETY_DRAIN = 1;

    /// <summary>Satiety from eating one food item (capped at SATIETY_MAX).</summary>
    public const int MEAL_VALUE = 2000;

    /// <summary>A hot meal (cooked at a stove) fills more.</summary>
    public const int HOT_MEAL_VALUE = 2600;

    /// <summary>Ticks it takes to eat.</summary>
    public const int EAT_TICKS = 30;

    /// <summary>A hungry survivor who found no food looks again after this many ticks.</summary>
    public const int MEAL_RETRY_TICKS = 20;

    /// <summary>Survivors start staggered by index (100%, 80%, 60%, 90%, then repeat), offset from rest.</summary>
    public static double initialSatiety(int index)
    {
        return JsMath.floor(((double)(((double)(SATIETY_MAX) * (Js.NN(new List<double> { 100, 80, 60, 90 }[Js.Idx(index % 4)])))) / (100)));
    }
    public static double initialSatiety(double index) => initialSatiety(Js.I(index));
}
