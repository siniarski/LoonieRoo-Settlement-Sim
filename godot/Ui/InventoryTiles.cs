// The inventory screen's controls (InventoryScreen, Inventory.cs, builds and drives them): a grid of cells with the
// stacks drawn across their footprints, the ghost of what you hold and the cursor (GridView); a worn or wielded
// slot (SlotTile); a quick slot (QuickTile); the weight you carry (Meter). They draw and report clicks; what a move
// does is the screen's.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Control = Godot.Control;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Ui;

/// <summary>A grid of cells: the stacks in it drawn across their footprints, the ghost of what you hold, the cursor.</summary>
public sealed partial class GridView : Control
{
    readonly InventoryScreen screen = null!;
    public readonly string gridRef = "";
    public readonly bool ground;
    public Label caption = null!;
    public List<Placed> stacks = new();
    public int w = 1, baseH = 1, rows = 1;

    public GridView() { }

    public GridView(InventoryScreen screen, string gridRef, bool ground)
    {
        this.screen = screen;
        this.gridRef = gridRef;
        this.ground = ground;
        MouseFilter = MouseFilterEnum.Stop;
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
    }

    public void Set(List<Placed> stacks, int w, int baseH, int rows)
    {
        this.stacks = stacks;
        this.w = w;
        this.baseH = baseH;
        this.rows = rows;
        CustomMinimumSize = new Vector2(w * InventoryScreen.CELL + 1, rows * InventoryScreen.CELL + 1);
        QueueRedraw();
    }

    public (int x, int y) CellAt(Vector2 global)
    {
        var local = global - GetGlobalRect().Position;
        return ((int)Math.Floor(local.X / InventoryScreen.CELL), (int)Math.Floor(local.Y / InventoryScreen.CELL));
    }

    public Placed? StackAtCell(int x, int y) => stacks.FirstOrDefault((p) => x >= p.x && x < p.x + p.w && y >= p.y && y < p.y + p.h);

    static readonly Color CellBg = Ctx.Css("#161a17");
    static readonly Color CellOver = Ctx.Css("#2a1a18");
    static readonly Color GroundBg = Ctx.Css("#1b1915");
    static readonly Color Line = Ctx.Css("#2d342f");

    public override void _Draw()
    {
        const int C = InventoryScreen.CELL;
        var font = Kit.Font;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < w; x++)
            {
                var r = new Rect2(x * C, y * C, C, C);
                DrawRect(r, ground ? GroundBg : (y >= baseH ? CellOver : CellBg));
                DrawRect(r, Line, false, 1);
            }
        var you = screen.you;
        foreach (var p in stacks)
        {
            var r = new Rect2(p.x * C + 2, p.y * C + 2, p.w * C - 3, p.h * C - 3);
            var fill = InventoryScreen.KindColour(p.first.kind);
            bool lifted = screen.held != null && screen.held.items.Contains(p.first);
            DrawRect(r, lifted ? fill with { A = 0.3f } : fill);
            DrawRect(r, fill.Lightened(0.35f), false, 1);
            var lines = Lines(Items.nameOf(p.first), (int)r.Size.X, (int)r.Size.Y);
            for (int i = 0; i < lines.Count; i++)
                DrawString(font, new Vector2(r.Position.X + 3, r.Position.Y + 11 + i * 10), lines[i], HorizontalAlignment.Left, r.Size.X - 4, 9, Kit.Text);
            if (p.items.Count > 1)
                DrawString(font, new Vector2(r.Position.X, r.End.Y - 3), $"×{p.items.Count}", HorizontalAlignment.Right, r.Size.X - 3, 10, Kit.Gold);
            if (you?.quick is { } quick && p.items.Select((i) => quick.IndexOf(i.id)).Where((n) => n >= 0).DefaultIfEmpty(-1).Min() is int q && q >= 0)
                DrawString(font, new Vector2(r.Position.X + 2, r.End.Y - 3), $"{q + 1}", HorizontalAlignment.Left, 12, 9, Kit.Gold);
        }
        if (screen.aim is { } a && a.g == this && screen.held is { } h)
        {
            var (fw, fh) = Inventory.footprint(h.items[0], h.turned);
            var colour = ground || a.why == null ? new Color(0.35f, 0.75f, 0.4f, 0.45f) : a.swap != null ? new Color(0.9f, 0.65f, 0.2f, 0.45f) : new Color(0.85f, 0.25f, 0.2f, 0.45f);
            var r = ground ? new Rect2(0, 0, w * C, rows * C) : new Rect2(a.x * C, a.y * C, fw * C, fh * C);
            DrawRect(r, colour);
            DrawRect(r, colour with { A = 1 }, false, 2);
        }
        if (screen.focus == this)
            DrawRect(new Rect2(screen.cx * C + 1, screen.cy * C + 1, C - 2, C - 2), Kit.Gold, false, 2);
    }

    /// <summary>A name broken into lines that fit a tile (words cut short if they must).</summary>
    static List<string> Lines(string name, int wpx, int hpx)
    {
        int chars = Math.Max(2, (wpx - 4) / 5);
        int most = Math.Max(1, (hpx - 2) / 10);
        var lines = new List<string>();
        var line = "";
        foreach (var word in name.Split(' '))
        {
            var cut = word.Length > chars ? word[..chars] : word;
            if (line == "") line = cut;
            else if (line.Length + 1 + cut.Length <= chars) line += " " + cut;
            else { lines.Add(line); line = cut; }
        }
        if (line != "") lines.Add(line);
        return lines.Take(most).ToList();
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        var (x, y) = ((int)(atPosition.X / InventoryScreen.CELL), (int)(atPosition.Y / InventoryScreen.CELL));
        var p = StackAtCell(x, y);
        if (p == null) return ground ? "The ground beside you: drop things here" : "";
        var (iw, ih, _) = Inventory.sizeOf(p.first);
        double weight = p.items.Sum((i) => Inventory.weightOf(i));
        return $"{InventoryScreen.Label(p.items)}\n{(double.IsInfinity(weight) ? "too heavy to carry in a bag" : InventoryScreen.kg(weight) + " kg")} · {iw}x{ih}{(p.turned ? ", turned" : "")}\n"
            + "Drag to move it (R turns it) · Shift-click: across · Ctrl-click: one · Right-click: more";
    }
}

