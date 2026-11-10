// Ported from src/sim/wounds.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Wounds: a zombie or raider blow sometimes lands on an arm or a leg
// (WOUND_CHANCE, a hash). The wound bleeds (a health point every
// BLEED_EVERY ticks) for BLEED_TICKS or until a bandage stops it, and slows its owner for
// WOUND_TICKS: an arm wound slows work, a leg wound slows walking.
//
// A bite that infects is remembered by limb. Within amputateWindow() of the
// bite, the limb can come off (the amputate command): the infection goes with
// it, at a heavy cost in health and for good (lostArm or lostLeg; lostLimbs says which side and
// where: "armLeft:elbow", for the renderer and one-handed rules).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Wounds
{
    static Wounds()
    {
        Time.onClock(() => WOUND_TICKS = ((double)(2) * (Time.DAY_LENGTH)));
    }

    public const double WOUND_CHANCE = 0.15;

    public static double WOUND_TICKS = ((double)(2) * (Time.DAY_LENGTH));

    public const int BLEED_EVERY = 15;

    /// <summary>Bleeding stops by itself after this (sooner with a bandage). Balance pass 5: it ran on until bandaged, and bandages ran out.</summary>
    public const int BLEED_TICKS = 300;

    /// <summary>The first quarter of the infection; a function, as INFECTION_TURN follows the day length (time.ts).</summary>
    public static double amputateWindow()
    {
        return ((double)(Infection.INFECTION_TURN) / (4));
    }

    public const int AMPUTATION_COST = 300;

    /// <summary>A blow landed: maybe a wound (and the limb a bite would have gone into).</summary>
    public static string maybeWound(SimState state, Survivor s, string attackerId)
    {
        string limb = (Hash.hashRoll(Hash.idNumber(attackerId), state.tick, Hash.idNumber(s.id), 1) < 0.5) ? "arm" : "leg";
        if (!Js.T(s.wound) && (Hash.hashRoll(Hash.idNumber(attackerId), state.tick, Hash.idNumber(s.id), 2) < WOUND_CHANCE))
        {
            s.wound = new Wound { limb = limb, until = state.tick + WOUND_TICKS };
            s.bleeding = true;
            s.bleedUntil = state.tick + BLEED_TICKS;
        }
        return limb;
    }

    public static void updateWounds(SimState state, Survivor s)
    {
        if (Js.T(s.wound) && (state.tick >= s.wound.until))
        {
            s.wound = null;
        }
        if (s.bleeding && (state.tick >= s.bleedUntil))
        {
            s.bleeding = false;
        }
        if (s.bleeding && ((state.tick % BLEED_EVERY) == 0))
        {
            s.health = JsMath.max(0, s.health - 1);
            s.lastHurtBy = "bleeding";
        }
    }

    /// <summary>Slowed at work: a wounded or lost arm.</summary>
    public static bool armSlowed(Survivor s)
    {
        return (s.wound?.limb == "arm") || Skills.hasTrait(s, "lostArm");
    }

    /// <summary>Slowed walking: a wounded or lost leg.</summary>
    public static bool legSlowed(Survivor s)
    {
        return (s.wound?.limb == "leg") || Skills.hasTrait(s, "lostLeg");
    }

    /// <summary>Which limb comes off, with side and level ("armLeft:elbow", "legRight:knee"): the side
    /// by a hash (no RNG draw), the other one if that side is already gone; arms below the elbow, legs
    /// below the knee (where a bite on the forearm or the calf is taken off).</summary>
    public static string lostLimb(SimState state, Survivor s, string limb)
    {
        var side = (Hash.hashRoll(Hash.idNumber(s.id), state.tick, 0x51de) < 0.5) ? "Left" : "Right";
        if (s.lostLimbs.some((l) => l.StartsWith(limb + side)))
        {
            side = (side == "Left") ? "Right" : "Left";
        }
        return $"{limb}{side}:{((limb == "arm") ? "elbow" : "knee")}";
    }

    public static bool canAmputate(Survivor s)
    {
        return ((s.infection != null) && (s.infection < amputateWindow())) && (s.bitten != null);
    }

    /// <summary>Takes the bitten limb off. Returns an error, or null when done.</summary>
    public static string? amputate(SimState state, Survivor s)
    {
        if (!canAmputate(s))
        {
            return (s.infection == null) ? $"{Js.Str(s.name)} isn't infected" : "too late: it has spread";
        }
        var limb = Js.NN(s.bitten);
        s.infection = null;
        s.bitten = null;
        s.bleeding = true;
        s.bleedUntil = state.tick + BLEED_TICKS;
        s.health = JsMath.max(1, s.health - AMPUTATION_COST);
        s.lastHurtBy = "an amputation";
        var trait = (limb == "arm") ? "lostArm" : "lostLeg";
        if (!s.traits.includes(trait))
        {
            s.traits.push(trait);
        }
        s.lostLimbs.push(lostLimb(state, s, limb));
        Events.logEvent(state, $"{Js.Str(s.name)}'s bitten {Js.Str(limb)} came off. The infection went with it");
        return null;
    }
}
