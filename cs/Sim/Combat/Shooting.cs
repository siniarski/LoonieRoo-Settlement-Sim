// Firing a gun (2026-11-10): a shot is a line from the shooter's centre along an aim, with a sideways error
// from the weapon's spread (a stateless hash of shooter, tick and pellet: no RNG, so a replay lands the same
// shots). The line runs tile by tile through the grid and stops at what stops sight: a wall, a shut door or a
// barricade on a tile, an edge wall, a shut edge door or boards across a window (Pathfinding.seesThrough: an
// unboarded window, whole or broken, lets it through). Along it, the first of the dead, the animals or the
// settlements' people whose body (a circle of HIT_RADIUS) it passes through takes the hit; a shot that
// misses one carries on to what is behind it. Past the weapon's range it falls. Damage falls from full at
// `effective` to `falloff` percent at `range`.
//
// Your own people stop the shot: a survivor on the line before whatever you aim at and the player (or the
// guard) holds fire (friendInLine), so a miss never hurts one of them: a stray round only reaches the dead,
// the animals and residents. Pellets (a shotgun's eight) each have their own spread and their own hit.
#nullable disable warnings
namespace LoonieRoo.Sim;

public sealed class ShotOutcome
{
    public bool fired;
    /// <summary>Pellets (projectiles) that hit something.</summary>
    public int hits;
    public int pellets;
    public bool killed;
    /// <summary>What it did, for the note: "shot it dead", "missed".</summary>
    public string note = "";
}

public static class Shooting
{
    /// <summary>The radius of what a shot can hit (SUB): a body.</summary>
    public const int HIT_RADIUS = 96;

    /// <summary>The longest aim vector a fire command takes (a component, either way).</summary>
    public const int AIM_VECTOR_MAX = 4096;

    /// <summary>Spread (as a ratio of sideways error to distance) for this shooter now: the weapon's own, and more moving, panicking or winded.</summary>
    public static double spreadOf(Survivor s, WeaponSpec w, bool moving)
    {
        double thousandths = w.spread;
        if (moving) thousandths += w.spreadMove;
        if (BodyMod.isPanicking(s)) thousandths += w.spreadPanic;
        if (BodyMod.isWinded(s)) thousandths += w.spreadWinded;
        return thousandths / 1000;
    }

    /// <summary>Damage fraction (percent) of a projectile that has flown this far (SUB).</summary>
    public static double damagePercent(WeaponSpec w, double distance)
    {
        var full = (double)w.effective * Motion.SUB;
        var max = (double)w.range * Motion.SUB;
        if (distance <= full || max <= full) return 100;
        var f = JsMath.min(1, (distance - full) / (max - full));
        return 100 - (100 - w.falloff) * f;
    }

    /// <summary>How far (SUB) a line from `at` along the unit vector (ux, uy) runs before a wall, a shut door, a barricade or an edge wall stops it, or `maxT` if nothing does.</summary>
    public static double wallDistance(Grid grid, Point at, double ux, double uy, double maxT)
    {
        int tx = Js.I(JsMath.floor((double)at.x / Motion.SUB));
        int ty = Js.I(JsMath.floor((double)at.y / Motion.SUB));
        int sx = ux > 0 ? 1 : -1;
        int sy = uy > 0 ? 1 : -1;
        double tMaxX = (ux != 0) ? (((sx > 0 ? (tx + 1) : tx) * (double)Motion.SUB) - at.x) / ux : double.PositiveInfinity;
        double tMaxY = (uy != 0) ? (((sy > 0 ? (ty + 1) : ty) * (double)Motion.SUB) - at.y) / uy : double.PositiveInfinity;
        double tDeltaX = (ux != 0) ? Motion.SUB / JsMath.abs(ux) : double.PositiveInfinity;
        double tDeltaY = (uy != 0) ? Motion.SUB / JsMath.abs(uy) : double.PositiveInfinity;
        for (; ; )
        {
            bool xFirst = tMaxX <= tMaxY;
            var t = xFirst ? tMaxX : tMaxY;
            if (t > maxT) return maxT;
            int nx = tx + (xFirst ? sx : 0);
            int ny = ty + (xFirst ? 0 : sy);
            if (!Grids.inBounds(grid, nx, ny)) return t;
            if (!Pathfinding.seesThrough(grid, Grids.edgeBetween(grid, tx, ty, nx, ny))) return t;
            var tile = Grids.getTile(grid, nx, ny);
            if (tile == "wall" || tile == "barricade" || (tile == "door" && !Grids.isOpen(grid, nx, ny))) return t;
            tx = nx;
            ty = ny;
            if (xFirst) tMaxX += tDeltaX; else tMaxY += tDeltaY;
        }
    }

