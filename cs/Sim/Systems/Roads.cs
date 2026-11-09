// Ported from src/sim/roads.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Country roads: routed between towns across the land (worldplan.ts), once per world.
//
//   network  towns (and the start) joined by a minimum spanning tree, plus a few extra links
//            between near neighbours, and some roads out to the map's edge (the world goes on).
//   routing  A* over a coarse grid (ROAD_CELL tiles a cell, eight ways): open land is cheap, woods
//            dearer, water dear (a bridge: only where it must), the sea impassable, other towns
//            avoided, and roads already laid cheap, so later roads join earlier ones.
//   drawing  the cell path is smoothed (corner cutting) into a curve, drawn two to three tiles wide.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Roads
{

    public const int ROAD_CELL = 4;

    /// <summary>The most heap pops a road's search may take, per cell of the coarse grid.</summary>
    public const int EXPANSIONS_PER_CELL = 8;

    /// <summary>Which nodes to join: a minimum spanning tree over the nodes, plus a few short extra links.</summary>
    public static List<List<double>> network(double seed, List<Point> nodes)
    {
        List<List<double>> edges = [];
        if (nodes.length < 2)
        {
            return edges;
        }
        double d(double a, double b)
        {
            return JsMath.sqrt((JsMath.pow((Js.NN(Js.Get(nodes, a)).x - Js.NN(Js.Get(nodes, b)).x), 2)) + (JsMath.pow((Js.NN(Js.Get(nodes, a)).y - Js.NN(Js.Get(nodes, b)).y), 2)));
        }
        List<double> inTree = [0];
        var rest = nodes.map((_, i) => i).slice(1);
        while (rest.length > 0)
        {
            List<double>? best = null;
            foreach (var a in Js.Iter(inTree))
            {
                foreach (var b in Js.Iter(rest))
                {
                    if (!Js.T(best) || (d(a, b) < Js.Get(best, 2)))
                    {
                        best = [a, b, d(a, b)];
                    }
                }
            }
            edges.push([Js.Get(Js.NN(best), 0), Js.Get(Js.NN(best), 1)]);
            inTree.push(Js.Get(Js.NN(best), 1));
            rest.splice(rest.indexOf(Js.Get(Js.NN(best), 1)), 1);
        }
        nodes.forEach((_, a) =>
        {
            var linked = edges.filter((__p0) =>
        {
            var (p, q) = __p0;
            return (p == a) || (q == a);
        }).map((__p1) =>
        {
            var (p, q) = __p1;
            return (p == a) ? q : p;
        });
            var longest = JsMath.max(linked.map((b) => d(a, b)));
            var __nears = nodes.map((__1, b) => b).filter((b) => (b != a) && !linked.includes(b)).sort((p, q) => d(a, p) - d(a, q));
            if (__nears.length == 0) return;
            var near = __nears[0];
            if ((((near != null) && (d(a, near) < ((double)(longest) * (1.4)))) && (NatureMod.roll(seed, a, near, 0x7300) < 0.5)) && !edges.some((__p2) =>
        {
            var (p, q) = __p2;
            return (p == near) && (q == a);
        }))
            {
                edges.push([a, near]);
            }
        });
        return edges;
    }

    /// <summary>Routes a road from `from` to `to`; returns its smoothed centre line in tiles, or null if there's no way.</summary>
    public static List<PointD>? route(Nature n, Float32Array cost, Point from, Point to)
    {
        var N = JsMath.ceil(((double)(n.size) / (ROAD_CELL)));
        double cell(Point p)
        {
            return ((double)(JsMath.min(N - 1, JsMath.max(0, JsMath.floor(((double)(p.y) / (ROAD_CELL)))))) * (N)) + JsMath.min(N - 1, JsMath.max(0, JsMath.floor(((double)(p.x) / (ROAD_CELL)))));
        }
        var start = cell(from);
        var goal = cell(to);
        // g in doubles (2026-11-09; schema 73). It was a Float32Array, as the TypeScript had it, compared in doubles: a g
        // that rounded up when stored stayed "improvable" by the same path, so cells re-pushed each other without end
        // (one road in a 2048 world: 181 million expansions, 316 s). See docs/worldgen-speed.md.
        var g = new Float64Array(((double)(N) * (N))).fill(double.PositiveInfinity);
        var came = new Int32Array(((double)(N) * (N))).fill(-1);
        var gx = goal % N;
        var gy = JsMath.floor(((double)(goal) / (N)));
        List<List<double>> heap = [];
        void push(double f, double c)
        {
            heap.push([f, c]);
            var i = heap.length - 1;
            while (i > 0)
            {
                var p = Js.Shr(i - 1, 1);
                if (Js.Get(Js.NN(Js.Get(heap, p)), 0) <= Js.Get(Js.NN(Js.Get(heap, i)), 0))
                {
                    break;
                }
                (heap[Js.Idx(p)], heap[Js.Idx(i)]) = (Js.NN(Js.Get(heap, i)), Js.NN(Js.Get(heap, p)));
                i = p;
            }
        }
        List<double> pop()
        {
            var top = Js.NN(Js.Get(heap, 0));
            var last = Js.NN(heap.pop());
            if (heap.length > 0)
            {
                heap[Js.Idx(0)] = last;
                double i = 0;
                for (; ; )
                {
                    var l = ((double)(2) * (i)) + 1;
                    var r = l + 1;
                    double m = i;
                    if ((l < heap.length) && (Js.Get(Js.NN(Js.Get(heap, l)), 0) < Js.Get(Js.NN(Js.Get(heap, m)), 0)))
                    {
                        m = l;
                    }
                    if ((r < heap.length) && (Js.Get(Js.NN(Js.Get(heap, r)), 0) < Js.Get(Js.NN(Js.Get(heap, m)), 0)))
                    {
                        m = r;
                    }
                    if (m == i)
                    {
                        break;
                    }
                    (heap[Js.Idx(m)], heap[Js.Idx(i)]) = (Js.NN(Js.Get(heap, i)), Js.NN(Js.Get(heap, m)));
                    i = m;
                }
            }
            return top;
        }
        g[Js.Idx(start)] = 0;
        push(0, start);
        var MIN = 0.35;
        // A backstop: no road expands more than EXPANSIONS_PER_CELL times the grid (a sound search needs about one).
        double budget = ((double)(N) * (N)) * EXPANSIONS_PER_CELL;
        while (heap.length > 0)
        {
            if (--budget < 0)
            {
                return null;
            }
            var (f, c) = pop();
            if (c == goal)
            {
                break;
            }
            var cx = c % N;
            var cy = JsMath.floor(((double)(c) / (N)));
            if (f > ((Js.NN(Js.Get(g, c)) + ((double)(MIN) * (JsMath.sqrt((JsMath.pow((gx - cx), 2)) + (JsMath.pow((gy - cy), 2)))))) + 1e-6))
            {
                continue;
            }
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0) && (dy == 0))
                    {
                        continue;
                    }
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if ((((nx < 0) || (ny < 0)) || (nx >= N)) || (ny >= N))
                    {
                        continue;
                    }
                    var nc = ((double)(ny) * (N)) + nx;
                    var step = ((double)(((double)((Js.NN(Js.Get(cost, c)) + Js.NN(Js.Get(cost, nc)))) / (2))) * ((((dx != 0) && (dy != 0)) ? JsMath.SQRT2 : 1)));
                    if (!JsNumber.isFinite(step))
                    {
                        continue;
                    }
                    var ng = Js.NN(Js.Get(g, c)) + step;
                    if (ng < Js.NN(Js.Get(g, nc)))
                    {
                        g[Js.Idx(nc)] = ng;
                        came[Js.Idx(nc)] = c;
                        push(ng + ((double)(MIN) * (JsMath.sqrt((JsMath.pow((gx - nx), 2)) + (JsMath.pow((gy - ny), 2))))), nc);
                    }
                }
            }
        }
        if (!JsNumber.isFinite(Js.NN(Js.Get(g, goal))))
        {
            return null;
        }
        List<Point> cells = [];
        for (var c = goal; c != -1; c = Js.NN(Js.Get(came, c)))
        {
            cells.push(new Point { x = Js.I(((double)((c % N)) * (ROAD_CELL)) + ((double)(ROAD_CELL) / (2))), y = Js.I(((double)(JsMath.floor(((double)(c) / (N)))) * (ROAD_CELL)) + ((double)(ROAD_CELL) / (2))) });
        }
        cells.reverse();
        List<PointD> line = [from, .. cells.slice(1, -1), to];
        for (var pass = 0; pass < 2; pass++)
        {
            List<PointD> next = [Js.NN(Js.Get(line, 0))];
            for (var i = 0; i < (line.length - 1); i++)
            {
                var a = Js.NN(Js.Get(line, i));
                var b = Js.NN(Js.Get(line, i + 1));
                next.pushAll([new PointD { x = (a.x * 0.75) + (b.x * 0.25), y = (a.y * 0.75) + (b.y * 0.25) }, new PointD { x = (a.x * 0.25) + (b.x * 0.75), y = (a.y * 0.25) + (b.y * 0.75) }]);
            }
            next.push(Js.NN(line.at(-1)));
            line = next;
        }
        return line;
    }

    /// <summary>The cost of crossing each coarse cell: land 1, woods 3, water 14 (a bridge), sea impassable.</summary>
    public static Float32Array costGrid(Nature n, List<Town> towns)
    {
        var N = JsMath.ceil(((double)(n.size) / (ROAD_CELL)));
        var c = new Float32Array(((double)(N) * (N)));
        for (var j = 0; j < N; j++)
        {
            for (var i = 0; i < N; i++)
            {
                var x = ((double)(i) * (ROAD_CELL)) + ((double)(ROAD_CELL) / (2));
                var y = ((double)(j) * (ROAD_CELL)) + ((double)(ROAD_CELL) / (2));
                var v = NatureMod.isSea(n, x, y) ? double.PositiveInfinity : (NatureMod.isWater(n, x, y) ? 14 : (NatureMod.isForest(n, x, y) ? 3 : 1));
                if (towns.some((t) => JsMath.sqrt((JsMath.pow((x - t.x), 2)) + (JsMath.pow((y - t.y), 2))) < ((double)(t.radius) * (0.9))))
                {
                    v += 4;
                }
                c[Js.Idx(((double)(j) * (N)) + i)] = v;
            }
        }
        return c;
    }

    /// <summary>Marks a laid road's cells cheap, so later roads share it.</summary>
    public static void reuse(Float32Array cost, double size, List<PointD> line)
    {
        var N = JsMath.ceil(((double)(size) / (ROAD_CELL)));
        foreach (var p in Js.Iter(line))
        {
            var i = ((double)(JsMath.min(N - 1, JsMath.max(0, JsMath.floor(((double)(p.y) / (ROAD_CELL)))))) * (N)) + JsMath.min(N - 1, JsMath.max(0, JsMath.floor(((double)(p.x) / (ROAD_CELL)))));
            if (JsNumber.isFinite(Js.NN(Js.Get(cost, i))))
            {
                cost[Js.Idx(i)] = 0.35;
            }
        }
    }

    /// <summary>Draws a road's centre line into the mask, about two and a half tiles wide.</summary>
    public static void drawLine(Uint8Array mask, double size, List<PointD> line, double value)
    {
        for (var i = 0; i < (line.length - 1); i++)
        {
            var a = Js.NN(Js.Get(line, i));
            var b = Js.NN(Js.Get(line, i + 1));
            var steps = JsMath.max(1, JsMath.ceil(((double)(JsMath.sqrt((JsMath.pow((b.x - a.x), 2)) + (JsMath.pow((b.y - a.y), 2)))) / (0.4))));
            for (var s = 0; s <= steps; s++)
            {
                var px = a.x + ((double)(((double)((b.x - a.x)) * (s))) / (steps));
                var py = a.y + ((double)(((double)((b.y - a.y)) * (s))) / (steps));
                for (var y = JsMath.floor(py - 1); y <= JsMath.ceil(py + 1); y++)
                {
                    for (var x = JsMath.floor(px - 1); x <= JsMath.ceil(px + 1); x++)
                    {
                        if ((((x < 0) || (y < 0)) || (x >= size)) || (y >= size))
                        {
                            continue;
                        }
                        if ((((JsMath.pow(((x + 0.5) - px), 2)) + (JsMath.pow(((y + 0.5) - py), 2))) <= 1.1) && (Js.Get(mask, ((double)(y) * (size)) + x) == 0))
                        {
                            mask[Js.Idx(((double)(y) * (size)) + x)] = value;
                        }
                    }
                }
            }
        }
    }
}
