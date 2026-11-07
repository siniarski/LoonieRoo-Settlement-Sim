// Ported from src/sim/weapons.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Weapons: scavenged, kept in the armory, carried by guards.
//
// Guns go in the same slot. A loaded gun shoots from up to GUN_RANGE tiles
// with a clear line (jobs.ts), loudly (noise.ts); an empty one is a club. A
// guard with an empty gun fetches ammo the way they'd fetch a weapon: each
// box loads AMMO_ROUNDS.
//
// A survivor with guarding switched on and no weapon picks up the nearest free
// one between jobs. An equipped weapon is carried (`carriedBy`, no `pos`) in
// the survivor's `weapon` slot, separate from the hauling slot. Armed guards
// hit harder; a weapon breaks after WEAPON_KILLS kills, and is dropped where
// its owner dies.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Weapons
{

    /// <summary>Damage per blow with a weapon (unarmed is GUARD_DAMAGE, 50; zombies have 200).</summary>
    public const int ARMED_DAMAGE = 100;

    /// <summary>Kills before a weapon breaks.</summary>
    public const int WEAPON_KILLS = 5;

    /// <summary>When no weapon was found, don't look again for this many ticks.</summary>
    public const int ARM_RETRY_TICKS = 50;

    public const int GUN_RANGE = 6;

    public const int GUN_DAMAGE = 150;

    /// <summary>Ticks between shots.</summary>
    public const int GUN_EVERY = 15;

    public const int AMMO_ROUNDS = 6;

    public static bool wantsWeapon(SimState state, Survivor s)
    {
        return ((((((((!Js.T(s.dressing) && (s.priorities["guard"] > 0)) && !Js.T(s.jobId)) && !Js.T(s.sleep)) && !Js.T(s.meal)) && !Js.T(s.treatment)) && !Js.T(s.breakdown)) && !Js.T(s.leisure)) && !Needs.wantsSleep(state, s)) && (state.tick >= s.nextArmTick);
    }

    /// <summary>Runs when standing still, after treatment and before sleep: walk to the reserved weapon and pick it up. Returns true if arming handled this tick.</summary>
    public static bool updateArming(SimState state, Survivor s)
    {
        if (!Js.T(s.arming) && !startArming(state, s))
        {
            return false;
        }
        var item = Items.getItem(state, Js.NN(s.arming));
        if (!Js.T(Js.T(item?.pos)) || (item.reservedBy != s.id))
        {
            cancelArming(state, s, "weapon gone");
            return false;
        }
        if (!Space.samePoint(s.pos, item.pos))
        {
            var path = Pathfinding.findPathToAny(state.grid, s.pos, [item.pos]);
            if (!Js.T(path) || (path.length == 0))
            {
                cancelArming(state, s, "can't reach the weapon");
                s.nextArmTick = state.tick + ARM_RETRY_TICKS;
                return false;
            }
            Survivors.walk(s, path, state.walkabilityVersion);
            return true;
        }
        s.arming = null;
        if (item.kind == "ammo")
        {
            var gun = Js.T(s.weapon) ? Items.getItem(state, s.weapon) : null;
            if (gun?.kind != "gun")
            {
                item.reservedBy = null;
                return true;
            }
            Items.removeItem(state, item);
            gun.wear += AMMO_ROUNDS;
            s.note = "loaded their gun";
            return true;
        }
        item.pos = null;
        item.carriedBy = s.id;
        item.reservedBy = null;
        s.weapon = item.id;
        s.note = (item.kind == "gun") ? "armed with a gun" : "armed with a weapon";
        return true;
    }

    /// <summary>The survivor's gun, if it has rounds in it.</summary>
    public static Item? loadedGun(SimState state, Survivor s)
    {
        var gun = Js.T(s.weapon) ? Items.getItem(state, s.weapon) : null;
        return ((gun?.kind == "gun") && (gun.wear > 0)) ? gun : null;
    }

    public static bool startArming(SimState state, Survivor s)
    {
        if (!wantsWeapon(state, s))
        {
            return false;
        }
        var held = Js.T(s.weapon) ? Items.getItem(state, s.weapon) : null;
        List<string> wants = !Js.T(held) ? ["gun", "weapon"] : (((held.kind == "gun") && (held.wear == 0)) ? ((List<string>)["ammo"]) : ((List<string>)[]));
        if (wants.length == 0)
        {
            return false;
        }
        var weapon = wants.map((k) => Needs.nearestFreeItem(state, s, k)).find((i) => Js.T(Js.T(i?.pos)));
        if (!Js.T(Js.T(weapon?.pos)))
        {
            s.nextArmTick = state.tick + ARM_RETRY_TICKS;
            return false;
        }
        weapon.reservedBy = s.id;
        s.arming = weapon.id;
        s.note = $"going for {Js.Str((weapon.kind == "ammo") ? Js.Str("ammo") : Js.Str((weapon.kind == "gun") ? "a gun" : "a weapon"))} at ({Js.Str(weapon.pos.x)}, {Js.Str(weapon.pos.y)})";
        return true;
    }

    /// <summary>Gives up the reserved weapon (if any). Safe to call when not arming.</summary>
    public static void cancelArming(SimState state, Survivor s, string note)
    {
        if (!Js.T(s.arming))
        {
            return;
        }
        var item = Items.getItem(state, s.arming);
        if (Js.T(item) && (item.reservedBy == s.id))
        {
            item.reservedBy = null;
        }
        s.arming = null;
        s.goal = null;
        s.path = [];
        s.pathVersion = -1;
        s.note = note;
    }

    /// <summary>Drops the reserved-weapon plan if the weapon went away between ticks.</summary>
    public static void reconcileArming(SimState state, Survivor s)
    {
        if (!Js.T(s.arming))
        {
            return;
        }
        var item = Items.getItem(state, s.arming);
        if ((!Js.T(item) || (item.reservedBy != s.id)) || !Js.T(item.pos))
        {
            cancelArming(state, s, "weapon gone");
        }
    }

    /// <summary>Damage this survivor deals per blow.</summary>
    public static double blowDamage(Survivor s, double unarmed)
    {
        return Js.T(s.weapon) ? ARMED_DAMAGE : unarmed;
    }

    /// <summary>After an armed kill: wear the weapon, and break it at WEAPON_KILLS. (Guns don't break.)</summary>
    public static void wearWeapon(SimState state, Survivor s)
    {
        var item = Js.T(s.weapon) ? Items.getItem(state, s.weapon) : null;
        if (!Js.T(item) || (item.kind == "gun"))
        {
            return;
        }
        item.wear += 1;
        if (item.wear < WEAPON_KILLS)
        {
            return;
        }
        Items.removeItem(state, item);
        s.weapon = null;
        Events.logEvent(state, $"{Js.Str(s.name)}'s weapon broke");
    }

    /// <summary>Puts an equipped weapon on the ground where the survivor stands.</summary>
    public static void dropWeapon(SimState state, Survivor s)
    {
        var item = Js.T(s.weapon) ? Items.getItem(state, s.weapon) : null;
        s.weapon = null;
        if (Js.T(item))
        {
            Items.dropItem(state, item, s.pos);
        }
    }
}
