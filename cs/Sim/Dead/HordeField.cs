// The flow field, part of Hordes (Hordes.cs): what it costs a horde member to enter a tile or cross an edge (a step,
// or battering through: enterCost, edgeCost, stepCost), the field itself (flowField: from every tile in the horde's
// box, the cost to reach a goal, worked out backwards from the goals; cached by the horde, never saved), and a
// member's next step on it (hordeStep).
#nullable disable warnings
using System.Runtime.CompilerServices;

namespace LoonieRoo.Sim;

public static partial class Hordes
{
    /// <summary>Each horde's flow field as last made (flowField): a derived cache, never saved, held weakly by the
    /// horde, so a horde that has disbanded or merged takes its field with it.</summary>
    static readonly ConditionalWeakTable<Horde, Field> fields = new();

    public const int UNREACHED = 0x7fffffff;

    /// <summary>Entering tile b: a step, plus battering it down if it's a wall, door or barricade (Infinity: never). `asShut`: a door counts as shut whether it is or not (the field's view: doors swing open and shut all the time, and the field shouldn't be worked out again for each).</summary>
    public static double enterCost(SimState state, int bx, int by, bool asShut = false)
    {
        var __d1 = state;
        var grid = __d1.grid;
        var tile = Grids.getTile(grid, bx, by);
        if ((tile == null) || (tile == "water"))
        {
            return double.PositiveInfinity;
        }
        if (Pathfinding.isPassable(grid, bx, by, "zombie") && !(Js.T(asShut) && (tile == "door")))
        {
            return STEP;
        }
        if (Crossings.standsOn(grid, bx, by, "zombie"))
        {
            return ((double)(STEP) * (Crossings.CAR_COST)); // a parked car: climbed over (Crossings.cs)
        }
        if (!Breach.isStructure(tile))
        {
            return double.PositiveInfinity;
        }
        var i = ((double)(by) * (grid.width)) + bx;
        return STEP + JsMath.ceil(((double)(((double)(((double)(Js.Get(Breach.STRUCTURE_HEALTH, tile)) * ((grid.reinforced.includes(i) ? Defences.REINFORCE_FACTOR : 1)))) * (STEP))) / (batterRate())));
    }
    public static double enterCost(SimState state, double bx, double by, bool asShut = false) => enterCost(state, Js.I(bx), Js.I(by), asShut);

    /// <summary>Crossing the border from a to b: battering it down if it's in the way, else nothing (`asShut`: an edge door counts as shut).</summary>
    public static double edgeCost(SimState state, int ax, int ay, int bx, int by, bool asShut = false)
    {
        var __d2 = state;
        var grid = __d2.grid;
        var e = Grids.edgeBetween(grid, ax, ay, bx, by);
        if ((e < 0) || (Grids.edgeKind(grid, e) == 0))
        {
            return 0;
        }
        if (!(Js.T(asShut) && (Grids.edgeKind(grid, e) == Grids.EDGE_DOOR)) && Pathfinding.crossable(grid, ax, ay, bx, by, "zombie"))
        {
            return 0;
        }
        return JsMath.ceil(((double)(((double)(JsMath.max(1, Breach.edgeHealth(grid, e))) * (STEP))) / (batterRate())));
    }
    public static double edgeCost(SimState state, double ax, double ay, double bx, double by, bool asShut = false) => edgeCost(state, Js.I(ax), Js.I(ay), Js.I(bx), Js.I(by), asShut);

    /// <summary>What it costs a zombie to go from tile a into its neighbour b (Infinity: it can't, ever).</summary>
    public static double stepCost(SimState state, int ax, int ay, int bx, int by)
    {
        var enter = enterCost(state, bx, by);
        return (enter == double.PositiveInfinity) ? double.PositiveInfinity : (enter + edgeCost(state, ax, ay, bx, by));
    }
    public static double stepCost(SimState state, double ax, double ay, double bx, double by) => stepCost(state, Js.I(ax), Js.I(ay), Js.I(bx), Js.I(by));

