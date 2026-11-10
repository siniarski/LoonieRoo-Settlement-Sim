// Towns by coarse cell (2026-11-09, docs/worldgen-speed.md step 4): for the loops that asked every town about one
// place (Roads.costGrid, Worldplan.farmLots, Heights.townness). A world has a town per 170 x 170 tiles, so those
// loops grew with towns x area. Near() gives the towns whose centre is within a reach, in list order (a superset
// of what the callers' own tests pick out, so their answers, and the first match in order, are the same).
#nullable disable warnings
namespace LoonieRoo.Sim;

public sealed class TownIndex
{
    const int CELL = 64;
    readonly List<Town> towns;
    readonly int cells;
    readonly List<int>[] byCell;

    public TownIndex(List<Town> towns, double size)
    {
        this.towns = towns;
        cells = (int)(size / CELL) + 1;
        byCell = new List<int>[cells * cells];
        for (int i = 0; i < towns.Count; i++)
        {
            var t = towns[i];
            int at = Cell(t.y) * cells + Cell(t.x);
            (byCell[at] ??= new()).Add(i);
        }
    }

    int Cell(double v) => Math.Clamp((int)Math.Floor(v / CELL), 0, cells - 1);

    /// <summary>The indexes of towns with their centre within `reach` (tiles, a box) of (x, y), ascending.</summary>
    public List<int> Near(double x, double y, double reach, List<int> into)
    {
        into.Clear();
        int x0 = Cell(x - reach), x1 = Cell(x + reach), y0 = Cell(y - reach), y1 = Cell(y + reach);
        for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
                if (byCell[cy * cells + cx] is { } list) into.AddRange(list);
        if (into.Count > 1) into.Sort();
        return into;
    }

    /// <summary>The first town in list order, of those within `reach`, that `pick` accepts (as towns.find would), or null.</summary>
    public Town Find(double x, double y, double reach, Func<Town, bool> pick, List<int> scratch)
    {
        foreach (var i in Near(x, y, reach, scratch))
            if (pick(towns[i])) return towns[i];
        return null;
    }

    public Town this[int i] => towns[i];

    /// <summary>The largest town radius (tiles), for working out a reach.</summary>
    public double Biggest => towns.Count == 0 ? 0 : towns.Max(t => t.radius);
}
