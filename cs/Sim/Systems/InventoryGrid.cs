// The inventory as grids (2026-11-10). Every container has a grid of cells and every item a footprint in
// cells (ITEM_SIZE in Inventory.cs, beside the weights: a bundle of planks 2x4, a tin 1x1), which may lie a
// quarter turn round. Small things of a kind stack in one cell up to the kind's limit.
//
//   grids      your bag (BAG_W x BAG_H, always there); what has pockets (ITEM_GRID: a coat's pockets, a vest's
//              pouches, a body's pockets), worn, in your bag, in a crate or on the ground; a crate (CRATE_GRID);
//              a car's boot (BOOT_GRID, TRUCK_GRID). A grid is named by a "ref": BAG, or the id of the
//              furniture, vehicle or item that has it. GROUND is the floor beside you, not a grid in the sim.
//   where      in one of your grids an item is carried (carriedBy, in survivor.pack) and `grid` names the
//              container item it's in (null: the bag); in any other grid it's `inside` its container.
//              Either way gx, gy are its top-left cell and `turned` says it lies a quarter turn round.
//   nesting    a container item keeps what's in it wherever it goes: worn, in your bag, in a crate, on the
//              ground (follow). One level deep: a container item never goes into another one's grid, so
//              nothing goes inside itself. Removed (worn out, rotted away), it spills what it held (spill).
//   layout     what's in a grid and where. Things not placed yet (an old save's bag, a search's finds) are
//              laid out by first-fit in item order, under the grid if they don't fit (an overstuffed crate
//              grows rows until it's emptied): pure and deterministic, so the screen shows it before
//              anything's written, and every move settles the grids it touches first.
//   first-fit  a stack of the kind with room, else the first free cell row by row, left to right, as the
//              thing lies, then turned. Picking up (stow), stashing and the scripted player all use it.
//   weight     a second limit, as before: PACK_MAX on everything in your grids, a crate's or boot's load
//              (Inventory.containerOf), and a coat's pockets count towards whoever holds the coat.
//   quick      keys 1 to 9 use survivor.quick, a row of nine slots you assign (setQuick); picking up a
//              usable kind none of them has fills the first empty one; a slot whose thing is gone uses
//              another of its kind. An old save (quick null) keeps the bag's first nine until then.
#nullable disable warnings
namespace LoonieRoo.Sim;

/// <summary>A stack laid out in a grid: one or more things of a kind on one cell, or one bigger thing.</summary>
public sealed class Placed
{
    public List<Item> items = new();
    public int x;
    public int y;
    public int w;
    public int h;
    public bool turned;
    public Item first => items[0];
}

public static partial class Inventory
{
    /// <summary>Your bag's ref, and the floor beside you (a drop, not a grid).</summary>
    public const string BAG = "bag";
    public const string GROUND = "ground";

    /// <summary>The bag: 8 x 6 cells holds 12 kg of anything as dense as a coat (2.5 kg a cell, 6 cells).</summary>
    public const int BAG_W = 8;
    public const int BAG_H = 6;

    /// <summary>A crate (any container furniture: a shelf, a fridge), a car's boot, a truck's.</summary>
    public static readonly (int w, int h) CRATE_GRID = (8, 8);
    public static readonly (int w, int h) BOOT_GRID = (10, 6);
    public static readonly (int w, int h) TRUCK_GRID = (12, 10);

    /// <summary>Things with a grid of their own, what it's called, and its size.</summary>
    public static readonly JsRecord<(int w, int h, string name)> ITEM_GRID = new()
    {
        ["coat"] = (2, 2, "pockets"),
        ["armour"] = (3, 1, "pouches"),
        ["corpse"] = (3, 3, "pockets"),
    };

    /// <summary>Kinds a quick slot takes on its own when you pick one up (things with a use from the bag).</summary>
    public static readonly List<string> QUICK_KINDS = ["food", "meal", "bandage", "medicine", "medkit", "weapon", "gun", "axe", "hammer", "flashlight"];

    /// <summary>Whether a quick slot takes this kind on its own: the list above, and every weapon you can wield (WeaponTable.cs).</summary>
    public static bool isQuickKind(string kind) => QUICK_KINDS.includes(kind) || Weapons.isWieldKind(kind);

    public const int QUICK_SLOTS = 9;

    // --- sizes ------------------------------------------------------------------------------------

    public static (int w, int h, int stack) sizeOf(Item item)
    {
        if ((item.kind == "weapon") && NAME_SIZE.TryGetValue(Items.nameOf(item), out var named))
        {
            return named;
        }
        return ITEM_SIZE.TryGetValue(item.kind, out var size) ? size : (1, 1, 1);
    }

    /// <summary>Cells across and down as it lies.</summary>
    public static (int w, int h) footprint(Item item, bool turned)
    {
        var (w, h, _) = sizeOf(item);
        return turned ? (h, w) : (w, h);
    }

    public static bool stackable(Item item)
    {
        var (w, h, n) = sizeOf(item);
        return (w == 1) && (h == 1) && (n > 1);
    }

    public static bool hasGrid(string kind) => ITEM_GRID.has(kind);

    /// <summary>Weight in tenths of a kilo (Infinity for a cart or a body).</summary>
    public static double weightOf(Item item) => Js.Get(ITEM_WEIGHT, item.kind);

    // --- which grid ---------------------------------------------------------------------------------

    /// <summary>In your bag, or worn (a slot).</summary>
    public static bool yours(Survivor s, Item item)
    {
        return (item.carriedBy == s.id) && (s.pack.includes(item.id) || (slotOf(s, item.id) != null));
    }