    /// <summary>The horde's field: cost to a goal from each tile in its box, from what's stored on it and the walls now, every door taken as shut. (A member's own next step sees doors as they are: hordeStep.)</summary>
    public static Field flowField(SimState state, Horde h)
    {
        var b = h.box;
        var key = $"{Js.Str(h.goals.map((g) => $"{Js.Str(g.x)},{Js.Str(g.y)}").join(";"))}|{Js.Str(b.x0)},{Js.Str(b.y0)},{Js.Str(b.x1)},{Js.Str(b.y1)}|{Js.Str(state.walkabilityVersion)}";
        if (fields.TryGetValue(h, out var hit) && (hit.key == key))
        {
            return hit;
        }
        var __d3 = h.box;
        var x0 = __d3.x0;
        var y0 = __d3.y0;
        var x1 = __d3.x1;
        var y1 = __d3.y1;
        var w = (x1 - x0) + 1;
        var ht = (y1 - y0) + 1;
        var dist = new Int32Array(((double)(w) * (ht))).fill(UNREACHED);
        var hc = new Float64Array(1024);
        var hi = new Int32Array(1024);
        double size = 0;
        bool less(double a, double b)
        {
            return (Js.NN(Js.Get(hc, a)) < Js.NN(Js.Get(hc, b))) || ((Js.Get(hc, a) == Js.Get(hc, b)) && (Js.NN(Js.Get(hi, a)) < Js.NN(Js.Get(hi, b))));
        }
        void swap(double a, double b)
        {
            var c = Js.NN(Js.Get(hc, a));
            hc[Js.Idx(a)] = Js.NN(Js.Get(hc, b));
            hc[Js.Idx(b)] = c;
            var i = Js.NN(Js.Get(hi, a));
            hi[Js.Idx(a)] = Js.NN(Js.Get(hi, b));
            hi[Js.Idx(b)] = i;
        }
        void push(double c, double i)
        {
            if (size == hc.length)
            {
                var nc = new Float64Array(((double)(size) * (2)));
                nc.set(hc);
                hc = nc;
                var ni = new Int32Array(((double)(size) * (2)));
                ni.set(hi);
                hi = ni;
            }
            hc[Js.Idx(size)] = c;
            hi[Js.Idx(size)] = i;
            var k = size++;
            while (k > 0)
            {
                var p = Js.Shr(k - 1, 1);
                if (!less(k, p))
                {
                    break;
                }
                swap(k, p);
                k = p;
            }
        }
        List<double> pop()
        {
            List<double> top = [Js.NN(Js.Get(hc, 0)), Js.NN(Js.Get(hi, 0))];
            size--;
            if (size > 0)
            {
                hc[Js.Idx(0)] = Js.NN(Js.Get(hc, size));
                hi[Js.Idx(0)] = Js.NN(Js.Get(hi, size));
                double k = 0;
                for (; ; )
                {
                    var l = ((double)(2) * (k)) + 1;
                    var r = l + 1;
                    double m = k;
                    if ((l < size) && less(l, m))
                    {
                        m = l;
                    }
                    if ((r < size) && less(r, m))
                    {
                        m = r;
                    }
                    if (m == k)
                    {
                        break;
                    }
                    swap(k, m);
                    k = m;
                }
            }
            return top;
        }
        bool inBox(double x, double y)
        {
            return (((x >= x0) && (x <= x1)) && (y >= y0)) && (y <= y1);
        }
        var outside = h.goals.filter((g) => !inBox(g.x, g.y));
        foreach (var g in Js.Iter(h.goals))
        {
            if (inBox(g.x, g.y))
            {
                push(0, ((double)((g.y - y0)) * (w)) + (g.x - x0));
            }
        }
        if (outside.length > 0)
        {
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    if ((((x != x0) && (x != x1)) && (y != y0)) && (y != y1))
                    {
                        continue;
                    }
                    if ((Grids.getTile(state.grid, x, y) == "water") || !Grids.inBounds(state.grid, x, y))
                    {
                        continue;
                    }
                    var best = double.PositiveInfinity;
                    foreach (var g in Js.Iter(outside))
                    {
                        best = JsMath.min(best, Space.manhattan(g, new Point { x = Js.I(x), y = Js.I(y) }));
                    }
                    push(((double)(best) * (STEP)), ((double)((y - y0)) * (w)) + (x - x0));
                }
            }
        }
        var enter = new Float64Array(((double)(w) * (ht))).fill(-1);
        double stepIn(double ax, double ay, double bx, double by, double bi)
        {
            if (Js.Get(enter, bi) == -1)
            {
                enter[Js.Idx(bi)] = enterCost(state, bx, by, true);
            }
            var @base = Js.NN(Js.Get(enter, bi));
            if (@base == double.PositiveInfinity)
            {
                return double.PositiveInfinity;
            }
            return @base + edgeCost(state, ax, ay, bx, by, true);
        }
        while (size > 0)
        {
            var (c, i) = pop();
            if (c >= Js.NN(Js.Get(dist, i)))
            {
                continue;
            }
            dist[Js.Idx(i)] = c;
            var bx = (i % w) + x0;
            var by = JsMath.floor(((double)(i) / (w))) + y0;
            foreach (var d in Js.Iter(Pathfinding.DIRS))
            {
                var ax = bx + d.x;
                var ay = by + d.y;
                if (!inBox(ax, ay))
                {
                    continue;
                }
                var j = ((double)((ay - y0)) * (w)) + (ax - x0);
                if (Js.Get(dist, j) != UNREACHED)
                {
                    continue;
                }
                var step = stepIn(ax, ay, bx, by, i);
                if (step == double.PositiveInfinity)
                {
                    continue;
                }
                push(c + step, j);
            }
        }
        Field field = new Field { key = key, dist = dist, box = h.box };
        fields.AddOrUpdate(h, field);
        return field;
    }

    /// <summary>A horde member's next tile: the neighbour it costs least to go on from (ties in N, E, S, W order), or null.</summary>
    public static Point? hordeStep(SimState state, Zombie z, Horde h)
    {
        return hordeStepFrom(state, z.pos, h);
    }

    /// <summary>The same from any tile: where a member standing there would go next (Crossings.cs follows the field over a parked car).</summary>
    public static Point? hordeStepFrom(SimState state, Point here, Horde h)
    {
        var f = flowField(state, h);
        var __d4 = f.box;
        var x0 = __d4.x0;
        var y0 = __d4.y0;
        var x1 = __d4.x1;
        var y1 = __d4.y1;
        var w = (x1 - x0) + 1;
        if ((((here.x < x0) || (here.x > x1)) || (here.y < y0)) || (here.y > y1))
        {
            return null;
        }
        if (Js.Get(f.dist, ((double)((here.y - y0)) * (w)) + (here.x - x0)) == 0)
        {
            return null;
        }
        Point? best = null;
        var bestCost = double.PositiveInfinity;
        foreach (var d in Js.Iter(Pathfinding.DIRS))
        {
            var nx = here.x + d.x;
            var ny = here.y + d.y;
            if ((((nx < x0) || (nx > x1)) || (ny < y0)) || (ny > y1))
            {
                continue;
            }
            var rest = Js.NN(Js.Get(f.dist, ((double)((ny - y0)) * (w)) + (nx - x0)));
            if (rest == UNREACHED)
            {
                continue;
            }
            var c = stepCost(state, here.x, here.y, nx, ny) + rest;
            if (c < bestCost)
            {
                bestCost = c;
                best = new Point { x = Js.I(nx), y = Js.I(ny) };
            }
        }
        return best;
    }
}
