// Ported from src/sim/zombieKinds.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Kinds of zombie, beyond the plain walker. As the nights go on, some of the
// night's arrivals, herds and ruin sleepers are something worse (a hash of the
// zombie's id, not the sim RNG):
//   runner    quick (a tile every 4 ticks, walkers take 6), but frail
//   brute     twice the health, slow, hits hard, and batters walls three times as fast
//   screamer  frail; when it first spots someone it screams (Noise.cs), waking and drawing others
//   crawler   frail; slips under barricades
// Each counters a different defence: runners outpace guards, brutes break walls,
// screamers punish fighting near ruins, crawlers make barricades no wall.
// Since 2026-11-11 (Crossings.cs) runners also vault barricades, every kind climbs over a parked car at its own
// pace, and a crawler goes under a pickup.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class ZombieKinds
{

    public static readonly JsRecord<VariantInfo> VARIANTS = new() { ["walker"] = new() { healthPct = 100, ticksPerTile = 6, damage = 40, battering = 1, from = 1, chance = 0 }, ["runner"] = new() { healthPct = 60, ticksPerTile = 4, damage = 30, battering = 1, from = 3, chance = 0.12 }, ["screamer"] = new() { healthPct = 80, ticksPerTile = 6, damage = 30, battering = 1, from = 4, chance = 0.06 }, ["brute"] = new() { healthPct = 200, ticksPerTile = 8, damage = 70, battering = 3, from = 5, chance = 0.08 }, ["crawler"] = new() { healthPct = 70, ticksPerTile = 7, damage = 35, battering = 1, from = 6, chance = 0.08 } };

    public static readonly List<string> ORDER = ["runner", "screamer", "brute", "crawler"];

    /// <summary>Turns a fresh walker into something worse, maybe, by its id and the night.</summary>
    public static void giveVariant(Zombie z, double night)
    {
        var roll = Hash.hashRoll(Hash.idNumber(z.id), 0x2b1d);
        double at = 0;
        foreach (var v in Js.Iter(ORDER))
        {
            if (night < Js.Get(VARIANTS, v).from)
            {
                continue;
            }
            at += Js.Get(VARIANTS, v).chance;
            if (roll >= at)
            {
                continue;
            }
            z.variant = v;
            z.health = z.maxHealth = JsMath.max(1, JsMath.round(((double)(((double)(z.maxHealth) * (Js.Get(VARIANTS, v).healthPct))) / (100))));
            return;
        }
    }
}
