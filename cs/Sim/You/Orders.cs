// Ported from src/sim/orders.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Group orders: what the people with you do when you aren't running a colony.
//
//   follow  keep close to whoever you're steering (control.ts), a couple of tiles behind
//   hold    stand at a spot and keep it
//   loot    search every crate in a room (or around a spot outdoors), then come back to you
//   settle  (no order) the colony sim: jobs by priority, as usual
//
// Under an order a survivor still eats, drinks, treats wounds, arms and sleeps,
// but only takes the jobs that keep people alive (guarding, firefighting, doctoring),
// and only near where they're meant to be. No reading or rec-room visits either.
// Following with nobody under control means waiting where you are. Followers hurry when
// they fall behind, and step out of the way when they're standing in front of you.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Orders
{

    /// <summary>The only job types taken under an order.</summary>
    public static readonly List<string> ORDER_JOBS = ["guard", "firefight", "doctor"];

    /// <summary>Jobs further than this (Manhattan) from where they're meant to be are left alone.</summary>
    public const int ORDER_REACH = 8;

    /// <summary>Followers start walking once they're this far behind.</summary>
    public const int FOLLOW_GAP = 3;

    /// <summary>A shouted order (follow, hold, loot, settle) reaches people this close.</summary>
    public const int SHOUT_RANGE = 12;

    /// <summary>Followers further behind than this walk a tick quicker per tile.</summary>
    public static readonly double HURRY_GAP = FOLLOW_GAP + 3;

    /// <summary>Followers keep off this many tiles straight in front of you.</summary>
    public const int FIRE_LANE = 4;

    /// <summary>Outdoors, looting takes the crates within this many tiles of the spot.</summary>
    public const int LOOT_RADIUS = 5;

    /// <summary>Where an ordered survivor is meant to be, or null with no order (or nobody to follow).</summary>
    public static Point? orderAnchor(SimState state, Survivor s)
    {
        if (!Js.T(s.order))
        {
            return null;
        }
        if (s.order.kind != "follow")
        {
            return new Point { x = Js.I(Js.NN(s.order.x)), y = Js.I(Js.NN(s.order.y)) };
        }
        var leader = ControlMod.controlled(state);
        return (Js.T(leader) && (leader.id != s.id)) ? leader.pos : null;
    }

    /// <summary>Can this survivor take a job at (x, y) under their order?</summary>
    public static bool withinOrder(SimState state, Survivor s, Point p)
    {
        if (!Js.T(s.order))
        {
            return true;
        }
        var at = orderAnchor(state, s) ?? s.pos;
        return (JsMath.abs(p.x - at.x) + JsMath.abs(p.y - at.y)) <= ORDER_REACH;
    }

    /// <summary>Runs when an ordered survivor is standing still with no need pressing: walk back into place if they've drifted. Returns true if that took the tick.</summary>
    public static bool keepOrder(SimState state, Survivor s)
    {
        if (Js.T(!Js.T(s.order) || Js.T(s.jobId)))
        {
            return false;
        }
        if (s.order.kind == "loot")
        {
            return loot(state, s, s.order);
        }
        var at = orderAnchor(state, s);
        if (!Js.T(at))
        {
            return false;
        }
        var gap = (s.order.kind == "follow") ? FOLLOW_GAP : 0;
        var far = JsMath.max(JsMath.abs(at.x - s.pos.x), JsMath.abs(at.y - s.pos.y));
        if (far <= gap)
        {
            return (s.order.kind == "follow") && stepAside(state, s);
        }
        var path = Pathfinding.findPath(state.grid, s.pos, at);
        if (!Js.T(path) || (path.length == 0))
        {
            s.note = (s.order.kind == "follow") ? "can't reach you" : "can't get back to their post";
            return false;
        }
        Survivors.walk(s, (s.order.kind == "follow") ? path.slice(0, -1) : path, state.walkabilityVersion);
        s.note = (s.order.kind == "follow") ? "following you" : "going back to their post";
        return true;
    }

    public static string describeOrder(Order? o)
    {
        return !Js.T(o) ? "settled" : ((o.kind == "follow") ? "following" : ((o.kind == "loot") ? $"looting ({Js.Str(o.x)}, {Js.Str(o.y)})" : $"holding ({Js.Str(o.x)}, {Js.Str(o.y)})"));
    }

    /// <summary>A follower falling behind hurries (stepPace in survivors.ts).</summary>
    public static bool hurries(SimState state, Survivor s)
    {
        if (s.order?.kind != "follow")
        {
            return false;
        }
        var leader = ControlMod.controlled(state);
        return ((leader != null) && (leader != s)) && (JsMath.max(JsMath.abs(leader.pos.x - s.pos.x), JsMath.abs(leader.pos.y - s.pos.y)) > HURRY_GAP);
    }

    /// <summary>Standing in your line of fire: step to a free tile beside it. Returns true if they moved.</summary>
    public static bool stepAside(SimState state, Survivor s)
    {
        var c = state.control;
        var leader = ControlMod.controlled(state);
        if (!Js.T(c) || !Js.T(leader))
        {
            return false;
        }
        bool lane(Point p)
        {
            for (var k = 1; k <= FIRE_LANE; k++)
            {
                if ((p.x == (leader.pos.x + ((double)(c.facing.x) * (k)))) && (p.y == (leader.pos.y + ((double)(c.facing.y) * (k)))))
                {
                    return true;
                }
            }
            return false;
        }
        if (!lane(s.pos))
        {
            return false;
        }
        var side = new List<Point> { new Point { x = Js.I(-c.facing.y), y = Js.I(c.facing.x) }, new Point { x = Js.I(c.facing.y), y = Js.I(-c.facing.x) } }.map((d) => new Point { x = Js.I(s.pos.x + JsMath.sign(d.x)), y = Js.I(s.pos.y + JsMath.sign(d.y)) }).find((p) => Pathfinding.isPassable(state.grid, p.x, p.y, "survivor") && !lane(p));
        if (!Js.T(side))
        {
            return false;
        }
        Survivors.walk(s, [side], state.walkabilityVersion);
        s.note = "out of your line of fire";
        return true;
    }

    /// <summary>The crates left to loot for a loot order: in the same room as the spot, or around it outdoors.</summary>
    public static List<Furniture> crates(SimState state, Point o)
    {
        var room = Rooms.roomAt(state, o.x, o.y);
        return FurnitureMod.ofKind(state, "crate").filter((f) => Js.T((f.loot > 0) && (Js.T(Js.T(room) ? (Rooms.roomAt(state, f.x, f.y)?.index == room.index) : (JsMath.max(JsMath.abs(f.x - o.x), JsMath.abs(f.y - o.y)) <= LOOT_RADIUS)))));
    }

    /// <summary>One tick of a loot order: walk to the next crate and search it; with none left, follow again.</summary>
    public static bool loot(SimState state, Survivor s, Order o)
    {
        var left = crates(state, o);
        var crate = left.find((f) => f.id == o.crateId);
        if (!Js.T(crate))
        {
            crate = Js.Get(left.sort((a, b) => (JsMath.abs(a.x - s.pos.x) + JsMath.abs(a.y - s.pos.y)) - (JsMath.abs(b.x - s.pos.x) + JsMath.abs(b.y - s.pos.y))), 0);
            o.crateId = crate?.id ?? null;
            o.progress = 0;
        }
        if (!Js.T(crate))
        {
            s.order = new Order { kind = "follow" };
            s.note = "nothing left to loot, coming back";
            return false;
        }
        if ((JsMath.abs(crate.x - s.pos.x) + JsMath.abs(crate.y - s.pos.y)) != 1)
        {
            var path = Pathfinding.findPathToAny(state.grid, s.pos, Grids.neighbours4(state.grid, crate.x, crate.y));
            if (!Js.T(path) || (path.length == 0))
            {
                s.order = new Order { kind = "follow" };
                s.note = "can't get to the crates";
                return false;
            }
            Survivors.walk(s, path, state.walkabilityVersion);
            s.note = $"going to loot ({Js.Str(crate.x)}, {Js.Str(crate.y)})";
            return true;
        }
        o.progress += 1;
        s.note = "searching";
        if (o.progress < Jobs.SCAVENGE_WORK)
        {
            return true;
        }
        o.progress = 0;
        var kind = Difficulty.spawnFind(state, Work.scavengeFind(Rng.random(state), crate.table), s.pos.x, s.pos.y).kind;
        crate.loot -= 1;
        if (crate.loot == 0)
        {
            FurnitureMod.setScavenge(state, crate, false);
        }
        s.note = $"found {Js.Str(kind)}";
        return true;
    }
}
