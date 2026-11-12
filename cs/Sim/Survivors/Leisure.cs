// Ported from src/sim/leisure.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Leisure and breakdowns: what mood does.
//
// Leisure: a survivor with nothing doable to do, in a mood below RELAX_BELOW
// and not already relaxed today, walks to a free spot in a complete rec room
// and unwinds for RELAX_TICKS ("relaxed", a good thought). A job turning up
// ends it: they go and take it. Someone grieving a death goes to a graveyard
// instead, if there is one, and pays their respects (a bigger lift, once).
//
// Breakdowns: a miserable survivor (see Mood.cs) may break down. Rolled
// every BREAKDOWN_CHECK_EVERY ticks (sim RNG). For BREAKDOWN_TICKS they drop
// their work and won't take any; needs still run. Afterwards they've
// "vented", a good thought, so it doesn't happen again straight away.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Leisure
{

    /// <summary>Idle survivors below this mood go to the rec room.</summary>
    public const int RELAX_BELOW = 70;

    public const int RELAX_TICKS = 200;

    public const int BREAKDOWN_CHECK_EVERY = 200;

    /// <summary>Chance per check that a miserable survivor breaks down.</summary>
    public const double BREAKDOWN_CHANCE = 0.25;

    public const int BREAKDOWN_TICKS = 600;

    /// <summary>Called when an idle survivor found no job. Returns true if they set off to relax.</summary>
    public static bool startLeisure(SimState state, Survivor s)
    {
        if (Js.T((Js.T(Js.T(s.leisure) || Js.T(s.breakdown))) || (Mood.mood(state, s) >= RELAX_BELOW)))
        {
            return false;
        }
        bool has(string kind)
        {
            return s.thoughts.some((t) => t.kind == kind);
        }
        var grieving = (has("lostFriend") || has("sawDeath")) && !has("paidRespects");
        string? at = (Js.T(grieving) && (Rooms.completeRooms(state, "graveyard").size > 0)) ? "graveyard" : (has("relaxed") ? null : "recroom");
        if (!Js.T(at))
        {
            return false;
        }
        var spot = pickSpot(state, s, at);
        if (!Js.T(spot))
        {
            return false;
        }
        s.leisure = new SurvivorLeisure { x = Js.I(spot.x), y = Js.I(spot.y), progress = 0, at = at };
        s.note = (at == "graveyard") ? $"going to the graveyard at ({Js.Str(spot.x)}, {Js.Str(spot.y)})" : $"going to relax at ({Js.Str(spot.x)}, {Js.Str(spot.y)})";
        return true;
    }

    /// <summary>Runs when standing still, after sleep, before work. Returns true if leisure handled this tick.</summary>
    public static bool updateLeisure(SimState state, Survivor s)
    {
        if (!Js.T(s.leisure))
        {
            return false;
        }
        if (state.tick >= s.nextSeekTick)
        {
            s.nextSeekTick = state.tick + Jobs.IDLE_RETRY_TICKS;
            var was = s.leisure;
            s.leisure = null;
            if (JobChoice.seekJob(state, s))
            {
                return true;
            }
            s.leisure = was;
        }
        Point spot = new Point { x = Js.I(s.leisure.x), y = Js.I(s.leisure.y) };
        if (!spotFree(state, s, spot, s.leisure.at))
        {
            s.leisure = null;
            s.note = "rec room spot gone";
            return false;
        }
        if (!Space.samePoint(s.pos, spot))
        {
            if (!Survivors.walkTo(state, s, spot))
            {
                s.leisure = null;
                s.note = "can't reach the rec room";
                return false;
            }
            return true;
        }
        s.leisure.progress += 1;
        var mourning = s.leisure.at == "graveyard";
        if (s.leisure.progress == 1)
        {
            s.note = mourning ? $"paying respects at ({Js.Str(spot.x)}, {Js.Str(spot.y)})" : $"relaxing at ({Js.Str(spot.x)}, {Js.Str(spot.y)})";
        }
        if (s.leisure.progress < Time.scaled(RELAX_TICKS))
        {
            return true;
        }
        s.leisure = null;
        Mood.addThought(state, s, mourning ? "paidRespects" : "relaxed");
        s.note = mourning ? "paid their respects" : "relaxed in the rec room";
        return true;
    }

    /// <summary>A free floor tile in a complete rec room (or graveyard): walkable, no furniture, nobody else there.</summary>
    public static bool spotFree(SimState state, Survivor s, Point p, string at)
    {
        var rooms = Rooms.completeRooms(state, at);
        return ((rooms.has(Rooms.labelAt(state, p.x, p.y)) && Grids.isWalkable(state.grid, p.x, p.y)) && !Js.T(FurnitureMod.furnitureAt(state, p.x, p.y))) && !state.survivors.some((o) => ((Js.T((o != s) && Js.T(o.leisure))) && (o.leisure.x == p.x)) && (o.leisure.y == p.y));
    }

    public static Point? pickSpot(SimState state, Survivor s, string at)
    {
        var rooms = Rooms.completeRooms(state, at);
        if (rooms.size == 0)
        {
            return null;
        }
        var __d0 = Rooms.getRoomMap(state);
        var all = __d0.rooms;
        var __d1 = state.grid;
        var width = __d1.width;
        List<Point> spots = [];
        foreach (var index in Js.Iter(Js.ListOf(rooms).sort((a, b) => a - b)))
        {
            foreach (var i in Js.Iter(Js.NN(Js.Get(all, index)).tiles))
            {
                Point p = new Point { x = Js.I(i % width), y = Js.I(JsMath.floor(((double)(i) / (width)))) };
                if (spotFree(state, s, p, at))
                {
                    spots.push(p);
                }
            }
        }
        spots.sort((a, b) => Space.manhattan(s.pos, a) - Space.manhattan(s.pos, b));
        return spots.find((p) => Pathfinding.canReach(state.grid, state.walkabilityVersion, s.pos, [p])) ?? null;
    }

    /// <summary>Every tick, for every survivor: maybe start a breakdown, or end one.</summary>
    public static void updateBreakdown(SimState state, Survivor s)
    {
        if (Js.T(s.breakdown))
        {
            if (state.tick < s.breakdown.until)
            {
                return;
            }
            s.breakdown = null;
            Mood.addThought(state, s, "vented");
            s.note = "pulled themselves together";
            return;
        }
        if (Js.T((((!Time.slowEvery(state.tick, BREAKDOWN_CHECK_EVERY)) || !Mood.isMiserable(state, s)) || Resolve.isSteeled(s)) || Js.T(s.sleep)))
        {
            return;
        }
        if (Rng.random(state) >= BREAKDOWN_CHANCE)
        {
            return;
        }
        if (Js.T(s.jobId))
        {
            Jobs.releaseSurvivor(state, s, "having a breakdown");
        }
        s.leisure = null;
        Books.stopReading(state, s, "having a breakdown");
        s.breakdown = new SurvivorBreakdown { until = state.tick + Time.scaled(BREAKDOWN_TICKS) };
        s.note = "having a breakdown";
        Events.logEvent(state, $"{Js.Str(s.name)} is having a breakdown");
        Relationships.lashOut(state, s);
    }
}
