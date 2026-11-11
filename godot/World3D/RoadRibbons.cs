// Country roads as smooth ribbons (2026-11-12, docs/roads-off-grid.md step 1): the sim's roads are tiles, and a
// road on the slant drawn tile by tile is a staircase. The world plan already holds each country road as a line
// (WorldPlan.roads: the smoothed curve its tiles were stamped from), so first person draws the road along that
// line instead: a strip of asphalt as wide as the stamp, laid on the ground's heights, with a dashed centre line
// and solid edge lines following the curve. The tiles under it are drawn as the verge beside them (WorldMesh,
// while Ribboned), and still decide everything the sim does. Town streets (sunk between kerbs, straight) keep
// their tiles. Reads state only.
//
// The joins (2026-11-12, log 09): the roads of every planned region in reach (RoadNet: a road crossing into the
// next region is in both plans, drawn once). Where roads meet, a disc of asphalt fills the corner and no lane line
// runs through another road's asphalt. Where a road meets a town's street the strip is cut along the street's
// kerb line (an edge of the tiles: the street's are square). Over a bridge the strip goes on as a deck of planks
// at the bridge's height, with its sides down to the water; the bridge's tiles are drawn as water under it
// (WorldMesh). With the far field on, these ribbons are drawn only where the near chunks are (the hole); the far
// field draws the rest, coarser (FarField.Roads).

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class RoadRibbons
{
    /// <summary>How far round the eye roads are drawn (m), and how far you go before they are made again.</summary>
    const float RANGE = 110, AGAIN = 30;
    /// <summary>Half the road's width (the stamp's reach: Roads.ROAD_REACH) and how far over the ground it lies.</summary>
    public const float HALF = (float)Roads.ROAD_REACH, LIFT = 0.035f, LINE_LIFT = 0.05f;
    /// <summary>A bridge's deck: how far its sides go down (m).</summary>
    const float DECK_SIDE = 0.55f;
    static readonly int ROAD = Array.IndexOf(Grids.GROUNDS, "road"), BRIDGE = Array.IndexOf(Grids.GROUNDS, "bridge");
    public static readonly Color LINE = new(0.62f, 0.6f, 0.5f);
    /// <summary>What a piece of the line runs over (Kind).</summary>
    const int NONE = 0, COUNTRY = 1, DECK = 2, STREET = 3;

    readonly MeshInstance3D node = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    readonly Material material;
    Vector3 madeAt = new(float.MaxValue, 0, 0);
    Vector4? madeHole;
    object? madeFor;
    bool missing;
    int frame;

    /// <summary>Metres of road drawn (the self-test), and the last build's time (ms).</summary>
    public float Drawn { get; private set; }
    public double BuildMs { get; private set; }

    public RoadRibbons(Node3D parent, Material material)
    {
        this.material = material;
        parent.AddChild(node);
    }

    /// <summary>`hole`: the near chunks' square (tiles: x0, z0, x1, z1) when the far field draws past it, else null.</summary>
    public void Update(SimState state, Vector3 eye, Vector4? hole)
    {
        WorldMesh.Ribboned = state.world != null;
        node.Visible = WorldMesh.Ribboned;
        if (state.world == null) return;
        var flat = new Vector3(eye.X, 0, eye.Z);
        frame++;
        bool again = !ReferenceEquals(madeFor, state.grid) || flat.DistanceTo(madeAt) >= AGAIN || hole != madeHole || (missing && frame % 30 == 0);
        if (!again) return;
        ulong t0 = Godot.Time.GetTicksUsec();
        var regions = Worldplan.regions(state.world.seed, state.world.size, state.settings.map);
        // The square drawn: round the eye, and inside the hole (a metre over, so the two meet) when the far field is on.
        float x0 = eye.X - RANGE, z0 = eye.Z - RANGE, x1 = eye.X + RANGE, z1 = eye.Z + RANGE;
        if (hole is { } h) { x0 = Math.Max(x0, h.X - 1); z0 = Math.Max(z0, h.Y - 1); x1 = Math.Min(x1, h.Z + 1); z1 = Math.Min(z1, h.W + 1); }
        var net = RoadNet.Gather(regions, x0 - 8, z0 - 8, x1 + 8, z1 + 8);
        missing = net.Missing;
        madeFor = state.grid;
        madeAt = flat;
        madeHole = hole;
        node.Mesh = Build(state.grid, net, eye, new Rect2(x0, z0, x1 - x0, z1 - z0), hole == null);
        BuildMs = (Godot.Time.GetTicksUsec() - t0) / 1000.0;
    }

    Mesh? Build(Grid g, RoadNet net, Vector3 eye, Rect2 box, bool round)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var asphalt = new Look(WorldLook.GroundColour("road").Lightened(0.08f), WorldLook.Ground("road"));
        var planks = new Look(WorldLook.GroundColour("bridge").Lightened(0.07f), WorldLook.Ground("bridge"));
        var line = new Look(LINE, Surfaces.None);
        var eye2 = new Vector2(eye.X, eye.Z);
        bool In(Vector2 p) => box.HasPoint(p) && (!round || eye2.DistanceTo(p) <= RANGE);
        Drawn = 0;
        bool any = false;
        for (int li = 0; li < net.lines.Count; li++)
        {
            var pts = RoadNet.Resample(net.lines[li], 1);
            var kinds = new int[Math.Max(0, pts.Count - 1)];
            for (int i = 0; i < kinds.Length; i++) kinds[i] = Kind(g, (pts[i] + pts[i + 1]) / 2);
            float along = 0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                float len = a.DistanceTo(b);
                float from = along;
                along += len;
                if (len < 1e-4f) continue;
                var mid = (a + b) / 2;
                int kind = kinds[i];
                if (!In(mid) || kind is NONE or STREET) continue;
                // Across the road at each end: the average of the directions in and out, so bends close up.
                Vector2 na = RoadNet.Across(pts, i), nb = RoadNet.Across(pts, i + 1);
                if (kind == DECK)
                {
                    Deck(st, a, b, na, nb, planks);
                    Drawn += len;
                    any = true;
                    continue;
                }
                // Next to a town's street: the strip runs on to the street's kerb line and is cut there.
                Clip? clip = null;
                var dir = (b - a) / len;
                if (i + 1 < kinds.Length && kinds[i + 1] == STREET && KerbLine(g, mid, pts[i + 1] + (pts[Math.Min(pts.Count - 1, i + 2)] - pts[i + 1]) / 2) is { } c1) { clip = c1; b += dir * 4; }
                else if (i > 0 && kinds[i - 1] == STREET && KerbLine(g, mid, (pts[i - 1] + a) / 2) is { } c0) { clip = c0; a -= dir * 4; }
                Strip(st, a, b, na, nb, -HALF, HALF, 3, LIFT, asphalt, clip);
                // The lines, but not through another road's asphalt or a junction.
                bool Clear(float o) => net.DistOther(li, mid + (na + nb).Normalized() * o) > HALF - 0.05f && !net.NearJoin(mid + (na + nb).Normalized() * o, HALF);
                if (Clear(-HALF + 0.3f)) Strip(st, a, b, na, nb, -HALF + 0.26f, -HALF + 0.34f, 1, LINE_LIFT, line, clip);
                if (Clear(HALF - 0.3f)) Strip(st, a, b, na, nb, HALF - 0.34f, HALF - 0.26f, 1, LINE_LIFT, line, clip);
                if ((int)Math.Floor(from / 2) % 2 == 0 && Clear(0)) Strip(st, a, b, na, nb, -0.06f, 0.06f, 1, LINE_LIFT, line, clip);
                Drawn += len;
                any = true;
            }
        }
        // The junctions: a disc of asphalt where roads meet, so the corner between their strips is filled.
        foreach (var j in net.joins)
            if (In(j) && Kind(g, j) == COUNTRY) { Disc(st, j, HALF, asphalt, p => Terrain.Height(p.X, p.Y) + LIFT); any = true; }
        if (!any) return null;
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    /// <summary>What the line runs over at a point: a country road's tiles, a bridge, a street between kerbs, or none.</summary>
    static int Kind(Grid g, Vector2 p)
    {
        int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
        if (x < 0 || y < 0 || x >= g.width || y >= g.height) return NONE;
        int i = y * g.width + x, k = Grids.groundAt(g, i);
        if (Grids.tileAt(g, i) != "floor") return NONE;
        if (k == BRIDGE) return DECK;
        if (k != ROAD) return NONE;
        return Terrain.Sunk(x, y) ? STREET : COUNTRY;
    }

    /// <summary>Keep the side of a line (n·p &lt;= c).</summary>
    readonly record struct Clip(Vector2 n, float c);

    /// <summary>Where the line goes from road to street between `from` and `to`: the edge of the tiles it crosses
    /// there (the street's kerb runs along it), as the side to keep.</summary>
    static Clip? KerbLine(Grid g, Vector2 from, Vector2 to)
    {
        var last = from;
        for (int s = 1; s <= 40; s++)
        {
            var p = from.Lerp(to, s / 40f);
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            if (x >= 0 && y >= 0 && x < g.width && y < g.height && Terrain.Sunk(x, y))
            {
                int lx = (int)Math.Floor(last.X), ly = (int)Math.Floor(last.Y);
                if (lx != x) return lx < x ? new Clip(new Vector2(1, 0), x) : new Clip(new Vector2(-1, 0), -lx);
                if (ly != y) return ly < y ? new Clip(new Vector2(0, 1), y) : new Clip(new Vector2(0, -1), -ly);
                return null;
            }
            last = p;
        }
        return null;
    }

    /// <summary>A strip from a to b between two offsets across the road, in `parts` across (so it follows ground
    /// that is not flat across), lifted over the ground, cut by `clip`.</summary>
    static void Strip(SurfaceTool st, Vector2 a, Vector2 b, Vector2 na, Vector2 nb, float o0, float o1, int parts, float lift, Look look, Clip? clip = null)
    {
        st.SetColor(look.colour);
        st.SetUV(look.what);
        st.SetNormal(Vector3.Up);
        for (int k = 0; k < parts; k++)
        {
            float u0 = o0 + (o1 - o0) * k / parts, u1 = o0 + (o1 - o0) * (k + 1) / parts;
            Vector2 p0 = a + na * u0, p1 = a + na * u1, p2 = b + nb * u1, p3 = b + nb * u0;
            Flat(st, p0, p1, p2, lift, clip);
            Flat(st, p0, p2, p3, lift, clip);
        }
    }

    /// <summary>A triangle on the ground's heights, cut by `clip` (none: as it is).</summary>
    static void Flat(SurfaceTool st, Vector2 p0, Vector2 p1, Vector2 p2, float lift, Clip? clip)
    {
        Vector3 P(Vector2 q) => new(q.X, Terrain.Height(q.X, q.Y) + lift, q.Y);
        if (clip is not { } c) { Tri(st, P(p0), P(p1), P(p2)); return; }
        var poly = new List<Vector2>(4);
        var tri = new[] { p0, p1, p2 };
        for (int k = 0; k < 3; k++)
        {
            Vector2 s = tri[k], e = tri[(k + 1) % 3];
            float ds = c.n.Dot(s) - c.c, de = c.n.Dot(e) - c.c;
            if (ds <= 0) poly.Add(s);
            if ((ds <= 0) != (de <= 0)) poly.Add(s.Lerp(e, ds / (ds - de)));
        }
        for (int k = 1; k + 1 < poly.Count; k++) Tri(st, P(poly[0]), P(poly[k]), P(poly[k + 1]));
    }

    /// <summary>A bridge's deck from a to b: planks flat across at the bridge's height on the line, its sides down.</summary>
    static void Deck(SurfaceTool st, Vector2 a, Vector2 b, Vector2 na, Vector2 nb, Look look)
    {
        float ha = Terrain.Height(a.X, a.Y) + LIFT, hb = Terrain.Height(b.X, b.Y) + LIFT;
        Vector3 Q(Vector2 p, Vector2 n, float o, float h) => new(p.X + n.X * o, h, p.Y + n.Y * o);
        st.SetColor(look.colour);
        st.SetUV(look.what);
        st.SetNormal(Vector3.Up);
        Vector3 l0 = Q(a, na, -HALF, ha), r0 = Q(a, na, HALF, ha), r1 = Q(b, nb, HALF, hb), l1 = Q(b, nb, -HALF, hb);
        Tri(st, l0, r0, r1);
        Tri(st, l0, r1, l1);
        var side = look.colour.Darkened(0.25f);
        foreach (var (p, q, o) in new[] { (l0, l1, -1f), (r0, r1, 1f) })
        {
            var n3 = new Vector3(na.X * o, 0, na.Y * o);
            st.SetColor(side);
            st.SetNormal(n3);
            Vector3 pd = p - Vector3.Up * DECK_SIDE, qd = q - Vector3.Up * DECK_SIDE;
            Side(st, p, q, qd, pd, n3);
        }
    }

    /// <summary>A quad facing `n`.</summary>
    static void Side(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
    {
        if ((b - a).Cross(c - a).Dot(n) > 0) (b, d) = (d, b);
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
    }

    /// <summary>A disc of `look` round p, on the heights `h` gives.</summary>
    public static void Disc(SurfaceTool st, Vector2 p, float r, Look look, Func<Vector2, float> h, int parts = 16, bool uv = true)
    {
        st.SetColor(look.colour);
        if (uv) st.SetUV(look.what);
        st.SetNormal(Vector3.Up);
        Vector3 P(Vector2 q) => new(q.X, h(q), q.Y);
        var c = P(p);
        for (int k = 0; k < parts; k++)
        {
            float t0 = k * MathF.Tau / parts, t1 = (k + 1) * MathF.Tau / parts;
            Tri(st, c, P(p + new Vector2(MathF.Cos(t0), MathF.Sin(t0)) * r), P(p + new Vector2(MathF.Cos(t1), MathF.Sin(t1)) * r));
        }
    }

    /// <summary>Front faces wind clockwise seen from above, as the ground's do.</summary>
    public static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
    {
        if ((b - a).Cross(c - a).Y > 0) (b, c) = (c, b);
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
    }
}

