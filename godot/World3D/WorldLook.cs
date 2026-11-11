// What the world's surfaces are made of: a colour and a detail layer (Surfaces.cs) per face, chosen from what the
// sim already knows: the ground's kind, the building's kind and style, a roll of the lot. And the materials that draw
// them: one for the chunks, one for roofs (snow settles on it), one for glass. Reads nothing: plain tables.
//
// A chunk stays one surface and one draw: the layer is a number on the vertex (its UV, as docs/textures.md has it),
// not a material per kind.

using System.Collections.Generic;
using Godot;
using LoonieRoo.Game.Render;

namespace LoonieRoo.Game.World3D;

/// <summary>A face's colour and its detail layer (a vertex's UV: Surfaces.What, Turned, Mapped or None).</summary>
public readonly record struct Look(Color colour, Vector2 what)
{
    public Look Darkened(float by) => new(colour.Darkened(by), what);
}

public static class WorldLook
{
    /// <summary>A building's surfaces: its walls outside and in, its ceiling, and its trim (door and window frames,
    /// a wall's ends). `id` tells two apart (a chunk's signature).</summary>
    public sealed record Style(int id, Look outside, Look inside, Look ceiling, Look trim);

    const string HEAD = "shader_type spatial;\nrender_mode diffuse_burley, specular_schlick_ggx";
    const string SOLID = HEAD + ";\n" + Surfaces.INCLUDE + @"
void vertex() { SURFACE_BY_WORLD }
void fragment() {
    ALBEDO = COLOR.rgb * SURFACE_DETAIL;
    ROUGHNESS = 0.95;
}";
    const string ROOF = HEAD + ";\n" + Surfaces.INCLUDE + "#include \"res://assets/shaders/snow_cover.gdshaderinc\"\n" + @"
void vertex() { SURFACE_BY_WORLD }
void fragment() {
    ALBEDO = snowy(COLOR.rgb * SURFACE_DETAIL, normalize(surface_n));
    ROUGHNESS = 1.0;
}";
    // Glass: dust and runs from the grime layer make it paler and less clear where they lie.
    const string GLASS = HEAD + ", blend_mix, cull_disabled;\n" + Surfaces.INCLUDE + @"
void vertex() { SURFACE_BY_WORLD }
void fragment() {
    float dirt = clamp((SURFACE_DETAIL.g - 0.97) * 5.0, 0.0, 1.0);
    ALBEDO = mix(COLOR.rgb, vec3(0.42, 0.41, 0.37), dirt);
    ALPHA = mix(COLOR.a, 0.8, dirt);
    ROUGHNESS = mix(0.1, 0.7, dirt);
    METALLIC = 0.3 * (1.0 - dirt);
}";

    static ShaderMaterial? solid, roof, glass;
    public static ShaderMaterial Solid => solid ??= new ShaderMaterial { Shader = Surfaces.Shader(SOLID) };
    /// <summary>As Solid, with snow on what faces up (Weather.cs declares `snow_cover`: make a WeatherLayer first).</summary>
    public static ShaderMaterial Roof => roof ??= new ShaderMaterial { Shader = Surfaces.Shader(ROOF) };
    public static ShaderMaterial Glass => glass ??= new ShaderMaterial { Shader = Surfaces.Shader(GLASS) };

    static Look L(float r, float g, float b, string layer, bool turned = false) =>
        new(new Color(r, g, b), layer == "" ? Surfaces.None : turned ? Surfaces.Turned(layer) : Surfaces.What(layer));

    // --- the ground ---

    static readonly Dictionary<string, string> GROUND = new()
    {
        ["grass"] = "grass", ["dirt"] = "earth", ["road"] = "asphalt", ["forest"] = "litter", ["bridge"] = "planks", ["field"] = "earth",
        ["paving"] = "paving", ["boards"] = "floorboards", ["tiles"] = "tiles", ["concrete"] = "concrete",
    };
    static readonly Dictionary<string, Vector2> groundWhat = new();

