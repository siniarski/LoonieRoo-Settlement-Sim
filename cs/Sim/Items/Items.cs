// Ported from src/sim/items.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Items: the things of the world, one list in state. An item is on the ground (`pos`), carried (`carriedBy`: in a
// hand, worn, or in someone's bag) or `inside` a container (a crate, a car's boot, a coat's pockets), never two of
// these; where it lies in a container's grid, and stacks, are InventoryGrid.cs.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Items
{

    public static Item? getItem(SimState state, string id)
    {
        return Lookup.findById(state.items, id);
    }

    /// <summary>Items of this kind lying on the ground (not carried or stored) that nobody has reserved, in list order.</summary>
    public static List<Item> freeOfKind(SimState state, string kind) => state.items.filter((i) => ((i.kind == kind) && (i.pos != null)) && (i.reservedBy == null));

    public static Item? itemAt(SimState state, int x, int y)
    {
        return state.items.find((i) => ((i.pos != null) && (i.pos.x == x)) && (i.pos.y == y));
    }
    public static Item? itemAt(SimState state, double x, double y) => itemAt(state, Js.I(x), Js.I(y));

    /// <summary>A tile an item can be dropped on: walkable, empty, no furniture, and not about to become a wall or furniture.</summary>
    public static bool canHoldItem(SimState state, int x, int y)
    {
        return ((Grids.isWalkable(state.grid, x, y) && !Js.T(itemAt(state, x, y))) && !Js.T(FurnitureMod.furnitureAt(state, x, y))) && !state.blueprints.some((b) => ((b.x == x) && (b.y == y)) && ((b.target == "wall") || Designations.isFurnitureTarget(b.target)));
    }
    public static bool canHoldItem(SimState state, double x, double y) => canHoldItem(state, Js.I(x), Js.I(y));

    /// <summary>Creates an item on the nearest free tile to (x, y).</summary>
    public static Item spawnItem(SimState state, string kind, int x, int y)
    {
        // (A request for the old generic "weapon" or "gun" still makes one: WeaponTable.cs reads it as a specific weapon.)
        Item item = new Item { id = $"i{Js.Str(state.nextId++)}", kind = kind, pos = null, carriedBy = null, reservedBy = null, wear = Weapons.ammoOfKind(kind)?.box ?? 0, tag = null };
        if (kind == "book")
        {
            item.tag = Books.bookSubject(item.id);
        }
        state.items.push(item);
        placeNear(state, item, new Point { x = Js.I(x), y = Js.I(y) });
        return item;
    }
    public static Item spawnItem(SimState state, string kind, double x, double y) => spawnItem(state, kind, Js.I(x), Js.I(y));

    /// <summary>Puts an item on the ground at the nearest free tile and clears carry/reservation.</summary>
    public static void dropItem(SimState state, Item item, Point near)
    {
        Inventory.unplace(item);
        item.carriedBy = null;
        item.reservedBy = null;
        placeNear(state, item, near);
    }

    public static void removeItem(SimState state, Item item)
    {
        Inventory.forgetQuick(state, item); // its quick slot passes to another of its kind
        Inventory.spill(state, item); // what was in its pockets stays behind
        var i = state.items.indexOf(item);
        if (i >= 0)
        {
            state.items.splice(i, 1);
        }
    }

    public static void placeNear(SimState state, Item item, Point near)
    {
        item.pos = null;
        var held = Js.Set(state.items.flatMap((i) => Js.T(i.pos) ? ((List<string>)[$"{Js.Str(i.pos.x)},{Js.Str(i.pos.y)}"]) : ((List<string>)[])));
        bool free(double x, double y)
        {
            return ((Grids.isWalkable(state.grid, x, y) && !held.has($"{Js.Str(x)},{Js.Str(y)}")) && !Js.T(FurnitureMod.furnitureAt(state, x, y))) && !state.blueprints.some((b) => ((b.x == x) && (b.y == y)) && ((b.target == "wall") || Designations.isFurnitureTarget(b.target)));
        }
        item.pos = Space.nearestTile(state.grid, near, (__f0, __f1) => free(__f0, __f1)) ?? new Point { x = Js.I(near.x), y = Js.I(near.y) };
    }
}
