// Ported from src/sim/weapons.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Weapons: scavenged, kept in the armory, carried by guards.
//
// What each weapon does is in WeaponTable.cs (damage, reach, swing time, arc, noise, durability, magazine, range,
// spread...); shots are in Shooting.cs. Guns go in the same slot as melee weapons. A loaded gun shoots along a
// line as far as its range, loudly (noise.ts: each weapon its own radius); an empty one is a club. A guard with
// an empty gun fetches ammo the way they'd fetch a weapon: a box gives as many rounds as the gun has room for,
// and what is left stays in the box.
//
// A survivor with guarding switched on and no weapon picks up the nearest free
// one between jobs: a gun if ammunition for it is lying about, else a melee weapon. An equipped weapon is
// carried (`carriedBy`, no `pos`) in the survivor's `weapon` slot, separate from the hauling slot. A melee
// weapon breaks after its durability in kills, and is dropped where its owner dies.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Weapons
{

    /// <summary>Damage per blow of the old generic weapon (the crowbar's; each weapon has its own now: WeaponTable.cs).</summary>
    public const int ARMED_DAMAGE = 100;

    /// <summary>Kills before the old generic weapon broke (a weapon's own durability now).</summary>
    public const int WEAPON_KILLS = 5;

    /// <summary>When no weapon was found, don't look again for this many ticks.</summary>
    public const int ARM_RETRY_TICKS = 50;

    public const int GUN_RANGE = 6;

    public const int GUN_DAMAGE = 150;

    /// <summary>Ticks between shots.</summary>
    public const int GUN_EVERY = 15;

    /// <summary>A turret's load per box, and the rounds in a box from before boxes had a count.</summary>
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
        if (isAmmoKind(item.kind))
        {
            var gun = heldGun(state, s);
            if (gun == null || ammoKindOf(gun) != item.kind || gun.wear >= capacity(gun))
            {
                item.reservedBy = null;
                return true;
            }
            var loaded = loadRounds(state, gun, item);
            var w = specOf(gun)!;
            s.readyTick = JsMath.max(s.readyTick, state.tick + (w.perRound ? ((double)w.reload * loaded) : w.reload));
            s.note = "loaded their gun";
            return true;
        }
        if (wielded(state, s) is { } old)
        {
            // Changing a spent gun for a club: the gun is put down here.
            s.weapon = null;
            Items.dropItem(state, old, s.pos);
        }
        item.pos = null;
        item.carriedBy = s.id;
        item.reservedBy = null;
        s.weapon = item.id;
        s.note = isGunKind(item.kind) ? "armed with a gun" : "armed with a weapon";
        return true;
    }

    /// <summary>Moves up to the gun's room of rounds from a box into it; the box is used up at zero (else it stays, with the rest). Returns the rounds moved.</summary>
    public static double loadRounds(SimState state, Item gun, Item box, double max = double.PositiveInfinity)
    {
        var take = JsMath.min(JsMath.min(capacity(gun) - gun.wear, rounds(box)), max);
        if (take <= 0) return 0;
        gun.wear += take;
        box.wear = rounds(box) - take;
        box.reservedBy = null;
        if (box.wear <= 0)
        {
            if (box.carriedBy != null)
            {
                var holder = state.survivors.find((q) => q.id == box.carriedBy);
                if (holder != null) Inventory.unpack(holder, box.id);
            }
            Items.removeItem(state, box);
        }
        return take;
    }

    /// <summary>What the survivor wields in the weapon slot (a weapon or a gun), or null.</summary>
    public static Item? wielded(SimState state, Survivor s)
    {
        return Js.T(s.weapon) ? Items.getItem(state, s.weapon) : null;
    }

    /// <summary>The survivor's gun (loaded or not), or null.</summary>
    public static Item? heldGun(SimState state, Survivor s)
    {
        var item = wielded(state, s);
        return (item != null && isGunKind(item.kind)) ? item : null;
    }

    /// <summary>Hands free for a two-handed weapon: nothing carried in them, no cart, both arms.</summary>
    public static bool handsFree(Survivor s)
    {
        return !Js.T(s.carrying) && !Js.T(s.cart) && !Skills.hasTrait(s, "lostArm");
    }

    /// <summary>A two-handed gun can't be fired with the hands full.</summary>
    static bool handsFreeFor(Item gun, Survivor s)
    {
        return !(specOf(gun)?.twoHanded ?? false) || handsFree(s);
    }

    /// <summary>What this survivor's blow is now: the melee weapon in hand, else an axe or a hammer in the tool slot, a gun's butt, or bare hands; a two-handed one with the hands full is bare hands.</summary>
    public static WeaponSpec meleeSpec(SimState state, Survivor s)
    {
        var spec = specOf(wielded(state, s));
        if (spec != null && spec.isFirearm) spec = BUTT;
        if (spec == null)
        {
            var tool = Js.T(s.tool) ? Items.getItem(state, s.tool) : null;
            var ts = specOf(tool);
            if (ts != null && ts.slot == "tool") spec = ts;
        }
        spec ??= UNARMED;
        return (spec.twoHanded && !handsFree(s)) ? UNARMED : spec;
    }

    /// <summary>The item a blow wears: the melee weapon in the weapon slot (a gun or a tool doesn't wear), or null.</summary>
    static Item? wornByBlows(SimState state, Survivor s)
    {
        var w = wielded(state, s);
        var spec = specOf(w);
        return (spec != null && !spec.isFirearm && spec.durability > 0) ? w : null;
    }

    /// <summary>The survivor's gun, if it has rounds in it (and the hands to fire it).</summary>
    public static Item? loadedGun(SimState state, Survivor s)
    {
        var gun = heldGun(state, s);
        return ((gun != null) && (gun.wear > 0) && handsFreeFor(gun, s)) ? gun : null;
    }

    /// <summary>What guards pick up, best first: a gun only if ammunition for it is lying about.</summary>
    static readonly string[] GUN_PREFERENCE = ["rifle", "shotgun", "revolver", "pistol", "gun", "crossbow"];
    static readonly string[] MELEE_PREFERENCE = ["machete", "crowbar", "hatchet", "pipe", "bat", "spear", "knife", "sledge", "plank", "weapon"];

    /// <summary>How a guard (or the scripted player) ranks a weapon lying about: lower is better, guns first.</summary>
    public static int preferenceRank(string kind)
    {
        var g = Array.IndexOf(GUN_PREFERENCE, kind);
        if (g >= 0) return g;
        var m = Array.IndexOf(MELEE_PREFERENCE, kind);
        return m >= 0 ? 10 + m : 99;
    }

    public static bool startArming(SimState state, Survivor s)
    {
        if (!wantsWeapon(state, s))
        {
            return false;
        }
        var held = wielded(state, s);
        bool spentGun = held != null && isGunKind(held.kind) && held.wear == 0;
        if (held != null && !spentGun)
        {
            return false;
        }
        // The free arms and ammunition in the world, once: the nearest reachable of a kind from here.
        var free = state.items.filter((i) => (i.pos != null) && (i.reservedBy == null) && isArmoryKind(i.kind));
        Item? nearest(string kind) => free.filter((i) => i.kind == kind).sort((a, b) => Space.manhattan(s.pos, a.pos) - Space.manhattan(s.pos, b.pos)).find((i) => Pathfinding.canReach(state.grid, state.walkabilityVersion, s.pos, [i.pos]));
        Item? weapon = null;
        if (spentGun)
        {
            weapon = nearest(ammoKindOf(held!)!);
        }
        else
        {
            foreach (var k in GUN_PREFERENCE)
            {
                if (!free.some((i) => i.kind == k)) continue;
                var ammoKind = specOfKind(k)?.ammo ?? "ammo";
                if (!free.some((i) => i.kind == ammoKind)) continue;
                weapon = nearest(k);
                if (weapon != null) break;
            }
        }
        if (weapon == null)
        {
            foreach (var k in MELEE_PREFERENCE)
            {
                weapon = nearest(k);
                if (weapon != null) break;
            }
        }
        if (!Js.T(Js.T(weapon?.pos)))
        {
            s.nextArmTick = state.tick + ARM_RETRY_TICKS;
            return false;
        }
        weapon.reservedBy = s.id;
        s.arming = weapon.id;
        s.note = $"going for {Js.Str(isAmmoKind(weapon.kind) ? "ammo" : (isGunKind(weapon.kind) ? "a gun" : "a weapon"))} at ({Js.Str(weapon.pos.x)}, {Js.Str(weapon.pos.y)})";
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

    /// <summary>Damage this survivor deals per blow: the weapon's own (bare hands: `unarmed`).</summary>
    public static double blowDamage(SimState state, Survivor s, double unarmed)
    {
        var spec = meleeSpec(state, s);
        return (spec == UNARMED) ? unarmed : spec.damage;
    }

    /// <summary>After an armed kill: wear the weapon, and break it at its durability. (Guns, axes and hammers don't break.)</summary>
    public static void wearWeapon(SimState state, Survivor s)
    {
        var item = wornByBlows(state, s);
        if (item == null)
        {
            return;
        }
        item.wear += 1;
        if (item.wear < specOf(item)!.durability)
        {
            return;
        }
        Items.removeItem(state, item);
        s.weapon = null;
        Events.logEvent(state, $"{Js.Str(s.name)}'s weapon broke");
    }

    /// <summary>How far (tiles) a guard will shoot from: past the full-damage range, half the way to the furthest.</summary>
    public static double guardRange(WeaponSpec w)
    {
        return w.effective + ((double)(w.range - w.effective) / 2);
    }

    /// <summary>A guard's shot at a zombie: the gun loaded, the zombie within the gun's guard range, the line clear and none of ours in it. Returns whether the guard is lined up (a round is fired if the gun is ready: readyTick).</summary>
    public static bool guardShoot(SimState state, Survivor s, Item gun, Zombie z, double extraRange)
    {
        var w = specOf(gun)!;
        double dx = z.at.x - s.at.x, dy = z.at.y - s.at.y;
        var dist = JsMath.sqrt((dx * dx) + (dy * dy));
        if (dist > ((guardRange(w) + extraRange) * Motion.SUB)) return false;
        if (!Shooting.unit(dx, dy, out var ux, out var uy)) return false;
        if (Shooting.wallDistance(state.grid, s.at, ux, uy, dist) < (dist - Motion.HALF)) return false;
        if (Shooting.friendInLine(state, s, w, dx, dy) != null) return false;
        if (state.tick < s.readyTick) return true;
        Shooting.fire(state, s, gun, dx, dy, s.move != null);
        s.readyTick = state.tick + w.swing;
        return true;
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
