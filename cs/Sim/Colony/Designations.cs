// Ported from src/sim/designations.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Designations (blueprints): what the player wants built or removed.
// Each blueprint gets one job at a time from Jobs.cs: a haul job while it
// still needs material, then a build job.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Designations
{

    /// <summary>Build targets that are furniture rather than a tile.</summary>
    public static readonly List<string> FURNITURE_TARGETS = ["bed", "table", "workbench", "stove", "trap", "tower", "well", "collector", "generator", "lamp", "fridge", "heater", "coop", "radio", "lab", "beacon", "turret", "lure", "pit"];

    public static bool isFurnitureTarget(string target)
    {
        return (FURNITURE_TARGETS).includes(target);
    }

    /// <summary>Ticks of work (10 ticks = 1s at 1x).</summary>
    public static readonly JsRecord<double> BUILD_WORK = new() { ["wall"] = 30, ["door"] = 20, ["barricade"] = 15, ["fence"] = 30, ["bed"] = 25, ["table"] = 25, ["workbench"] = 40, ["stove"] = 40, ["trap"] = 20, ["tower"] = 60, ["well"] = 120, ["collector"] = 40, ["generator"] = 80, ["lamp"] = 20, ["fridge"] = 60, ["heater"] = 30, ["coop"] = 60, ["radio"] = 60, ["lab"] = 100, ["beacon"] = 200, ["turret"] = 80, ["lure"] = 30, ["pit"] = 50, ["reinforce"] = 40, ["floor"] = 20 };

    /// <summary>What a blueprint needs delivered: scrap to reinforce, wood for everything else.</summary>
    public static string materialFor(string target)
    {
        return (target == "reinforce") ? "scrap" : "wood";
    }

    /// <summary>How many of it (materialFor) a blueprint needs: two wood for a tall fence, one for everything else.
    /// Blueprint.got counts the loads in while more than one is needed.</summary>
    public static int materialCount(string target) => target == "fence" ? 2 : 1;

    /// <summary>A load delivered to a blueprint: delivered once all of them are in.</summary>
    public static void deliver(Blueprint bp)
    {
        int n = materialCount(bp.target);
        if (n > 1) bp.got = (bp.got ?? 0) + 1;
        bp.delivered = n <= 1 || bp.got >= n;
    }

    public static Blueprint? getBlueprint(SimState state, string id)
    {
        return state.blueprints.find((b) => b.id == id);
    }

    public static Blueprint? blueprintAt(SimState state, int x, int y)
    {
        return state.blueprints.find((b) => (b.x == x) && (b.y == y));
    }
    public static Blueprint? blueprintAt(SimState state, double x, double y) => blueprintAt(state, Js.I(x), Js.I(y));

    /// <summary>Can something be removed here?</summary>
    public static bool hasRemovable(SimState state, int x, int y)
    {
        var tile = Grids.getTile(state.grid, x, y);
        return (((tile == "wall") || (tile == "door")) || (tile == "barricade")) || (FurnitureMod.furnitureAt(state, x, y) != null);
    }
    public static bool hasRemovable(SimState state, double x, double y) => hasRemovable(state, Js.I(x), Js.I(y));

    /// <summary>Building needs empty floor (no furniture); removing needs something to remove.</summary>
    public static bool preconditionHolds(SimState state, Blueprint bp)
    {
        if (Js.T(bp.side))
        {
            var e = Grids.edgeIndex(state.grid, bp.x, bp.y, bp.side);
            if ((bp.target == "barricade") && Grids.isWindow(Grids.edgeKind(state.grid, e)))
            {
                return !state.grid.edgeBoarded.includes(e);
            }
            return Grids.edgeKind(state.grid, e) == Grids.EDGE_NONE;
        }
        if (bp.target == "floor")
        {
            return hasRemovable(state, bp.x, bp.y);
        }
        if (bp.target == "reinforce")
        {
            return Breach.isStructure(Grids.getTile(state.grid, bp.x, bp.y)) && !state.grid.reinforced.includes(((double)(bp.y) * (state.grid.width)) + bp.x);
        }
        return (Grids.getTile(state.grid, bp.x, bp.y) == "floor") && !Js.T(FurnitureMod.furnitureAt(state, bp.x, bp.y));
    }

    public static Blueprint addBlueprint(SimState state, int x, int y, string target, string? side = default)
    {
        Blueprint bp = new Blueprint { id = $"b{Js.Str(state.nextId++)}", x = Js.I(x), y = Js.I(y), target = target, needsMaterial = target != "floor", delivered = false, workDone = 0, workRequired = Js.Get(BUILD_WORK, target) };
        if (Js.T(side))
        {
            bp.side = side;
        }
        state.blueprints.push(bp);
        return bp;
    }
    public static Blueprint addBlueprint(SimState state, double x, double y, string target, string? side = default) => addBlueprint(state, Js.I(x), Js.I(y), target, side);

    /// <summary>Cancels a blueprint. Delivered material comes back as an item. Its job is dropped by reconcileJobs.</summary>
    public static void cancelBlueprint(SimState state, Blueprint bp)
    {
        var i = state.blueprints.indexOf(bp);
        if (i >= 0)
        {
            state.blueprints.splice(i, 1);
        }
        var back = bp.delivered ? materialCount(bp.target) : (bp.got ?? 0);
        for (int k = 0; k < back; k++)
        {
            Items.spawnItem(state, materialFor(bp.target), bp.x, bp.y);
        }
    }

    /// <summary>Finishes construction: changes the tile or furniture and clears anything in the way.</summary>
    public static void completeBlueprint(SimState state, Blueprint bp)
    {
        var i = state.blueprints.indexOf(bp);
        if (i >= 0)
        {
            state.blueprints.splice(i, 1);
        }
        if ((Js.T(bp.side) && (bp.target == "barricade")) && Grids.isWindow(Grids.edgeKind(state.grid, Grids.edgeIndex(state.grid, bp.x, bp.y, bp.side))))
        {
            var e = Grids.edgeIndex(state.grid, bp.x, bp.y, bp.side);
            if (!state.grid.edgeBoarded.includes(e))
            {
                state.grid.edgeBoarded.push(e);
            }
            Grids.noteEdgeChange(state.grid, e);
            state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
            return;
        }
        if (Js.T(bp.side) && (((bp.target == "wall") || (bp.target == "door")) || (bp.target == "barricade") || (bp.target == "fence")))
        {
            if (Grids.setEdge(state.grid, bp.x, bp.y, bp.side, (bp.target == "wall") ? Grids.EDGE_WALL : ((bp.target == "door") ? Grids.EDGE_DOOR : ((bp.target == "fence") ? Grids.EDGE_FENCE : Grids.EDGE_BARRICADE))))
            {
                state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
            }
            return;
        }
        Growth.clear(state, bp.x, bp.y);
        switch (bp.target)
        {
            case "wall":
            case "door":
            case "barricade":
                {
                    if (Grids.setTile(state.grid, bp.x, bp.y, bp.target))
                    {
                    state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
                }
                    if (bp.target == "wall")
                    {
                    evictFrom(state, bp);
                }
                    Zombies.evictZombies(state, bp);
                    Farming.clearPlot(state, bp);
                    return;
                }
            case "bed":
            case "table":
            case "workbench":
            case "stove":
            case "trap":
            case "tower":
            case "well":
            case "collector":
            case "generator":
            case "lamp":
            case "fridge":
            case "heater":
            case "coop":
            case "radio":
            case "lab":
            case "beacon":
            case "turret":
            case "lure":
            case "pit":
                {
                    {
                    FurnitureMod.addFurniture(state, bp.target, bp.x, bp.y);
                    var item = Items.itemAt(state, bp.x, bp.y);
                    if (Js.T(item))
                    {
                        Items.dropItem(state, item, bp);
                    }
                    return;
                }
                }
            case "reinforce":
                {
                    {
                    var i_1 = ((double)(bp.y) * (state.grid.width)) + bp.x;
                    if (!state.grid.reinforced.includes(i_1))
                    {
                        state.grid.reinforced.push(i_1);
                    }
                    return;
                }
                }
            case "floor":
                {
                    {
                    var f = FurnitureMod.furnitureAt(state, bp.x, bp.y);
                    if (Js.T(f))
                    {
                        FurnitureMod.removeFurniture(state, f);
                    }
                    else if (Grids.setTile(state.grid, bp.x, bp.y, "floor"))
                    {
                        state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
                    }
                    Items.spawnItem(state, "wood", bp.x, bp.y);
                    return;
                }
                }
        }
    }

    /// <summary>Moves survivors and items off a tile that just became solid.</summary>
    public static void evictFrom(SimState state, Point tile)
    {
        foreach (var s in Js.Iter(state.survivors))
        {
            var onIt = Space.samePoint(s.pos, tile) || ((s.move != null) && Space.samePoint(s.move.to, tile));
            if (!Js.T(onIt))
            {
                continue;
            }
            var to = Space.nearestTile(state.grid, tile, (x, y) => Grids.isWalkable(state.grid, x, y));
            if (!Js.T(to))
            {
                continue;
            }
            s.pos = to;
            s.move = null;
            s.path = [];
            s.pathVersion = -1;
        }
        var item = Items.itemAt(state, tile.x, tile.y);
        if (Js.T(item))
        {
            Items.dropItem(state, item, tile);
        }
    }
}
