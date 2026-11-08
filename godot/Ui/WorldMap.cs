// The map (M) (src/ui/worldmap.ts): the whole world as far as you've seen it, full screen.
//
// The ground you've explored (fogged where nobody has been), town names once you've seen their
// middle, neighbours' settlements, places of interest, cars you've seen, your people, and you
// (ringed). Your own notes (Mapnotes): click the map to pin one, right-click a pin to take it off.
// Neighbours' errands are ringed in orange; caravans on ground you've seen are small carts. Wheel to
// zoom, drag to pan, double-click to look there in the game. M or Escape closes. The Colony tab
// beside it (ScreenTabs) is the colony's management.
// Reads state only; notes go through commands.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Control = Godot.Control;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Ui;

public sealed partial class WorldMap : Control
{
    static readonly Dictionary<string, Color> RGB = new()
    {
        ["fog"] = C(10, 11, 13), ["water"] = C(36, 70, 94), ["wall"] = C(150, 156, 166), ["door"] = C(168, 113, 58), ["grass"] = C(52, 62, 50),
        ["dirt"] = C(70, 62, 52), ["road"] = C(96, 98, 102), ["forest"] = C(36, 70, 40), ["bridge"] = C(110, 88, 60), ["field"] = C(88, 80, 46),
        ["paving"] = C(88, 89, 86), ["boards"] = C(88, 72, 54), ["tiles"] = C(78, 84, 86), ["concrete"] = C(76, 78, 76),
    };
    static Color C(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);
    /// <summary>Rebuild the ground picture every this many frames (exploring changes it slowly).</summary>
    const int REBUILD_EVERY = 20;
    /// <summary>A right-click this close (px) to a pin takes it off.</summary>
    const double PIN_REACH = 10;
    const float HEAD = 40;

    readonly View view = null!;
    readonly Func<Command, bool> command = null!;
    readonly Ctx ctx = null!;
    readonly LineEdit note = new() { MaxLength = Mapnotes.NOTE_MAX, PlaceholderText = "a note, then Enter", Visible = false, CustomMinimumSize = new Vector2(220, 0) };
    Image? image;
    ImageTexture? ground;
    int frame;
    /// <summary>Map view: the tile at the centre, and pixels per tile.</summary>
    (double x, double y, double zoom)? cam;
    SimState? state;
    Point? pending;
    (double x, double y, bool moved)? drag;

    public WorldMap() { }

    public WorldMap(View view, Func<Command, bool> command)
    {
        this.view = view;
        this.command = command;
        ctx = new Ctx(this);
        Theme = Kit.MakeTheme();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        var head = new HBoxContainer { Position = new Vector2(12, 8) };
        head.AddThemeConstantOverride("separation", 12);
        head.AddChild(Kit.Label("Map", Kit.Title, 15));
        head.AddChild(Kit.Label("click: pin a note · right-click a pin: remove · wheel: zoom · drag: pan · double-click: look there · M or Esc: back", Ctx.Css("#8f998f"), 12));
        AddChild(head);
        AddChild(note);
        note.TextSubmitted += (text) =>
        {
            if (pending != null && text.Trim() != "") command(new Command { type = "addMapNote", x = pending.x, y = pending.y, text = text });
            CloseNote();
        };
        note.FocusExited += CloseNote;
    }

    void CloseNote()
    {
        pending = null;
        note.Visible = false;
    }

    Vector2 Area => new(Size.X, Size.Y - HEAD);
    P ToTile(double px, double py) => new(cam!.Value.x + (px - Area.X / 2) / cam.Value.zoom, cam.Value.y + (py - HEAD - Area.Y / 2) / cam.Value.zoom);
    P ToScreen(double tx, double ty) => new(Area.X / 2 + (tx - cam!.Value.x) * cam.Value.zoom, HEAD + Area.Y / 2 + (ty - cam.Value.y) * cam.Value.zoom);

