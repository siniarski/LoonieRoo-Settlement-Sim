// Ported from src/sim/hordes.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Hordes: zombies moving together, on one flow field.
//
// Not every zombie is in one. Sleepers and the odd wanderer stay loners and think for themselves
// (Zombies.cs plan). A horde is:
//   made     by a night's wave (a group of HORDE_MIN or more arriving together: Zombies.cs
//            spawnGroup), or a herd breaking into real zombies near your people (Herds.cs).
//   goals    hunting: the survivors it's after (a night wave knows where everyone is; others go
//            for whoever any member sees or hears). Roaming: a heading, somewhere off across the map.
//   moves    on one flow field (flowField, HordeField.cs): the cost to reach a goal from every tile in a box round
//            the horde, worked out backwards from the goals. Walking a tile costs STEP; a wall, door,
//            window or barricade in the way costs the time it takes to batter down (its health at
//            BATTER_RATE), a parked car the steps it costs to climb (Crossings.cs). So a walled-in colony draws a horde to its weakest point, and the whole
//            horde piles up there. Each member steps to its cheapest neighbour; battering is done as
//            it always was (Zombies.cs). Goals outside the box pull through its edge (each edge tile
//            seeded with its distance to the nearest goal).
//   refresh  every REFRESH_EVERY ticks: members gone are dropped, goals and the box worked out
//            again. The field depends only on what's stored here (goals, box) and on the walls and
//            open doors now, so it's derived data, cached, never saved, and a reloaded game moves
//            exactly as the one that was saved.
// Every SOCIAL_EVERY ticks hordes meet and part (in list order, the same way every time):
//   joining  two hordes within JOIN_RADIUS of each other that both roam, or both hunt, become one
//            (the bigger keeps its goals). An awake loner (static or herd, after nobody) within
//            LONER_JOIN of a horde falls in with it. So hordes snowball as they travel.
//   noise    (Noise.cs) a roaming horde mostly in earshot turns towards a noise; if only some of it
//            is, those split off as their own horde (or loners, if too few).
//   strays   a member more than STRAY_RADIUS from the horde's middle drops out, a loner.
//   shedding a big horde (SHED_MIN or more) has a small chance each hour (SHED_CHANCE, a hash roll)
//            of a small part (SHED_SHARE_MIN..MAX, the ones furthest back) breaking away, heading off
//            to one side. So even a huge herd leaves stragglers across the map.
//   dissolve under HORDE_MIN members, the rest are loners.
//   far off  a roaming horde with nobody of yours within DEMOTE_RADIUS becomes a herd marker again
//            (Herds.cs), its zombies counted into it, until it comes near someone and breaks up again.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Hordes
{

    public const int HORDE_MIN = 3;

    public const int REFRESH_EVERY = 15;

    public const int SOCIAL_EVERY = 50;

    /// <summary>Box margin round the members (and goals near them), and its largest side.</summary>
    public const int BOX_MARGIN = 14;

    public const int BOX_MAX = 160;

    /// <summary>Field costs: a tile walked is STEP; battering costs STEP per BATTER_RATE health.</summary>
    public const int STEP = 10;

    /// <summary>Damage a zombie does in the time it takes to walk a tile.</summary>
    public static double batterRate()
    {
        return ((double)(((double)(Zombies.ZOMBIE_DAMAGE) * (Zombies.ZOMBIE_TICKS_PER_TILE))) / (Zombies.ZOMBIE_ATTACK_EVERY));
    }

    /// <summary>A goal (a survivor) moving no further than this from where it was leaves the field as it is.</summary>
    public const int GOAL_SLACK = 3;

    /// <summary>Goals that moved further are taken afresh no more often than this.</summary>
    public const int GOAL_EVERY = 60;

    /// <summary>Members this close to the box's edge (not the map's) make it be worked out again.</summary>
    public const int BOX_SLACK = 4;

    public const int JOIN_RADIUS = 10;

    public const int LONER_JOIN = 6;

    public const int STRAY_RADIUS = 24;

    public const int SHED_MIN = 20;

    public const double SHED_CHANCE = 0.15;

    public const double SHED_SHARE_MIN = 0.05;

    public const double SHED_SHARE_MAX = 0.15;

    /// <summary>Roaming hordes this far from all your people go back to being herd markers (past where herds break up).</summary>
    public static double demoteRadius()
    {
        return Herds.MATERIALIZE_RADIUS + 20;
    }

    public static List<Horde> hordesOf(SimState state)
    {
        return state.hordes ?? [];
    }

    public static Horde? getHorde(SimState state, string? id)
    {
        return Js.T(id) ? Lookup.findById(hordesOf(state), id) : null;
    }

    /// <summary>A new horde of these zombies (HORDE_MIN or more): hunting the colony (a night's wave) or roaming towards a heading.</summary>
    public static Horde? formHorde(SimState state, List<Zombie> members, Point? heading, bool night)
    {
        if (members.length < HORDE_MIN)
        {
            return null;
        }
        state.hordes ??= [];
        Horde h = new Horde { id = $"hd{Js.Str(state.nextId++)}", members = members.map((z) => z.id), mode = night ? "hunt" : "roam", goals = [], goalIds = [], heading = heading, box = new HordeBox { x0 = Js.I(0), y0 = Js.I(0), x1 = Js.I(0), y1 = Js.I(0) }, refreshTick = -REFRESH_EVERY, night = night };
        foreach (var z in Js.Iter(members))
        {
            z.hordeId = h.id;
        }
        state.hordes.push(h);
        refresh(state, h);
        return h;
    }

    /// <summary>The horde's members still about.</summary>
    public static List<Zombie> membersOf(SimState state, Horde h)
    {
        List<Zombie> @out = [];
        foreach (var id in Js.Iter(h.members))
        {
            var z = Lookup.findById(state.zombies, id);
            if (Js.T(z) && (z.hordeId == h.id))
            {
                @out.push(z);
            }
        }
        return @out;
    }

    /// <summary>The middle of a horde: the median each way, so a few stragglers don't drag it after them.</summary>
    public static Point middle(List<Zombie> zs)
    {
        var xs = zs.map((z) => z.pos.x).sort((a, b) => a - b);
        var ys = zs.map((z) => z.pos.y).sort((a, b) => a - b);
        var k = Js.Shr(zs.length - 1, 1);
        return new Point { x = Js.I(Js.NN(Js.Get(xs, k))), y = Js.I(Js.NN(Js.Get(ys, k))) };
    }

    /// <summary>Members dropped, goals and the box worked out again.</summary>
    public static void refresh(SimState state, Horde h)
    {
        var zs = membersOf(state, h);
        h.members = zs.map((z) => z.id);
        h.refreshTick = state.tick;
        if (zs.length == 0)
        {
            return;
        }
        List<Survivor> prey = h.night ? (state.survivors).ToList() : state.survivors.filter((s) => zs.some((z) => Zombies.noticesSurvivor(state, z, s)));
        var wasMode = h.mode;
        var was = h.goals;
        var wasIds = h.goalIds;
        if (prey.length > 0)
        {
            h.mode = "hunt";
            var goals = prey.map((s) => new Point { x = Js.I(s.pos.x), y = Js.I(s.pos.y) });
            var ids = prey.map((s) => s.id);
            var samePeople = ((wasMode == "hunt") && (ids.length == wasIds.length)) && ids.every((id, i) => id == Js.Get(wasIds, i));
            var barely = Js.T(samePeople) && ids.every((_, i) => Space.manhattan(Js.NN(Js.Get(goals, i)), Js.NN(Js.Get(was, i))) <= GOAL_SLACK);
            if (!Js.T(samePeople) || (!Js.T(barely) && ((state.tick - (h.goalTick ?? -GOAL_EVERY)) >= GOAL_EVERY)))
            {
                h.goals = goals;
                h.goalIds = ids;
                h.goalTick = state.tick;
            }
        }
        else
        {
            if (((h.mode == "hunt") && !Js.T(h.heading)) && (h.goals.length > 0))
            {
                h.heading = Js.CopyAs<Point>(Js.NN(Js.Get(h.goals, 0)));
            }
            h.mode = "roam";
            h.goals = Js.T(h.heading) ? [Js.Clone(h.heading)] : [];
            h.goalIds = [];
        }
        var goalsKept = h.goals == was;
        var mid = middle(zs);
        double x0 = mid.x;
        double y0 = mid.y;
        double x1 = mid.x;
        double y1 = mid.y;
        foreach (var p in Js.Iter([.. zs.map((z) => z.pos), .. h.goals.filter((g) => Space.manhattan(g, mid) <= BOX_MAX)]))
        {
            x0 = JsMath.min(x0, p.x);
            y0 = JsMath.min(y0, p.y);
            x1 = JsMath.max(x1, p.x);
            y1 = JsMath.max(y1, p.y);
        }
        var __d0 = state.grid;
        var width = __d0.width;
        var height = __d0.height;
        (double, double) clampBox(double lo, double hi, double centre, double size)
        {
            lo -= BOX_MARGIN;
            hi += BOX_MARGIN;
            if (((hi - lo) + 1) > BOX_MAX)
            {
                lo = centre - (Js.Shr(BOX_MAX, 1));
                hi = (lo + BOX_MAX) - 1;
            }
            return (JsMath.max(0, lo), JsMath.min(size - 1, hi));
        }
        var __bx = clampBox(x0, x1, mid.x, width); x0 = (int)__bx.Item1; x1 = (int)__bx.Item2;
        var __by = clampBox(y0, y1, mid.y, height); y0 = (int)__by.Item1; y1 = (int)__by.Item2;
        var b = h.box;
        bool inside(Point q)
        {
            return (((((q.x - b.x0) >= BOX_SLACK) || (b.x0 == 0)) && (((b.x1 - q.x) >= BOX_SLACK) || (b.x1 == (width - 1)))) && (((q.y - b.y0) >= BOX_SLACK) || (b.y0 == 0))) && (((b.y1 - q.y) >= BOX_SLACK) || (b.y1 == (height - 1)));
        }
        if (!goalsKept || !zs.every((z) => inside(z.pos)))
        {
            h.box = new HordeBox { x0 = Js.I(x0), y0 = Js.I(y0), x1 = Js.I(x1), y1 = Js.I(y1) };
        }
        foreach (var z in Js.Iter(zs))
        {
            z.targetId = null;
            if (h.mode == "hunt")
            {
                var best = double.PositiveInfinity;
                h.goals.forEach((g, i) =>
        {
            var d = Space.manhattan(g, z.pos);
            if (d < best)
            {
                best = d;
                z.targetId = Js.NN(Js.Get(h.goalIds, i));
            }
        });
            }
        }
    }

    /// <summary>Every tick: refreshes due, and every SOCIAL_EVERY ticks hordes meet and part.</summary>
    public static void updateHordes(SimState state)
    {
        var list = hordesOf(state);
        if ((list.length == 0) && ((state.tick % SOCIAL_EVERY) != 0))
        {
            return;
        }
        foreach (var h in Js.Iter((list).ToList()))
        {
            if ((state.tick - h.refreshTick) >= REFRESH_EVERY)
            {
                refresh(state, h);
            }
        }
        if ((state.tick % SOCIAL_EVERY) == 0)
        {
            social(state);
        }
        foreach (var h in Js.Iter((hordesOf(state)).ToList()))
        {
            var zs = membersOf(state, h);
            if (zs.length < HORDE_MIN)
            {
                disband(state, h, zs);
            }
        }
    }

    public static void disband(SimState state, Horde h, List<Zombie> zs)
    {
        foreach (var z in Js.Iter(zs))
        {
            z.hordeId = null;
            if (Js.T((h.mode == "roam") && Js.T(h.heading)))
            {
                z.heading = Js.CopyAs<Point>(h.heading);
            }
        }
        Js.NN(state.hordes).splice(Js.NN(state.hordes).indexOf(h), 1);
    }

    public static void social(SimState state)
    {
        var list = hordesOf(state);
        for (var merged = true; Js.T(merged); )
        {
            merged = false;
            for (var i = 0; (i < list.length) && !Js.T(merged); i++)
            {
                for (var j = i + 1; (j < list.length) && !Js.T(merged); j++)
                {
                    var a = Js.NN(Js.Get(list, i));
                    var b = Js.NN(Js.Get(list, j));
                    if ((a.mode != b.mode) || (a.night != b.night))
                    {
                        continue;
                    }
                    var za = membersOf(state, a);
                    var zb = membersOf(state, b);
                    if (((za.length == 0) || (zb.length == 0)) || (Space.manhattan(middle(za), middle(zb)) > JOIN_RADIUS))
                    {
                        continue;
                    }
                    var __c5 = za.length >= zb.length; var big = __c5 ? a : b; var small = __c5 ? b : a; var smallZs = __c5 ? zb : za;
                    foreach (var z in Js.Iter(smallZs))
                    {
                        z.hordeId = big.id;
                    }
                    big.members.pushAll(small.members);
                    list.splice(list.indexOf(small), 1);
                    refresh(state, big);
                    merged = true;
                }
            }
        }
        foreach (var h in Js.Iter((list).ToList()))
        {
            var zs = membersOf(state, h);
            if (zs.length == 0)
            {
                continue;
            }
            var mid = middle(zs);
            foreach (var z in Js.Iter(state.zombies))
            {
                if (Js.T((Js.T((Js.T(Js.T(z.hordeId) || ((z.kind != "static") && (z.kind != "herd")))) || (z.targetId != null))) || (state.tick < z.stuckUntil)))
                {
                    continue;
                }
                if (Space.manhattan(z.pos, mid) > LONER_JOIN)
                {
                    continue;
                }
                z.hordeId = h.id;
                z.heading = null;
                h.members.push(z.id);
            }
            foreach (var z in Js.Iter(zs))
            {
                if (Space.manhattan(z.pos, mid) <= STRAY_RADIUS)
                {
                    continue;
                }
                z.hordeId = null;
                if (Js.T(h.heading))
                {
                    z.heading = Js.CopyAs<Point>(h.heading);
                }
            }
            shed(state, h);
            demote(state, h);
        }
    }

    /// <summary>A big horde, now and then, loses a small part off its back, heading off to one side.</summary>
    public static void shed(SimState state, Horde h)
    {
        if ((state.tick % Time.TICKS_PER_HOUR) != 0)
        {
            return;
        }
        var zs = membersOf(state, h);
        if (zs.length < SHED_MIN)
        {
            return;
        }
        double roll(double k)
        {
            return Hash.hashRoll(Hash.idNumber(h.id), state.tick, 1517.0, k);
        }
        if (roll(0) >= SHED_CHANCE)
        {
            return;
        }
        var share = SHED_SHARE_MIN + ((double)(roll(1)) * ((SHED_SHARE_MAX - SHED_SHARE_MIN)));
        var n = JsMath.max(1, JsMath.floor(((double)(zs.length) * (share))));
        var goal = Js.At(h.goals, 0, middle(zs));
        var back = Js.ListOf(zs).sort((a, b) => ((Space.manhattan(b.pos, goal) - Space.manhattan(a.pos, goal)) is var __or6 && Js.T(__or6) ? __or6 : ((string.CompareOrdinal(a.id, b.id) < 0) ? -1 : 1))).slice(0, n);
        var mid = middle(back);
        var dx = goal.x - mid.x;
        var dy = goal.y - mid.y;
        var side = (roll(2) < 0.5) ? 1 : -1;
        var __d7 = state.grid;
        var width = __d7.width;
        var height = __d7.height;
        Point heading = new Point { x = Js.I(JsMath.max(0, JsMath.min(width - 1, mid.x - ((double)(side) * (dy))))), y = Js.I(JsMath.max(0, JsMath.min(height - 1, mid.y + ((double)(side) * (dx))))) };
        foreach (var z in Js.Iter(back))
        {
            z.hordeId = null;
        }
        var split = formHorde(state, back, heading, false);
        if (!Js.T(split))
        {
            foreach (var z in Js.Iter(back))
            {
                z.heading = Js.CopyAs<Point>(heading);
            }
        }
        Events.logEvent(state, $"{Js.Str(n)} broke away from a horde of {Js.Str(zs.length)} near ({Js.Str(mid.x)}, {Js.Str(mid.y)})");
    }

    /// <summary>A roaming horde with nobody of yours near goes back to being a herd marker (Herds.cs).</summary>
    public static void demote(SimState state, Horde h)
    {
        if ((h.mode != "roam") || h.night)
        {
            return;
        }
        var zs = membersOf(state, h);
        if (zs.length == 0)
        {
            return;
        }
        var mid = middle(zs);
        if (state.survivors.some((s) => Space.manhattan(s.pos, mid) <= demoteRadius()))
        {
            return;
        }
        var __d8 = state.grid;
        var width = __d8.width;
        var height = __d8.height;
        var to = h.heading ?? new Point { x = Js.I((width - 1) - mid.x), y = Js.I((height - 1) - mid.y) };
        foreach (var z in Js.Iter(zs))
        {
            Zombies.removeZombie(state, z);
        }
        Js.NN(state.hordes).splice(Js.NN(state.hordes).indexOf(h), 1);
        state.herds.push(new() { id = $"h{Js.Str(state.nextId++)}", x = Js.I(mid.x), y = Js.I(mid.y), to = new Point { x = Js.I(JsMath.max(0, JsMath.min(width - 1, to.x))), y = Js.I(JsMath.max(0, JsMath.min(height - 1, to.y))) }, size = zs.length, nextStepTick = state.tick, heard = false });
    }

    /// <summary>A noise (Noise.cs): a roaming horde mostly in earshot turns to it; if only part is, that part splits off.</summary>
    public static void hordesHear(SimState state, Point p, double r, Func<Point, Point> toward)
    {
        foreach (var h in Js.Iter((hordesOf(state)).ToList()))
        {
            if (h.mode != "roam")
            {
                continue;
            }
            var zs = membersOf(state, h);
            var heard = zs.filter((z) => Space.manhattan(z.pos, p) <= r);
            if (heard.length == 0)
            {
                continue;
            }
            var mid = middle(heard);
            var heading = toward(mid);
            if (((double)(heard.length) * (2)) > zs.length)
            {
                h.heading = heading;
                h.goals = [Js.Clone(heading)];
                refresh(state, h);
                continue;
            }
            foreach (var z in Js.Iter(heard))
            {
                z.hordeId = null;
            }
            if (!Js.T(formHorde(state, heard, heading, false)))
            {
                foreach (var z in Js.Iter(heard))
                {
                    z.heading = Js.CopyAs<Point>(heading);
                }
            }
        }
    }
}
