// The colony's storage rules, part of Rooms (Grid/Rooms.cs): which store category an item kind is in, which complete
// storage, armory and graveyard rooms take what (a storage room can refuse categories), and so where an item belongs.
// Hauling (WorkHaul.cs) and job choice ask these; Rooms itself only finds rooms and judges them complete.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Rooms
{
    /// <summary>The categories a storage room can take or refuse.</summary>
    public static readonly List<string> STORE_CATEGORIES = ["food", "materials", "medical", "gear"];

    public static string? categoryOf(string kind)
    {
        if ((kind == "food") || (kind == "meal"))
        {
            return "food";
        }
        if (((kind == "wood") || (kind == "cloth")) || (kind == "scrap"))
        {
            return "materials";
        }
        if ((kind == "bandage") || (kind == "medicine"))
        {
            return "medical";
        }
        return (kind == "corpse") ? null : "gear";
    }

    public static StoreRooms completeStoreRooms(SimState state)
    {
        var storage = completeRooms(state, "storage");
        JsSet<double> refusing(string c)
        {
            return Js.Set(state.roomDesignations.filter((d) => Js.T((d.purpose == "storage") && Js.T(d.refuses?.includes(c)))).map((d) => labelAt(state, d.x, d.y)));
        }
        var storageFor = Js.AsRecord<JsSet<double>>(Js.Object_fromEntries(STORE_CATEGORIES.map((c) =>
        {
            var no = refusing(c);
            return (c, Js.Set(Js.ListOf(storage).filter((r) => !no.has(r))));
        })));
        return new StoreRooms { storage = storage, armory = completeRooms(state, "armory"), graveyard = completeRooms(state, "graveyard"), storageFor = storageFor };
    }

    /// <summary>Where an item belongs: the dead in a graveyard; weapons, guns and ammo in an armory if there is one, everything else (and those otherwise) in storage that takes it.</summary>
    public static JsSet<double> homeFor(StoreRooms rooms, string kind)
    {
        if (kind == "corpse")
        {
            return rooms.graveyard;
        }
        if (Weapons.isArmoryKind(kind) && (rooms.armory.size > 0))
        {
            return rooms.armory;
        }
        return Js.Get(rooms.storageFor, categoryOf(kind) ?? "gear");
    }
}