    public override void _GuiInput(InputEvent e)
    {
        if (cam is not { } c || state == null) return;
        if (e is InputEventMouseButton mb && mb.Pressed && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown))
        {
            var before = ToTile(mb.Position.X, mb.Position.Y);
            cam = (c.x, c.y, Math.Max(0.5, Math.Min(24, c.zoom * (mb.ButtonIndex == MouseButton.WheelUp ? 1.15 : 1 / 1.15))));
            var after = ToTile(mb.Position.X, mb.Position.Y);
            cam = (cam.Value.x + before.x - after.x, cam.Value.y + before.y - after.y, cam.Value.zoom);
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } left)
        {
            if (left.DoubleClick)
            {
                var t = ToTile(left.Position.X, left.Position.Y);
                view.camera = new Cam(t.x, t.y, view.camera.zoom);
                CloseNote();
                view.openTab("colony"); // the top-down view, looking there
            }
            else if (left.Pressed) drag = (left.Position.X, left.Position.Y, false);
            else
            {
                var was = drag;
                drag = null;
                if (was == null || was.Value.moved) return;
                // A click: a note here.
                var t = ToTile(left.Position.X, left.Position.Y);
                int x = (int)Math.Floor(t.x), y = (int)Math.Floor(t.y);
                if (x < 0 || y < 0 || x >= state.grid.width || y >= state.grid.height) return;
                pending = new Point(x, y);
                note.Visible = true;
                note.Text = "";
                note.Position = new Vector2(Math.Min(left.Position.X, Size.X - 230), left.Position.Y + 20);
                note.CallDeferred(Control.MethodName.GrabFocus);
            }
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } right)
        {
            foreach (var n in Mapnotes.notesOf(state))
            {
                var s = ToScreen(n.x + 0.5, n.y + 0.5);
                if (Math.Abs(s.x - right.Position.X) <= PIN_REACH && Math.Abs(s.y - right.Position.Y) <= PIN_REACH)
                {
                    command(new Command { type = "removeMapNote", id = n.id });
                    return;
                }
            }
        }
        else if (e is InputEventMouseMotion mm && drag is { } d)
        {
            if (Math.Abs(mm.Position.X - d.x) + Math.Abs(mm.Position.Y - d.y) > 3) drag = (d.x, d.y, true);
            if (!drag.Value.moved) return;
            cam = (c.x - (mm.Position.X - d.x) / c.zoom, c.y - (mm.Position.Y - d.y) / c.zoom, c.zoom);
            drag = (mm.Position.X, mm.Position.Y, true);
        }
        AcceptEvent();
    }

    void Rebuild(SimState s)
    {
        var grid = s.grid;
        if (image == null || image.GetWidth() != grid.width || image.GetHeight() != grid.height) image = Image.CreateEmpty(grid.width, grid.height, false, Image.Format.Rgba8);
        for (int i = 0; i < grid.width * grid.height; i++)
        {
            int edge = grid.edges[i * 2] != 0 ? grid.edges[i * 2] : grid.edges[i * 2 + 1];
            var tile = grid.tiles[i];
            var c = grid.explored[i] != 1 ? RGB["fog"]
                : edge != 0 ? (edge == 2 ? RGB["door"] : RGB["wall"])
                : tile == "floor" ? RGB[grid.ground[i] >= 0 && grid.ground[i] < Grids.GROUNDS.Length ? Grids.GROUNDS[grid.ground[i]] : "grass"]
                : RGB.TryGetValue(tile, out var t) ? t : RGB["wall"];
            image.SetPixel(i % grid.width, i / grid.width, c);
        }
        if (ground == null || ground.GetWidth() != grid.width || ground.GetHeight() != grid.height) ground = ImageTexture.CreateFromImage(image);
        else ground.Update(image);
    }

    public void Update(SimState s)
    {
        Visible = view.showMap;
        state = s;
        if (!view.showMap)
        {
            frame = 0;
            cam = null;
            CloseNote();
            return;
        }
        // Opened: centred on what the game is looking at, the whole map in view.
        cam ??= (view.camera.x, view.camera.y, Math.Max(0.5, Math.Min(Area.X / s.grid.width, Area.Y / s.grid.height) * 0.95));
        if (frame++ % REBUILD_EVERY == 0) Rebuild(s);
        QueueRedraw();
    }

    void Label(double tx, double ty, string text, string colour, int px = 12, bool bold = false)
    {
        var p = ToScreen(tx, ty);
        ctx.font(px, bold);
        ctx.textAlign = "center";
        ctx.textBaseline = "alphabetic";
        ctx.fillStyle = "rgba(0, 0, 0, 0.85)";
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) }) ctx.fillText(text, p.x + dx, p.y + dy);
        ctx.fillStyle = colour;
        ctx.fillText(text, p.x, p.y);
    }

    void Dot(double tx, double ty, string colour, double r)
    {
        var p = ToScreen(tx, ty);
        ctx.fillStyle = colour;
        ctx.beginPath();
        ctx.arc(p.x, p.y, r, 0, Math.PI * 2);
        ctx.fill();
    }

    public override void _Draw()
    {
        if (state == null || cam is not { } c || ground == null) return;
        var s = state;
        ctx.Begin();
        ctx.fillStyle = "rgba(8, 9, 10, 0.97)";
        ctx.fillRect(0, 0, Size.X, Size.Y);
        var o = ToScreen(0, 0);
        DrawTextureRect(ground, new Rect2((float)o.x, (float)o.y, (float)(s.grid.width * c.zoom), (float)(s.grid.height * c.zoom)), false);
        bool seen(double x, double y)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            return ix >= 0 && iy >= 0 && ix < s.grid.width && iy < s.grid.height && s.grid.explored[iy * s.grid.width + ix] == 1;
        }
        // Towns: named once you've seen the middle.
        foreach (var t in Mapnotes.townNames(s)) if (seen(t.x, t.y)) Label(t.x + 0.5, t.y + 0.5, t.name.ToUpperInvariant(), "#e8d9a8", 14, true);
        foreach (var n in s.settlements)
        {
            var colour = n.people <= 0 ? "#888888" : n.reputation <= Settlements.HOSTILE ? "#ff7a6a" : "#7ad7ff";
            Dot(n.x + 0.5, n.y + 0.5, colour, 5);
            Label(n.x + 0.5, n.y - 1, n.name, colour);
        }
        foreach (var p in s.pointsOfInterest)
        {
            Dot(p.x + 0.5, p.y + 0.5, "#ffd166", 3);
            if (c.zoom >= 3) Label(p.x + 0.5, p.y - 0.6, p.label, "#ffd166", 11);
        }
        // Neighbours' errands for you: where, and what.
        foreach (var e in Errands.errandsOf(s))
        {
            var n = s.settlements.FirstOrDefault((x) => x.id == e.settlementId);
            var p = ToScreen(e.x + 0.5, e.y + 0.5);
            ctx.strokeStyle = "#ffa24a";
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.arc(p.x, p.y, 7, 0, Math.PI * 2);
            ctx.stroke();
            Label(e.x + 0.5, e.y - 1, $"{(e.kind == "nest" ? "the dead" : $"missing: {e.who}")} ({n?.name ?? "?"})", "#ffbf80");
        }
        // Caravans on the road, on ground you've seen.
        foreach (var cv in Caravans.caravansOf(s))
        {
            if (!seen(cv.x, cv.y)) continue;
            var p = ToScreen(cv.x + 0.5, cv.y + 0.5);
            ctx.fillStyle = "#c49a5a";
            ctx.fillRect(p.x - 4, p.y - 3, 8, 6);
            if (c.zoom >= 3) Label(cv.x + 0.5, cv.y - 0.6, "caravan", "#e0c9a0", 11);
        }
        // Neighbours' scavengers, on ground you've seen.
        foreach (var r in Runs.runsOf(s))
        {
            if (!seen(r.x, r.y)) continue;
            var p = ToScreen(r.x + 0.5, r.y + 0.5);
            ctx.fillStyle = "#e0c9a0";
            ctx.fillRect(p.x - 2, p.y - 2, 4, 4);
            if (c.zoom >= 3) Label(r.x + 0.5, r.y - 0.6, "scavengers", "#e0c9a0", 11);
        }
        foreach (var v in Vehicles.vehiclesOf(s))
        {
            if (!seen(v.pos.x, v.pos.y)) continue;
            var p = ToScreen((double)v.at.x / Motion.SUB, (double)v.at.y / Motion.SUB);
            ctx.fillStyle = v.health <= 0 ? "#6b6560" : "#c9c2b0";
            ctx.fillRect(p.x - 3, p.y - 2, 6, 4);
        }
        var you = s.control?.survivorId;
        foreach (var sv in s.survivors)
            if (sv.id != you) Dot((double)sv.at.x / Motion.SUB, (double)sv.at.y / Motion.SUB, "#ffffff", 2.5);
        var me = s.survivors.FirstOrDefault((x) => x.id == you);
        if (me != null)
        {
            double mx = (double)me.at.x / Motion.SUB, my = (double)me.at.y / Motion.SUB;
            var p = ToScreen(mx, my);
            ctx.strokeStyle = "#8cf0a4";
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.arc(p.x, p.y, 7, 0, Math.PI * 2);
            ctx.stroke();
            Dot(mx, my, "#ffffff", 3);
            Label(mx, my - 1.2, "you", "#8cf0a4", 11);
        }
        // Your notes: a pin and the words.
        foreach (var n in Mapnotes.notesOf(s))
        {
            var p = ToScreen(n.x + 0.5, n.y + 0.5);
            ctx.fillStyle = "#ff5a4a";
            ctx.beginPath();
            ctx.arc(p.x, p.y - 6, 4, 0, Math.PI * 2);
            ctx.fill();
            ctx.strokeStyle = "#ff5a4a";
            ctx.lineWidth = 1.5;
            ctx.beginPath();
            ctx.moveTo(p.x, p.y - 2);
            ctx.lineTo(p.x, p.y + 2);
            ctx.stroke();
            Label(n.x + 0.5, n.y - 0.8 - 8 / c.zoom, n.text, "#ffe0c8");
        }
        if (pending != null) Dot(pending.x + 0.5, pending.y + 0.5, "#ff5a4a", 4);
        // The header strip.
        ctx.fillStyle = "rgba(8, 9, 10, 1)";
        ctx.fillRect(0, 0, Size.X, HEAD);
        ctx.strokeStyle = "#333a35";
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(0, HEAD - 0.5);
        ctx.lineTo(Size.X, HEAD - 0.5);
        ctx.stroke();
    }
}