    /// <summary>A container item of yours, worn or in your bag: its grid is one of yours.</summary>
    static bool carriedContainer(SimState state, Survivor s, string id)
    {
        var item = Items.getItem(state, id);
        return Js.T(item) && hasGrid(item.kind) && yours(s, item);
    }

    /// <summary>The grid an item is in (BAG, a container's id), or null (on the ground, worn, in your hands).</summary>
    public static string? gridOf(SimState state, Survivor s, Item item)
    {
        if ((item.carriedBy == s.id) && s.pack.includes(item.id))
        {
            return (Js.T(item.grid) && (item.grid != item.id) && carriedContainer(state, s, item.grid)) ? item.grid : BAG;
        }
        return item.inside;
    }

    /// <summary>Your grids: the bag, then what you wear that has pockets (in SLOTS order), then what's in your bag that has.</summary>
    public static List<string> yourGrids(SimState state, Survivor s)
    {
        List<string> refs = [BAG];
        foreach (var slot in Js.Iter(SLOTS))
        {
            var id = Js.GetProp<string?>(s, slot);
            var item = Js.T(id) ? Items.getItem(state, id) : null;
            if (Js.T(item) && hasGrid(item.kind))
            {
                refs.push(item.id);
            }
        }
        foreach (var id in Js.Iter(s.pack))
        {
            var item = Items.getItem(state, id);
            if (Js.T(item) && hasGrid(item.kind) && (gridOf(state, s, item) == BAG))
            {
                refs.push(item.id);
            }
        }
        return refs;
    }

    /// <summary>A container's grid and the container items in it (a coat in a crate), in item order.</summary>
    public static List<string> openGrids(SimState state, string containerId)
    {
        List<string> refs = [containerId];
        foreach (var item in Js.Iter(state.items))
        {
            if ((item.inside == containerId) && hasGrid(item.kind))
            {
                refs.push(item.id);
            }
        }
        return refs;
    }

    /// <summary>A grid's size in cells (before an overstuffed one grows), or null if `gridRef` has none.</summary>
    public static (int w, int h)? gridSize(SimState state, string gridRef)
    {
        if (gridRef == BAG)
        {
            return (BAG_W, BAG_H);
        }
        var item = Items.getItem(state, gridRef);
        if (Js.T(item))
        {
            return ITEM_GRID.TryGetValue(item.kind, out var g) ? (g.w, g.h) : null;
        }
        var f = FurnitureMod.getFurniture(state, gridRef);
        if (Js.T(f))
        {
            return (f.kind == "crate") ? CRATE_GRID : null;
        }
        var v = Vehicles.vehiclesOf(state).find((x) => x.id == gridRef);
        return Js.T(v) ? ((v.kind == "truck") ? TRUCK_GRID : BOOT_GRID) : null;
    }

    /// <summary>What a grid is called on screen: "Bag", "Parka (pockets)", "Kitchen counter", "Car's boot".</summary>
    public static string gridName(SimState state, string gridRef)
    {
        if (gridRef == BAG)
        {
            return "Bag";
        }
        var item = Items.getItem(state, gridRef);
        if (Js.T(item))
        {
            return $"{capitalise(Items.nameOf(item))} ({ITEM_GRID[item.kind].name})";
        }
        var f = FurnitureMod.getFurniture(state, gridRef);
        if (Js.T(f))
        {
            return capitalise(FurnitureMod.containerName(f));
        }
        var v = Vehicles.vehiclesOf(state).find((x) => x.id == gridRef);
        return (v?.kind == "truck") ? "Truck's boot" : "Car's boot";
    }

    static string capitalise(string s) => (s.Length == 0) ? s : (char.ToUpperInvariant(s[0]) + s[1..]);

    /// <summary>The things in a grid, in item order (your grids in bag order).</summary>
    public static List<Item> itemsIn(SimState state, Survivor? s, string gridRef)
    {
        if (Js.T(s) && ((gridRef == BAG) || carriedContainer(state, s, gridRef)))
        {
            List<Item> mine = [];
            foreach (var id in Js.Iter(s.pack))
            {
                var item = Items.getItem(state, id);
                if (Js.T(item) && (gridOf(state, s, item) == gridRef))
                {
                    mine.push(item);
                }
            }
            return mine;
        }
        return state.items.filter((i) => i.inside == gridRef);
    }

    // --- layout -------------------------------------------------------------------------------------

    /// <summary>Which stack covers each cell: `w` across, rows added as needed.</summary>
    sealed class Cells
    {
        public readonly int w;
        readonly List<Placed?> cells = new();

        public Cells(int w)
        {
            this.w = w;
        }

        public int rows => cells.Count / w;

        public Placed? at(int x, int y)
        {
            if ((x < 0) || (x >= w) || (y < 0))
            {
                return null;
            }
            var i = (y * w) + x;
            return (i < cells.Count) ? cells[i] : null;
        }

