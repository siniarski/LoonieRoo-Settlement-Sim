// Country roads as smooth ribbons (2026-11-12, docs/roads-off-grid.md step 1): the sim's roads are tiles, and a
// road on the slant drawn tile by tile is a staircase. The world plan already holds each country road as a line
// (WorldPlan.roads: the smoothed curve its tiles were stamped from), so first person draws the road along that
// line instead: a strip of asphalt as wide as the stamp, laid on the ground's heights, with a dashed centre line
// and solid edge lines following the curve. The tiles under it are drawn as the verge beside them (WorldMesh,
// while Ribboned), and still decide everything the sim does. Town streets (sunk between kerbs, straight) keep
// their tiles. Only where the line runs over country road tiles: it stops at a town's street, a bridge, the map's
// edge. One mesh for the roads near the eye, made again as you move. Reads state only.

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
    const float HALF = (float)Roads.ROAD_REACH, LIFT = 0.035f, LINE_LIFT = 0.05f;
    static readonly int ROAD = Array.IndexOf(Grids.GROUNDS, "road");
    static readonly Color LINE = new(0.62f, 0.6f, 0.5f);

    readonly MeshInstance3D node = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    readonly Material material;
    Vector3 madeAt = new(float.MaxValue, 0, 0);
    object? madeFor;

    /// <summary>Metres of road drawn (the self-test).</summary>
    public float Drawn { get; private set; }

    public RoadRibbons(Node3D parent, Material material)
    {
        this.material = material;
        parent.AddChild(node);
    }

    public void Update(SimState state, Vector3 eye)
    {
        WorldMesh.Ribboned = state.world != null;
        node.Visible = WorldMesh.Ribboned;
        if (state.world == null) return;
        var flat = new Vector3(eye.X, 0, eye.Z);
        if (ReferenceEquals(madeFor, state.grid) && flat.DistanceTo(madeAt) < AGAIN) return;
        var regions = Worldplan.regions(state.world.seed, state.world.size, state.settings.map);
        var (rx, ry) = regions.RegionOf(eye.X, eye.Z);
        if (!regions.IsPlanned(rx, ry)) { regions.PlanSoon(rx, ry); return; } // on a thread; drawn when it is there
        madeFor = state.grid;
        madeAt = flat;
        node.Mesh = Build(state.grid, regions.Region(rx, ry).roads, eye);
    }

    Mesh? Build(Grid g, List<List<PointD>> roads, Vector3 eye)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var asphalt = new Look(WorldLook.GroundColour("road").Lightened(0.08f), WorldLook.Ground("road"));
        var line = new Look(LINE, Surfaces.None);
        Drawn = 0;
        bool any = false;
        foreach (var road in roads)
        {
            // The line a metre at a time, with how far along each point is.
            var pts = new List<Vector2>();
            for (int i = 0; i < road.Count - 1; i++)
            {
                Vector2 a = new((float)road[i].x, (float)road[i].y), b = new((float)road[i + 1].x, (float)road[i + 1].y);
                int steps = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b)));
                for (int s = 0; s < steps; s++) pts.Add(a.Lerp(b, s / (float)steps));
                if (i == road.Count - 2) pts.Add(b);
            }
            float along = 0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                float len = a.DistanceTo(b);
                float from = along;
                along += len;
                if (len < 1e-4f) continue;
                var mid = (a + b) / 2;
                if (new Vector2(eye.X, eye.Z).DistanceTo(mid) > RANGE || !Country(g, mid)) continue;
                // Across the road at each end: the average of the directions in and out, so bends close up.
                Vector2 na = Across(pts, i), nb = Across(pts, i + 1);
                Strip(st, a, b, na, nb, -HALF, HALF, 3, LIFT, asphalt);
                Strip(st, a, b, na, nb, -HALF + 0.26f, -HALF + 0.34f, 1, LINE_LIFT, line);
                Strip(st, a, b, na, nb, HALF - 0.34f, HALF - 0.26f, 1, LINE_LIFT, line);
                if ((int)Math.Floor(from / 2) % 2 == 0) Strip(st, a, b, na, nb, -0.06f, 0.06f, 1, LINE_LIFT, line);
                Drawn += len;
                any = true;
            }
        }
        if (!any) return null;
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    /// <summary>Is this point on a country road's tiles: road, open, on the map, not a street between kerbs?</summary>
    static bool Country(Grid g, Vector2 p)
    {
        int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
        if (x < 0 || y < 0 || x >= g.width || y >= g.height) return false;
        int i = y * g.width + x;
        return Grids.groundAt(g, i) == ROAD && Grids.tileAt(g, i) == "floor" && !Terrain.Sunk(x, y);
    }

    static Vector2 Across(List<Vector2> pts, int i)
    {
        var dir = (pts[Math.Min(pts.Count - 1, i + 1)] - pts[Math.Max(0, i - 1)]).Normalized();
        return new Vector2(-dir.Y, dir.X);
    }

    /// <summary>A strip from a to b between two offsets across the road, in `parts` across (so it follows ground
    /// that is not flat across), lifted over the ground.</summary>
    static void Strip(SurfaceTool st, Vector2 a, Vector2 b, Vector2 na, Vector2 nb, float o0, float o1, int parts, float lift, Look look)
    {
        Vector3 P(Vector2 p, Vector2 n, float o)
        {
            var q = p + n * o;
            return new Vector3(q.X, Terrain.Height(q.X, q.Y) + lift, q.Y);
        }
        st.SetColor(look.colour);
        st.SetUV(look.what);
        st.SetNormal(Vector3.Up);
        for (int k = 0; k < parts; k++)
        {
            float u0 = o0 + (o1 - o0) * k / parts, u1 = o0 + (o1 - o0) * (k + 1) / parts;
            Vector3 p0 = P(a, na, u0), p1 = P(a, na, u1), p2 = P(b, nb, u1), p3 = P(b, nb, u0);
            Tri(st, p0, p1, p2);
            Tri(st, p0, p2, p3);
        }
    }

    /// <summary>Front faces wind clockwise seen from above, as the ground's do.</summary>
    static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
    {
        if ((b - a).Cross(c - a).Y > 0) (b, c) = (c, b);
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
    }
}