/// <summary>The plan's country road lines in a box, from every planned region it touches, each once (a road
/// crossing from one region to the next is in both plans); where they meet (a line's end on or by another line);
/// and how far a point is from the other lines. The near ribbons and the far field share it.</summary>
public sealed class RoadNet
{
    const int BUCKET = 8;
    public readonly List<List<PointD>> lines = new();
    public readonly List<Vector2> joins = new();
    /// <summary>A region in the box isn't planned yet (asked for on a worker): gather again later.</summary>
    public bool Missing;
    readonly Dictionary<(int, int), List<(int line, Vector2 a, Vector2 b)>> buckets = new();

    /// <summary>`ask`: a region in the box not planned yet is asked for (and Missing set); else it is left out.</summary>
    public static RoadNet Gather(WorldRegions regions, float x0, float z0, float x1, float z1, bool ask = true)
    {
        var net = new RoadNet();
        var seen = new HashSet<string>();
        var (ra, rb) = regions.RegionOf(x0, z0);
        var (rc, rd) = regions.RegionOf(x1, z1);
        for (int ry = rb; ry <= rd; ry++)
            for (int rx = ra; rx <= rc; rx++)
            {
                if (!regions.IsPlanned(rx, ry)) { if (ask) { regions.PlanSoon(rx, ry); net.Missing = true; } continue; }
                foreach (var road in regions.Region(rx, ry).roads)
                {
                    if (road.Count < 2 || !Touches(road, x0, z0, x1, z1)) continue;
                    if (!seen.Add($"{road[0].x},{road[0].y},{road[^1].x},{road[^1].y},{road.Count}")) continue;
                    net.Add(road);
                }
            }
        net.FindJoins();
        return net;
    }

