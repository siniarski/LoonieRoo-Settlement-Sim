// What things are called (2026-11-09). An item's kind decides what it does ("weapon", "gun", "food");
// its name is what you see: a "weapon" is a kitchen knife or a crowbar, a "gun" a pistol or a revolver.
// The name is a variant of the kind, picked by a stateless hash of the item's id (Hash.hashRoll), so it
// needs no state: it never changes, it costs the RNG stream nothing, and items in old saves get names too.
// Books are named by their subject (item.tag), bodies by whose they were (nameOf).
// A name is flavour only: every variant of a kind weighs and works the same. (The weapons' kinds are specific, one per
// weapon: WeaponTable.cs. The old generic "weapon" is read as a crowbar.)
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Items
{
    /// <summary>The names a kind's items go by (lowercase); one is picked per item.</summary>
    public static readonly System.Collections.Generic.Dictionary<string, string[]> NAMES = new()
    {
        ["wood"] = ["planks"],
        ["food"] = ["tinned beans", "tinned soup", "tinned peaches", "crackers", "dried pasta", "rice", "peanut butter", "tinned fish"],
        ["meal"] = ["stew", "broth", "roast meat", "fried potatoes"],
        ["bandage"] = ["bandage"],
        ["medicine"] = ["painkillers", "antibiotics", "cold pills"],
        ["weapon"] = ["crowbar"],  // the old generic kind (saves before 2026-11-10) is read as a crowbar: see WeaponTable.cs
        ["knife"] = ["kitchen knife", "hunting knife"],
        ["hatchet"] = ["hatchet"],
        ["machete"] = ["machete"],
        ["crowbar"] = ["crowbar"],
        ["bat"] = ["baseball bat"],
        ["pipe"] = ["lead pipe", "tyre iron"],
        ["sledge"] = ["sledgehammer"],
        ["spear"] = ["spear"],
        ["plank"] = ["plank with nails"],
        ["cloth"] = ["rags", "bedsheet", "torn shirt"],
        ["coat"] = ["parka", "wool coat", "rain jacket", "leather jacket"],
        ["armour"] = ["riot vest", "stab vest"],
        ["boots"] = ["work boots", "hiking boots", "rubber boots"],
        ["gun"] = ["pistol"],
        ["pistol"] = ["pistol"],
        ["revolver"] = ["revolver"],
        ["shotgun"] = ["pump shotgun"],
        ["rifle"] = ["hunting rifle"],
        ["crossbow"] = ["crossbow"],
        ["ammo"] = ["box of 9mm rounds"],
        ["ammo38"] = ["box of .38 rounds"],
        ["shells"] = ["box of shotgun shells"],
        ["ammo308"] = ["box of .308 rounds"],
        ["bolts"] = ["crossbow bolts"],
        ["corpse"] = ["body"],
        ["book"] = ["book"],
        ["cart"] = ["hand cart"],
        ["axe"] = ["felling axe"],
        ["hammer"] = ["claw hammer"],
        ["medkit"] = ["first aid kit"],
        ["sample"] = ["blood sample"],
        ["scrap"] = ["scrap metal"],
        ["fuel"] = ["fuel can"],
        ["flashlight"] = ["flashlight"],
    };

    /// <summary>Which of its kind's names an item goes by (0 if the kind has one, or none).</summary>
    public static int variant(Item item)
    {
        var names = NAMES.GetValueOrDefault(item.kind);
        if (names == null || names.Length < 2) return 0;
        return Js.I(JsMath.floor(Hash.hashRoll(Hash.idNumber(item.id), 0x4a3e) * names.Length)) % names.Length;
    }

    /// <summary>What an item is called: "hunting knife", "book on farming", "Ana's body".</summary>
    public static string nameOf(Item item)
    {
        if (item.kind == "book" && item.tag != null) return $"book on {BOOK_TOPICS.GetValueOrDefault(item.tag) ?? item.tag}";
        if (item.kind == "corpse") return item.tag != null ? $"{item.tag}'s body" : "a zombie's body";
        var names = NAMES.GetValueOrDefault(item.kind);
        return names != null ? names[variant(item)] : item.kind;
    }

    /// <summary>What a stack of things of one kind is called: the name if they all share it, else the kind's word.</summary>
    public static string stackName(System.Collections.Generic.List<Item> stack)
    {
        if (stack.Count == 0) return "";
        var first = nameOf(stack[0]);
        foreach (var i in stack) if (nameOf(i) != first) return KIND_WORDS.GetValueOrDefault(stack[0].kind) ?? stack[0].kind;
        return first;
    }

    /// <summary>A book's subject (Books: a skill or a piece of knowledge) as its title would put it.</summary>
    public static readonly System.Collections.Generic.Dictionary<string, string> BOOK_TOPICS = new()
    {
        ["build"] = "building", ["farm"] = "farming", ["craft"] = "crafting", ["cook"] = "cooking", ["doctor"] = "first aid", ["guard"] = "fighting",
    };

    /// <summary>A kind in a word, for a stack of mixed names.</summary>
    public static readonly System.Collections.Generic.Dictionary<string, string> KIND_WORDS = new()
    {
        ["food"] = "tinned food", ["meal"] = "meals", ["medicine"] = "pills", ["weapon"] = "weapons", ["cloth"] = "cloth",
        ["coat"] = "coats", ["armour"] = "vests", ["boots"] = "boots", ["gun"] = "guns", ["book"] = "books", ["axe"] = "axes",
        ["knife"] = "knives", ["hatchet"] = "hatchets", ["machete"] = "machetes", ["crowbar"] = "crowbars", ["bat"] = "bats", ["pipe"] = "pipes",
        ["sledge"] = "sledgehammers", ["spear"] = "spears", ["plank"] = "planks with nails", ["pistol"] = "pistols", ["revolver"] = "revolvers",
        ["shotgun"] = "shotguns", ["rifle"] = "rifles", ["crossbow"] = "crossbows", ["ammo"] = "boxes of 9mm rounds", ["ammo38"] = "boxes of .38 rounds",
        ["shells"] = "boxes of shells", ["ammo308"] = "boxes of .308 rounds", ["bolts"] = "bundles of bolts",
    };
}
