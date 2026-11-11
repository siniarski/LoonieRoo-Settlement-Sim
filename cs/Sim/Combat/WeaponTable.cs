// The weapons as data (2026-11-10). Each weapon is an item kind of its own ("knife", "pistol"...); this table gives
// what it does. Melee: damage a blow, reach (SUB: 256 a tile), ticks between blows, the arc in front it covers, the
// endurance a blow costs, the noise radius it makes, how many kills it lasts. Firearms: the same where it matters, and
// the ammunition kind, the magazine, reload time, projectiles a shot (pellets), range and fall-off, spread, fire rate.
//
// The old generic kinds stay valid for saves, scenarios and the start (Worldgen's weapon): a "weapon" is read as a
// crowbar (the old generic blow was the crowbar's numbers), a "gun" as a pistol, "ammo" is 9 mm. New loot and
// crafting make the specific kinds.
//
// Wear: a melee weapon's `wear` counts its kills (it breaks at `durability`; the axe and the hammer, tools first,
// don't); a firearm's `wear` is the rounds in it; an ammunition item's `wear` is the rounds in the box (0 in an
// old save: a box of AMMO_ROUNDS).
//
// Numbers are for a night-1 zombie (200 health, 10 more each night), the unarmed blow is 50 and the old generic
// weapon was 100 a blow every 10 ticks (the crowbar here). Spread is the sideways error at 1000 tiles' distance per
// tile of range (35 = 0.035: a shot at 10 tiles lands within 0.35 of a tile of the line), uniform either side.
#nullable disable warnings
namespace LoonieRoo.Sim;

public sealed class WeaponSpec
{
    public string kind = "";
    public string name = "";
    /// <summary>The action the animation plays: "swing", "stab" (melee), "shoot" (a gun) or "bow" (a crossbow).</summary>
    public string cls = "swing";
    /// <summary>What it is for, in a line.</summary>
    public string role = "";
    /// <summary>"weapon" (the hand's slot) or "tool" (an axe, a hammer: a weapon only when nothing is wielded).</summary>
    public string slot = "weapon";
    public bool twoHanded;
    /// <summary>Damage a blow, or a projectile (a pellet).</summary>
    public double damage;
    /// <summary>Melee reach, SUB (centre to centre).</summary>
    public int reach;
    /// <summary>Ticks between blows (melee) or shots (firearms: fireEvery).</summary>
    public int swing;
    public int arcDeg;
    /// <summary>cos^2 of the arc's half angle, written out so no maths library decides a hit.</summary>
    public double arcCos2;
    /// <summary>Endurance a blow costs (BodyMod.exert).</summary>
    public double stamina;
    /// <summary>Noise radius (Manhattan tiles), at a blow or a shot.</summary>
    public int noise;
    /// <summary>Kills before it breaks (0: it doesn't).</summary>
    public int durability;
    /// <summary>Percent of the damage on a blow at one who hasn't noticed you.</summary>
    public int sneak = 300;

    // Firearms.
    public string? ammo;
    public int magazine;
    /// <summary>Ticks to reload: one round each when perRound, else the whole magazine.</summary>
    public int reload;
    public bool perRound;
    public int pellets = 1;
    /// <summary>Furthest a projectile flies, tiles.</summary>
    public int range;
    /// <summary>Full damage out to here (tiles), then falling in a line to `falloff` percent at `range`.</summary>
    public int effective;
    public int falloff = 100;
    public int spread;
    public int spreadMove;
    public int spreadPanic;
    public int spreadWinded;

    public bool isFirearm => ammo != null;
}

public sealed class AmmoSpec
{
    public string kind = "";
    public string calibre = "";
    /// <summary>Rounds in a box as found or made.</summary>
    public int box;
}

public static partial class Weapons
{
    static WeaponSpec M(string kind, string name, string cls, int damage, int swing, int reach, int arcDeg, double arcCos2, double stamina, int noise, int durability, bool twoHanded, string role, int sneak = 300, string slot = "weapon")
        => new() { kind = kind, name = name, cls = cls, damage = damage, swing = swing, reach = reach, arcDeg = arcDeg, arcCos2 = arcCos2, stamina = stamina, noise = noise, durability = durability, twoHanded = twoHanded, role = role, sneak = sneak, slot = slot };

