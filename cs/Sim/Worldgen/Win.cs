// A window of the map (2026-11-09, docs/worldplan-regions.md): the tiles [x0, x1) x [y0, y1) a region's plan covers,
// which of its sides are the map's edge (the rest border another region), and the region's index. The planners
// (Towns, Roads, Worldplan) work inside one; over the whole map (Win.Whole) they are exactly what they were.
#nullable disable warnings
namespace LoonieRoo.Sim;

public readonly record struct Win(int x0, int y0, int x1, int y1, bool edgeL, bool edgeT, bool edgeR, bool edgeB, int index)
{
    public int W => x1 - x0;
    public int H => y1 - y0;

    /// <summary>The whole map: one window, every side the map's edge.</summary>
    public static Win Whole(double size) => new(0, 0, (int)size, (int)size, true, true, true, true, 0);

    public bool Holds(double x, double y) => x >= x0 && y >= y0 && x < x1 && y < y1;

    /// <summary>The coarse road cells (ROAD_CELL tiles) the window covers: the first column and row, and how many.</summary>
    public int Kx0 => x0 / Roads.ROAD_CELL;
    public int Ky0 => y0 / Roads.ROAD_CELL;
    public int KW => (int)Math.Ceiling(x1 / (double)Roads.ROAD_CELL) - Kx0;
    public int KH => (int)Math.Ceiling(y1 / (double)Roads.ROAD_CELL) - Ky0;

    /// <summary>The smallest window holding both (for a road between two regions); its edges are the map's where either's are.</summary>
    public static Win Union(Win a, Win b) => new(Math.Min(a.x0, b.x0), Math.Min(a.y0, b.y0), Math.Max(a.x1, b.x1), Math.Max(a.y1, b.y1),
        a.edgeL && b.edgeL, a.edgeT && b.edgeT, a.edgeR && b.edgeR, a.edgeB && b.edgeB, -1);
}