    /// <summary>The detail under a ground kind (Grids.GROUNDS); the colour is the palette's, as the far view's.</summary>
    public static Vector2 Ground(string ground)
    {
        if (!groundWhat.TryGetValue(ground, out var what)) groundWhat[ground] = what = Surfaces.What(GROUND.GetValueOrDefault(ground, "grass"));
        return what;
    }

    // The ground's colours in first person and the far view (linear, as the walls'). The palette's (Render/Palette.cs)
    // are the Colony tab's: near-greys made for a dark 2D map, which the sun here lit to one pale grey for everything.
    static readonly Dictionary<string, Color> GROUND_COLOUR = new()
    {
        ["grass"] = new(0.13f, 0.18f, 0.075f), ["forest"] = new(0.1f, 0.115f, 0.06f), ["dirt"] = new(0.21f, 0.16f, 0.105f), ["field"] = new(0.19f, 0.14f, 0.09f),
        ["road"] = new(0.1f, 0.1f, 0.105f), ["paving"] = new(0.27f, 0.27f, 0.25f), ["concrete"] = new(0.3f, 0.3f, 0.29f),
        ["boards"] = new(0.27f, 0.19f, 0.115f), ["bridge"] = new(0.25f, 0.18f, 0.115f), ["tiles"] = new(0.33f, 0.34f, 0.33f),
    };

    /// <summary>A ground kind's colour in first person and the far view (the same in both, so they meet).</summary>
    public static Color GroundColour(string ground) => GROUND_COLOUR.TryGetValue(ground, out var c) ? c : GROUND_COLOUR["grass"];

    public static Look RiverBed => bed ??= L(0.2f, 0.18f, 0.14f, "earth");
    static Look? bed;

    // --- walls ---

    // Outside: boards in a few paints, brick, render, block, sheet. Colours are linear, as the walls' grey always was.
    static readonly (float r, float g, float b, string layer)[] SIDING = { (0.7f, 0.69f, 0.64f, "planks"), (0.66f, 0.6f, 0.46f, "planks"), (0.46f, 0.53f, 0.58f, "planks"), (0.5f, 0.55f, 0.45f, "planks"), (0.52f, 0.5f, 0.47f, "planks") };
    static readonly (float r, float g, float b, string layer)[] BRICK = { (0.44f, 0.22f, 0.16f, "brick"), (0.5f, 0.3f, 0.2f, "brick"), (0.56f, 0.44f, 0.32f, "brick") };
    static readonly (float r, float g, float b, string layer)[] RENDER = { (0.68f, 0.65f, 0.58f, "plaster"), (0.62f, 0.6f, 0.56f, "plaster"), (0.66f, 0.58f, 0.47f, "plaster") };
    static readonly (float r, float g, float b, string layer)[] BLOCK = { (0.56f, 0.55f, 0.52f, "concrete"), (0.5f, 0.5f, 0.49f, "concrete") };
    static readonly (float r, float g, float b, string layer)[] SHEET = { (0.47f, 0.5f, 0.52f, "sheetmetal"), (0.52f, 0.49f, 0.42f, "sheetmetal"), (0.4f, 0.46f, 0.42f, "sheetmetal") };
    static readonly (float r, float g, float b, string layer)[] BARN = { (0.42f, 0.15f, 0.11f, "planks"), (0.42f, 0.36f, 0.29f, "planks") };
    static readonly (float r, float g, float b, string layer)[] ROUGH = { (0.42f, 0.36f, 0.29f, "planks"), (0.36f, 0.27f, 0.18f, "planks") };
    // Inside: plaster in off-whites.
    static readonly (float r, float g, float b)[] ROOMS = { (0.66f, 0.63f, 0.56f), (0.62f, 0.64f, 0.6f), (0.68f, 0.62f, 0.54f), (0.6f, 0.62f, 0.65f), (0.64f, 0.62f, 0.6f) };

