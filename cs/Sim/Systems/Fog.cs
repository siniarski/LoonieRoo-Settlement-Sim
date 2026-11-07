// Ported from src/sim/fog.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Fog of war and exploring.
//
// grid.explored marks tiles some survivor has seen: what is in their field of view (sight.ts:
// walls and shut doors hide what is behind them, glass doesn't, and night shortens it).
// Scenario maps start fully explored; generated worlds start dark except
// round the camp. Nothing can be designated on unexplored ground, but
// survivors may walk into it, and that is how it gets explored.
//
// Explore areas are boxes the player wants scouted. Each gets one scout job
// (jobs.ts), which keeps walking to the nearest unexplored walkable tile in
// the box until none is left; then the area is done and removed. Scouts
// head home at SCOUT_HOME_HOUR and go out again in the morning. A newly
// seen crate with loot marks its ruin as a point of interest.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Fog
{

    /// <summary>Explore boxes are capped at this many tiles a side.</summary>
    public const int MAX_EXPLORE_SIDE = 64;

    /// <summary>Scouts stop and head home at this hour (night falls at NIGHT_START), and set out again at dawn.</summary>
    public const int SCOUT_HOME_HOUR = 19;

    /// <summary>One point of interest per this many tiles: a ruin is marked once, not per crate.</summary>
    public const int POI_SPACING = 10;

    public static bool isExplored(SimState state, int x, int y)
    {
        return Grids.inBounds(state.grid, x, y) && (Js.Get(state.grid.explored, Grids.tileIndex(state.grid, x, y)) == 1);
    }
    public static bool isExplored(SimState state, double x, double y) => isExplored(state, Js.I(x), Js.I(y));

    /// <summary>Reveals a whole circle, walls or not (the camp at the start).</summary>
    public static void revealAround(SimState state, Point p, double r = Sight.SIGHT_RADIUS)
    {
        var __d0 = state;
        var grid = __d0.grid;
        for (var y = JsMath.max(0, p.y - r); y <= JsMath.min(grid.height - 1, p.y + r); y++)
        {
            for (var x = JsMath.max(0, p.x - r); x <= JsMath.min(grid.width - 1, p.x + r); x++)
            {
                var i = ((double)(y) * (grid.width)) + x;
                if ((Js.Get(grid.explored, i) == 1) || (((JsMath.pow((x - p.x), 2)) + (JsMath.pow((y - p.y), 2))) > ((double)(r) * (r))))
                {
                    continue;
                }
                grid.explored[Js.Idx(i)] = 1;
                noteFind(state, x, y);
            }
        }
    }

    /// <summary>A newly seen crate with loot marks its ruin (once per ruin).</summary>
    public static void noteFind(SimState state, int x, int y)
    {
        var f = FurnitureMod.furnitureAt(state, x, y);
        if ((f?.kind != "crate") || (f.loot == 0))
        {
            return;
        }
        if (state.pointsOfInterest.some((p) => Space.manhattan(p, new Point { x = Js.I(x), y = Js.I(y) }) <= POI_SPACING))
        {
            return;
        }
        state.pointsOfInterest.push(new() { id = $"p{Js.Str(state.nextId++)}", x = Js.I(x), y = Js.I(y), label = "ruin with crates" });
        Events.logEvent(state, $"Found a ruin with crates at ({Js.Str(x)}, {Js.Str(y)})");
    }
    public static void noteFind(SimState state, double x, double y) => noteFind(state, Js.I(x), Js.I(y));

    /// <summary>Drops markers whose crates have all been searched.</summary>
    public static void reconcilePointsOfInterest(SimState state)
    {
        if (((state.tick % 50) != 0) || (state.pointsOfInterest.length == 0))
        {
            return;
        }
        var crates = FurnitureMod.ofKind(state, "crate").filter((f) => f.loot > 0);
        state.pointsOfInterest = state.pointsOfInterest.filter((p) => crates.some((c) => Space.manhattan(c, p) <= POI_SPACING));
    }

    /// <summary>Late in the day: scouts stop exploring until morning.</summary>
    public static bool scoutingOver(int tick)
    {
        return Time.isNight(tick) || (Time.hourOf(tick) >= SCOUT_HOME_HOUR);
    }
    public static bool scoutingOver(double tick) => scoutingOver(Js.I(tick));

    /// <summary>What each survivor last looked at: skip the look when nothing it depends on has changed.</summary>
    public static readonly JsMap<SimState, JsRecord<string>> looked = new JsMap<SimState, JsRecord<string>>();

    /// <summary>Everyone sees what is in their field of view, every tick. A look is skipped when the survivor, how far they see, the walls and the open doors are all as they were last time (the same field, all of it already explored), so the outcome never depends on the cache.</summary>
    public static void updateFog(SimState state)
    {
        var __d1 = state;
        var grid = __d1.grid;
        var last = looked.get(state) ?? new();
        looked.set(state, last);
        string? doors = null;
        foreach (var s in Js.Iter(state.survivors))
        {
            Worldgen.ensureGeneratedAround(state, s.pos);
            var r = Sight.sightRadius(state, s);
            doors ??= (grid.open.join(",") + "|") + grid.edgeOpen.join(",");
            var key = $"{Js.Str(s.pos.x)},{Js.Str(s.pos.y)},{Js.Str(r)},{Js.Str(Towers.onTower(state, s))},{Js.Str(state.walkabilityVersion)},{Js.Str(doors)}";
            if (Js.Get(last, s.id) == key)
            {
                continue;
            }
            last[Js.Idx(s.id)] = key;
            foreach (var i in Js.Iter(Sight.sightOf(state, s, (j) => Js.Get(grid.explored, j) == 1)))
            {
                grid.explored[Js.Idx(i)] = 1;
                noteFind(state, i % grid.width, JsMath.floor(((double)(i) / (grid.width))));
            }
        }
    }

    public static ExploreArea? getExploreArea(SimState state, string id)
    {
        return state.exploreAreas.find((a) => a.id == id);
    }

    /// <summary>Unexplored, walkable tiles in the area.</summary>
    public static List<Point> unexploredIn(SimState state, ExploreArea a)
    {
        List<Point> @out = [];
        for (var y = a.y0; y <= a.y1; y++)
        {
            for (var x = a.x0; x <= a.x1; x++)
            {
                if (!isExplored(state, x, y) && Grids.isWalkable(state.grid, x, y))
                {
                    @out.push(new Point { x = Js.I(x), y = Js.I(y) });
                }
            }
        }
        return @out;
    }

    /// <summary>The nearest unexplored tile in the area that `from` can walk to, or null when the area is done.</summary>
    public static Point? scoutTarget(SimState state, ExploreArea a, Point from)
    {
        var targets = unexploredIn(state, a).sort((p, q) => (((Space.manhattan(from, p) - Space.manhattan(from, q)) is var __or3 && Js.T(__or3) ? __or3 : (p.y - q.y)) is var __or2 && Js.T(__or2) ? __or2 : (p.x - q.x)));
        return targets.find((p) => Pathfinding.canReach(state.grid, state.walkabilityVersion, from, [p])) ?? null;
    }

    /// <summary>Drops areas with nothing left to see.</summary>
    public static void reconcileExploreAreas(SimState state)
    {
        state.exploreAreas = state.exploreAreas.filter((a) => unexploredIn(state, a).length > 0);
    }
}
