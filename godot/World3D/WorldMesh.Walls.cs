// WorldMesh's walls (the class's parts: WorldMesh.cs): what stands on a tile's north or west edge (a wall, a door or a
// garage's wide door, a window with its frame and glass, boarded or not, a fence, a barricade), and the old door that
// fills its tile. Each stands on its edge's higher end and goes down to a footing under its lower one. A face looks
// like the inside of the building it faces into, or the outside of the one behind it (WorldLook.Style).

using System;
using Godot;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public static partial class WorldMesh
{
    const float WALL_T = 0.12f;
    const float DOOR_H = 2.05f;
    /// <summary>A window's sill over the floor (m; a vaulting hand goes on it: People.Leap.cs).</summary>
    internal const float SILL_H = 0.9f;
    const float LINTEL_H = 2.1f;
    /// <summary>An edge wall stops this short of its corner's far side, so its end is inside the wall it meets, not level with that wall's face.</summary>
    const float TUCK = 0.002f;
    /// <summary>A barricade's planks: the bottom of each over the floor (m), and their height; its top is the top plank's (a vaulting hand goes on it).</summary>
    static readonly float[] PLANKS = { 0.25f, 0.65f, 1.05f };
    const float PLANK_H = 0.22f;
    internal const float BARRICADE_TOP = 1.05f + PLANK_H;

    /// <summary>The walls' grey: the far view's blocks, and a wall that fills its tile (hand-made maps).</summary>
    internal static readonly Color WALL = new(0.62f, 0.6f, 0.56f);
    static Look TileWall => new(WALL, Surfaces.What("concrete"));

    /// <summary>A door with a door beside it in the same wall is part of one wide door (a garage's): which ends of it
    /// run on into the next (1 the start, 2 the end; 0 a door on its own, or not a door).</summary>
    static int WideDoor(Grid g, int x, int y, int side, int kind)
    {
        if (kind != Grids.EDGE_DOOR) return 0;
        int wide = 0;
        int ax = side == 0 ? 1 : 0, ay = side == 0 ? 0 : 1; // along the wall
        if (Grids.inBounds(g, x - ax, y - ay) && Doors.sameDoor(g, x - ax, y - ay, x, y, side)) wide |= 1;
        if (Grids.inBounds(g, x + ax, y + ay) && Doors.sameDoor(g, x, y, x + ax, y + ay, side)) wide |= 2;
        return wide;
    }

    /// <summary>An edge: a wall, door, barricade or window on the north or west side of tile (x, y). `before` is the
    /// building of the tile north or west of it and `after` the tile's own (null: none): a face looks like the inside of
    /// the building it faces into, or the outside of the one behind it. Returns whether it put glass in.</summary>
    static bool Edge(MeshBuf st, MeshBuf gl, int x, int y, bool north, int kind, bool open, bool locked, bool boarded, WorldLook.Style? before, WorldLook.Style? after, int wide = 0)
    {
        // Along the edge from a to a + 1, thickness across it. Heights are over the edge's floor (its higher
        // end); what starts at the ground goes down to a footing under its lower end.
        float c0 = Terrain.Corner(x, y), c1 = north ? Terrain.Corner(x + 1, y) : Terrain.Corner(x, y + 1);
        float floor = Math.Max(c0, c1), foot = Math.Min(c0, c1) - FOOTING;
        var own = after ?? before ?? WorldLook.Built;
        Look faceBefore = before != null ? before.inside : own.outside, faceAfter = after != null ? after.inside : own.outside;
        Vector3 Lo(float along, float yy, float across) => north ? new(x + along, yy, y + across) : new(x + across, yy, y + along);
        // A slab of the edge: its two faces, its top and its ends (a wall's ends are its outside; an opening's are trim).
        void Slab(float a0, float a1, float h0, float h1, float t, Look sideBefore, Look sideAfter, Look top, Look ends)
        {
            var p = Lo(a0, h0, -t);
            var q = Lo(a1, h1, t);
            Vector3 lo = new(Math.Min(p.X, q.X), h0 == 0 ? foot : floor + h0, Math.Min(p.Z, q.Z)), hi = new(Math.Max(p.X, q.X), floor + h1, Math.Max(p.Z, q.Z));
            // On a slope the wall stands on a footing: concrete from the lower ground up to the floor.
            if (h0 == 0 && floor - foot > FOOTING + 0.03f)
            {
                Box(st, lo, hi with { Y = floor }, WorldLook.Footing);
                lo.Y = floor;
            }
            if (north) Box(st, lo, hi, top, sideBefore, sideAfter, ends, ends);
            else Box(st, lo, hi, top, ends, ends, sideBefore, sideAfter);
        }
        // A wall between two rooms ends as it faces; an outer wall ends as its outside.
        var ends = before != null && after != null ? faceAfter : own.outside;
        void Wall(float a0, float a1, float h0, float h1, bool opening = false) => Slab(a0, a1, h0, h1, WALL_T / 2, faceBefore, faceAfter, ends, opening ? own.trim : ends);
        void Piece(float a0, float a1, float h0, float h1, float t, Look look) => Slab(a0, a1, h0, h1, t, look, look, look, look);
        float t = WALL_T / 2, end = t - TUCK;
        switch (kind)
        {
            case Grids.EDGE_WALL:
                Wall(-end, 1 + end, 0, WALL_H);
                return false;
            case Grids.EDGE_DOOR when wide != 0:
            {
                // Part of a wide door (a garage's): a jamb only at the run's ends, a lintel across, and a sheet-metal
                // leaf from side to side; open, it is rolled up under the lintel.
                float a0 = (wide & 1) != 0 ? 0 : 0.05f, a1 = (wide & 2) != 0 ? 1 : 0.95f;
                if ((wide & 1) == 0) Wall(-end, 0.05f, 0, WALL_H, true);
                if ((wide & 2) == 0) Wall(0.95f, 1 + end, 0, WALL_H, true);
                Wall(a0, a1, DOOR_H + 0.15f, WALL_H);
                var sheet = new Look(new Color(0.55f, 0.56f, 0.55f), Surfaces.What("sheetmetal"));
                if (!open) Piece(a0, a1, 0.01f, DOOR_H + 0.15f, 0.03f, sheet);
                else Piece(a0, a1, DOOR_H - 0.1f, DOOR_H + 0.15f, 0.09f, sheet);
                return false;
            }
            case Grids.EDGE_DOOR:
                Wall(-end, 0.05f, 0, WALL_H, true);
                Wall(0.95f, 1 + end, 0, WALL_H, true);
                Wall(0.05f, 0.95f, DOOR_H, WALL_H);
                // The leaf hangs from the lintel: its foot clears the floor, not the ground below it.
                if (!open) Piece(0.06f, 0.94f, 0.01f, DOOR_H, 0.03f, locked ? WorldLook.DoorLocked : WorldLook.Door);
                else
                {
                    // Swung back against the wall, hinged at the edge's start.
                    var p = Lo(0.06f, 0, 0);
                    var q = Lo(0.12f, DOOR_H, 0.88f);
                    Box(st, new(Math.Min(p.X, q.X), floor + 0.01f, Math.Min(p.Z, q.Z)), new(Math.Max(p.X, q.X), floor + DOOR_H, Math.Max(p.Z, q.Z)), WorldLook.Door);
                }
                return false;
            case Grids.EDGE_BARRICADE:
                foreach (var h in PLANKS) Piece(-0.05f, 1.05f, h, h + PLANK_H, 0.05f, WorldLook.Plank);
                return false;
            case Grids.EDGE_FENCE:
                // A tall fence: two posts and rails, boards close side by side to above head height.
                Piece(-0.05f, 0.05f, 0, 2.05f, 0.07f, WorldLook.Plank);
                Piece(0.95f, 1.05f, 0, 2.05f, 0.07f, WorldLook.Plank);
                foreach (var h in new[] { 0.3f, 1.7f }) Piece(-0.05f, 1.05f, h, h + 0.1f, 0.06f, WorldLook.Plank);
                for (int k = 0; k < 6; k++) Piece(0.06f + k * 0.148f, 0.06f + k * 0.148f + 0.13f, 0.05f, 1.95f, 0.035f, WorldLook.Plank);
                return false;
            case Grids.EDGE_WINDOW:
            case Grids.EDGE_WINDOW_BROKEN:
                Wall(-end, 1 + end, 0, SILL_H);
                Wall(-end, 1 + end, LINTEL_H, WALL_H);
                Wall(-end, 0.06f, SILL_H, LINTEL_H, true);
                Wall(0.94f, 1 + end, SILL_H, LINTEL_H, true);
                // The frame, a little proud of the wall: a sill, a head and two stiles.
                float ft = t + 0.012f;
                Piece(0.04f, 0.96f, SILL_H - 0.03f, SILL_H + 0.03f, ft + 0.02f, own.trim);
                Piece(0.06f, 0.94f, LINTEL_H - 0.04f, LINTEL_H, ft, own.trim);
                Piece(0.06f, 0.1f, SILL_H + 0.03f, LINTEL_H - 0.04f, ft, own.trim);
                Piece(0.9f, 0.94f, SILL_H + 0.03f, LINTEL_H - 0.04f, ft, own.trim);
                if (boarded) foreach (var h in new[] { 1.0f, 1.4f, 1.8f }) Piece(0.0f, 1.0f, h, h + 0.18f, t + 0.04f, WorldLook.Plank);
                if (kind == Grids.EDGE_WINDOW)
                {
                    var p = Lo(0.06f, SILL_H, -0.01f);
                    var q = Lo(0.94f, LINTEL_H, 0.01f);
                    Box(gl, new(Math.Min(p.X, q.X), floor + SILL_H, Math.Min(p.Z, q.Z)), new(Math.Max(p.X, q.X), floor + LINTEL_H, Math.Max(p.Z, q.Z)), WorldLook.Pane);
                    return true;
                }
                return false;
        }
        return false;
    }

    /// <summary>An old-style door that fills its tile: posts and a lintel, the leaf across the gap unless open.</summary>
    static void TileDoor(MeshBuf st, Grid g, int x, int y, bool open, bool locked, float floor, float low)
    {
        // Across the gap the walls are on: walls west and east mean the door runs along x.
        bool alongX = Grids.getTile(g, x - 1, y) == "wall" || Grids.getTile(g, x + 1, y) == "wall";
        float c = 0.5f;
        if (alongX)
        {
            Box(st, new(x, floor + DOOR_H, y + c - 0.06f), new(x + 1, floor + WALL_H, y + c + 0.06f), TileWall);
            if (!open) Box(st, new(x + 0.04f, low, y + c - 0.03f), new(x + 0.96f, floor + DOOR_H, y + c + 0.03f), locked ? WorldLook.DoorLocked : WorldLook.Door);
        }
        else
        {
            Box(st, new(x + c - 0.06f, floor + DOOR_H, y), new(x + c + 0.06f, floor + WALL_H, y + 1), TileWall);
            if (!open) Box(st, new(x + c - 0.03f, low, y + 0.04f), new(x + c + 0.03f, floor + DOOR_H, y + 0.96f), locked ? WorldLook.DoorLocked : WorldLook.Door);
        }
    }
}