    /// <summary>The first thing a line hits: how far along, and what (a Zombie, Animal or Resident), or null. `bodies` are those within `maxT` + HIT_RADIUS of the start only.</summary>
    static (double along, object? target) firstHit(SimState state, Point at, double ux, double uy, double maxT, Survivor? alsoFriends, out Survivor? friend)
    {
        double best = maxT + 1;
        object? target = null;
        friend = null;
        var reach2 = (maxT + HIT_RADIUS) * (maxT + HIT_RADIUS);
        void test(IHasAt t, object what, bool isFriend = false)
        {
            double dx = t.at.x - at.x, dy = t.at.y - at.y;
            if (dx * dx + dy * dy > reach2) return;
            double along = dx * ux + dy * uy;
            if (along < 0 || along > maxT) return;
            double perp = JsMath.abs(dx * uy - dy * ux);
            if (perp > HIT_RADIUS) return;
            if (along >= best) return;
            best = along;
            target = what;
        }
        foreach (var z in state.zombies) test(z, z);
        foreach (var a in state.animals) if (!AnimalCars.hidden(a)) test(a, a); // under a car: the car takes it
        foreach (var r in state.residents) test(r, r);
        if (alsoFriends != null)
        {
            // Our own people: the nearest, if nearer than any of the rest.
            double bestFriend = double.PositiveInfinity;
            foreach (var o in state.survivors)
            {
                if (o == alsoFriends) continue;
                double dx = o.at.x - at.x, dy = o.at.y - at.y;
                if (dx * dx + dy * dy > reach2) continue;
                double along = dx * ux + dy * uy;
                if (along < 0 || along > maxT || JsMath.abs(dx * uy - dy * ux) > HIT_RADIUS - 16) continue;
                if (along < bestFriend) { bestFriend = along; friend = o; }
            }
            if (friend != null && bestFriend >= best) friend = null;
        }
        return (best > maxT ? maxT : best, target);
    }

    /// <summary>A guard's job to hunt a zombie that has just died (shot by someone else: a stray round): the job is done with, and whoever held it is freed.</summary>
    static void forgetZombie(SimState state, string zombieId)
    {
        foreach (var job in state.jobs.filter((j) => (j.type == "guard") && (j.zombieId == zombieId)))
        {
            var holder = state.survivors.find((q) => q.jobId == job.id);
            if (holder != null) Work.finishJob(state, holder, job, "the zombie was shot");
            else state.jobs.splice(state.jobs.indexOf(job), 1);
        }
    }

    /// <summary>The unit vector of an aim (x, y), or false if there is none.</summary>
    public static bool unit(double x, double y, out double ux, out double uy)
    {
        var len = JsMath.sqrt(x * x + y * y);
        if (len == 0) { ux = uy = 0; return false; }
        ux = x / len;
        uy = y / len;
        return true;
    }

    /// <summary>A survivor of ours standing on the straight line from you along the aim before whatever it would hit, or before the weapon's range or a wall.</summary>
    public static Survivor? friendInLine(SimState state, Survivor s, WeaponSpec w, double aimX, double aimY)
    {
        if (!unit(aimX, aimY, out var ux, out var uy)) return null;
        var max = JsMath.min((double)w.range * Motion.SUB, wallDistance(state.grid, s.at, ux, uy, (double)w.range * Motion.SUB));
        firstHit(state, s.at, ux, uy, max, s, out var friend);
        return friend;
    }

    /// <summary>One shot, the ammunition already checked: the round used, the noise made, the pellets flown. The caller sets the cooldown.</summary>
    public static ShotOutcome fire(SimState state, Survivor s, Item gun, double aimX, double aimY, bool moving)
    {
        var o = new ShotOutcome();
        var w = Weapons.specOf(gun);
        if (w == null || !w.isFirearm || gun.wear < 1 || !unit(aimX, aimY, out var bx, out var by)) return o;
        o.fired = true;
        o.pellets = w.pellets;
        gun.wear -= 1;
        Noise.makeNoise(state, s.pos, "gunfire", w.noise);
        var spread = spreadOf(s, w, moving);
        var maxT = (double)w.range * Motion.SUB;
        // The most a bolt or a pellet could do: the skill and trait bonuses split between the pellets.
        var total = Moodlets.boostDamage(s, (w.damage * w.pellets + (double)Skills.GUARD_DAMAGE_PER_LEVEL * Skills.skillLevel(s, "guard")) + (Skills.hasTrait(s, "hardened") ? Skills.HARDENED_DAMAGE : 0));
        var each = total / w.pellets;
        var damage = new List<(object target, double dmg)>();
        for (int p = 0; p < w.pellets; p++)
        {
            var side = (Hash.hashRoll(Hash.idNumber(s.id), state.tick, p, 0x5107) * 2 - 1) * spread;
            unit(bx - by * side, by + bx * side, out var ux, out var uy);
            var reach = wallDistance(state.grid, s.at, ux, uy, maxT);
            var (along, target) = firstHit(state, s.at, ux, uy, reach, null, out _);
            if (target == null) continue;
            o.hits++;
            var dmg = each * damagePercent(w, along) / 100;
            var at = damage.FindIndex((d) => d.target == target);
            if (at >= 0) damage[at] = (target, damage[at].dmg + dmg);
            else damage.Add((target, dmg));
        }
        string last = "";
        foreach (var (target, dmg) in damage)
        {
            if (target is Zombie z)
            {
                if (Zombies.damageZombie(state, z, JsMath.round(dmg), s)) { o.killed = true; last = "shot it dead"; forgetZombie(state, z.id); }
                else last = "shot it";
            }
            else if (target is Animal a)
            {
                last = Handiwork.hitAnimal(state, s, a.id, JsMath.round(dmg));
            }
            else if (target is Resident r)
            {
                last = Residents.hitResident(state, r, JsMath.round(dmg), s) ? $"killed {r.name}" : $"hit {r.name}";
            }
        }
        o.note = (o.hits == 0) ? "missed" : ((o.pellets > 1 && !o.killed) ? $"{last} ({o.hits} of {o.pellets} pellets)" : last);
        return o;
    }
}
