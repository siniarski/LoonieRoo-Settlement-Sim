// Colours and glyphs the views share (src/render/palette.ts), as CSS colour strings for Ctx.

using System.Collections.Generic;

namespace LoonieRoo.Game.Render;

public static class COLOURS
{
    public const string fog = "#07080a";
    /// <summary>Over explored ground out of view now (Sight.cs).</summary>
    public const string unseen = "rgba(7, 8, 10, 0.45)";
    public const string background = "#0d0f0e";
    public const string mapEdge = "#3a3f3b";
    public const string gridLine = "rgba(255, 255, 255, 0.05)";
    public static readonly Dictionary<string, string> tiles = new() { ["floor"] = "#262b27", ["wall"] = "#8b919c", ["door"] = "#a8713a", ["water"] = "#24465e", ["barricade"] = "#262b27" };
    public const string plank = "#7a5a35";
    public const string plankEdge = "#4a3420";
    public const string spike = "#b8bec6";
    public static readonly Dictionary<string, string> ground = new()
    {
        ["grass"] = "#262b27", ["dirt"] = "#2e2a24", ["road"] = "#36383a", ["forest"] = "#1f2a1f", ["bridge"] = "#4a3a28",
        ["field"] = "#33301f", ["paving"] = "#3a3b38", ["boards"] = "#3a3026", ["tiles"] = "#34383a", ["concrete"] = "#323432",
    };
    public const string waterRipple = "rgba(160, 200, 230, 0.18)";
    public const string wallTop = "#b2b8c2";
    public const string doorFrame = "#5a3c1e";
    public static readonly Dictionary<string, string> tools = new()
    {
        ["wall"] = "#9fd3ff", ["door"] = "#ffc27a", ["remove"] = "#ff7a7a", ["cancel"] = "#c9c9c9", ["scavenge"] = "#ffd166", ["explore"] = "#9fc3ff",
        ["lock"] = "#c9a227", ["well"] = "#5a8cc8", ["collector"] = "#5a8cc8", ["generator"] = "#c8b450", ["lamp"] = "#ffe678", ["fridge"] = "#c8dcf0",
        ["farm"] = "#9ed36a", ["furnish"] = "#c7a6ff", ["bed"] = "#c7a6ff", ["housing"] = "#6ea8ff", ["storage"] = "#e6be6e", ["armory"] = "#d98c6a",
        ["table"] = "#d6aa78", ["workbench"] = "#be9664", ["stove"] = "#8a8f96", ["barricade"] = "#aa7846", ["fence"] = "#8a5a32", ["trap"] = "#b8bec6", ["tower"] = "#96704a",
        ["messhall"] = "#9ee0c0", ["recroom"] = "#ec9fdc", ["infirmary"] = "#f4f1ea", ["barracks"] = "#c3d48c", ["workshop"] = "#d6b47c", ["graveyard"] = "#b4b4c8",
        ["burn"] = "#ff7a2e", ["coop"] = "#c8aa6e", ["greenhouse"] = "#9ee09e", ["heater"] = "#e6783c", ["herb"] = "#7ad7a0", ["wheat"] = "#e8c860",
        ["hunt"] = "#ff8a7a", ["cell"] = "#e0a080", ["radio"] = "#a0c8a0", ["perimeter"] = "#6eb4ff", ["lab"] = "#c8dcff", ["beacon"] = "#ff5a5a",
        ["turret"] = "#78828c", ["lure"] = "#dcc850", ["pit"] = "#3c3228", ["reinforce"] = "#a0aab4",
    };
    public const string selectHover = "#e8ece6";
    public const string survivor = "#f2e6c9";
    public const string survivorOutline = "#1a1d1b";
    public const string survivorAsleep = "#8a8fa8";
    public const string wanderer = "#9a9a8c";
    public const string wandererMark = "#fff3c4";
    public const string rest = "#7fb2ff";
    public const string satiety = "#ffb35c";
    public const string needLow = "#ff6a5c";
    public const string health = "#7ee08a";
    public const string night = "#0a1030";
    public const string zombie = "#7da35a";
    public const string enemy = "#d0584a";
    public const string radiation = "rgba(140, 255, 60, 0.18)";
    public const string zombieOutline = "#1f2a14";
    public const string swing = "#f0e2b0";
    public const string windup = "#e0483a";
    public const string hitFlash = "#f2e6d8";
    public const string staggered = "#9a9a8a";
    public const string zombieEye = "#ff5a4a";
    public const string zombieChase = "rgba(255, 90, 74, 0.6)";
    public const string foodCan = "#b9c2c9";
    public const string foodLabel = "#d9534f";
    public const string foodRim = "#6f7880";
    public const string bandage = "#f4f1ea";
    public const string bandageCross = "#d9534f";
    public const string selected = "#8cf0a4";
    public const string controlled = "#f0c850";
    public const string path = "rgba(140, 240, 164, 0.7)";
    public const string goal = "#8cf0a4";
    public const string label = "#f2e6c9";
    public const string dirt = "rgba(120, 92, 56, 0.85)";
    public const string wood = "#b07a3f";
    public const string woodGrain = "#7a4f24";
    public static readonly Dictionary<string, string> blueprint = new()
    {
        ["wall"] = "rgba(110, 180, 255, 0.35)", ["door"] = "rgba(255, 190, 110, 0.35)", ["bed"] = "rgba(199, 166, 255, 0.30)", ["barricade"] = "rgba(170, 120, 70, 0.30)", ["fence"] = "rgba(138, 90, 50, 0.35)",
        ["trap"] = "rgba(184, 190, 198, 0.30)", ["table"] = "rgba(214, 170, 120, 0.30)", ["stove"] = "rgba(120, 124, 130, 0.30)", ["workbench"] = "rgba(190, 150, 100, 0.30)",
        ["tower"] = "rgba(150, 110, 70, 0.30)", ["well"] = "rgba(90, 140, 200, 0.30)", ["collector"] = "rgba(90, 140, 200, 0.30)", ["generator"] = "rgba(200, 180, 80, 0.30)",
        ["lamp"] = "rgba(255, 230, 120, 0.30)", ["fridge"] = "rgba(200, 220, 240, 0.30)", ["heater"] = "rgba(230, 120, 60, 0.30)", ["coop"] = "rgba(200, 170, 110, 0.30)",
        ["radio"] = "rgba(160, 200, 160, 0.30)", ["lab"] = "rgba(200, 220, 255, 0.30)", ["beacon"] = "rgba(255, 90, 90, 0.30)", ["turret"] = "rgba(120, 130, 140, 0.30)",
        ["lure"] = "rgba(220, 200, 80, 0.30)", ["pit"] = "rgba(60, 50, 40, 0.30)", ["reinforce"] = "rgba(160, 170, 180, 0.35)", ["floor"] = "rgba(255, 110, 110, 0.0)",
    };
    public static readonly Dictionary<string, string> blueprintEdge = new()
    {
        ["wall"] = "#6eb4ff", ["door"] = "#ffbe6e", ["bed"] = "#c7a6ff", ["table"] = "#d6aa78", ["workbench"] = "#be9664", ["stove"] = "#8a8f96",
        ["barricade"] = "#aa7846", ["fence"] = "#8a5a32", ["trap"] = "#b8bec6", ["tower"] = "#96704a", ["well"] = "#5a8cc8", ["collector"] = "#5a8cc8", ["generator"] = "#c8b450",
        ["lamp"] = "#ffe678", ["fridge"] = "#c8dcf0", ["heater"] = "#e6783c", ["coop"] = "#c8aa6e", ["radio"] = "#a0c8a0", ["lab"] = "#c8dcff",
        ["beacon"] = "#ff5a5a", ["turret"] = "#78828c", ["lure"] = "#dcc850", ["pit"] = "#3c3228", ["reinforce"] = "#a0aab4", ["floor"] = "#ff6e6e",
    };
    public const string progress = "#8cf0a4";
    public const string jobLine = "rgba(255, 214, 102, 0.8)";
    public const string soil = "#4a3726";
    public const string furrow = "#3a2a1c";
    public const string sprout = "#6fbf4a";
    public const string ripe = "#e8c45a";
    public const string crate = "#7a5a35";
    public const string leaves = "#3f7d3a";
    public const string trunk = "#5a3f26";
    public const string crateSlat = "#5a4026";
    public const string crateEmpty = "#4a4038";
    public const string crateMarked = "#ffd166";
    public const string lootPip = "#ffd166";
    public const string bedFrame = "#5b4a7a";
    public const string bedSheet = "#9c8cc4";
    public const string bedPillow = "#e9e3f5";
    public static readonly Dictionary<string, string> room = new()
    {
        ["housing"] = "rgba(110, 168, 255, 0.10)", ["storage"] = "rgba(230, 190, 110, 0.10)", ["armory"] = "rgba(217, 140, 106, 0.12)",
        ["messhall"] = "rgba(158, 224, 192, 0.10)", ["recroom"] = "rgba(236, 160, 220, 0.10)", ["infirmary"] = "rgba(240, 240, 240, 0.10)",
        ["barracks"] = "rgba(170, 190, 120, 0.12)", ["workshop"] = "rgba(190, 150, 100, 0.12)", ["graveyard"] = "rgba(120, 120, 140, 0.14)",
        ["greenhouse"] = "rgba(120, 220, 120, 0.12)", ["cell"] = "rgba(200, 120, 80, 0.14)",
    };
    public static readonly Dictionary<string, string> roomLabel = new()
    {
        ["housing"] = "#9cc4ff", ["storage"] = "#f0d39a", ["armory"] = "#f0b39a", ["messhall"] = "#b8f0d4", ["recroom"] = "#f3c4ea", ["infirmary"] = "#f4f1ea",
        ["barracks"] = "#d4e0a8", ["workshop"] = "#e0c89a", ["graveyard"] = "#b4b4c8", ["greenhouse"] = "#9ee09e", ["cell"] = "#e0a080",
    };
    public const string tableTop = "#8a6440";
    public const string tableEdge = "#5c4128";
    public const string blade = "#cfd6dc";
    public const string hilt = "#6b4a2e";
    public const string incomplete = "#ff8a7a";
    public const string roomOutline = "rgba(255, 255, 255, 0.35)";
    public const string labelBg = "rgba(13, 15, 14, 0.75)";

