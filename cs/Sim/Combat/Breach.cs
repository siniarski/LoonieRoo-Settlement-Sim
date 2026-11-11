// Ported from src/sim/breach.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Breaching: zombies batter the doors and walls between them and survivors.
//
// Damage is kept per tile in `grid.damage` (same indexing as tiles; only walls
// and doors carry any). At STRUCTURE_HEALTH the structure breaks: the tile
// becomes floor, and a blueprint to rebuild it is left in its place. Damaged
// structures get repair jobs (build type, see Jobs.cs), done from next door.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Breach
{

    /// <summary>Damage a structure takes before it breaks. Zombies hit for 40, every 10 ticks.</summary>
    public static readonly JsRecord<double> STRUCTURE_HEALTH = new() { ["door"] = 400, ["wall"] = 1200, ["barricade"] = 600, ["fence"] = 900 };

    /// <summary>Damage a repairer removes per tick of work: half a zombie's rate, so repairs slow a breach but can't stop it alone.</summary>
    public const int REPAIR_PER_TICK = 2;

    /// <summary>What it takes to break this structure (reinforced ones take more; Defences.cs).</summary>
    public static double structureHealth(Grid grid, int i)
    {
        var tile = Js.NN(Grids.tileAt(grid, i));
        if (!isStructure(tile))
        {
            return 0;
        }
        return grid.reinforced.includes(i) ? ((double)(Js.Get(STRUCTURE_HEALTH, tile)) * (Defences.REINFORCE_FACTOR)) : Js.Get(STRUCTURE_HEALTH, tile);
    }
    public static double structureHealth(Grid grid, double i) => structureHealth(grid, Js.I(i));

    public static bool isStructure(string? tile)
    {
        return ((tile == "wall") || (tile == "door")) || (tile == "barricade");
    }

    public static double structureDamage(SimState state, int x, int y)
    {
        return isStructure(Grids.getTile(state.grid, x, y)) ? Js.NN(Grids.damageAt(state.grid, Grids.tileIndex(state.grid, x, y))) : 0;
    }
    public static double structureDamage(SimState state, double x, double y) => structureDamage(state, Js.I(x), Js.I(y));

    /// <summary>Hits the wall or door at (x, y). Returns true if it broke.</summary>
    public static bool damageStructure(SimState state, int x, int y, double amount)
    {
        var tile = Grids.getTile(state.grid, x, y);
        if (!isStructure(tile))
        {
            return false;
        }
        var i = Grids.tileIndex(state.grid, x, y);
        Grids.setDamage(state.grid, i, Js.NN(Grids.damageAt(state.grid, i)) + amount);
        if (Js.NN(Grids.damageAt(state.grid, i)) < structureHealth(state.grid, i))
        {
            return false;
        }
        Grids.setTile(state.grid, x, y, "floor");
        state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
        var planned = Designations.blueprintAt(state, x, y);
        if (Js.T(planned))
        {
            Designations.cancelBlueprint(state, planned);
        }
        Designations.addBlueprint(state, x, y, tile);
        Events.logEvent(state, $"Zombies broke through the {Js.Str(tile)} at ({Js.Str(x)}, {Js.Str(y)})");
        return true;
    }
    public static bool damageStructure(SimState state, double x, double y, double amount) => damageStructure(state, Js.I(x), Js.I(y), amount);

    /// <summary>Undoes up to `amount` damage. Returns the damage left.</summary>
    public static double repairStructure(SimState state, int x, int y, double amount)
    {
        if (!isStructure(Grids.getTile(state.grid, x, y)))
        {
            return 0;
        }
        var i = Grids.tileIndex(state.grid, x, y);
        Grids.setDamage(state.grid, i, JsMath.max(0, Js.NN(Grids.damageAt(state.grid, i)) - amount));
        return Js.NN(Grids.damageAt(state.grid, i));
    }
    public static double repairStructure(SimState state, double x, double y, double amount) => repairStructure(state, Js.I(x), Js.I(y), amount);

    /// <summary>What it takes to break an edge wall or door: the same as the tile kinds.</summary>
    public static double edgeHealth(Grid grid, int e)
    {
        var kind = Grids.edgeKind(grid, e);
        if (grid.edgeBoarded.includes(e))
        {
            return BOARDS_HEALTH;
        }
        if (kind == Grids.EDGE_WINDOW)
        {
            return GLASS_HEALTH;
        }
        return (kind == Grids.EDGE_WALL) ? STRUCTURE_HEALTH["wall"] : ((kind == Grids.EDGE_DOOR) ? STRUCTURE_HEALTH["door"] : ((kind == Grids.EDGE_BARRICADE) ? STRUCTURE_HEALTH["barricade"] : ((kind == Grids.EDGE_FENCE) ? STRUCTURE_HEALTH["fence"] : 0)));
    }
    public static double edgeHealth(Grid grid, double e) => edgeHealth(grid, Js.I(e));

    public static double edgeDamage(Grid grid, int e)
    {
        return Js.Q(Js.Get(grid.edgeDamage, Js.Str(e)), 0);
    }
    public static double edgeDamage(Grid grid, double e) => edgeDamage(grid, Js.I(e));

    /// <summary>Hits an edge wall or door. Returns true if it broke (it's gone, and the way is open).</summary>
    public static bool damageEdge(SimState state, int e, double amount)
    {
        var __d0 = state;
        var grid = __d0.grid;
        var kind = Grids.edgeKind(grid, e);
        var boarded = grid.edgeBoarded.includes(e);
        if (((((kind != Grids.EDGE_WALL) && (kind != Grids.EDGE_DOOR)) && (kind != Grids.EDGE_BARRICADE)) && (kind != Grids.EDGE_WINDOW)) && (kind != Grids.EDGE_FENCE) && !boarded)
        {
            return false;
        }
        var damage = edgeDamage(grid, e) + amount;
        if (damage < edgeHealth(grid, e))
        {
            grid.edgeDamage[Js.Idx(Js.Str(e))] = damage;
            return false;
        }
        if (boarded || (kind == Grids.EDGE_WINDOW))
        {
            grid.edgeDamage.Remove(Js.Str(Js.Str(e)));
            var tile_1 = JsMath.floor(((double)(e) / (2)));
            var x = tile_1 % grid.width;
            var y = JsMath.floor(((double)(tile_1) / (grid.width)));
            if (boarded)
            {
                grid.edgeBoarded.splice(grid.edgeBoarded.indexOf(e), 1);
            }
            else
            {
                smashGlass(state, e);
            }
            Grids.noteEdgeChange(grid, e);
            state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
            Events.logEvent(state, ((((("Zombies " + (boarded ? "tore the boards off" : "smashed")) + " the window at (") + x) + ", ") + y) + ")");
            return true;
        }
        Grids.putEdge(grid, e, Grids.EDGE_NONE);
        Grids.noteEdgeChange(grid, e);
        grid.edgeDamage.Remove(Js.Str(Js.Str(e)));
        var open = grid.edgeOpen.indexOf(e);
        if (open >= 0)
        {
            grid.edgeOpen.splice(open, 1);
        }
        var passing = grid.edgePassing.indexOf(e);
        if (passing >= 0)
        {
            grid.edgePassing.splice(passing, 1);
        }
        state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
        var tile = JsMath.floor(((double)(e) / (2)));
        Events.logEvent(state, ((((("Zombies broke through the " + ((kind == Grids.EDGE_WALL) ? "wall" : ((kind == Grids.EDGE_DOOR) ? "door" : ((kind == Grids.EDGE_FENCE) ? "fence" : "barricade")))) + " at (") + (tile % grid.width)) + ", ") + JsMath.floor(((double)(tile) / (grid.width)))) + ")");
        return true;
    }
    public static bool damageEdge(SimState state, double e, double amount) => damageEdge(state, Js.I(e), amount);

    /// <summary>Undoes up to `amount` damage on an edge. Returns the damage left.</summary>
    public static double repairEdge(SimState state, int e, double amount)
    {
        var left = JsMath.max(0, edgeDamage(state.grid, e) - amount);
        if (left == 0)
        {
            state.grid.edgeDamage.Remove(Js.Str(Js.Str(e)));
        }
        else
        {
            state.grid.edgeDamage[Js.Idx(Js.Str(e))] = left;
        }
        return left;
    }
    public static double repairEdge(SimState state, double e, double amount) => repairEdge(state, Js.I(e), amount);

    /// <summary>Glass breaks at this much damage; boards over a window at this.</summary>
    public const int GLASS_HEALTH = 100;

    public const int BOARDS_HEALTH = 400;

    /// <summary>The glass in a window goes: loud, and the frame can be climbed through.</summary>
    public static void smashGlass(SimState state, int e)
    {
        var __d1 = state;
        var grid = __d1.grid;
        if (Grids.edgeKind(grid, e) != Grids.EDGE_WINDOW)
        {
            return;
        }
        Grids.putEdge(grid, e, Grids.EDGE_WINDOW_BROKEN);
        Grids.noteEdgeChange(grid, e);
        state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
        var tile = JsMath.floor(((double)(e) / (2)));
        Noise.makeNoise(state, new Point { x = Js.I(tile % grid.width), y = Js.I(JsMath.floor(((double)(tile) / (grid.width)))) }, "glass");
    }
    public static void smashGlass(SimState state, double e) => smashGlass(state, Js.I(e));
}