        public bool free(int x, int y, int fw, int fh, int maxRows)
        {
            if ((x < 0) || (y < 0) || ((x + fw) > w) || ((y + fh) > maxRows))
            {
                return false;
            }
            for (var yy = y; yy < (y + fh); yy++)
            {
                for (var xx = x; xx < (x + fw); xx++)
                {
                    if (at(xx, yy) != null)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public void mark(Placed p, Placed? with)
        {
            while (cells.Count < ((p.y + p.h) * w))
            {
                cells.Add(null);
            }
            for (var yy = p.y; yy < (p.y + p.h); yy++)
            {
                for (var xx = p.x; xx < (p.x + p.w); xx++)
                {
                    cells[(yy * w) + xx] = with;
                }
            }
        }
    }

    /// <summary>A grid's stacks: where things were put, then what wasn't placed yet by first-fit (in item order, under
    /// the grid if it's full). Pure: the same state lays out the same way every time. `leaving` are left out afterwards,
    /// so the rest lie where they would with them.</summary>
    public static List<Placed> layout(SimState state, Survivor? s, string gridRef, ICollection<Item>? leaving = null)
    {
        var size = gridSize(state, gridRef) ?? (1, 1);
        var items = itemsIn(state, s, gridRef);
        var cells = new Cells(size.w);
        List<Placed> stacks = [];
        List<Item> loose = [];
        foreach (var item in Js.Iter(items))
        {
            if (!item.gx.HasValue || !item.gy.HasValue)
            {
                loose.push(item);
                continue;
            }
            int x = item.gx.Value, y = item.gy.Value;
            var turned = item.turned == true;
            var on = cells.at(x, y);
            if ((on != null) && (on.x == x) && (on.y == y) && canJoin(on, item))
            {
                on.items.push(item);
                continue;
            }
            var (fw, fh) = footprint(item, turned);
            if (!cells.free(x, y, fw, fh, int.MaxValue))
            {
                loose.push(item);
                continue;
            }
            var p = new Placed { items = [item], x = x, y = y, w = fw, h = fh, turned = turned };
            stacks.push(p);
            cells.mark(p, p);
        }
        foreach (var item in Js.Iter(loose))
        {
            var spot = fit(cells, stacks, item, int.MaxValue, true);
            if (spot.join != null)
            {
                spot.join.items.push(item);
                continue;
            }
            var p = new Placed { items = [item], x = spot.x, y = spot.y, w = spot.w, h = spot.h, turned = spot.turned };
            stacks.push(p);
            cells.mark(p, p);
        }
        if (leaving == null || leaving.Count == 0)
        {
            return stacks;
        }
        // Without what's leaving, everything else stays where it is (where a settle would put it).
        List<Placed> kept = [];
        foreach (var p in Js.Iter(stacks))
        {
            var left = p.items.filter((i) => !leaving.Contains(i));
            if (left.length > 0)
            {
                p.items = left;
                kept.push(p);
            }
        }
        return kept;
    }

    /// <summary>Things laid out loose, `w` across with rows as needed, by first-fit (stacking where they can): the screen's
    /// picture of the ground beside you, which isn't a grid in the sim. Pure.</summary>
    public static List<Placed> packLoose(List<Item> items, int w)
    {
        var cells = new Cells(w);
        List<Placed> stacks = [];
        foreach (var item in Js.Iter(items))
        {
            var spot = fit(cells, stacks, item, int.MaxValue, true);
            if (spot.join != null)
            {
                spot.join.items.push(item);
                continue;
            }
            if (spot.x < 0)
            {
                continue; // wider than the picture
            }
            var p = new Placed { items = [item], x = spot.x, y = spot.y, w = spot.w, h = spot.h, turned = spot.turned };
            stacks.push(p);
            cells.mark(p, p);
        }
        return stacks;
    }

    /// <summary>Rows a grid shows: its own, or more while it's overstuffed.</summary>
    public static int rowsOf(SimState state, List<Placed> stacks, string gridRef)
    {
        var h = gridSize(state, gridRef)?.h ?? 1;
        foreach (var p in Js.Iter(stacks))
        {
            h = Math.Max(h, p.y + p.h);
        }
        return h;
    }

    static bool canJoin(Placed on, Item item)
    {
        return stackable(item) && (on.first.kind == item.kind) && (on.items.Count < sizeOf(item).stack);
    }

    /// <summary>First-fit: a stack of its kind with room (if `join`), else the first free cell row by row, as it lies,
    /// then turned; under `maxRows`, or below the grid when that's unbounded.</summary>
    static (Placed? join, int x, int y, int w, int h, bool turned) fit(Cells cells, List<Placed> stacks, Item item, int maxRows, bool join)
    {
        if (join)
        {
            foreach (var p in Js.Iter(stacks))
            {
                if (canJoin(p, item))
                {
                    return (p, p.x, p.y, p.w, p.h, p.turned);
                }
            }
        }
        var (w, h, _) = sizeOf(item);
        foreach (var turned in (w == h) ? new[] { false } : new[] { false, true })
        {
            var (fw, fh) = turned ? (h, w) : (w, h);
            var limit = (maxRows == int.MaxValue) ? (cells.rows + fh) : maxRows;
            for (var y = 0; (y + fh) <= limit; y++)
            {
                for (var x = 0; (x + fw) <= cells.w; x++)
                {
                    if (cells.free(x, y, fw, fh, limit))
                    {
                        return (null, x, y, fw, fh, turned);
                    }
                }
            }
        }
        return (null, -1, -1, 0, 0, false);
    }

    static Cells cellsOf(SimState state, string gridRef, List<Placed> stacks)
    {
        var cells = new Cells((gridSize(state, gridRef) ?? (1, 1)).w);
        foreach (var p in Js.Iter(stacks))
        {
            cells.mark(p, p);
        }
        return cells;
    }

    /// <summary>Writes a grid's layout into its items, so what was laid out by first-fit stays put.</summary>
    public static void settle(SimState state, Survivor? s, string? gridRef)
    {
        if (!Js.T(gridRef) || (gridRef == GROUND) || (gridSize(state, gridRef) == null))
        {
            return;
        }
        foreach (var p in Js.Iter(layout(state, s, gridRef)))
        {
            foreach (var item in Js.Iter(p.items))
            {
                item.gx = p.x;
                item.gy = p.y;
                item.turned = p.turned ? true : null;
            }
        }
    }

    /// <summary>The stack an item is in (itself first), or just the item when it's in no grid.</summary>
    public static List<Item> stackOf(SimState state, Survivor s, Item item)
    {
        var gridRef = gridOf(state, s, item);
        if (!Js.T(gridRef) || (gridSize(state, gridRef) == null))
        {
            return [item];
        }
        var p = layout(state, s, gridRef).find((q) => q.items.includes(item));
        if (!Js.T(p))
        {
            return [item];
        }
        List<Item> stack = [item];
        foreach (var other in Js.Iter(p.items))
        {
            if (other != item)
            {
                stack.push(other);
            }
        }
        return stack;
    }

    // --- reach and weight ------------------------------------------------------------------------------

    /// <summary>A grid you can put things in or take them from: yours, a container beside you, or a container item
    /// (a coat) on the ground beside you or in a container beside you.</summary>
    public static bool reachable(SimState state, Survivor s, string? gridRef)
    {
        if (!Js.T(gridRef))
        {
            return false;
        }
        if ((gridRef == BAG) || (gridRef == GROUND))
        {
            return true;
        }
        var item = Items.getItem(state, gridRef);
        if (Js.T(item))
        {
            if (!hasGrid(item.kind))
            {
                return false;
            }
            if (item.carriedBy == s.id)
            {
                return yours(s, item);
            }
            if (Js.T(item.pos))
            {
                return Commands.besideYou(s, item.pos);
            }
            return Js.T(item.inside) && !Js.T(Items.getItem(state, item.inside)) && reachable(state, s, item.inside);
        }
        var box = containerOf(state, gridRef);
        return Js.T(box) && Commands.besideYou(s, box.at);
    }

    /// <summary>Something you can lay hands on: carried, worn, on the ground beside you, or in a grid you can reach.</summary>
    public static bool canReach(SimState state, Survivor s, Item item)
    {
        if (item.carriedBy == s.id)
        {
            return true;
        }
        if (Js.T(item.carriedBy))
        {
            return false;
        }
        if (Js.T(item.pos))
        {
            return Commands.besideYou(s, item.pos);
        }
        return reachable(state, s, item.inside);
    }

    /// <summary>Whose weight limit a grid's things count towards: "pack" (yours), a crate's or boot's id, or null (none:
    /// a coat on the ground).</summary>
    static string? weighedBy(SimState state, Survivor s, string gridRef)
    {
        if ((gridRef == BAG) || carriedContainer(state, s, gridRef))
        {
            return "pack";
        }
        var item = Items.getItem(state, gridRef);
        if (Js.T(item))
        {
            return Js.T(item.inside) ? weighedBy(state, s, item.inside) : null;
        }
        return gridRef;
    }

    /// <summary>Where an item's weight counts now (as weighedBy), or null (worn, held, on the ground).</summary>
    static string? weighedNow(SimState state, Survivor s, Item item)
    {
        if ((item.carriedBy == s.id) && s.pack.includes(item.id))
        {
            return "pack";
        }
        return Js.T(item.inside) ? weighedBy(state, s, item.inside) : null;
    }

    /// <summary>A container item's contents (wherever it is).</summary>
    public static List<Item> contentsOf(SimState state, Item container)
    {
        if (!hasGrid(container.kind))
        {
            return [];
        }
        return state.items.filter((i) => (i.inside == container.id) || ((i.grid == container.id) && Js.T(i.carriedBy)));
    }

    /// <summary>The weight `moving` (and what's in them) would add to `owner`'s load: what doesn't count there already.</summary>
    static double addedWeight(SimState state, Survivor s, string owner, IEnumerable<Item> moving)
    {
        double sum = 0;
        foreach (var m in moving)
        {
            if (weighedNow(state, s, m) != owner)
            {
                sum += weightOf(m);
            }
            foreach (var c in Js.Iter(contentsOf(state, m)))
            {
                if (weighedNow(state, s, c) != owner)
                {
                    sum += weightOf(c);
                }
            }
        }
        return sum;
    }

    /// <summary>Why `moving` can't go under a weight limit (yours, or a crate's), or null.</summary>
    static string? tooHeavy(SimState state, Survivor s, string gridRef, IEnumerable<Item> moving, IEnumerable<Item>? leaving = null)
    {
        var owner = weighedBy(state, s, gridRef);
        if (!Js.T(owner))
        {
            return null;
        }
        var added = addedWeight(state, s, owner, moving);
        if (added == 0)
        {
            return null;
        }
        if (added == double.PositiveInfinity)
        {
            var big = moving.First((m) => weightOf(m) == double.PositiveInfinity);
            return (owner == "pack") ? $"a {Js.Str(big.kind)} won't go in a bag" : $"a {Js.Str(big.kind)} won't go in there";
        }
        if (leaving != null)
        {
            added -= leaving.Sum((l) => (weighedNow(state, s, l) == owner) ? (weightOf(l) + contentsOf(state, l).Sum((c) => (weighedNow(state, s, c) == owner) ? weightOf(c) : 0)) : 0);
        }
        if (owner == "pack")
        {
            return ((packWeight(state, s) + added) > PACK_MAX) ? "your bag is too full" : null;
        }
        var box = containerOf(state, owner);
        return (Js.T(box) && ((load(state, owner) + added) > box.max)) ? "it won't fit: too heavy" : null;
    }

    // --- moving things ----------------------------------------------------------------------------------

    /// <summary>Why `moving` (one stack) can't go at (x, y) in a grid as it would lie, or null. Merging onto a stack of
    /// its kind is fine up to the kind's limit (`room` says how many would go).</summary>
    public static string? whyNot(SimState state, Survivor s, List<Item> moving, string gridRef, int x, int y, bool turned, out int room, IEnumerable<Item>? leaving = null)
    {
        room = 0;
        if (moving.length == 0)
        {
            return "nothing to move";
        }
        if (!reachable(state, s, gridRef))
        {
            return "out of reach";
        }
        var size = gridSize(state, gridRef);
        if (!Js.T(size))
        {
            return "not a container";
        }
        var first = moving[0];
        if (moving.some((m) => m.id == gridRef))
        {
            return "it can't go inside itself";
        }
        if (hasGrid(first.kind) && Js.T(Items.getItem(state, gridRef)))
        {
            return $"a {Items.nameOf(first)} won't go inside another";
        }
        if (!moving.every((m) => canReach(state, s, m)))
        {
            return "out of reach";
        }
        var gone = new List<Item>(moving);
        if (leaving != null)
        {
            gone.AddRange(leaving);
        }
        var stacks = layout(state, s, gridRef, gone);
        var cells = cellsOf(state, gridRef, stacks);
        var (fw, fh) = footprint(first, turned);
        var on = cells.at(x, y);
        if ((on != null) && (on.x == x) && (on.y == y) && stackable(first) && (on.first.kind == first.kind))
        {
            room = Math.Min(moving.length, sizeOf(first).stack - on.items.Count);
            if (room <= 0)
            {
                return "that stack is full";
            }
            return tooHeavy(state, s, gridRef, moving.slice(0, room), leaving);
        }
        if ((x < 0) || (y < 0) || ((x + fw) > size.Value.w) || ((y + fh) > size.Value.h))
        {
            return "it doesn't fit there";
        }
        if (!cells.free(x, y, fw, fh, size.Value.h))
        {
            return "something's in the way";
        }
        room = Math.Min(moving.length, sizeOf(first).stack);
        return tooHeavy(state, s, gridRef, moving.slice(0, room), leaving);
    }

    /// <summary>Puts an item at a cell of a grid (yours or a container's), from wherever it is, and brings what's in it along.</summary>
    static void putAt(SimState state, Survivor s, Item item, string gridRef, int x, int y, bool turned)
    {
        var slot = slotOf(s, item.id);
        if (Js.T(slot))
        {
            Js.SetProp(s, slot, null);
        }
        if (s.carrying == item.id)
        {
            s.carrying = null;
        }
        var mine = (gridRef == BAG) || carriedContainer(state, s, gridRef);
        if (!mine && Js.T(item.carriedBy) && (item.carriedBy != s.id) && Survivors.getSurvivor(state, item.carriedBy) is { } bearer)
        {
            unpackFrom(bearer.pack, item.id);
        }
        if (sizeOf(item).w == sizeOf(item).h)
        {
            turned = false;
        }
        item.pos = null;
        item.reservedBy = null;
        item.gx = x;
        item.gy = y;
        item.turned = turned ? true : null;
        if (mine)
        {
            var wasYours = s.pack.includes(item.id);
            item.inside = null;
            item.carriedBy = s.id;
            item.grid = (gridRef == BAG) ? null : gridRef;
            if (!wasYours)
            {
                s.pack.push(item.id);
                autoQuick(state, s, item);
            }
        }
        else
        {
            unpack(s, item.id);
            item.carriedBy = null;
            item.grid = null;
            item.inside = gridRef;
        }
        follow(state, s, item);
    }

    static void unpackFrom(List<string> pack, string id)
    {
        var at = pack.indexOf(id);
        if (at >= 0)
        {
            pack.splice(at, 1);
        }
    }

    /// <summary>What's in a container item goes where it goes: carried with it when it's yours (worn or in your
    /// bag), inside it otherwise (on the ground, in a crate, in someone else's hands).</summary>
    public static void follow(SimState state, Survivor s, Item container)
    {
        if (!hasGrid(container.kind))
        {
            return;
        }
        var carried = yours(s, container);
        foreach (var c in Js.Iter(contentsOf(state, container)))
        {
            if (carried)
            {
                c.inside = null;
                c.pos = null;
                c.carriedBy = s.id;
                c.grid = container.id;
                if (!s.pack.includes(c.id))
                {
                    s.pack.push(c.id);
                }
            }
            else
            {
                if (Js.T(c.carriedBy))
                {
                    var bearer = Survivors.getSurvivor(state, c.carriedBy);
                    if (Js.T(bearer))
                    {
                        unpackFrom(bearer.pack, c.id);
                    }
                }
                c.carriedBy = null;
                c.grid = null;
                c.pos = null;
                c.inside = container.id;
            }
        }
    }

    /// <summary>A container item going out of the world (worn through, rotted away) leaves what was in it where it was:
    /// on the ground, in the crate it was in, or in its bearer's bag.</summary>
    public static void spill(SimState state, Item container)
    {
        if (!hasGrid(container.kind))
        {
            return;
        }
        var bearer = Js.T(container.carriedBy) ? Survivors.getSurvivor(state, container.carriedBy) : null;
        foreach (var c in Js.Iter(contentsOf(state, container)))
        {
            c.gx = null;
            c.gy = null;
            c.turned = null;
            c.grid = null;
            if (Js.T(c.carriedBy))
            {
                continue; // in the bearer's bag now
            }
            if (Js.T(container.inside) && !Js.T(container.pos))
            {
                c.inside = container.inside;
                continue;
            }
            var at = container.pos ?? bearer?.pos;
            c.inside = null;
            if (Js.T(at))
            {
                Items.dropItem(state, c, at);
            }
        }
    }

    /// <summary>Clears an item's place in a grid (it's on the ground, worn, or in someone's hands).</summary>
    public static void unplace(Item item)
    {
        item.gx = null;
        item.gy = null;
        item.turned = null;
        item.grid = null;
    }

    /// <summary>Drops something from your grids or what you wear at your feet; a container item takes its contents with it.</summary>
    public static void dropHere(SimState state, Survivor s, Item item)
    {
        var slot = slotOf(s, item.id);
        if (Js.T(slot))
        {
            Js.SetProp(s, slot, null);
        }
        unpack(s, item.id);
        unplace(item);
        item.inside = null;
        Items.dropItem(state, item, s.pos);
        follow(state, s, item);
    }

    /// <summary>The first spot first-fit gives an item in one of `refs` (join: onto a stack of its kind with room), or null.</summary>
    static (string gridRef, int x, int y, bool turned)? firstFit(SimState state, Survivor s, Item item, List<string> refs)
    {
        foreach (var gridRef in Js.Iter(refs))
        {
            if (!reachable(state, s, gridRef) || (gridRef == item.id) || (hasGrid(item.kind) && Js.T(Items.getItem(state, gridRef))))
            {
                continue;
            }
            var size = gridSize(state, gridRef);
            if (!Js.T(size))
            {
                continue;
            }
            var stacks = layout(state, s, gridRef, [item]);
            var spot = fit(cellsOf(state, gridRef, stacks), stacks, item, size.Value.h, true);
            if (spot.join != null || spot.x >= 0)
            {
                return (gridRef, spot.x, spot.y, spot.turned);
            }
        }
        return null;
    }

    /// <summary>Puts an item into the first of `refs` it fits by first-fit (stacking it where it can), weight allowing.
    /// Returns why not, or null.</summary>
    public static string? placeFirstFit(SimState state, Survivor s, Item item, List<string> refs, string full)
    {
        string? heavy = null;
        List<string> light = [];
        foreach (var gridRef in Js.Iter(refs))
        {
            var why = tooHeavy(state, s, gridRef, [item]);
            if (why == null)
            {
                light.push(gridRef);
            }
            else
            {
                heavy ??= why;
            }
        }
        if (light.length == 0)
        {
            return heavy ?? full;
        }
        var spot = firstFit(state, s, item, light);
        if (!Js.T(spot))
        {
            return full;
        }
        settle(state, s, gridOf(state, s, item));
        settle(state, s, spot.Value.gridRef);
        putAt(state, s, item, spot.Value.gridRef, spot.Value.x, spot.Value.y, spot.Value.turned);
        return null;
    }

    // --- the commands -----------------------------------------------------------------------------------

    /// <summary>moveItem: a stack (or `count` of it) to (x, y) of a grid, as it would lie; onto a stack of its kind it
    /// merges as far as there's room. GROUND drops it. Returns why not, or null.</summary>
    public static string? moveItem(SimState state, Survivor s, Item item, string gridRef, int x, int y, bool turned, int? count)
    {
        if (!canReach(state, s, item))
        {
            return "out of reach";
        }
        var moving = stackOf(state, s, item).slice(0, count ?? int.MaxValue);
        if (moving.length == 0)
        {
            return "nothing to move";
        }
        if (gridRef == GROUND)
        {
            foreach (var m in Js.Iter(moving))
            {
                dropHere(state, s, m);
            }
            return null;
        }
        var why = whyNot(state, s, moving, gridRef, x, y, turned, out var room);
        if (why != null)
        {
            return why;
        }
        var from = gridOf(state, s, item);
        settle(state, s, from);
        settle(state, s, gridRef);
        foreach (var m in Js.Iter(moving.slice(0, room)))
        {
            putAt(state, s, m, gridRef, x, y, turned);
        }
        return null;
    }

    /// <summary>swapItems: two stacks trade places (each lies as it did, or turned if only that fits). Returns why not, or null.</summary>
    public static string? swapItems(SimState state, Survivor s, Item a, Item b)
    {
        if (!canReach(state, s, a) || !canReach(state, s, b))
        {
            return "out of reach";
        }
        var gridA = gridOf(state, s, a);
        var gridB = gridOf(state, s, b);
        if (!Js.T(gridA) || !Js.T(gridB) || (gridSize(state, gridA) == null) || (gridSize(state, gridB) == null))
        {
            return "only things in a grid swap";
        }
        var pa = layout(state, s, gridA).find((p) => p.items.includes(a));
        var pb = layout(state, s, gridB).find((p) => p.items.includes(b));
        var stackA = stackOf(state, s, a);
        var stackB = stackOf(state, s, b);
        if (!Js.T(pa) || !Js.T(pb) || stackA.includes(b))
        {
            return "they're the same stack";
        }
        if (!reachable(state, s, gridA) || !reachable(state, s, gridB))
        {
            return "out of reach";
        }
        int ax = pa.x, ay = pa.y, bx = pb.x, by = pb.y;
        bool at = pa.turned, bt = pb.turned;
        // Each into the other's place, with both out of the way.
        var both = new List<Item>(stackA);
        both.AddRange(stackB);
        bool? aTurn = fitsSwap(state, s, stackA, gridB, bx, by, at, both);
        bool? bTurn = fitsSwap(state, s, stackB, gridA, ax, ay, bt, both);
        if (!aTurn.HasValue || !bTurn.HasValue)
        {
            return "they don't fit each other's places";
        }
        // Both footprints must fit together where they overlap (the same grid).
        if (gridA == gridB)
        {
            var (aw, ah) = footprint(a, aTurn.Value);
            var (bw, bh) = footprint(b, bTurn.Value);
            if ((bx < (ax + bw)) && (ax < (bx + aw)) && (by < (ay + bh)) && (ay < (by + ah)))
            {
                return "they don't fit each other's places";
            }
        }
        var heavyA = tooHeavy(state, s, gridB, stackA, stackB);
        var heavyB = tooHeavy(state, s, gridA, stackB, stackA);
        if ((heavyA ?? heavyB) != null)
        {
            return heavyA ?? heavyB;
        }
        settle(state, s, gridA);
        settle(state, s, gridB);
        foreach (var m in Js.Iter(stackA))
        {
            putAt(state, s, m, gridB, bx, by, aTurn.Value);
        }
        foreach (var m in Js.Iter(stackB))
        {
            putAt(state, s, m, gridA, ax, ay, bTurn.Value);
        }
        return null;
    }

    /// <summary>Whether a stack fits at (x, y) of a grid with `gone` out of the way: as it lies, else turned (null: neither).</summary>
    static bool? fitsSwap(SimState state, Survivor s, List<Item> stack, string gridRef, int x, int y, bool turned, List<Item> gone)
    {
        var first = stack[0];
        if ((hasGrid(first.kind) && Js.T(Items.getItem(state, gridRef))) || stack.some((m) => m.id == gridRef))
        {
            return null;
        }
        var size = gridSize(state, gridRef)!.Value;
        var cells = cellsOf(state, gridRef, layout(state, s, gridRef, gone));
        foreach (var t in new[] { turned, !turned })
        {
            var (fw, fh) = footprint(first, t);
            if (((x + fw) <= size.w) && ((y + fh) <= size.h) && cells.free(x, y, fw, fh, size.h))
            {
                return t;
            }
        }
        return null;
    }

    /// <summary>splitStack: `count` (half, rounded down, if not given) off a stack onto the first free cell of its grid.</summary>
    public static string? splitStack(SimState state, Survivor s, Item item, int? count)
    {
        if (!canReach(state, s, item))
        {
            return "out of reach";
        }
        var gridRef = gridOf(state, s, item);
        if (!Js.T(gridRef) || (gridSize(state, gridRef) == null))
        {
            return "not in a grid";
        }
        if (!reachable(state, s, gridRef))
        {
            return "out of reach";
        }
        var stack = stackOf(state, s, item);
        var n = count ?? (stack.length / 2);
        if ((stack.length < 2) || (n < 1) || (n >= stack.length))
        {
            return "nothing to split";
        }
        var moving = stack.slice(stack.length - n);
        var stacks = layout(state, s, gridRef, moving);
        var spot = fit(cellsOf(state, gridRef, stacks), stacks, item, gridSize(state, gridRef)!.Value.h, false);
        if (spot.x < 0)
        {
            return "no room to split it";
        }
        settle(state, s, gridRef);
        foreach (var m in Js.Iter(moving))
        {
            putAt(state, s, m, gridRef, spot.x, spot.y, spot.turned);
        }
        return null;
    }

    /// <summary>quickMove: a stack (or `count` of it) into another grid by first-fit (into your grids, by default),
    /// as many as fit. Returns why none went, or null.</summary>
    public static string? quickMove(SimState state, Survivor s, Item item, string? gridRef, int? count)
    {
        if (!canReach(state, s, item))
        {
            return "out of reach";
        }
        var refs = Js.T(gridRef) ? [gridRef] : yourGrids(state, s);
        if (Js.T(gridRef) && !reachable(state, s, gridRef))
        {
            return "out of reach";
        }
        var moving = stackOf(state, s, item).slice(0, count ?? int.MaxValue);
        string? why = null;
        double moved = 0;
        foreach (var m in Js.Iter(moving))
        {
            var not = (gridOf(state, s, m) is { } here && refs.includes(here)) ? "it's there already" : placeFirstFit(state, s, m, refs, Js.T(gridRef) ? "no room in it" : "no room in your bag");
            if (not == null)
            {
                moved++;
            }
            else
            {
                why ??= not;
                break;
            }
        }
        return (moved > 0) ? null : why;
    }

    /// <summary>arrange: lays a grid out again, biggest first by first-fit (stacks of a kind merged up to their limit).
    /// Refused, and left as it was, if that wouldn't hold everything.</summary>
    public static string? arrange(SimState state, Survivor s, string gridRef)
    {
        if (!reachable(state, s, gridRef))
        {
            return "out of reach";
        }
        var size = gridSize(state, gridRef);
        if (!Js.T(size))
        {
            return "not a container";
        }
        var items = itemsIn(state, s, gridRef);
        // Stacks of a kind, in item order, each up to its limit.
        List<List<Item>> stacks = [];
        foreach (var item in Js.Iter(items))
        {
            var into = stackable(item) ? stacks.find((st) => (st[0].kind == item.kind) && (st.length < sizeOf(item).stack)) : null;
            if (Js.T(into))
            {
                into.push(item);
            }
            else
            {
                stacks.push([item]);
            }
        }
        var order = stacks.Select((st, i) => (st, i)).OrderByDescending((p) => sizeOf(p.st[0]).w * sizeOf(p.st[0]).h)
            .ThenByDescending((p) => Math.Max(sizeOf(p.st[0]).w, sizeOf(p.st[0]).h))
            .ThenBy((p) => p.st[0].kind, StringComparer.Ordinal).ThenBy((p) => p.i).Select((p) => p.st).ToList();
        var cells = new Cells(size.Value.w);
        List<(List<Item> st, Placed p)> laid = [];
        foreach (var st in Js.Iter(order))
        {
            var spot = fit(cells, [], st[0], size.Value.h, false);
            if (spot.x < 0)
            {
                return "it won't pack any tighter";
            }
            var p = new Placed { items = st, x = spot.x, y = spot.y, w = spot.w, h = spot.h, turned = spot.turned };
            cells.mark(p, p);
            laid.push((st, p));
        }
        foreach (var (st, p) in Js.Iter(laid))
        {
            foreach (var item in Js.Iter(st))
            {
                item.gx = p.x;
                item.gy = p.y;
                item.turned = p.turned ? true : null;
            }
        }
        return null;
    }

    /// <summary>The grid commands (Commands.bagCommand passes them here).</summary>
    public static readonly List<string> GRID_COMMANDS = ["moveItem", "swapItems", "splitStack", "quickMove", "arrange", "setQuick"];

    /// <summary>moveItem (itemId, containerId: a grid's ref or GROUND, x, y, turned, count), swapItems (itemId, id),
    /// splitStack (itemId, count), quickMove (itemId, containerId: none for your grids, count), arrange (containerId:
    /// none for the bag), setQuick (slot, itemId: none to clear). Returns why not, or null.</summary>
    public static string? gridCommand(SimState state, Survivor s, Command command)
    {
        if (command.count.HasValue && (!JsNumber.isInteger(command.count) || (command.count < 1)))
        {
            return "how many?";
        }
        int? count = command.count.HasValue ? Js.I(command.count.Value) : null;
        if (command.type == "arrange")
        {
            return arrange(state, s, command.containerId ?? BAG);
        }
        var item = Js.T(command.itemId) ? Items.getItem(state, command.itemId) : null;
        if (Js.T(command.itemId) && !Js.T(item))
        {
            return "no such thing";
        }
        if (command.type == "setQuick")
        {
            return setQuick(state, s, command.slot ?? -1, item);
        }
        if (!Js.T(item))
        {
            return "no such thing";
        }
        switch (command.type)
        {
            case "moveItem":
                if (!Js.T(command.containerId) || ((command.containerId != GROUND) && (!command.x.HasValue || !command.y.HasValue)))
                {
                    return "where to?";
                }
                return moveItem(state, s, item, command.containerId, command.x ?? 0, command.y ?? 0, command.turned == true, count);
            case "swapItems":
                var other = Js.T(command.id) ? Items.getItem(state, command.id) : null;
                return Js.T(other) ? swapItems(state, s, item, other) : "no such thing";
            case "splitStack":
                return splitStack(state, s, item, count);
            default:
                return quickMove(state, s, item, command.containerId, count);
        }
    }

    // --- quick slots ------------------------------------------------------------------------------------

    /// <summary>What key `n` (0 for 1) uses: the slot's thing if it's in your bag, else another of its kind there. Old
    /// saves (no slots yet): the bag's n-th.</summary>
    public static Item? quickItem(SimState state, Survivor s, int n)
    {
        if (s.quick == null)
        {
            return (n < s.pack.length) ? Items.getItem(state, s.pack[n]) : null;
        }
        var id = (n < s.quick.length) ? s.quick[n] : "";
        var item = (id != "") ? Items.getItem(state, id) : null;
        if (!Js.T(item))
        {
            return null;
        }
        if ((item.carriedBy == s.id) && s.pack.includes(item.id))
        {
            return item;
        }
        return s.pack.map((i) => Items.getItem(state, i)).find((i) => i?.kind == item.kind);
    }

    static void ensureQuick(SimState state, Survivor s)
    {
        if (s.quick != null)
        {
            return;
        }
        s.quick = [];
        for (var n = 0; n < QUICK_SLOTS; n++)
        {
            s.quick.push((n < s.pack.length) ? s.pack[n] : "");
        }
    }

    /// <summary>setQuick: puts something of yours in quick slot `slot` (0-8; null clears it), out of any other.</summary>
    public static string? setQuick(SimState state, Survivor s, int slot, Item? item)
    {
        if ((slot < 0) || (slot >= QUICK_SLOTS))
        {
            return "no such quick slot";
        }
        if (Js.T(item) && !yours(s, item))
        {
            return "not yours";
        }
        ensureQuick(state, s);
        if (Js.T(item))
        {
            for (var n = 0; n < s.quick.length; n++)
            {
                if (s.quick[n] == item.id)
                {
                    s.quick[n] = "";
                }
            }
        }
        s.quick[slot] = item?.id ?? "";
        return null;
    }

    /// <summary>Something usable come into your bag takes the first empty quick slot, if none has its kind.</summary>
    static void autoQuick(SimState state, Survivor s, Item item)
    {
        if (state.control?.survivorId != s.id)
        {
            return;
        }
        ensureQuick(state, s);
        if (!isQuickKind(item.kind))
        {
            return;
        }
        var kinds = s.quick.map((id) => (id != "") ? Items.getItem(state, id)?.kind : null);
        if (kinds.includes(item.kind) || s.quick.includes(item.id))
        {
            return;
        }
        var free = kinds.indexOf(null);
        if (free >= 0)
        {
            s.quick[free] = item.id;
        }
    }

    /// <summary>A thing used up: its quick slot passes to another of its kind in the bag, or empties.</summary>
    public static void forgetQuick(SimState state, Item item)
    {
        var s = Js.T(item.carriedBy) ? Survivors.getSurvivor(state, item.carriedBy) : null;
        var at = s?.quick?.indexOf(item.id) ?? -1;
        if (at < 0)
        {
            return;
        }
        var next = s.pack.map((id) => Items.getItem(state, id)).find((i) => Js.T(i) && (i.id != item.id) && (i.kind == item.kind) && !s.quick.includes(i.id));
        s.quick[at] = next?.id ?? "";
    }
}