    /// <summary>Furniture without a drawing of its own.</summary>
    public static readonly Dictionary<string, (string colour, string letter)> FURNITURE_GLYPHS = new()
    {
        ["tower"] = ("#7a5636", "T"), ["well"] = ("#3c6496", "W"), ["collector"] = ("#4a7ab4", "R"), ["generator"] = ("#8c7a2e", "E"), ["lamp"] = ("#b4a046", "l"),
        ["fridge"] = ("#a0b4c8", "F"), ["heater"] = ("#b4501e", "h"), ["coop"] = ("#a08250", "c"), ["radio"] = ("#4a6a4a", "r"), ["lab"] = ("#5a7aa0", "L"),
        ["beacon"] = ("#b43030", "B"), ["turret"] = ("#50585f", "G"), ["lure"] = ("#a09030", "!"), ["pit"] = ("#2a221a", "o"), ["truck"] = ("#4a5a3a", "T"),
    };

    /// <summary>Items without a drawing of their own: a coloured tile with a letter.</summary>
    public static readonly Dictionary<string, (string colour, string letter)> GLYPHS = new()
    {
        ["cloth"] = ("#c9b8a0", "c"), ["coat"] = ("#8a5a3a", "C"), ["armour"] = ("#7a8088", "A"), ["boots"] = ("#4a3626", "B"), ["gun"] = ("#2e3238", "G"),
        ["ammo"] = ("#b08a2e", "a"), ["corpse"] = ("#5a4a4a", "+"), ["book"] = ("#6a3a7a", "b"), ["cart"] = ("#7a6040", "K"), ["axe"] = ("#8a8070", "X"),
        ["hammer"] = ("#707880", "H"), ["knife"] = ("#b8bcc4", "k"), ["hatchet"] = ("#9a8a70", "h"), ["machete"] = ("#a8b0b8", "m"), ["crowbar"] = ("#b03a30", "j"),
        ["bat"] = ("#c8a070", "t"), ["pipe"] = ("#7a7e88", "p"), ["sledge"] = ("#606870", "S"), ["spear"] = ("#a08a60", "l"), ["plank"] = ("#9a7a4a", "n"),
        ["pistol"] = ("#2e3238", "G"), ["revolver"] = ("#3a3e46", "R"), ["shotgun"] = ("#4a3a2a", "Y"), ["rifle"] = ("#5a4430", "F"), ["crossbow"] = ("#46503a", "W"),
        ["ammo38"] = ("#6a7ab0", "a"), ["shells"] = ("#b0402e", "a"), ["ammo308"] = ("#8a8a6a", "a"), ["bolts"] = ("#b09a6a", "a"), ["medkit"] = ("#c84040", "+"), ["sample"] = ("#7ab06a", "s"), ["scrap"] = ("#8a8f96", "#"), ["fuel"] = ("#b06a2a", "f"), ["flashlight"] = ("#d8c860", "i"),
    };
}
