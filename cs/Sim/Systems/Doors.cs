// Ported from src/sim/doors.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Doors held open by whoever is passing through.
//
// Colonists (and raiders) open a closed door as they step into the doorway, and it swings shut
// behind them once nobody is in it or stepping into it: grid.passing lists the doors open only
// for someone passing. A door you opened yourself (toggleDoor) isn't in it, so it stays open.
// While it's held open, a zombie can slip through.
// This doesn't bump walkabilityVersion: survivors path through doors either way, and zombies
// check each next tile as they go (zombies.ts), so nothing cached needs throwing away.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Doors
{

    /// <summary>Someone is stepping into this tile: if it's a closed door, open it until they're through. Given where they're coming from, an edge door between the two opens the same way.</summary>
    public static void passThrough(SimState state, int x, int y, bool hasKey, Point? from = default)
    {
        if (Js.T(from))
        {
            var e = Grids.edgeBetween(state.grid, from.x, from.y, x, y);
            if ((((e >= 0) && (Js.Get(state.grid.edges, e) == Grids.EDGE_DOOR)) && !Grids.edgeIsOpen(state.grid, e)) && (hasKey || !state.grid.edgeLocked.includes(e)))
            {
                state.grid.edgeOpen.push(e);
                state.grid.edgePassing.push(e);
            }
        }
        if ((Grids.getTile(state.grid, x, y) != "door") || Grids.isOpen(state.grid, x, y))
        {
            return;
        }
        if (Grids.isLocked(state.grid, x, y) && !hasKey)
        {
            return;
        }
        var i = Grids.tileIndex(state.grid, x, y);
        state.grid.open.push(i);
        state.grid.passing.push(i);
    }
    public static void passThrough(SimState state, double x, double y, bool hasKey, Point? from = default) => passThrough(state, Js.I(x), Js.I(y), hasKey, from);

    /// <summary>Doors held open for someone passing close once the doorway is clear. Runs every tick.</summary>
    public static void closeBehind(SimState state)
    {
        closeEdgesBehind(state);
        var __d0 = state;
        var grid = __d0.grid;
        if (grid.passing.length == 0)
        {
            return;
        }
        var occupied = new JsSet<double>();
        /*TODO return type*/object mark(double x, double y)
        {
            return occupied.add(((double)(y) * (grid.width)) + x);
        }
        foreach (var s in Js.Iter(state.survivors))
        {
            mark(s.pos.x, s.pos.y);
            if (Js.T(s.move))
            {
                mark(s.move.to.x, s.move.to.y);
            }
        }
        foreach (var z in Js.Iter(state.zombies))
        {
            mark(z.pos.x, z.pos.y);
            if (Js.T(Js.Get(z.path, 0)))
            {
                mark(Js.Get(z.path, 0).x, Js.Get(z.path, 0).y);
            }
        }
        foreach (var r in Js.Iter(state.residents))
        {
            mark(r.pos.x, r.pos.y);
        }
        foreach (var i in Js.Iter((grid.passing).ToList()))
        {
            if (occupied.has(i))
            {
                continue;
            }
            grid.passing.splice(grid.passing.indexOf(i), 1);
            var at = grid.open.indexOf(i);
            if (at >= 0)
            {
                grid.open.splice(at, 1);
            }
        }
    }

    /// <summary>Edge doors held open for someone passing shut once nobody is crossing: nobody mid-step across it (a colonist's step, a zombie's next tile) and nobody's body touching the doorway.</summary>
    public static void closeEdgesBehind(SimState state)
    {
        var __d1 = state;
        var grid = __d1.grid;
        if (grid.edgePassing.length == 0)
        {
            return;
        }
        var crossing = new JsSet<double>();
        void across(Point a, Point? b)
        {
            if (!Js.T(b))
            {
                return;
            }
            var e = Grids.edgeBetween(grid, a.x, a.y, b.x, b.y);
            if (e >= 0)
            {
                crossing.add(e);
            }
        }
        foreach (var s in Js.Iter(state.survivors))
        {
            if (Js.T(s.move))
            {
                across(s.move.from, s.move.to);
            }
        }
        foreach (var z in Js.Iter(state.zombies))
        {
            across(z.pos, Js.Get(z.path, 0));
        }
        var bodies = Js.ListOf<IBody>(state.survivors, state.zombies, state.residents).map((b) => b.at);
        foreach (var e in Js.Iter((grid.edgePassing).ToList()))
        {
            if (crossing.has(e))
            {
                continue;
            }
            var tile = JsMath.floor(((double)(e) / (2)));
            var x = ((double)((tile % grid.width)) * (Motion.SUB));
            var y = ((double)(JsMath.floor(((double)(tile) / (grid.width)))) * (Motion.SUB));
            var __c2 = (e % 2) == 0; var x1 = __c2 ? (x + Motion.SUB) : x; var y1 = __c2 ? y : (y + Motion.SUB);
            var r = Motion.BODY_RADIUS + ((double)(Motion.SUB) / (8));
            var inDoorway = bodies.some((c) =>
        {
            var px = JsMath.max(x, JsMath.min(x1, c.x));
            var py = JsMath.max(y, JsMath.min(y1, c.y));
            return ((JsMath.pow((c.x - px), 2)) + (JsMath.pow((c.y - py), 2))) < ((double)(r) * (r));
        });
            if (Js.T(inDoorway))
            {
                continue;
            }
            grid.edgePassing.splice(grid.edgePassing.indexOf(e), 1);
            var at = grid.edgeOpen.indexOf(e);
            if (at >= 0)
            {
                grid.edgeOpen.splice(at, 1);
            }
        }
    }

    /// <summary>Opens or shuts the edge door between two tiles side by side. Returns what happened, or why not (null: no door there).</summary>
    public static string? toggleEdgeDoor(SimState state, Point a, Point b)
    {
        var __d3 = state;
        var grid = __d3.grid;
        var e = Grids.edgeBetween(grid, a.x, a.y, b.x, b.y);
        if ((e < 0) || (Js.Get(grid.edges, e) != Grids.EDGE_DOOR))
        {
            return null;
        }
        var passing = grid.edgePassing.indexOf(e);
        if (passing >= 0)
        {
            grid.edgePassing.splice(passing, 1);
            return "opened the door";
        }
        var at = grid.edgeOpen.indexOf(e);
        if (at >= 0)
        {
            grid.edgeOpen.splice(at, 1);
            return "closed the door";
        }
        grid.edgeOpen.push(e);
        return "opened the door";
    }
}
