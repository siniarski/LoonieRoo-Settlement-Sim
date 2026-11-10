// Ported from src/sim/nature.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// The natural world, generated first: per-tile functions of the seed for water (sea, island
// channels, rivers, lakes, ponds) and forest. Towns, roads and lots (worldplan.ts) are laid out over it.
// Everything here is a pure function of (seed, position), from a stateless hash, never the sim RNG.
//
// Since 2026-11-10 the large scale comes from Terrain (elevation, moisture, where people settled; rivers that drain
// downhill and grow as they join, lakes filling hollows), and the small scale from fBm here (octaves of value noise,
// their coordinates warped): woods that thin out at their edges and stand in clumps beyond them.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class NatureMod
{

    /// <summary>Kept clear around the start, so the first day is calm.</summary>
    public const int START_CLEAR_RADIUS = 16;

    /// <summary>A well-mixed 32-bit hash of the seed and up to three integers.</summary>
    public static double hash(double seed, double a, double b, double salt)
    {
        double h = Js.Xor(Js.Xor(Js.Xor(JsMath.imul(Js.Xor(seed, 2654435769.0), 2246822507.0), JsMath.imul(a, 3266489909.0)), JsMath.imul(b, 668265263.0)), JsMath.imul(salt, 0x165667b1));
        h = JsMath.imul(Js.Xor(h, Js.Ushr(h, 15)), 0x2c1b3c6d);
        h = JsMath.imul(Js.Xor(h, Js.Ushr(h, 12)), 0x297a2d39);
        return Js.Ushr((Js.Xor(h, Js.Ushr(h, 15))), 0);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public static double roll(double seed, double a, double b, double salt)
    {
        return ((double)(hash(seed, a, b, salt)) / (4294967296.0));
    }

    public static double smooth(double t)
    {
        return ((double)(((double)(t) * (t))) * ((3 - ((double)(2) * (t)))));
    }

    /// <summary>Smooth value noise in [0, 1): hashed lattice every `scale` tiles, blended between.</summary>
    public static double noise(double seed, int x, int y, double scale, double salt)
    {
        var gx = JsMath.floor(((double)(x) / (scale)));
        var gy = JsMath.floor(((double)(y) / (scale)));
        var fx = smooth(((double)(x) / (scale)) - gx);
        var fy = smooth(((double)(y) / (scale)) - gy);
        double c(double i, double j)
        {
            return roll(seed, gx + i, gy + j, salt);
        }
        var top = c(0, 0) + ((double)((c(1, 0) - c(0, 0))) * (fx));
        var bottom = c(0, 1) + ((double)((c(1, 1) - c(0, 1))) * (fx));
        return top + ((double)((bottom - top)) * (fy));
    }
    public static double noise(double seed, double x, double y, double scale, double salt) => noise(seed, Js.I(x), Js.I(y), scale, salt);

    /// <summary>The same at any point (a warped coordinate): at a whole tile, the same as noise.</summary>
    public static double noiseD(double seed, double x, double y, double scale, double salt)
    {
        double sx = x / scale, sy = y / scale;
        double gx = Math.Floor(sx), gy = Math.Floor(sy);
        double fx = smooth(sx - gx), fy = smooth(sy - gy);
        double c00 = roll(seed, gx, gy, salt), c10 = roll(seed, gx + 1, gy, salt), c01 = roll(seed, gx, gy + 1, salt), c11 = roll(seed, gx + 1, gy + 1, salt);
        double top = c00 + (c10 - c00) * fx, bottom = c01 + (c11 - c01) * fx;
        return top + (bottom - top) * fy;
    }

    /// <summary>Fractal noise in [0, 1): `octaves` of noiseD from `scale` down, each half the size and half the weight
    /// (a salt each). Crowds round 0.5 more than one octave does.</summary>
    public static double fbm(double seed, double x, double y, double scale, double salt, int octaves)
    {
        double sum = 0, weight = 0, amp = 1;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * noiseD(seed, x, y, scale, salt + o * 0x101);
            weight += amp;
            amp *= 0.5;
            scale *= 0.5;
        }
        return sum / weight;
    }

    public static Nature natureFor(double seed, double size, string map)
    {
        Point start = new Point { x = Js.I(JsMath.floor(((double)(size) / (2)))), y = Js.I(JsMath.floor(((double)(size) / (2)))) };
        var side = ((hash(seed, 0, 0, 3) % 2) == 0) ? -1 : 1;
        var riverBase = start.y + ((double)(side) * ((48 + (hash(seed, 0, 0, 4) % 24))));
        return new Nature { seed = seed, size = size, map = map, start = start, riverAlongX = (hash(seed, 0, 0, 5) % 2) == 0, riverBase = riverBase };
    }

    public static bool nearStart(Nature n, int x, int y, double r)
    {
        return ((JsMath.pow((x - n.start.x), 2)) + (JsMath.pow((y - n.start.y), 2))) < ((double)(r) * (r));
    }
    public static bool nearStart(Nature n, double x, double y, double r) => nearStart(n, Js.I(x), Js.I(y), r);

    /// <summary>Open sea round an island or a chain (no bridges reach out into it): beyond a coast that wanders around 40% of the map's width from the middle.</summary>
    public static bool isSea(Nature n, int x, int y)
    {
        if ((n.map != "island") && (n.map != "islands"))
        {
            return false;
        }
        if (nearStart(n, x, y, START_CLEAR_RADIUS + 8))
        {
            return false;
        }
        var r = ((double)(JsMath.sqrt((JsMath.pow((x - ((double)(n.size) / (2))), 2)) + (JsMath.pow((y - ((double)(n.size) / (2))), 2)))) / (((double)(n.size) / (2))));
        return r > (0.78 + ((double)(0.18) * ((noise(n.seed, x, y, 18, 30) - 0.5))));
    }
    public static bool isSea(Nature n, double x, double y) => isSea(n, Js.I(x), Js.I(y));

    /// <summary>An island chain: channels between the islands, which roads cross on bridges.</summary>
    public static bool isChannel(Nature n, int x, int y)
    {
        return (n.map == "islands") && (JsMath.abs(noise(n.seed, x, y, 40, 31) - 0.5) < 0.045);
    }
    public static bool isChannel(Nature n, double x, double y) => isChannel(n, Js.I(x), Js.I(y));

    /// <summary>Tiles from (x, y) to the nearest river's bank (Terrain's rivers): 0 in the water, Terrain.FAR at most.</summary>
    public static double riverDistance(Nature n, int x, int y) => Terrain.Of(n).riverDistance(x, y);
    public static double riverDistance(Nature n, double x, double y) => riverDistance(n, Js.I(x), Js.I(y));

    public static bool isWater(Nature n, int x, int y)
    {
        if (nearStart(n, x, y, START_CLEAR_RADIUS + 8))
        {
            return false;
        }
        if (isSea(n, x, y) || isChannel(n, x, y))
        {
            return true;
        }
        var t = Terrain.Of(n);
        if (t.riverDistance(x, y) == 0 || t.isLake(x, y))
        {
            return true;
        }
        return noise(n.seed, x, y, 24, 8) > PONDS_AT;
    }

    /// <summary>Standing water: one of Terrain's lakes (not a river, a pond or the sea).</summary>
    public static bool isLake(Nature n, int x, int y) => !nearStart(n, x, y, START_CLEAR_RADIUS + 8) && Terrain.Of(n).isLake(x, y);
    public static bool isLake(Nature n, double x, double y) => isLake(n, Js.I(x), Js.I(y));

    /// <summary>Small ponds, by one octave of noise (lakes are Terrain's): rarer than when they were the only lakes.</summary>
    public const double PONDS_AT = 0.9;
    public static bool isWater(Nature n, double x, double y) => isWater(n, Js.I(x), Js.I(y));

    /// <summary>How wooded the land is here, 0..1: forest where it's over FOREST_AT. Half the land's moisture (Terrain:
    /// whole woodlands and open country), half fBm at 60 tiles down, warped (the woods' edges, clumps and clearings).</summary>
    public static double woodedness(Nature n, int x, int y)
    {
        double wx = x + 14 * (noise(n.seed, x, y, 30, 0x5e70) - 0.5) * 2, wy = y + 14 * (noise(n.seed, x, y, 30, 0x5e71) - 0.5) * 2;
        double fine = Math.Clamp(0.5 + (fbm(n.seed, wx, wy, 60, 9, 3) - 0.5) * 2.2, 0, 1);
        return 0.5 * Terrain.Of(n).moisture(x, y) + 0.5 * fine;
    }
    public static double woodedness(Nature n, double x, double y) => woodedness(n, Js.I(x), Js.I(y));

    public const double FOREST_AT = 0.6;

    public static bool isForest(Nature n, int x, int y)
    {
        return woodedness(n, x, y) > FOREST_AT;
    }
    public static bool isForest(Nature n, double x, double y) => isForest(n, Js.I(x), Js.I(y));

    /// <summary>The chance a tree stands on this tile (open ground off roads, lots and the start's clearing: Worldgen
    /// rolls it, FarField draws what it would roll), 0..1.</summary>
    /// Thin at a wood's edge (TREE_EDGE) and thicker inside it (TREE_DEEP, FOREST_DEEP over FOREST_AT); out of the
    /// woods, the odd tree where the land is nearly wooded (up to TREE_STRAY, within TREE_FRINGE under FOREST_AT).</summary>
    public static double treeChance(Nature n, int x, int y) => treeChanceAt(woodedness(n, x, y));

    /// <summary>treeChance from a tile's woodedness.</summary>
    public static double treeChanceAt(double w)
    {
        if (w > FOREST_AT) return TREE_EDGE + (TREE_DEEP - TREE_EDGE) * Math.Min(1, (w - FOREST_AT) / FOREST_DEEP);
        if (w > FOREST_AT - TREE_FRINGE) return TREE_STRAY * (w - (FOREST_AT - TREE_FRINGE)) / TREE_FRINGE;
        return 0;
    }

    public const double TREE_EDGE = 0.08, TREE_DEEP = 0.38, FOREST_DEEP = 0.12, TREE_STRAY = 0.05, TREE_FRINGE = 0.08;

    /// <summary>Whether this tile's ground is bare earth (open ground off forest, roads and lots): patches of fBm.</summary>
    public static bool isDirt(Nature n, int x, int y) => fbm(n.seed, x, y, 14, 11, 2) > 0.68;

    /// <summary>A quarantine zone's fence ring, two tiles in from the edge.</summary>
    public static bool onFenceLine(Nature n, int x, int y)
    {
        if (n.map != "quarantine")
        {
            return false;
        }
        var s = n.size;
        return (((((((x == 2) || (y == 2)) || (x == (s - 3))) || (y == (s - 3))) && (x >= 2)) && (y >= 2)) && (x <= (s - 3))) && (y <= (s - 3));
    }
    public static bool onFenceLine(Nature n, double x, double y) => onFenceLine(n, Js.I(x), Js.I(y));
}