    /// <summary>Does any piece of the line come into the box (by its bounds: a straight road can be two points far apart)?</summary>
    static bool Touches(List<PointD> road, float x0, float z0, float x1, float z1)
    {
        for (int i = 0; i + 1 < road.Count; i++)
        {
            var (a, b) = (road[i], road[i + 1]);
            if (Math.Max(a.x, b.x) >= x0 && Math.Min(a.x, b.x) <= x1 && Math.Max(a.y, b.y) >= z0 && Math.Min(a.y, b.y) <= z1) return true;
        }
        return false;
    }

    void Add(List<PointD> road)
    {
        int li = lines.Count;
        lines.Add(road);
        for (int i = 0; i + 1 < road.Count; i++)
        {
            Vector2 a = new((float)road[i].x, (float)road[i].y), b = new((float)road[i + 1].x, (float)road[i + 1].y);
            int bx0 = (int)Math.Floor((Math.Min(a.X, b.X) - RoadRibbons.HALF - 1) / BUCKET), bx1 = (int)Math.Floor((Math.Max(a.X, b.X) + RoadRibbons.HALF + 1) / BUCKET);
            int by0 = (int)Math.Floor((Math.Min(a.Y, b.Y) - RoadRibbons.HALF - 1) / BUCKET), by1 = (int)Math.Floor((Math.Max(a.Y, b.Y) + RoadRibbons.HALF + 1) / BUCKET);
            for (int by = by0; by <= by1; by++)
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    if (!buckets.TryGetValue((bx, by), out var l)) buckets[(bx, by)] = l = new();
                    l.Add((li, a, b));
                }
        }
    }

    /// <summary>The distance from p to the nearest line other than `line` (far: more than HALF + 1).</summary>
    public float DistOther(int line, Vector2 p)
    {
        float best = float.MaxValue;
        if (!buckets.TryGetValue(((int)Math.Floor(p.X / BUCKET), (int)Math.Floor(p.Y / BUCKET)), out var l)) return best;
        foreach (var (li, a, b) in l)
        {
            if (li == line) continue;
            var d = b - a;
            float len2 = d.LengthSquared();
            float t = len2 < 1e-12f ? 0 : Math.Clamp((p - a).Dot(d) / len2, 0, 1);
            best = Math.Min(best, (a + d * t).DistanceTo(p));
        }
        return best;
    }

    /// <summary>Within r of a junction?</summary>
    public bool NearJoin(Vector2 p, float r)
    {
        foreach (var j in joins) if (j.DistanceSquaredTo(p) < r * r) return true;
        return false;
    }

    /// <summary>A junction is a line's end on or beside another line (each once).</summary>
    void FindJoins()
    {
        for (int li = 0; li < lines.Count; li++)
            foreach (var e in new[] { lines[li][0], lines[li][^1] })
            {
                var p = new Vector2((float)e.x, (float)e.y);
                if (DistOther(li, p) < RoadRibbons.HALF + 1 && !NearJoin(p, 1)) joins.Add(p);
            }
    }

    /// <summary>A line as points `step` m apart or less (each piece of it in equal steps), its ends kept.</summary>
    public static List<Vector2> Resample(List<PointD> road, float step)
    {
        var pts = new List<Vector2>();
        for (int i = 0; i < road.Count - 1; i++)
        {
            Vector2 a = new((float)road[i].x, (float)road[i].y), b = new((float)road[i + 1].x, (float)road[i + 1].y);
            int steps = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b) / step));
            for (int s = 0; s < steps; s++) pts.Add(a.Lerp(b, s / (float)steps));
            if (i == road.Count - 2) pts.Add(b);
        }
        return pts;
    }

    /// <summary>Across the line at point i: the average of the directions in and out, so bends close up.</summary>
    public static Vector2 Across(List<Vector2> pts, int i)
    {
        var dir = (pts[Math.Min(pts.Count - 1, i + 1)] - pts[Math.Max(0, i - 1)]).Normalized();
        return new Vector2(-dir.Y, dir.X);
    }
}
