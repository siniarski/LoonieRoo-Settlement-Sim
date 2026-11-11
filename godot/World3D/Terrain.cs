// The ground's height for everything the 3D view stands on it: the one hook (2026-11-08). The heights
// are the sim's (cs/Sim/Worldgen/Heights.cs: a pure field from the seed and the world plan, cached by
// chunk); this only reads them (rule 5). Hand-made maps have no plan and are flat (0).
// FirstPerson calls Use(state) each frame before anything asks; Eye calls it too.

using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public static class Terrain
{
    static Heights.Field? field;
    /// <summary>--flat: draw the ground flat whatever the sim's heights say (to compare).</summary>
    public static bool ForceFlat;
    static object? world;
    static string? map;

    /// <summary>The world the heights are read from (cheap when it hasn't changed).</summary>
    public static void Use(SimState state)
    {
        if (ReferenceEquals(state.world, world) && state.settings.map == map) return;
        world = state.world;
        map = state.settings.map;
        field = ForceFlat ? null : Heights.of(state);
    }

    /// <summary>No heights: a hand-made map, flat at 0 (water drawn as it always was).</summary>
    public static bool Flat => field == null;

    /// <summary>The height (m) of what stands at world x, z (tiles: x is grid x, z is grid y): the ground, and on a
    /// street's asphalt a kerb's height under it (Heights.Field.surface).</summary>
    public static float Height(float x, float z) => field == null ? 0f : (float)field.surface(x, z);

    /// <summary>Whether a tile is a street's asphalt, drawn a kerb's height under its corners (Heights step 6).</summary>
    public static bool Sunk(int x, int y) => field != null && field.sunk(x, y);

    /// <summary>How far (m) street asphalt is sunk at tile corner (x, y).</summary>
    public static float Kerb(int x, int y) => field == null ? 0f : (float)field.kerb(x, y);

    /// <summary>A cheap approximate corner height for far away (Heights.Field.coarse): the far field's ground.</summary>
    public static float Coarse(int x, int y, bool water) => field == null ? (water ? -0.25f : 0f) : (float)field.coarse(x, y, water);

    /// <summary>The ground's height (m) at tile corner (x, y).</summary>
    public static float Corner(int x, int y) => field == null ? 0f : (float)field.corner(x, y);

    /// <summary>A tile's floor (m): its highest corner (one level under a building). Walls, ceilings and roofs stand on it.</summary>
    public static float Floor(int x, int y) => field == null ? 0f : (float)field.floor(x, y);

    /// <summary>A tile's lowest corner (m): where a wall's footing goes down to.</summary>
    public static float Low(int x, int y) => field == null ? 0f
        : System.Math.Min(System.Math.Min(Corner(x, y), Corner(x + 1, y)), System.Math.Min(Corner(x, y + 1), Corner(x + 1, y + 1)));

    /// <summary>The water's surface (m) at a point (on hand-made maps, where it always was).</summary>
    public static float Water(float x, float z) => field == null ? -0.25f : (float)field.water(x, z);

    /// <summary>The ground's normal at a tile corner, from its neighbours (shared by every quad that meets there, so no seams).</summary>
    public static Godot.Vector3 Normal(int x, int y)
    {
        if (field == null) return Godot.Vector3.Up;
        float dx = Corner(x + 1, y) - Corner(x - 1, y), dz = Corner(x, y + 1) - Corner(x, y - 1);
        return new Godot.Vector3(-dx / 2, 1, -dz / 2).Normalized();
    }
}