    static WeaponSpec G(string kind, string name, string cls, string ammo, int damage, int pellets, int magazine, int reload, bool perRound, int fireEvery, int range, int effective, int falloff, int spread, int spreadMove, int spreadPanic, int spreadWinded, int noise, bool twoHanded, string role)
        => new()
        {
            kind = kind, name = name, cls = cls, ammo = ammo, damage = damage, pellets = pellets, magazine = magazine, reload = reload, perRound = perRound, swing = fireEvery,
            range = range, effective = effective, falloff = falloff, spread = spread, spreadMove = spreadMove, spreadPanic = spreadPanic, spreadWinded = spreadWinded, noise = noise, twoHanded = twoHanded, role = role,
            // Used as a club: the same for every gun.
            reach = 288, arcDeg = 50, arcCos2 = 0.413, stamina = 8,
        };

    /// <summary>Every weapon, guns then melee. Iterate this, never the lookup. (What a guard reaches for first is not
    /// this order: Weapons.GUN_PREFERENCE and MELEE_PREFERENCE.)</summary>
    public static readonly List<WeaponSpec> SPECS =
    [
        //  kind        name          class    ammo      dmg pel mag rel  perRnd every range eff fall spr mv  pn  wd noise 2H   role
        G("rifle",     "hunting rifle", "shoot", "ammo308", 260, 1, 5,  9,  true,  28,  30, 14, 70, 10, 80, 70, 40, 36, true,  "one round kills at long range; slow bolt, loud, hard to hold still enough on the move"),
        G("shotgun",   "shotgun",       "shoot", "shells",   38, 8, 5, 10,  true,  22,   9,  2, 15, 85, 30, 45, 20, 32, true,  "a cone of eight pellets that ruins what is close and little at range; slow pump, very loud"),
        G("revolver",  "revolver",      "shoot", "ammo38",  150, 1, 6,  8,  true,  13,  15,  6, 50, 28, 40, 55, 25, 26, false, "hits hard and holds six; slow to reload, a round at a time"),
        G("pistol",    "pistol",        "shoot", "ammo",    100, 1, 10, 20, false,  9,  14,  5, 40, 35, 45, 55, 25, 22, false, "quick and handy, a magazine swapped whole; the weakest gun, still loud"),
        G("crossbow",  "crossbow",      "bow",   "bolts",   150, 1, 1,  25, false, 12,  18,  8, 60, 18, 45, 50, 30,  4, true,  "nearly silent; one bolt, a slow cock; bolts can be made from wood"),

        //  kind        name              class   dmg swing reach arc cos2   stam noise dur 2H   role
        M("sledge",    "sledgehammer",   "swing", 220, 20, 352, 80, 0.03,  20, 12, 30, true,  "slow, heavy and loud: one blow ends a night-one zombie, and tires you at once"),
        M("axe",       "felling axe",    "swing", 150, 14, 336, 65, 0.179, 14,  8,  0, true,  "a tool first: heavy, two hands; strikes hard, never breaks", slot: "tool"),
        M("machete",   "machete",        "swing", 110,  9, 336, 75, 0.067,  9,  6, 10, false, "a wide, quick sweep that takes in several; wears out"),
        M("pipe",      "lead pipe",      "swing", 125, 12, 320, 55, 0.329, 12,  8, 18, false, "heavy and durable; every blow tires"),
        M("crowbar",   "crowbar",        "swing", 100, 10, 320, 60, 0.25,  10,  7, 25, false, "the all-rounder: sound, lasting, nothing special"),
        M("bat",       "baseball bat",   "swing", 105, 11, 352, 70, 0.117, 10,  8,  8, false, "long and wide, but it splinters soon"),
        M("hatchet",   "hatchet",        "swing", 100,  9, 288, 55, 0.329,  8,  6, 14, false, "short and light: quick, cheap on endurance"),
        M("spear",     "spear",          "stab",   95, 12, 480, 30, 0.75,   8,  4,  8, true,  "reach nearly two tiles: strikes before the dead can bite; narrow, two hands"),
        M("plank",     "plank with nails", "swing", 80,  9, 336, 60, 0.25,   7,  5,  4, false, "improvised from wood: cheap and soon broken"),
        M("hammer",    "claw hammer",    "swing",  85,  9, 288, 50, 0.413,  6,  5,  0, false, "a tool first: small, quick, never breaks", slot: "tool"),
        M("knife",     "knife",          "stab",   70,  6, 256, 40, 0.587,  4,  3, 12, false, "fast, weak and nearly silent; short reach; kills the unaware outright", sneak: 400),
    ];

    /// <summary>The ammunition kinds.</summary>
    public static readonly List<AmmoSpec> AMMO =
    [
        new() { kind = "ammo", calibre = "9mm", box = 12 },
        new() { kind = "ammo38", calibre = ".38", box = 6 },
        new() { kind = "shells", calibre = "12-gauge", box = 6 },
        new() { kind = "ammo308", calibre = ".308", box = 5 },
        new() { kind = "bolts", calibre = "bolts", box = 6 },
    ];

