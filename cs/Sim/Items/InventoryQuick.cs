// The quick slots (part of the inventory's grids, InventoryGrid.cs): keys 1 to 9 use survivor.quick, a row of nine
// slots you assign (setQuick). Picking up a usable kind none of them has fills the first empty one (autoQuick); a slot
// whose thing is gone uses another of its kind (quickItem), and forgets it when there is none (forgetQuick). An old
// save (quick null) keeps the bag's first nine until a slot is set.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Inventory
{
    /// <summary>Kinds a quick slot takes on its own when you pick one up (things with a use from the bag).</summary>
    public static readonly List<string> QUICK_KINDS = ["food", "meal", "bandage", "medicine", "medkit", "weapon", "gun", "axe", "hammer", "flashlight"];

    /// <summary>Whether a quick slot takes this kind on its own: the list above, and every weapon you can wield (WeaponTable.cs).</summary>
    public static bool isQuickKind(string kind) => QUICK_KINDS.includes(kind) || Weapons.isWieldKind(kind);

    public const int QUICK_SLOTS = 9;

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
