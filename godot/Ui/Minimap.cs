// The minimap (N) (src/ui/minimap.ts): the whole world at a glance, fogged where nobody has been.
// Survivors are white, zombies and herds you can see are red, points of interest yellow; the frame
// shows what the main view is looking at. Click to jump there. Reads state only; redrawn every few frames.

using System;
using System.Collections.Generic;
using Godot;
using Control = Godot.Control;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Ui;

public sealed partial class Minimap : Control
{
    /// <summary>Longest side of the minimap, in pixels.</summary>
    const int MAX_PX = 180;
    /// <summary>Redraw every this many frames: the world changes slowly at this scale.</summary>
    const int REDRAW_EVERY = 10;

    static readonly Dictionary<string, Color> RGB = new()
    {
        ["fog"] = C(10, 11, 13), ["water"] = C(36, 70, 94), ["wall"] = C(139, 145, 156), ["door"] = C(168, 113, 58), ["glass"] = C(150, 190, 215),
        ["grass"] = C(44, 52, 44), ["dirt"] = C(58, 52, 44), ["road"] = C(80, 82, 86), ["forest"] = C(34, 64, 36), ["bridge"] = C(96, 76, 52),
        ["field"] = C(74, 68, 40), ["paving"] = C(74, 75, 72), ["boards"] = C(74, 60, 46), ["tiles"] = C(66, 72, 74), ["concrete"] = C(64, 66, 64),
        ["barricade"] = C(120, 90, 50), ["floor"] = C(44, 52, 44),
    };
    static Color C(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);

    readonly View view = null!;
    Image? image;
    ImageTexture? texture;
    int frame;
    int scale = 1;
    int w, h, px = 1;
    readonly List<(int x, int y, Color c, int size)> dots = new();

    public Minimap() { }

    public Minimap(View view)
    {
        this.view = view;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            double x = mb.Position.X / px * scale, y = mb.Position.Y / px * scale;
            view.camera = new Cam(x, y, view.camera.zoom);
            AcceptEvent();
        }
    }

    public void Update(SimState state)
    {
        Visible = view.showMinimap;
        if (!view.showMinimap) return;
        if (frame++ % REDRAW_EVERY == 0) Redraw(state);
        QueueRedraw();
    }

    void Redraw(SimState state)
    {
        var grid = state.grid;
        scale = Math.Max(1, (int)Math.Ceiling(Math.Max(grid.width, grid.height) / (double)MAX_PX));
        int nw = (int)Math.Ceiling(grid.width / (double)scale), nh = (int)Math.Ceiling(grid.height / (double)scale);
        if (image == null || nw != w || nh != h)
        {
            w = nw;
            h = nh;
            px = Math.Max(1, MAX_PX / w);
            image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
            CustomMinimumSize = new Vector2(w * px, h * px);
        }
        for (int py = 0; py < h; py++)
            for (int qx = 0; qx < w; qx++)
            {
                int i = py * scale * grid.width + qx * scale;
                var tile = Grids.tileAt(grid, i);
                // A tile with an edge wall (or door) on its north or west side shows as that: at a pixel a
                // tile, the walls round a room still outline it.
                int edge = Grids.edgeKind(grid, i * 2) != 0 ? Grids.edgeKind(grid, i * 2) : Grids.edgeKind(grid, i * 2 + 1);
                Color colour = Grids.exploredAt(grid, i) != 1 ? RGB["fog"]
                    : edge != 0 ? (edge == Grids.EDGE_WALL ? RGB["wall"] : Grids.isWindow(edge) ? RGB["glass"] : RGB["door"])
                    : tile == "floor" ? RGB[Grids.groundAt(grid, i) >= 0 && Grids.groundAt(grid, i) < Grids.GROUNDS.Length ? Grids.GROUNDS[Grids.groundAt(grid, i)] : "grass"]
                    : RGB.TryGetValue(tile, out var c) ? c : RGB["fog"];
                image.SetPixel(qx, py, colour);
            }
        if (texture == null || texture.GetWidth() != w || texture.GetHeight() != h) texture = ImageTexture.CreateFromImage(image);
        else texture.Update(image);

        dots.Clear();
        void dot(int x, int y, string colour, int size = 2) => dots.Add((x, y, Ctx.Css(colour), size));
        bool seen(int x, int y) => Grids.exploredAt(grid, y * grid.width + x) == 1;
        foreach (var p in state.pointsOfInterest) dot(p.x, p.y, "#ffd166", 3);
        foreach (var n in state.settlements) dot(n.x, n.y, Colonies.isFallen(n) ? "#777777" : n.reputation <= Settlements.HOSTILE ? "#ff5a4a" : "#7ad7ff", 5);
        foreach (var z in state.zombies) if (seen(z.pos.x, z.pos.y)) dot(z.pos.x, z.pos.y, "#ff5a4a");
        foreach (var herd in state.herds) if (herd.heard || seen(herd.x, herd.y)) dot(herd.x, herd.y, "#ff5a4a", 4);
        foreach (var e in Errands.errandsOf(state)) dot(e.x, e.y, "#ffa24a", 4);
        foreach (var c in Caravans.caravansOf(state)) if (seen(c.x, c.y)) dot(c.x, c.y, "#c49a5a", 3);
        foreach (var r in Runs.runsOf(state)) if (seen(r.x, r.y)) dot(r.x, r.y, "#e0c9a0", 2);
        foreach (var s in state.survivors) dot(s.pos.x, s.pos.y, "#ffffff");
    }

    public override void _Draw()
    {
        if (texture == null) return;
        DrawRect(new Rect2(-1, -1, w * px + 2, h * px + 2), Ctx.Css("#333a35"), false, 1);
        DrawTextureRect(texture, new Rect2(0, 0, w * px, h * px), false);
        foreach (var (x, y, c, size) in dots)
            DrawRect(new Rect2((x / scale) * px - size / 2f, (y / scale) * px - size / 2f, size, size), c);
        // What the main view shows.
        double tiles = CameraMath.TILE_PX * view.camera.zoom;
        var vp = GetViewportRect().Size;
        double vw = vp.X / tiles / scale * px, vh = vp.Y / tiles / scale * px;
        DrawRect(new Rect2((float)(view.camera.x / scale * px - vw / 2), (float)(view.camera.y / scale * px - vh / 2), (float)vw, (float)vh), new Color(140 / 255f, 240 / 255f, 164 / 255f, 0.9f), false, 1);
    }
}
