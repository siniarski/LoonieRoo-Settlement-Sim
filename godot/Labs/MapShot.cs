// Whole-world overview pictures: a generated world (every chunk), drawn a pixel per tile, to see how the world
// generates at a glance. Ground by its colour, water, trees darker, walls, doors and windows on their edges,
// towns' circles (from the world plan) outlined, settlements boxed in yellow, the start ringed in red, and the
// dormant dead as dark red dots. Per world: <out>/<map>_<seed>.png (1 px a tile), <map>_<seed>_small.jpg
// (half size; the full-size PNGs are 6 MB and not in git), two close-ups at 3 px a tile: the biggest town and the start,
// and on a world of several regions the corner where the first four meet (2 px a tile). Sizes other than 2000 in the name.
//
//   Godot --path godot res://Labs/MapShot.tscn -- [--size=2000] [--worlds=mainland:1,mainland:2,island:4] [--out=DIR]

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public partial class MapShot : Node
{
    public override void _Ready()
    {
        var args = new Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            args[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        int size = int.Parse(args.GetValueOrDefault("size") ?? "2000");
        string outDir = args.GetValueOrDefault("out") ?? ProjectSettings.GlobalizePath("user://mapshots");
        DirAccess.MakeDirRecursiveAbsolute(outDir);
        var worlds = (args.GetValueOrDefault("worlds") ?? "mainland:1,mainland:2,mainland:3,island:4,islands:5,quarantine:6").Split(',');
        foreach (var w in worlds)
        {
            var p = w.Split(':');
            Shoot(p[0], double.Parse(p[1], CultureInfo.InvariantCulture), size, outDir);
        }
        GetTree().Quit();
    }

    static void Shoot(string map, double seed, int size, string outDir)
    {
        ulong t0 = Godot.Time.GetTicksMsec();
        var settings = new Settings { start = "solo", zombies = Difficulty.NORMAL.zombies, weather = Difficulty.NORMAL.weather, progression = Difficulty.NORMAL.progression, winter = Difficulty.NORMAL.winter, dayScale = Difficulty.NORMAL.dayScale, map = map };
        var state = Worldgen.createWorld(seed, size, settings);
        ulong t1 = Godot.Time.GetTicksMsec();
        for (int y = 0; y < size; y += Worldgen.CHUNK)
            for (int x = 0; x < size; x += Worldgen.CHUNK) Worldgen.ensureGeneratedAround(state, new Point(x, y));
        ulong t2 = Godot.Time.GetTicksMsec();
        var g = state.grid;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        var grounds = Array.ConvertAll(Grids.GROUNDS, (gr) => Ctx.Css(COLOURS.ground.GetValueOrDefault(gr, COLOURS.ground["grass"])));
        var water = Ctx.Css(COLOURS.tiles["water"]);
        var wall = new Color(0.2f, 0.2f, 0.22f);
        var door = new Color(0.55f, 0.35f, 0.18f);
        var window = new Color(0.55f, 0.75f, 0.85f);
        var tree = new Color(0.13f, 0.27f, 0.12f);
        var thing = new Color(0.45f, 0.42f, 0.38f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                string t = Grids.tileAt(g, i);
                Color c = t == "water" ? water : t == "floor" ? grounds[Math.Clamp(Grids.groundAt(g, i), 0, grounds.Length - 1)] : t == "door" ? door : wall;
                // A touch of noise, so even ground isn't a flat sheet.
                float n = ((x * 73856093) ^ (y * 19349663)) % 1000 / 1000f;
                c = c.Lightened(Math.Abs(n) * 0.05f);
                int north = Grids.edgeKind(g, 2 * i), west = Grids.edgeKind(g, 2 * i + 1);
                int e = north != 0 ? north : west;
                if (e == Grids.EDGE_WALL || e == Grids.EDGE_BARRICADE || e == Grids.EDGE_FENCE) c = wall;
                else if (e == Grids.EDGE_DOOR) c = door;
                else if (Grids.isWindow(e)) c = window;
                img.SetPixel(x, y, c);
            }
        foreach (var f in state.furniture)
            if (f.x >= 0 && f.y >= 0 && f.x < size && f.y < size)
                img.SetPixel(f.x, f.y, f.kind == "tree" ? tree : thing);
        foreach (var d in state.dormant)
            if (d.x >= 0 && d.y >= 0 && d.x < size && d.y < size) img.SetPixel(d.x, d.y, new Color(0.55f, 0.05f, 0.05f));
        var regions = Worldplan.regions(seed, size, map);
        var towns = regions.AllTowns();
        foreach (var town in towns) Ring(img, town.x, town.y, town.radius, new Color(1, 1, 1, 1), 0.55f);
        foreach (var s in state.settlements) Box(img, (int)s.x, (int)s.y, 6, new Color(1, 0.85f, 0.1f));
        var start = regions.nature.start;
        Ring(img, start.x, start.y, 10, new Color(1, 0.1f, 0.1f), 1);
        Ring(img, start.x, start.y, 11, new Color(1, 0.1f, 0.1f), 1);
        string name = size == 2000 ? $"{map}_{seed}" : $"{map}_{seed}_{size}";
        img.SavePng(System.IO.Path.Combine(outDir, name + ".png"));
        var small = (Image)img.Duplicate();
        small.Resize(size / 2, size / 2, Image.Interpolation.Lanczos);
        small.SaveJpg(System.IO.Path.Combine(outDir, name + "_small.jpg"), 0.88f);
        // Close-ups: the biggest town, and the start.
        var big = towns.OrderByDescending((t) => t.radius).FirstOrDefault();
        if (big != null) Crop(img, big.x, big.y, 300, 3, System.IO.Path.Combine(outDir, name + "_town.jpg"));
        Crop(img, start.x, start.y, 300, 3, System.IO.Path.Combine(outDir, name + "_start.jpg"));
        // Where four regions meet (worlds of more than one: WorldRegions), to see roads cross their borders.
        if (!regions.Single) Crop(img, WorldRegions.R, WorldRegions.R, 400, 2, System.IO.Path.Combine(outDir, name + "_border.jpg"));
        GD.Print($"mapshot {name}: {size}x{size}, plan + start {(t1 - t0) / 1000.0:0.0} s, every chunk {(t2 - t1) / 1000.0:0.0} s; regions {regions.side * regions.side}, towns {towns.Count}, roads {regions.RoadsPlanned} (in the {regions.Planned} regions still kept), settlements {state.settlements.Count}, trees {state.furniture.Count((f) => f.kind == "tree"):N0}, dormant {state.dormant.Count:N0}");
    }

    static void Crop(Image img, int cx, int cy, int span, int scale, string path)
    {
        int x0 = Math.Clamp(cx - span / 2, 0, img.GetWidth() - span), y0 = Math.Clamp(cy - span / 2, 0, img.GetHeight() - span);
        var c = img.GetRegion(new Rect2I(x0, y0, span, span));
        c.Resize(span * scale, span * scale, Image.Interpolation.Nearest);
        c.SaveJpg(path, 0.9f);
    }

    static void Ring(Image img, double cx, double cy, double r, Color c, float alpha)
    {
        int steps = Math.Max(32, (int)(r * 8));
        for (int k = 0; k < steps; k++)
        {
            double a = k * Math.Tau / steps;
            int x = (int)Math.Round(cx + Math.Cos(a) * r), y = (int)Math.Round(cy + Math.Sin(a) * r);
            if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
            img.SetPixel(x, y, img.GetPixel(x, y).Lerp(c, alpha));
        }
    }

    static void Box(Image img, int cx, int cy, int half, Color c)
    {
        for (int k = -half; k <= half; k++)
            foreach (var (x, y) in new[] { (cx + k, cy - half), (cx + k, cy + half), (cx - half, cy + k), (cx + half, cy + k) })
                if (x >= 0 && y >= 0 && x < img.GetWidth() && y < img.GetHeight()) img.SetPixel(x, y, c);
    }
}
