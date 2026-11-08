// The ground's height for anything the 3D view stands on it (trees and undergrowth now). Flat for now:
// the heightmap, when it comes, goes here, and everything placed through it follows.

namespace LoonieRoo.Game.World3D;

public static class Terrain
{
    /// <summary>The ground's height (m) at world x, z (tiles: x is grid x, z is grid y).</summary>
    public static float Height(float x, float z) => 0f;
}
