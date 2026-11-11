// Ported from src/sim/body.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// The body, 0..100 each: endurance, panic, body heat, wetness. Updated every tick
// (rates per tick, so they slow with longer days: time.ts slowTick).
//
//   endurance  swings and running (RUN_PCT of walking speed, RUN_TICK_COST a tick) spend it; it
//              comes back at rest. Under WINDED: swings come later and land softer, and no running.
//   panic      rises with the dead close by (faster alone, or with little resolve; slower
//              when steeled), and with every hit taken; ebbs when none are near. Over
//              PANICKING: some swings miss, and it weighs on mood. A hit, or the dead turning up
//              while calm (under CALM), is a startle (startledAt: the renderer's flinch).
//   body heat  falls fast while exposed (seasons.ts isCold: a winter night or blizzard outdoors,
//              no coat, no warmth; or soaked through outdoors at night), recovers otherwise.
//              Under COLD: the "cold" thought; under FREEZING: health goes too (the damage and the coat's
//              wear are dealt in Seasons.updateWeather, which reads FREEZING).
//   wetness    builds out in rain or snow (slowly under a coat), dries indoors or by warmth.
//              Over SOAKED: the "soaked" thought.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class BodyMod
{

    public const int BODY_MAX = 100;

    public const int WINDED = 25;

    public const int PANICKING = 60;

    public const int COLD = 30;

    public const int FREEZING = 10;

    public const int SOAKED = 50;

    /// <summary>Wet enough that a night outdoors chills you even out of winter.</summary>
    public const int DRENCHED = 60;

    public const int SWING_COST = 10;

    /// <summary>Running: this many percent of walking speed, for this much endurance a tick (about 10 s from full to winded).</summary>
    public const int RUN_PCT = 175;

    public const int RUN_TICK_COST = 1;

    public const int HIT_PANIC = 10;

    /// <summary>Panic per zombie close by, per check.</summary>
    public const int PANIC_PER_ZOMBIE = 3;

    public const int PANIC_RANGE = 6;

    public const int PANIC_EVERY = 10;

    /// <summary>While winded: extra ticks between swings, and damage in percent.</summary>
    public const int WINDED_WAIT = 5;

    public const int WINDED_DAMAGE_PCT = 80;

    /// <summary>While panicking: the chance a swing misses.</summary>
    public const double PANIC_MISS = 0.25;

    public const double PANIC_MOOD = -10;

    public static double clamp(double n)
    {
        return JsMath.max(0, JsMath.min(BODY_MAX, n));
    }

    /// <summary>Exposed to the cold right now: the season's cold, or soaked through outdoors at night.</summary>
    public static bool chilled(SimState state, Survivor s)
    {
        if (Seasons.isCold(state, s))
        {
            return true;
        }
        return (((s.wetness > DRENCHED) && Time.isNight(state.tick)) && !Rooms.isIndoors(state, s.pos.x, s.pos.y)) && !Power.isWarmed(state, s.pos);
    }

    public static void updateBody(SimState state, Survivor s)
    {
        if (!Time.slowTick(state.tick))
        {
            return;
        }
        if ((s.endurance < BODY_MAX) && ((state.tick % 4) == 0))
        {
            s.endurance = clamp(s.endurance + (Js.T(s.sleep?.asleep) ? 2 : 1));
        }
        var outdoors = !Rooms.isIndoors(state, s.pos.x, s.pos.y);
        var coat = Clothing.wearing(state, s, "coat") != null;
        if (outdoors && Seasons.isWet(state.weather))
        {
            if ((state.tick % (coat ? 12 : 3)) == 0)
            {
                s.wetness = clamp(s.wetness + 1);
            }
        }
        else if ((s.wetness > 0) && ((state.tick % (Power.isWarmed(state, s.pos) ? 2 : 5)) == 0))
        {
            s.wetness = clamp(s.wetness - 1);
        }
        if (chilled(state, s))
        {
            s.bodyHeat = clamp(s.bodyHeat - 1);
        }
        else if ((s.bodyHeat < 70) && ((state.tick % 2) == 0))
        {
            s.bodyHeat = clamp(s.bodyHeat + 1);
        }
        if (s.bodyHeat < COLD)
        {
            Mood.addThought(state, s, "cold");
        }
        if (s.wetness > SOAKED)
        {
            Mood.addThought(state, s, "soaked");
        }
        if ((state.tick % PANIC_EVERY) != 0)
        {
            return;
        }
        var near = Zombies.zombiesWithin(state, s.pos, PANIC_RANGE);
        if (near == 0)
        {
            s.panic = clamp(s.panic - 4);
            return;
        }
        if (s.panic < CALM)
        {
            startle(state, s);  // the dead, out of nowhere
        }
        var alone = !state.survivors.some((o) => (o != s) && (JsMath.max(JsMath.abs(o.pos.x - s.pos.x), JsMath.abs(o.pos.y - s.pos.y)) <= PANIC_RANGE));
        var factor = ((double)((alone ? 1.5 : 1)) * ((Resolve.isSteeled(s) ? 0.5 : (Resolve.isBreaking(s) ? 1.5 : 1))));
        s.panic = clamp(s.panic + JsMath.round(((double)(((double)(near) * (PANIC_PER_ZOMBIE))) * (factor))));
    }

    public static bool isWinded(Survivor s)
    {
        return s.endurance < WINDED;
    }

    public static bool isPanicking(Survivor s)
    {
        return s.panic > PANICKING;
    }

    /// <summary>Spend endurance (a swing, a step at a run).</summary>
    public static void exert(Survivor s, double cost)
    {
        s.endurance = clamp(s.endurance - cost);
    }

    /// <summary>Under this, calm: the dead turning up startle (the renderer's flinch: pose intent).</summary>
    public const int CALM = 10;

    /// <summary>A startle: a hit taken, or the dead noticed out of calm. Only the tick is kept (pose intent's flinch).</summary>
    public static void startle(SimState state, Survivor s)
    {
        s.startledAt = state.tick;
    }

    public static void frighten(Survivor s, int by)
    {
        s.panic = clamp(s.panic + by);
    }
    public static void frighten(Survivor s, double by) => frighten(s, Js.I(by));

    /// <summary>Panicking: does this swing miss? (A hash, not the sim RNG.)</summary>
    public static bool panicMiss(SimState state, Survivor s)
    {
        return isPanicking(s) && (Hash.hashRoll(Hash.idNumber(s.id), state.tick, 0x9a1) < PANIC_MISS);
    }

    /// <summary>Mood factors from the body.</summary>
    public static List<(string, double)> bodyFactors(Survivor s)
    {
        return isPanicking(s) ? [("panicking", PANIC_MOOD)] : [];
    }
}
