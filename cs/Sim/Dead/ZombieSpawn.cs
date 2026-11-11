// The night's arrivals (part of Zombies): how many of the dead come each night and how tough they are, when in the
// night they come, where (spawnPoint: in the dark past explored ground, a little way from someone; the map's edge once
// all is explored) and in what groups (spawnGroup). Zombies.updateZombies asks these each tick. Rolls: the sim RNG.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Zombies
{
    public static double zombiesForNight(double night, double colony = 0)
    {
        return ((FIRST_NIGHT_ZOMBIES + night) - 1) + JsMath.floor(((double)(colony) / (SURVIVORS_PER_EXTRA_ZOMBIE)));
    }

    /// <summary>Nights are sized for a group: someone alone faces half, two people three quarters, three or more the full count, in generated (harsh) worlds (solo balance pass: night waves built for a colony killed a lone survivor by night 4).</summary>
    public static double groupShare(double people)
    {
        return JsMath.min(1, ((double)((people + 1)) / (4)));
    }

    public static double zombieHealthForNight(double night)
    {
        return ZOMBIE_HEALTH + ((double)(ZOMBIE_HEALTH_PER_NIGHT) * ((night - 1)));
    }

    /// <summary>Tonight's total: fixed at nightfall from the colony's size then.</summary>
    public static double tonight(SimState state, double night)
    {
        return state.zombiesTonight ?? zombiesForNight(night, state.survivors.length);
    }

    /// <summary>From night 4 zombies come in groups: pairs on nights 4-6, threes on 7-9, and so on.</summary>
    public static double groupSize(double night)
    {
        return JsMath.ceil(((double)(night) / (3)));
    }

    /// <summary>How many zombies arrive at this tick (0 on most ticks). Groups are spread evenly over the night, half an interval in: night 1 (2 zombies, singly) spawns at 23:00 and 03:00; night 4 (5 zombies) as 2, 2, then 1.</summary>
    public static double spawnsAt(int tick, double? total = default)
    {
        var night = Time.nightNumber(tick);
        if (night == null)
        {
            return 0;
        }
        var count = total ?? zombiesForNight(Js.NN(night));
        var size = groupSize(Js.NN(night));
        var groups = JsMath.ceil(((double)(count) / (size)));
        var interval = JsMath.floor(((double)(Time.NIGHT_TICKS) / (groups)));
        var t = Time.ticksIntoNight(tick) - JsMath.floor(((double)(interval) / (2)));
        if (((t < 0) || ((t % interval) != 0)) || (((double)(t) / (interval)) >= groups))
        {
            return 0;
        }
        return JsMath.min(size, count - ((double)(((double)(t) / (interval))) * (size)));
    }
    public static double spawnsAt(double tick, double? total = default) => spawnsAt(Js.I(tick), total);

    /// <summary>The first tick of a night.</summary>
    public static bool nightFallsNow(int tick)
    {
        return (Time.nightNumber(tick) != null) && (Time.ticksIntoNight(tick) == 0);
    }
    public static bool nightFallsNow(double tick) => nightFallsNow(Js.I(tick));

    public static bool isSpawnTick(int tick)
    {
        return spawnsAt(tick) > 0;
    }
    public static bool isSpawnTick(double tick) => isSpawnTick(Js.I(tick));

    /// <summary>Where tonight's next group arrives: in the dark past explored ground, or (on a fully explored map) at the edge.</summary>
    public static Point? spawnPoint(SimState state)
    {
        var lamps = Power.litLamps(state);
        bool open(double x, double y)
        {
            return (((Grids.getTile(state.grid, x, y) == "floor") && (Rooms.labelAt(state, x, y) == Rooms.OUTDOORS)) && !state.survivors.some((s) => Space.manhattan(s.pos, new Point { x = Js.I(x), y = Js.I(y) }) < SPAWN_MIN_DISTANCE)) && !lamps.some((l) => Space.manhattan(l, new Point { x = Js.I(x), y = Js.I(y) }) < Power.LAMP_DETER);
        }
        if ((state.survivors.length > 0) && Grids.anyUnexplored(state.grid))
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var s = Rng.pick(state, state.survivors);
                var dx = Rng.randomInt(state, -FRONTIER_MAX, FRONTIER_MAX + 1);
                var dy = Rng.randomInt(state, -FRONTIER_MAX, FRONTIER_MAX + 1);
                if ((JsMath.abs(dx) + JsMath.abs(dy)) < FRONTIER_MIN)
                {
                    continue;
                }
                Point near = new Point { x = Js.I(JsMath.max(0, JsMath.min(state.grid.width - 1, s.pos.x + dx))), y = Js.I(JsMath.max(0, JsMath.min(state.grid.height - 1, s.pos.y + dy))) };
                var lit = Lights.near(state, near.x + 0.5, near.y + 0.5, FRONTIER_MAX + Lights.REACH);
                var p = Space.nearestTile(state.grid, near, (x, y) => !Fog.isExplored(state, x, y) && open(x, y) && ((lit.Count == 0) || (Lights.lightFrom(state, lit, Js.I(x), Js.I(y)).level < Lights.LIT)));
                if (Js.T(p) && (Space.manhattan(p, near) <= FRONTIER_MAX))
                {
                    return p;
                }
            }
        }
        var __d1 = state.grid;
        var width = __d1.width;
        var height = __d1.height;
        List<Point> edge = [];
        for (var x = 0; x < width; x++)
        {
            edge.pushAll([new Point { x = Js.I(x), y = Js.I(0) }, new Point { x = Js.I(x), y = Js.I(height - 1) }]);
        }
        for (var y = 1; y < (height - 1); y++)
        {
            edge.pushAll([new Point { x = Js.I(0), y = Js.I(y) }, new Point { x = Js.I(width - 1), y = Js.I(y) }]);
        }
        var candidates = edge.filter((p) => open(p.x, p.y));
        return (candidates.length > 0) ? Rng.pick(state, candidates) : null;
    }

    /// <summary>A group of `n` night zombies at one spot (the rest on the nearest free outdoor tiles).</summary>
    public static void spawnGroup(SimState state, double n, double health)
    {
        var p = spawnPoint(state);
        if (!Js.T(p))
        {
            return;
        }
        var night = Js.Q(Time.nightNumber(state.tick), 1);
        List<Zombie> group = [addZombie(state, p.x, p.y, health)];
        ZombieKinds.giveVariant(Js.NN(Js.Get(group, 0)), night);
        for (var i = 1; i < n; i++)
        {
            bool free(double x, double y)
            {
                return ((Grids.getTile(state.grid, x, y) == "floor") && (Rooms.labelAt(state, x, y) == Rooms.OUTDOORS)) && !state.zombies.some((z_1) => (z_1.pos.x == x) && (z_1.pos.y == y));
            }
            var q = Space.nearestTile(state.grid, p, (__f0, __f1) => free(__f0, __f1)) ?? p;
            var z = addZombie(state, q.x, q.y, health);
            ZombieKinds.giveVariant(z, night);
            group.push(z);
        }
        Hordes.formHorde(state, group, null, true);
        Events.logEvent(state, (n == 1) ? $"A zombie appeared at ({Js.Str(p.x)}, {Js.Str(p.y)})" : $"A group of {Js.Str(n)} zombies appeared at ({Js.Str(p.x)}, {Js.Str(p.y)})");
    }
}
