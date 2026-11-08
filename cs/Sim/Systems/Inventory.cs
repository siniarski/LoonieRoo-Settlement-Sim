// Ported from src/sim/inventory.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Inventory: a bag for the survivor you control, and what's in containers.
//
//   pack       item ids in your bag (survivor.pack), carried (carriedBy) like your hands and
//              slots. It holds PACK_MAX of weight (ITEM_WEIGHT, tenths of a kilo): a cart or a
//              body never goes in it.
//   use        an item in the bag: food is eaten, a remedy you need used, a weapon wielded (the
//              old one into the bag, or down if there's no room), clothing worn the same way.
//              The UI's quick slots (keys 1 to 9) use the bag's first nine.
//   containers searching one (control.ts) turns up everything left in it at once: the finds stay
//              inside (item.inside, no pos), listed in the loot list, to take one at a time or all.
//   stash      put something from the bag into a container beside you: a crate (CRATE_HOLD) or
//              a car's boot (vehicles.ts bootMax). Containers are found by id (containerOf).
//              stashAll puts in everything (or every one of a kind) that fits; takeAll (with a
//              kind) takes every one of a kind out. Things of a kind show as a stack.
//   slots      what you wear and wield (weapon, body, feet, tool): useItem puts it on from the
//              bag, unequip takes it off into the bag, drop takes it off onto the ground.
//   death      the bag's contents fall where you fall.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Inventory
{

    /// <summary>Weight in tenths of a kilo. A cart or a body can't go in a bag (Infinity).</summary>
    public static readonly JsRecord<double> ITEM_WEIGHT = new() { ["wood"] = 30, ["food"] = 5, ["meal"] = 5, ["bandage"] = 1, ["medicine"] = 1, ["weapon"] = 20, ["cloth"] = 2, ["coat"] = 15, ["armour"] = 40, ["boots"] = 10, ["gun"] = 30, ["ammo"] = 2, ["corpse"] = double.PositiveInfinity, ["book"] = 5, ["cart"] = double.PositiveInfinity, ["axe"] = 25, ["hammer"] = 15, ["medkit"] = 3, ["sample"] = 1, ["scrap"] = 10, ["fuel"] = 20 };

    /// <summary>What a bag holds: 12 kilos.</summary>
    public const int PACK_MAX = 120;

    public static double packWeight(SimState state, Survivor s)
    {
        return s.pack.reduce((sum, id) => sum + Js.Get(ITEM_WEIGHT, Items.getItem(state, id)?.kind ?? "sample"), 0.0);
    }

    public static bool fitsInPack(SimState state, Survivor s, Item item)
    {
        return (packWeight(state, s) + Js.Get(ITEM_WEIGHT, item.kind)) <= PACK_MAX;
    }

    /// <summary>What's inside a container (a crate, a car's boot), by its id.</summary>
    public static List<Item> contents(SimState state, Furniture f)
    {
        return state.items.filter((i) => i.inside == f.id);
    }

    /// <summary>The weight of what's in a container (tenths of a kilo).</summary>
    public static double load(SimState state, string containerId)
    {
        return state.items.filter((i) => i.inside == containerId).reduce((sum, i) => sum + Js.Get(ITEM_WEIGHT, i.kind), 0.0);
    }

    /// <summary>The slots you wear and wield things in, in the order the inventory shows them.</summary>
    public static readonly List<string> SLOTS = ["weapon", "body", "feet", "tool"];

    /// <summary>The slot an item goes in from the bag (a weapon, a garment, a tool), or null.</summary>
    public static string? slotFor(string kind)
    {
        if ((kind == "weapon") || (kind == "gun"))
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
        Js.SetProp(s, slot, null);
        var why = stow(state, s, item);
        if (why != null)
        {
            Js.SetProp(s, slot, item.id);
        }
        return why;
    }

    /// <summary>Takes off what you wear or wield and puts it down at your feet.</summary>
    public static string? dropWorn(SimState state, Survivor s, Item item)
    {
        var slot = slotOf(s, item.id);
        if (!Js.T(slot))
        {
            return "you don't have it on";
        }
        Js.SetProp(s, slot, null);
        Items.dropItem(state, item, s.pos);
        return null;
    }

    /// <summary>What a crate holds (tenths of a kilo).</summary>
    public const int CRATE_HOLD = 300;

    /// <summary>A container by id: a crate, or a car's boot. Where it is, and what it holds.</summary>
    public static ContainerOfResult? containerOf(SimState state, string id)
    {
        var f = FurnitureMod.getFurniture(state, id);
        if (Js.T(f))
        {
            return (f.kind == "crate") ? new() { at = new Point { x = Js.I(f.x), y = Js.I(f.y) }, max = CRATE_HOLD } : null;
        }
        var v = Vehicles.vehiclesOf(state).find((x) => x.id == id);
        return Js.T(v) ? new() { at = v.pos, max = Vehicles.bootMax(v) } : null;
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
        if ((load(state, containerId) + Js.Get(ITEM_WEIGHT, item.kind)) > box.max)
        {
            return "it won't fit";
        }
        unpack(s, item.id);
        item.carriedBy = null;
        item.pos = null;
        item.reservedBy = null;
        item.inside = containerId;
        return null;
    }

    /// <summary>Puts an item into the bag: from the ground, a container, or your hands. Returns why not, or null.</summary>
    public static string? stow(SimState state, Survivor s, Item item)
    {
        if (s.pack.includes(item.id))
        {
            return "already in your bag";
        }
        if (!fitsInPack(state, s, item))
        {
            return (Js.Get(ITEM_WEIGHT, item.kind) == double.PositiveInfinity) ? $"a {Js.Str(item.kind)} won't go in a bag" : "your bag is too full";
        }
        if (s.carrying == item.id)
        {
            s.carrying = null;
        }
        item.pos = null;
        item.inside = null;
        item.carriedBy = s.id;
        item.reservedBy = null;
        s.pack.push(item.id);
        return null;
    }

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
        unpack(s, id);
        Items.dropItem(state, item, s.pos);
        return null;
    }

    /// <summary>Wields a weapon or wears clothing from the bag: whatever was in the slot goes into the bag in its place (or down at your feet if it won't fit). Returns what happened.</summary>
    public static string equipFromPack(SimState state, Survivor s, Item item, Point where)
    {
        var slot = slotFor(item.kind);
        unpack(s, item.id);
        var old = Js.T(Js.GetProp<string?>(s, slot)) ? Items.getItem(state, Js.NN(Js.GetProp<string?>(s, slot))) : null;
        Js.SetProp(s, slot, item.id);
        if (Js.T(old))
        {
            Js.SetProp(s, slot, item.id);
            if (stow(state, s, old) != null)
            {
                Items.dropItem(state, old, where);
            }
        }
        return Clothing.isGarment(item.kind) ? $"put on the {Items.nameOf(item)}" : ((slot == "tool") ? $"took the {Items.nameOf(item)} in hand" : $"took up the {Items.nameOf(item)}");
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
                Items.dropItem(state, item, s.pos);
            }
        }
    }
}