    /// <summary>A box found before the box held a count (old saves): this many rounds.</summary>
    public const int LEGACY_BOX = AMMO_ROUNDS;

    /// <summary>The blow of nobody wielding anything (GUARD_DAMAGE every GUARD_ATTACK_EVERY), kept as a spec.</summary>
    public static readonly WeaponSpec UNARMED = M("unarmed", "bare hands", "swing", 50, 10, 320, 60, 0.25, 10, 6, 0, false, "");

    /// <summary>What a gun does as a club.</summary>
    public static readonly WeaponSpec BUTT = M("butt", "gun butt", "swing", 30, 12, 288, 50, 0.413, 8, 5, 0, false, "");

    static readonly System.Collections.Generic.Dictionary<string, WeaponSpec> SPEC_BY_KIND = BuildSpecs();

    static System.Collections.Generic.Dictionary<string, WeaponSpec> BuildSpecs()
    {
        var d = new System.Collections.Generic.Dictionary<string, WeaponSpec>();
        foreach (var w in SPECS) d[w.kind] = w;
        return d;
    }

    static readonly System.Collections.Generic.Dictionary<string, AmmoSpec> AMMO_BY_KIND = BuildAmmo();

    static System.Collections.Generic.Dictionary<string, AmmoSpec> BuildAmmo()
    {
        var d = new System.Collections.Generic.Dictionary<string, AmmoSpec>();
        foreach (var a in AMMO) d[a.kind] = a;
        return d;
    }

    /// <summary>The spec for a kind: a weapon, a firearm, or an axe or hammer (the generic "weapon" and "gun" need the item: see specOf(Item)). Null for anything else.</summary>
    public static WeaponSpec? specOfKind(string kind)
    {
        return SPEC_BY_KIND.GetValueOrDefault(kind);
    }

    /// <summary>What an item does as a weapon: its kind's spec; an old generic "weapon" is a crowbar, an old "gun" a pistol. Null if it isn't one.</summary>
    public static WeaponSpec? specOf(Item? item)
    {
        if (item == null) return null;
        if (item.kind == "weapon") return SPEC_BY_KIND["crowbar"];
        if (item.kind == "gun") return SPEC_BY_KIND["pistol"];
        return SPEC_BY_KIND.GetValueOrDefault(item.kind);
    }

    public static AmmoSpec? ammoOfKind(string kind)
    {
        return AMMO_BY_KIND.GetValueOrDefault(kind);
    }

    /// <summary>Items the weapon slot takes: a weapon of any sort or a firearm (the axe and the hammer are tools).</summary>
    public static bool isWieldKind(string kind)
    {
        if (kind == "weapon" || kind == "gun") return true;
        var w = SPEC_BY_KIND.GetValueOrDefault(kind);
        return w != null && w.slot == "weapon";
    }

    public static bool isGunKind(string kind)
    {
        return kind == "gun" || (SPEC_BY_KIND.GetValueOrDefault(kind)?.isFirearm ?? false);
    }

    public static bool isMeleeKind(string kind)
    {
        return kind == "weapon" || ((SPEC_BY_KIND.GetValueOrDefault(kind) is { } w) && !w.isFirearm && w.slot == "weapon");
    }

    public static bool isAmmoKind(string kind)
    {
        return AMMO_BY_KIND.ContainsKey(kind);
    }

    /// <summary>Where weapons and ammunition are kept (the armory).</summary>
    public static bool isArmoryKind(string kind)
    {
        return isWieldKind(kind) || isAmmoKind(kind);
    }

    /// <summary>Rounds in an ammunition item (an old box, with none counted: LEGACY_BOX).</summary>
    public static double rounds(Item item)
    {
        return item.wear > 0 ? item.wear : LEGACY_BOX;
    }

    /// <summary>How many rounds the gun holds at most.</summary>
    public static int capacity(Item gun)
    {
        return specOf(gun)?.magazine ?? 0;
    }

    /// <summary>The kind of ammunition a firearm takes (or null).</summary>
    public static string? ammoKindOf(Item gun)
    {
        return specOf(gun)?.ammo;
    }

    /// <summary>The action class an item plays (swing, stab, shoot, bow), for the animation; "swing" for anything else.</summary>
    public static string actionClass(Item? item)
    {
        return specOf(item)?.cls ?? "swing";
    }
}
