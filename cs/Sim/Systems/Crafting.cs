// Ported from src/sim/crafting.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Crafting and cooking: survivors turn one thing into another at a station.
//
// A recipe names its station (workbench or stove), its input and how many,
// and how long it takes. While anything a station makes is wanted, the
// station offers one job (craft at a workbench, cook at a stove): carry the
// input to it one piece at a time (the station keeps it in `stored`), then
// work, and the product drops beside it. Which recipe is made is decided
// when the input is in: the first wanted one, in CRAFTABLES order.
//
// Workbench products follow the player's stock targets. Hot meals follow the
// colony: MEALS_PER_SURVIVOR each, while there's raw food to cook.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Crafting
{

    public static readonly List<string> CRAFTABLES = ["weapon", "bandage", "medicine", "ammo", "gun", "coat", "armour", "boots", "cart", "axe", "hammer", "medkit", "meal"];

    /// <summary>What the player sets targets for (the Workbench orders panel).</summary>
    public static readonly List<string> ORDERABLE = ["weapon", "bandage", "medicine", "ammo", "gun", "coat", "armour", "boots", "cart", "axe", "hammer", "medkit"];

    public static readonly JsRecord<Recipe> RECIPES = new() { ["weapon"] = new() { station = "workbench", input = "wood", count = 2, label = "Weapons" }, ["bandage"] = new() { station = "workbench", input = "wood", count = 1, label = "Bandages" }, ["medicine"] = new() { station = "workbench", input = "wood", count = 2, label = "Medicine" }, ["ammo"] = new() { station = "workbench", input = "wood", count = 2, label = "Ammo (hand-loaded)", workshopOnly = true, needs = "gunsmithing" }, ["gun"] = new() { station = "workbench", input = "wood", count = 4, label = "Guns", workshopOnly = true, needs = "gunsmithing" }, ["coat"] = new() { station = "workbench", input = "cloth", count = 2, label = "Coats" }, ["armour"] = new() { station = "workbench", input = "cloth", count = 3, label = "Padded armour", needs = "tailoring" }, ["boots"] = new() { station = "workbench", input = "cloth", count = 1, label = "Boots" }, ["cart"] = new() { station = "workbench", input = "wood", count = 3, label = "Carts" }, ["axe"] = new() { station = "workbench", input = "wood", count = 2, label = "Axes (scavenging, chopping)" }, ["hammer"] = new() { station = "workbench", input = "wood", count = 2, label = "Hammers (building)" }, ["medkit"] = new() { station = "workbench", input = "cloth", count = 2, label = "Medic kits (doctoring)" }, ["meal"] = new() { station = "stove", input = "food", count = 1, label = "Hot meals" } };

    /// <summary>Ticks of work once the input is in.</summary>
    public const int CRAFT_WORK = 60;

    /// <summary>At a workbench in a complete workshop.</summary>
    public const int WORKSHOP_CRAFT_WORK = 40;

    /// <summary>Highest stock target the player can set.</summary>
    public const int MAX_CRAFT_TARGET = 20;

    /// <summary>Hot meals the cooks keep ready, per survivor.</summary>
    public const int MEALS_PER_SURVIVOR = 2;

    public static bool isCraftKind(object k)
    {
        return (/*TODO typeof*/Js.TypeOf(k) == "string") && (ORDERABLE).includes(k);
    }

    public static bool isStation(string kind)
    {
        return (kind == "workbench") || (kind == "stove");
    }

    /// <summary>Items of this kind the colony has spare: on the ground or being hauled, not wielded or worn.</summary>
    public static double craftStock(SimState state, string kind) => craftStock(state, kind, usedItems(state));

    /// <summary>The ids of what the survivors wield and wear (a set for `has` only).</summary>
    static HashSet<string> usedItems(SimState state)
    {
        var used = new HashSet<string>();
        foreach (var s in Js.Iter(state.survivors))
        {
            if (Js.T(s.weapon)) used.Add(s.weapon);
            if (Js.T(s.body)) used.Add(s.body);
            if (Js.T(s.feet)) used.Add(s.feet);
            if (Js.T(s.cart)) used.Add(s.cart);
            if (Js.T(s.tool)) used.Add(s.tool);
        }
        return used;
    }

    static double craftStock(SimState state, string kind, HashSet<string> used)
    {
        double n = 0;
        foreach (var i in Js.Iter(state.items))
        {
            if ((i.kind == kind) && !used.Contains(i.id))
            {
                n++;
            }
        }
        return n;
    }

    public static double target(SimState state, string kind)
    {
        return (kind == "meal") ? ((double)(MEALS_PER_SURVIVOR) * (state.survivors.length)) : Js.Get(state.craftTargets, kind);
    }

    /// <summary>Craftables for this station below their target, in CRAFTABLES order.</summary>
    public static List<string> wantedCrafts(SimState state, string station = "workbench", bool inWorkshop = false)
    {
        // What's wielded and worn, found once for all the craftables (it was once each).
        HashSet<string>? used = null;
        return CRAFTABLES.filter((k) => ((Js.T((Js.Get(RECIPES, k).station == station) && (Js.T(Js.T(inWorkshop) || !Js.T(Js.Get(RECIPES, k).workshopOnly))))) && (!Js.T(Js.Get(RECIPES, k).needs) || state.knowledge.includes(Js.NN(Js.Get(RECIPES, k).needs)))) && (craftStock(state, k, used ??= usedItems(state)) < target(state, k)));
    }

    /// <summary>The first craftable for this station below its target, or null when stocks are full.</summary>
    public static string? wantedCraft(SimState state, string station = "workbench", bool inWorkshop = false)
    {
        return Js.At(wantedCrafts(state, station, inWorkshop), 0, null);
    }

    /// <summary>What this station should make next: something that uses what's already in it if possible, else the first wanted thing. Null when stocks are full.</summary>
    public static string? stationCraft(SimState state, Furniture f)
    {
        if (!isStation(f.kind))
        {
            return null;
        }
        var wanted = wantedCrafts(state, f.kind, inWorkshop(state, f));
        bool holds(string k)
        {
            return (f.stored > 0) && (Js.Get(RECIPES, k).input == f.storedKind);
        }
        bool has(string k)
        {
            return craftStock(state, Js.Get(RECIPES, k).input) > 0;
        }
        return ((wanted.find((k) => holds(k) && ((f.stored >= Js.Get(RECIPES, k).count) || has(k))) ?? wanted.find((__f0) => has(__f0))) ?? Js.Get(wanted, 0)) ?? null;
    }

    /// <summary>Is this station in a complete workshop?</summary>
    public static bool inWorkshop(SimState state, Point f)
    {
        return Rooms.completeRooms(state, "workshop").has(Rooms.labelAt(state, f.x, f.y));
    }
}
