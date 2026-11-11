// Ported from src/sim/loot.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// What you find depends on where you look: each container carries a loot table, set by the room
// or building it was generated in (buildings.ts). Kitchens hold food, bathrooms medicine,
// garages tools and fuel, police stations guns. A crate with no table uses SCAVENGE_TABLE.
//
// One roll per find, as before (findIn takes the same [0, 1) roll scavengeFind did), so the
// RNG stream is consumed the same way whichever table answers.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Loot
{

    /// <summary>Each table's weights sum to 1.</summary>
    public static readonly JsRecord<List<(string, double)>> LOOT_TABLES = new() { ["kitchen"] = [("food", 0.66), ("knife", 0.08), ("fuel", 0.04), ("cloth", 0.1), ("scrap", 0.12)], ["bathroom"] = [("bandage", 0.42), ("medicine", 0.33), ("cloth", 0.2), ("medkit", 0.05)], ["bedroom"] = [("cloth", 0.34), ("coat", 0.18), ("boots", 0.14), ("book", 0.1), ("revolver", 0.01), ("pistol", 0.01), ("ammo38", 0.02), ("ammo", 0.04), ("bandage", 0.07), ("food", 0.08), ("knife", 0.01)], ["living"] = [("book", 0.3), ("food", 0.22), ("cloth", 0.2), ("wood", 0.1), ("bat", 0.03), ("crowbar", 0.01), ("scrap", 0.14)], ["garage"] = [("scrap", 0.24), ("fuel", 0.2), ("wood", 0.22), ("hammer", 0.07), ("axe", 0.06), ("crowbar", 0.04), ("pipe", 0.03), ("hatchet", 0.01), ("cart", 0.04), ("cloth", 0.09)], ["grocery"] = [("food", 0.86), ("cloth", 0.04), ("fuel", 0.04), ("scrap", 0.06)], ["pharmacy"] = [("medicine", 0.42), ("bandage", 0.4), ("medkit", 0.13), ("food", 0.05)], ["hardware"] = [("hammer", 0.1), ("axe", 0.09), ("hatchet", 0.03), ("crowbar", 0.01), ("scrap", 0.26), ("wood", 0.21), ("fuel", 0.1), ("cloth", 0.12), ("cart", 0.08)], ["bar"] = [("food", 0.5), ("bat", 0.05), ("knife", 0.03), ("scrap", 0.2), ("cloth", 0.12), ("book", 0.1)], ["station"] = [("fuel", 0.5), ("food", 0.3), ("scrap", 0.1), ("ammo", 0.03), ("crowbar", 0.04), ("pipe", 0.03)], ["office"] = [("book", 0.3), ("scrap", 0.3), ("food", 0.2), ("cloth", 0.2)], ["police"] = [("pistol", 0.1), ("revolver", 0.02), ("shotgun", 0.02), ("ammo", 0.18), ("ammo38", 0.04), ("shells", 0.08), ("armour", 0.14), ("crowbar", 0.07), ("bat", 0.05), ("knife", 0.06), ("boots", 0.1), ("bandage", 0.14)], ["clinic"] = [("medkit", 0.2), ("medicine", 0.35), ("bandage", 0.4), ("cloth", 0.05)], ["church"] = [("food", 0.35), ("cloth", 0.3), ("book", 0.2), ("bandage", 0.1), ("coat", 0.05)], ["school"] = [("book", 0.5), ("food", 0.25), ("cloth", 0.15), ("scrap", 0.1)], ["warehouse"] = [("scrap", 0.24), ("wood", 0.26), ("cloth", 0.2), ("food", 0.15), ("fuel", 0.1), ("cart", 0.05)], ["factory"] = [("scrap", 0.45), ("fuel", 0.2), ("wood", 0.15), ("hammer", 0.06), ("armour", 0.04), ("cloth", 0.1)], ["barn"] = [("food", 0.36), ("shotgun", 0.01), ("rifle", 0.005), ("shells", 0.015), ("ammo308", 0.01), ("wood", 0.2), ("fuel", 0.15), ("axe", 0.07), ("hammer", 0.06), ("scrap", 0.08), ("cloth", 0.04)], ["shed"] = [("wood", 0.3), ("scrap", 0.25), ("fuel", 0.15), ("axe", 0.1), ("hatchet", 0.02), ("hammer", 0.1), ("cart", 0.05), ("cloth", 0.03)], ["trash"] = [("scrap", 0.5), ("cloth", 0.25), ("food", 0.15), ("book", 0.1)] };

    /// <summary>Maps a [0, 1) roll to a find from a weighted table.</summary>
    public static string pickFrom(List<(string, double)> table, double roll)
    {
        double at = 0;
        foreach (var (kind, chance) in Js.Iter(table))
        {
            at += chance;
            if (roll < at)
            {
                return kind;
            }
        }
        return Js.NN(table.at(-1)).Item1;
    }
}
