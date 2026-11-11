// Ported from src/sim/inventory.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Inventory: a bag for the survivor you control, and what's in containers.
//
//   pack       item ids in your bag (survivor.pack), carried (carriedBy) like your hands and
//              slots. It holds PACK_MAX of weight (ITEM_WEIGHT, tenths of a kilo): a cart or a
//              body never goes in it.
//   use        an item in the bag: food is eaten, a remedy you need used, a weapon wielded (the
//              old one into the bag, or down if there's no room), clothing worn the same way.
//              The UI's quick slots (keys 1 to 9) use the bag's first nine.
//   containers searching one (ControlMod.cs) turns up everything left in it at once: the finds stay
//              inside (item.inside, no pos), listed in the loot list, to take one at a time or all.
//   stash      put something from the bag into a container beside you: a crate (CRATE_HOLD) or
//              a car's boot (Vehicles.cs bootMax). Containers are found by id (containerOf).
//              stashAll puts in everything (or every one of a kind) that fits; takeAll (with a
//              kind) takes every one of a kind out. Things of a kind show as a stack.
//   slots      what you wear and wield (weapon, body, feet, tool): useItem puts it on from the
//              bag, unequip takes it off into the bag, drop takes it off onto the ground.
//   death      the bag's contents fall where you fall.
//   grids      since 2026-11-10 every container is a grid and every item has a footprint (InventoryGrid.cs):
//              stow and stash place by first-fit, and fail when there's no room as well as when too heavy.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Inventory
{

    /// <summary>Weight in tenths of a kilo. A cart or a body can't go in a bag (Infinity).</summary>
    public static readonly JsRecord<double> ITEM_WEIGHT = new() { ["wood"] = 30, ["food"] = 5, ["meal"] = 5, ["bandage"] = 1, ["medicine"] = 1, ["weapon"] = 20, ["knife"] = 5, ["hatchet"] = 15, ["machete"] = 15, ["crowbar"] = 30, ["bat"] = 20, ["pipe"] = 30, ["sledge"] = 60, ["spear"] = 20, ["plank"] = 20, ["pistol"] = 15, ["revolver"] = 20, ["shotgun"] = 35, ["rifle"] = 40, ["crossbow"] = 30, ["ammo38"] = 2, ["shells"] = 3, ["ammo308"] = 2, ["bolts"] = 2, ["cloth"] = 2, ["coat"] = 15, ["armour"] = 40, ["boots"] = 10, ["gun"] = 30, ["ammo"] = 2, ["corpse"] = double.PositiveInfinity, ["book"] = 5, ["cart"] = double.PositiveInfinity, ["axe"] = 25, ["hammer"] = 15, ["medkit"] = 3, ["sample"] = 1, ["scrap"] = 10, ["fuel"] = 20, ["flashlight"] = 3 };

    /// <summary>Footprint in cells (w across, h down, as it lies unturned) and how many stack in one cell, by kind:
    /// the grid inventory (InventoryGrid.cs). A kind not here is 1x1, one to a cell; a new kind needs one line.</summary>
    public static readonly JsRecord<(int w, int h, int stack)> ITEM_SIZE = new()
    {
        ["wood"] = (2, 4, 1), // a bundle of planks
        ["food"] = (1, 1, 1), // a tin
        ["meal"] = (1, 1, 1),
        ["bandage"] = (1, 1, 5),
        ["medicine"] = (1, 1, 5),
        ["weapon"] = (1, 3, 1), // by name in NAME_SIZE: a knife 1x2, a bat 1x4
        // The weapons of WeaponTable.cs, each a kind of its own: by length, long guns two cells wide.
        ["knife"] = (1, 2, 1),
        ["hatchet"] = (1, 3, 1),
        ["machete"] = (1, 3, 1),
        ["crowbar"] = (1, 3, 1),
        ["bat"] = (1, 4, 1),
        ["pipe"] = (1, 3, 1),
        ["sledge"] = (2, 4, 1),
        ["spear"] = (1, 5, 1),
        ["plank"] = (1, 4, 1),
        ["pistol"] = (2, 2, 1),
        ["revolver"] = (2, 2, 1),
        ["shotgun"] = (2, 4, 1),
        ["rifle"] = (2, 5, 1),
        ["crossbow"] = (3, 3, 1),
        ["cloth"] = (1, 1, 5),
        ["coat"] = (2, 3, 1),
        ["armour"] = (2, 3, 1),
        ["boots"] = (2, 2, 1),
        ["gun"] = (2, 2, 1),
        ["ammo"] = (1, 1, 10), // boxes: the rounds are the box's `wear`, and boxes of a kind merge when stowed
        ["ammo38"] = (1, 1, 10),
        ["shells"] = (1, 1, 10),
        ["ammo308"] = (1, 1, 10),
        ["bolts"] = (1, 1, 10),
        ["flashlight"] = (1, 1, 1),
        ["book"] = (1, 2, 1),
        ["axe"] = (2, 4, 1),
        ["hammer"] = (1, 3, 1),
        ["medkit"] = (2, 2, 1),
        ["sample"] = (1, 1, 5),
        ["scrap"] = (2, 2, 1),
        ["fuel"] = (2, 2, 1), // a can
        ["cart"] = (4, 3, 1), // never in a bag (it weighs Infinity), but it takes room in a crate
        ["corpse"] = (5, 2, 1), // lying down: only ever on the ground
    };

    /// <summary>Sizes by name, where a kind's names differ in size (Items.NAMES): a knife is shorter than a bat.</summary>
    public static readonly JsRecord<(int w, int h, int stack)> NAME_SIZE = new()
    {
        ["kitchen knife"] = (1, 2, 1),
        ["hunting knife"] = (1, 2, 1),
        ["machete"] = (1, 3, 1),
        ["crowbar"] = (1, 3, 1),
        ["baseball bat"] = (1, 4, 1),
        ["tyre iron"] = (1, 3, 1),
    };

    /// <summary>What a bag holds: 12 kilos.</summary>
    public const int PACK_MAX = 120;

    public static double packWeight(SimState state, Survivor s)
    {
        return s.pack.reduce((sum, id) => sum + Js.Get(ITEM_WEIGHT, Items.getItem(state, id)?.kind ?? "sample"), 0.0);
    }

    /// <summary>Whether stow would take it: light enough, and room for it in one of your grids.</summary>
    public static bool canStow(SimState state, Survivor s, Item item)
    {
        return (tooHeavy(state, s, BAG, [item]) == null) && Js.T(firstFit(state, s, item, yourGrids(state, s)));
    }

    /// <summary>What's inside a container (a crate, a car's boot), by its id.</summary>
    public static List<Item> contents(SimState state, Furniture f)
    {
        return state.items.filter((i) => i.inside == f.id);
    }

    /// <summary>The weight of what's in a container (tenths of a kilo), what's in the pockets of what's in it too.</summary>
    public static double load(SimState state, string containerId)
    {
        return state.items.filter((i) => i.inside == containerId).reduce((sum, i) => sum + Js.Get(ITEM_WEIGHT, i.kind) + (hasGrid(i.kind) ? load(state, i.id) : 0), 0.0);
    }

    /// <summary>The slots you wear and wield things in, in the order the inventory shows them.</summary>
    public static readonly List<string> SLOTS = ["weapon", "body", "feet", "tool"];

    /// <summary>The slot an item goes in from the bag (a weapon, a garment, a tool), or null.</summary>
    public static string? slotFor(string kind)
    {
        if (Weapons.isWieldKind(kind))
        {
            return "weapon";
        }
        if (Clothing.isGarment(kind))
        {
            return Js.Get(Clothing.SLOT_OF, kind);
        }
        return Js.In(kind, Skills.TOOL_FOR) ? "tool" : null;
    }

    /// <summary>The slot you have this item in, or null.</summary>
    public static string? slotOf(Survivor s, string id)
    {
        return SLOTS.find((slot) => Js.GetProp<string?>(s, slot) == id);
    }

    /// <summary>Puts everything from the bag (or every one of `kind`) that fits into a container. Returns why nothing went in, or null.</summary>
    public static string? stashAll(SimState state, Survivor s, string containerId, string? kind)
    {
        var ids = s.pack.filter((id) => !Js.T(kind) || (Items.getItem(state, id)?.kind == kind));
        if (ids.length == 0)
        {
            return Js.T(kind) ? $"no {Js.Str(kind)} in your bag" : "your bag is empty";
        }
        string? why = null;
        double put = 0;
        foreach (var id in Js.Iter(ids))
        {
            var item = Items.getItem(state, id);
            var not = Js.T(item) ? stash(state, s, item, containerId) : "no such thing";
            if (not == null)
            {
                put++;
            }
            else
            {
                why ??= not;
            }
        }
        return (put > 0) ? null : why;
    }

    /// <summary>Takes off what you wear or wield, into the bag. Returns why not, or null.</summary>
    public static string? unequip(SimState state, Survivor s, Item item)
    {
        var slot = slotOf(s, item.id);
        if (!Js.T(slot))
        {
            return "you don't have it on";
        }
        return stow(state, s, item); // (stow takes it out of the slot once there's room)
    }

    /// <summary>Takes off what you wear or wield and puts it down at your feet.</summary>
    public static string? dropWorn(SimState state, Survivor s, Item item)
    {
        var slot = slotOf(s, item.id);
        if (!Js.T(slot))
        {
            return "you don't have it on";
        }
        dropHere(state, s, item);
        return null;
    }

    /// <summary>What a crate holds (tenths of a kilo).</summary>
    public const int CRATE_HOLD = 300;

    /// <summary>A container by id: a crate, a car's boot, or something with pockets (a coat, a body: no weight limit of
    /// its own). Where it is, and what it holds.</summary>
    public static ContainerOfResult? containerOf(SimState state, string id)
    {
        var item = Items.getItem(state, id);
        if (Js.T(item))
        {
            if (!hasGrid(item.kind))
            {
                return null;
            }
            var at = item.pos ?? (Js.T(item.inside) ? containerOf(state, item.inside)?.at : null) ?? (Js.T(item.carriedBy) ? Survivors.getSurvivor(state, item.carriedBy)?.pos : null);
            return Js.T(at) ? new() { at = at, max = double.PositiveInfinity } : null;
        }
        var f = FurnitureMod.getFurniture(state, id);
        if (Js.T(f))
        {
            return (f.kind == "crate") ? new() { at = new Point { x = Js.I(f.x), y = Js.I(f.y) }, max = CRATE_HOLD } : null;
        }
        var v = Vehicles.vehiclesOf(state).find((x) => x.id == id);
        return Js.T(v) ? new() { at = Vehicles.bootTile(v), max = Vehicles.bootMax(v) } : null;
    }

    /// <summary>Puts something from the bag into a container. Returns why not, or null. (Is it beside you? The caller checks.)</summary>
    public static string? stash(SimState state, Survivor s, Item item, string containerId)
    {
        var box = containerOf(state, containerId);
        if (!Js.T(box))
        {
            return "not a container";
        }
        if (!s.pack.includes(item.id))
        {
            return "not in your bag";
        }
        if (tooHeavy(state, s, containerId, [item]) != null)
        {
            return "it won't fit";
        }
        return placeFirstFit(state, s, item, [containerId], "no room in it");
    }

    /// <summary>Puts an item into the bag: from the ground, a container, or your hands. Returns why not, or null.</summary>
    public static string? stow(SimState state, Survivor s, Item item)
    {
        if (s.pack.includes(item.id))
        {
            return "already in your bag";
        }
        if (Weapons.isAmmoKind(item.kind))
        {
            // Ammunition stacks: a box of the same kind in the bag takes the rounds (its `wear` is the count), and this one is used up.
            var stack = s.pack.map((id) => getItemInBag(state, id)).find((i) => (i != null) && (i.kind == item.kind) && (i.id != item.id));
            if (stack != null)
            {
                stack.wear = Weapons.rounds(stack) + Weapons.rounds(item);
                if (s.carrying == item.id) s.carrying = null;
                Items.removeItem(state, item);
                return null;
            }
        }
        if (Js.Get(ITEM_WEIGHT, item.kind) == double.PositiveInfinity)
        {
            return $"a {Js.Str(item.kind)} won't go in a bag";
        }
        return placeFirstFit(state, s, item, yourGrids(state, s), "no room in your bag");
    }

    static Item? getItemInBag(SimState state, string id) => Items.getItem(state, id);

    /// <summary>Takes an item out of the bag (it's being used up, or moving to a slot).</summary>
    public static void unpack(Survivor s, string id)
    {
        var at = s.pack.indexOf(id);
        if (at >= 0)
        {
            s.pack.splice(at, 1);
        }
    }

    /// <summary>Drops an item from the bag at your feet (or the nearest free tile).</summary>
    public static string? dropFromPack(SimState state, Survivor s, string id)
    {
        var item = Items.getItem(state, id);
        if (!Js.T(item) || !s.pack.includes(id))
        {
            return "not in your bag";
        }
        dropHere(state, s, item);
        return null;
    }

    /// <summary>Wields a weapon or wears clothing from the bag: whatever was in the slot goes into the bag in its place (or down at your feet if it won't fit). Returns what happened.</summary>
    public static string equipFromPack(SimState state, Survivor s, Item item, Point where)
    {
        var slot = slotFor(item.kind);
        unpack(s, item.id);
        unplace(item);
        var old = Js.T(Js.GetProp<string?>(s, slot)) ? Items.getItem(state, Js.NN(Js.GetProp<string?>(s, slot))) : null;
        Js.SetProp(s, slot, item.id);
        follow(state, s, item);
        if (Js.T(old))
        {
            Js.SetProp(s, slot, item.id);
            if (stow(state, s, old) != null)
            {
                unplace(old);
                Items.dropItem(state, old, where);
                follow(state, s, old);
            }
        }
        return Clothing.isGarment(item.kind) ? $"put on the {Items.nameOf(item)}" : ((slot == "tool") ? $"took the {Items.nameOf(item)} in hand" : $"took up the {Items.nameOf(item)}");
    }

    /// <summary>Everything someone wears and carries leaves the world with them (lost far from home, where nothing
    /// can fall to be found): the worn slots, the bag, and what is in the pockets of either.</summary>
    public static void loseAll(SimState state, Survivor s)
    {
        var ids = new JsSet<string>(((List<string?>)[s.weapon, s.body, s.feet, s.cart, s.tool, s.carrying]).filter((id) => Js.T(id)).map((id) => id!));
        foreach (var id in Js.Iter(s.pack)) ids.add(id);
        state.items = state.items.filter((i) => !ids.has(i.id) && (i.carriedBy != s.id));
        s.weapon = s.body = s.feet = s.cart = s.tool = s.carrying = null;
        s.pack = [];
        if (s.quick != null) s.quick = s.quick.map((q) => "");
    }

    /// <summary>Everything in the bag falls where you fall (death, or leaving).</summary>
    public static void dropPack(SimState state, Survivor s)
    {
        foreach (var id in Js.Iter((s.pack).ToList()))
        {
            var item = Items.getItem(state, id);
            unpack(s, id);
            if (Js.T(item))
            {
                unplace(item);
                Items.dropItem(state, item, s.pos);
            }
        }
    }
}