    static readonly Dictionary<(string, string, int), Style> styles = new();

    /// <summary>A generated building's surfaces, by its plan's kind and style and a roll of its lot (0..1).</summary>
    public static Style Of(string kind, string style, double roll)
    {
        int pick = (int)(roll * 997) % 60; // the same building always rolls the same; 60 divides by every table's length
        var key = (kind, style, pick);
        if (styles.TryGetValue(key, out var s)) return s;
        (float r, float g, float b, string layer)[] outside = (kind, style) switch
        {
            ("house", "trailer") => SHEET,
            ("house", "cabin") => ROUGH,
            ("house", "farmhouse") => pick % 3 == 0 ? BRICK : SIDING,
            ("house", _) => (pick % 5) switch { 0 or 1 or 2 => SIDING, 3 => BRICK, _ => RENDER },
            ("shed", _) => ROUGH,
            ("barn", _) => BARN,
            ("church", _) => pick % 2 == 0 ? SIDING : BRICK,
            ("warehouse" or "factory", _) => pick % 3 == 0 ? BLOCK : SHEET,
            ("workshop" or "station", _) => pick % 2 == 0 ? BLOCK : SHEET,
            ("police" or "clinic" or "school", _) => pick % 3 == 0 ? BLOCK : BRICK,
            _ => pick % 3 == 0 ? RENDER : BRICK, // shops, bars, diners, offices
        };
        var o = outside[pick / 5 % outside.Length];
        if (kind == "church" && o.layer == "planks") o = SIDING[0];
        var outer = L(o.r, o.g, o.b, o.layer);
        // Bare buildings show the back of their boards, their block or their sheet inside; the rest are plastered.
        bool bare = kind is "shed" or "barn" or "warehouse" or "factory" or "workshop" or "station" || style == "cabin";
        var room = ROOMS[pick % ROOMS.Length];
        var inner = bare ? outer.Darkened(0.12f) : L(room.r, room.g, room.b, "plaster");
        var ceiling = bare ? (o.layer == "planks" ? L(0.34f, 0.28f, 0.2f, "planks") : L(0.42f, 0.42f, 0.41f, o.layer == "sheetmetal" ? "sheetmetal" : "concrete")) : L(0.5f, 0.49f, 0.46f, "plaster");
        var trim = o.layer is "planks" or "plaster" ? L(0.72f, 0.71f, 0.68f, "") : L(0.3f, 0.29f, 0.27f, "");
        if (bare) trim = outer.Darkened(0.25f) with { what = Surfaces.None };
        return styles[key] = new Style(styles.Count + 1, outer, inner, ceiling, trim);
    }

    /// <summary>What you put up yourself, and any wall that is no building's: bare boards.</summary>
    public static Style Built => built ??= new Style(0, L(0.5f, 0.39f, 0.25f, "planks"), L(0.5f, 0.39f, 0.25f, "planks"), L(0.36f, 0.3f, 0.22f, "planks"), L(0.36f, 0.27f, 0.17f, ""));
    static Style? built;

    // --- the rest ---

    public static Look Footing => footing ??= L(0.47f, 0.47f, 0.45f, "concrete");
    /// <summary>A kerb stone: the face between a street and its sidewalk.</summary>
    public static Look Kerb => kerb ??= L(0.5f, 0.5f, 0.48f, "concrete");
    public static Look Door => door ??= L(0.42f, 0.28f, 0.16f, "planks", true);
    public static Look DoorLocked => doorLocked ??= L(0.3f, 0.2f, 0.12f, "planks", true);
    public static Look Plank => plank ??= new Look(Ctx.Css(COLOURS.plank), Surfaces.What("planks"));
    public static Look Pane => pane ??= new Look(new Color(0.25f, 0.32f, 0.36f, 0.35f), Surfaces.What("grime"));
    static Look? footing, kerb, door, doorLocked, plank, pane;
}