/// <summary>A slot you wear or wield something in: drop something there to put it on; what's there drags.</summary>
public sealed partial class SlotTile : Control
{
    readonly InventoryScreen screen = null!;
    public readonly string slot = "";
    readonly string label = "";
    public Item? item;

    public SlotTile() { }

    public SlotTile(InventoryScreen screen, string slot, string label)
    {
        this.screen = screen;
        this.slot = slot;
        this.label = label;
        CustomMinimumSize = new Vector2(Inventory.BAG_W * InventoryScreen.CELL / 4 - 4, 40);
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = "Drop something here to wear or wield it · drag it out to take it off · right-click: more";
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, item != null ? InventoryScreen.KindColour(item.kind) : Ctx.Css("#161a17"));
        DrawRect(r, Ctx.Css("#3a423c"), false, 1);
        DrawString(Kit.Font, new Vector2(3, 11), label, HorizontalAlignment.Left, Size.X - 4, 9, Kit.Dim);
        var name = item != null ? InventoryScreen.NameOf(item) : "-";
        DrawString(Kit.Font, new Vector2(3, 25), name, HorizontalAlignment.Left, Size.X - 4, 9, item != null ? Kit.Text : Ctx.Css("#5f6862"));
    }
}

/// <summary>A quick slot (1-9): what key n uses; drop something of yours here to put it there.</summary>
public sealed partial class QuickTile : Control
{
    readonly InventoryScreen screen = null!;
    public readonly int n;
    public Item? item;

    public QuickTile() { }

    public QuickTile(InventoryScreen screen, int n)
    {
        this.screen = screen;
        this.n = n;
        CustomMinimumSize = new Vector2(Inventory.BAG_W * InventoryScreen.CELL / 9 - 2, 24);
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = $"Key {n + 1}: drop something of yours here (or press {n + 1} over it) to use it with the key";
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, item != null ? InventoryScreen.KindColour(item.kind) : Ctx.Css("#161a17"));
        DrawRect(r, Ctx.Css("#3a423c"), false, 1);
        DrawString(Kit.Font, new Vector2(2, 9), $"{n + 1}", HorizontalAlignment.Left, 10, 8, Kit.Gold);
        if (item != null) DrawString(Kit.Font, new Vector2(2, 20), Items.nameOf(item), HorizontalAlignment.Left, Size.X - 3, 8, Kit.Text);
    }
}

/// <summary>A thin bar: how full.</summary>
public sealed partial class Meter : Control
{
    double fraction;
    Color colour = Colors.White;

    public void Set(double f, Color c)
    {
        if (Math.Abs(f - fraction) < 0.0005 && c == colour) return;
        fraction = Math.Clamp(f, 0, 1);
        colour = c;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Ctx.Css("#2a2f2b"));
        DrawRect(new Rect2(Vector2.Zero, new Vector2((float)(Size.X * fraction), Size.Y)), colour);
    }
}

static class NodeAdd
{
    /// <summary>Adds a child and hands it back.</summary>
    public static T Add<T>(this Node parent, T child) where T : Node
    {
        parent.AddChild(child);
        return child;
    }
}
