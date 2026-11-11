// Camera maths: world (tile) coordinates <-> screen pixels (src/render/camera.ts, interp.ts).
// The top-down projection of the 2D views (the colony tab under M, and the game with nobody under
// your control). First person is World3D/; there is no isometric view, by decision (2026-11-08).

using System;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Render;

public struct Cam
{
    /// <summary>World position (in tiles) shown at the centre of the viewport.</summary>
    public double x, y, zoom;
    public Cam(double x, double y, double zoom) { this.x = x; this.y = y; this.zoom = zoom; }
}

public struct Vport
{
    public double width, height;
    public Vport(double w, double h) { width = w; height = h; }
}

public struct P
{
    public double x, y;
    public P(double x, double y) { this.x = x; this.y = y; }
}

public struct TileRange { public int x0, y0, x1, y1; }

public static class CameraMath
{
    /// <summary>Screen pixels per tile at zoom 1.</summary>
    public const double TILE_PX = 24;
    public const double MIN_ZOOM = 0.25;
    public const double MAX_ZOOM = 4;

    public static double tileSize(Cam c) => TILE_PX * c.zoom;

    public static Cam centredOn(double width, double height, double zoom = 1) => new(width / 2, height / 2, zoom);

    public static P worldToScreen(Cam c, Vport v, double wx, double wy)
    {
        double size = tileSize(c);
        return new P((wx - c.x) * size + v.width / 2, (wy - c.y) * size + v.height / 2);
    }

    public static P screenToWorld(Cam c, Vport v, double sx, double sy)
    {
        double size = tileSize(c);
        return new P((sx - v.width / 2) / size + c.x, (sy - v.height / 2) / size + c.y);
    }

    public static Point screenToTile(Cam c, Vport v, double sx, double sy)
    {
        var w = screenToWorld(c, v, sx, sy);
        return new Point((int)Math.Floor(w.x), (int)Math.Floor(w.y));
    }

    public static Cam panByScreen(Cam c, double dx, double dy)
    {
        double size = tileSize(c);
        return new Cam(c.x - dx / size, c.y - dy / size, c.zoom);
    }

    /// <summary>Zooms by factor, keeping the world point under (sx, sy) fixed on screen.</summary>
    public static Cam zoomAt(Cam c, Vport v, double sx, double sy, double factor)
    {
        double zoom = Math.Min(MAX_ZOOM, Math.Max(MIN_ZOOM, c.zoom * factor));
        var anchor = screenToWorld(c, v, sx, sy);
        double size = TILE_PX * zoom;
        return new Cam(anchor.x - (sx - v.width / 2) / size, anchor.y - (sy - v.height / 2) / size, zoom);
    }

    /// <summary>Inclusive range of tiles visible in the viewport, clamped to the map.</summary>
    public static TileRange visibleTiles(Cam c, Vport v, int mapWidth, int mapHeight)
    {
        var tl = screenToTile(c, v, 0, 0);
        var br = screenToTile(c, v, v.width, v.height);
        return new TileRange { x0 = Math.Max(0, tl.x), y0 = Math.Max(0, tl.y), x1 = Math.Min(mapWidth - 1, br.x), y1 = Math.Min(mapHeight - 1, br.y) };
    }

    /// <summary>Render-time interpolation: a mover's centre in tiles, blended from `was` to `at` by alpha.</summary>
    public static P worldPos(Point at, Point was, double alpha)
    {
        double a = Math.Min(1, Math.Max(0, alpha));
        return new P((was.x + (at.x - was.x) * a) / Motion.SUB, (was.y + (at.y - was.y) * a) / Motion.SUB);
    }

    public static P worldPos(Body b, double alpha) => worldPos(b.at, b.was, alpha);
}
