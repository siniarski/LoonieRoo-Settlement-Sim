// The inventory screen (Tab in first person; Tab in the Colony tab while you play someone): grids of cells
// (InventoryGrid.cs). On the left, a column kept for the character sheet; then you: what you wear and wield
// (In hand, Body, Feet, Tool), the quick slots 1-9, your bag and the pockets of what you wear or carry, with
// the weight you carry; on the right the container you're at (a crate, a shelf, a fridge, a car's boot, a
// body, a coat on the floor), the pockets of what's in it, and the ground beside you as a grid.
//
// Mouse: drag a thing to a cell (a ghost shows where it lands: green fits, amber swaps with what's there,
// red says why not), R turns it while you hold it, Esc lets go; drop it on a slot to wear or wield it, on a
// quick slot to put it there, on the ground grid or anywhere off the panels to drop it. Shift-click: the
// stack across (to the open container, or from it, or off the ground, into your grids); ctrl-click: one.
// Right-click: use, take or put, open, split, turn, drop, quick slot. Keys: arrows move a cursor, Q/E the
// grid; Enter uses, Space picks up and puts down (arrows move it, R turns it), F moves across (shift: one),
// G drops one (shift: the stack), X splits, R turns, Z arranges the grid, 1-9 put the thing in a quick slot.
//
// Things are plain labelled tiles (the game has 3D models, no icons), coloured by what they're for.
// Reads state only; every move is a sim command (moveItem, swapItems, splitStack, quickMove, arrange,
// setQuick, take, takeAll, drop, useItem, unequip) through the game's command path. What's open is view state.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Control = Godot.Control;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Ui;

public sealed partial class InventoryScreen : Control
{
    public const int CELL = 30;
    const int GROUND_W = 8;

    readonly View view;
    readonly Func<Command, CommandResult> command;
    readonly Label said = Kit.Label("", Ctx.Css("#ffb347"), 12);
    readonly Label hint = Kit.Label("", Kit.Dim, 11);
    readonly Control sheetColumn;
    readonly VBoxContainer youCol = new() { MouseFilter = MouseFilterEnum.Ignore };
    readonly VBoxContainer boxCol = new() { MouseFilter = MouseFilterEnum.Ignore };
    readonly PopupMenu menu = new();
    readonly PopupMenu quickMenu = new() { Name = "QuickMenu" };
    internal readonly List<GridView> grids = new();
    readonly List<SlotTile> slots = new();
    readonly List<QuickTile> quicks = new();
    readonly List<Control> panels = new();
    Label youTitle = null!, carryLine = null!, boxTitle = null!;
    Meter carryMeter = null!, boxMeter = null!;
    string structure = "\0";
    /// <summary>A container being searched, or a boot being opened: the screen opens on it when that's done.</summary>
    string? opening;
    ulong saidUntil;
    internal SimState? state;
    internal Survivor? you;
    /// <summary>The open container's id, or null.</summary>
    internal string? boxId;
    /// <summary>The self-test's mouse device: other mouse events are ignored while it's set (the real cursor).</summary>
    public int testDevice;

    /// <summary>What you hold (a drag, or picked up with Space): the stack, how it lies, and which of its cells the
    /// pointer (or the cursor) has.</summary>
    internal sealed class Held
    {
        public List<Item> items = new();
        public bool turned;
        public int grabX;
        public int grabY;
        public bool keyboard;
    }
    internal Held? held;
    /// <summary>Where what you hold would land: a grid, its top-left cell, why not (null: it fits), or what it'd swap with.</summary>
    internal (GridView g, int x, int y, string? why, Item? swap)? aim;
    Vector2? pressAt;
    /// <summary>Where the pointer last was (from this screen's own events: the self-test's, not the real cursor's).</summary>
    Vector2? lastPointer;
    Item? pressItem;
    Placed? pressPlaced;
    GridView? pressGrid;
    bool pressShift, pressCtrl;
    /// <summary>The keyboard's cursor: a grid and a cell.</summary>
    internal GridView? focus;
    internal int cx, cy;
    Item? menuItem;
    string? menuSlot;

    static readonly Dictionary<string, string> SLOT_NAMES = new() { ["weapon"] = "In hand", ["body"] = "Body", ["feet"] = "Feet", ["tool"] = "Tool" };

    public InventoryScreen() : this(null!, (_) => new CommandResult()) { }

    public InventoryScreen(View view, Func<Command, CommandResult> command)
    {
        this.view = view;
        this.command = command;
        Theme = Kit.MakeTheme();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        // The world and the HUD dimmed behind (a drop there is a drop at your feet), the columns a little below the top.
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.5f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var outer = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        outer.AddThemeConstantOverride("separation", 5);
        outer.AnchorLeft = outer.AnchorRight = 0.5f;
        outer.AnchorTop = outer.AnchorBottom = 0.03f;
        outer.GrowHorizontal = GrowDirection.Both;
        AddChild(outer);
        var title = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        var titleBox = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        titleBox.AddThemeStyleboxOverride("panel", Kit.Box("rgba(20, 23, 21, 0.9)", "#c9b88a", 4, 4));
        titleBox.AddChild(Kit.Label("Inventory · Tab or Esc to close · drag (R turns it) · shift-click: across, ctrl-click: one · right-click: more · keys: arrows, Q/E, Enter, Space, F, G, X, Z, 1-9", Kit.Title, 11));
        title.AddChild(titleBox);
        outer.AddChild(title);
        var columns = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        columns.AddThemeConstantOverride("separation", 10);
        outer.AddChild(columns);
        // CHARACTER SHEET COLUMN: about 320 px kept free on the left for the character sheet (godot/Ui/CharacterSheet.cs,
        // its own Control): add it with SheetColumn.AddChild(...). Worn and wielded slots stay in this screen's own column.
        sheetColumn = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0), MouseFilter = MouseFilterEnum.Ignore };
        columns.AddChild(sheetColumn);
        columns.AddChild(Panel(youCol));
        columns.AddChild(Panel(boxCol));
        var saidRow = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        said.HorizontalAlignment = HorizontalAlignment.Center;
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        saidRow.AddChild(hint);
        saidRow.AddChild(said);
        outer.AddChild(saidRow);
        menu.AddChild(quickMenu);
        AddChild(menu);
        menu.IdPressed += (id) => OnMenu((int)id);
        quickMenu.IdPressed += (id) => OnMenu((int)id);
    }

    /// <summary>The column kept for the character sheet (about 320 px wide, left of yours).</summary>
    public Control SheetColumn => sheetColumn;

    PanelContainer Panel(Control content)
    {
        var p = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, SizeFlagsVertical = SizeFlags.ShrinkBegin };
        p.AddThemeStyleboxOverride("panel", Kit.Box("rgba(20, 23, 21, 0.95)", "#3a423c", 4, 8));
        content.AddThemeConstantOverride("separation", 4);
        p.AddChild(content);
        panels.Add(p);
        return p;
    }

    /// <summary>The container open beside you: the one you used (a crate, a boot, a coat or a body on the floor), else a
    /// car's boot or a container beside you. Null if none.</summary>
    public static (string id, string title, double max)? Container(SimState state, Survivor s, string? open)
    {
        if (Vehicles.vehicleOf(state, s) != null) return null;
        if (open != null && Inventory.containerOf(state, open) is { } o && Inventory.reachable(state, s, open) && !IsYours(state, s, open)) return (open, Inventory.gridName(state, open), o.max);
        var car = Vehicles.vehicleNear(state, s.at, Motion.SUB * 1.5);
        if (car != null && Inventory.containerOf(state, car.id) is { } b && Commands.besideYou(s, b.at)) return (car.id, Inventory.gridName(state, car.id), b.max);
        Furniture? searched = null;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var f = FurnitureMod.furnitureAt(state, s.pos.x + dx, s.pos.y + dy);
                if (f?.kind != "crate") continue;
                if (state.items.Any((i) => i.inside == f.id)) return (f.id, Inventory.gridName(state, f.id), Inventory.CRATE_HOLD);
                if (f.loot <= 0) searched ??= f;
            }
        return searched != null ? (searched.id, Inventory.gridName(state, searched.id), Inventory.CRATE_HOLD) : null;
    }

    static bool IsYours(SimState state, Survivor s, string id) => Items.getItem(state, id) is { } i && Inventory.yours(s, i);

    /// <summary>Using a searched container beside you opens the screen on it. Returns whether it did.</summary>
    public bool OpenAt(SimState state, Point tile)
    {
        var s = ControlMod.controlled(state);
        if (s == null) return false;
        var f = FurnitureMod.furnitureAt(state, tile.x, tile.y);
        if (f?.kind != "crate" || f.loot > 0 || !Commands.besideYou(s, new Point(f.x, f.y))) return false;
        Open(f.id);
        return true;
    }

    void Open(string id)
    {
        view.openContainer = id;
        view.showInventory = true;
    }

    public void Update(SimState state)
    {
        this.state = state;
        var s = ControlMod.controlled(state);
        you = s;
        WatchOpening(state, s);
        Visible = view.showInventory && s != null && view.inventoryAllowed(true);
        if (!view.showInventory) view.openContainer = null;
        if (!Visible || s == null)
        {
            held = null;
            aim = null;
            pressAt = null;
            return;
        }
        if (saidUntil != 0 && Godot.Time.GetTicksMsec() > saidUntil) { said.Text = ""; saidUntil = 0; }
        var open = Container(state, s, view.openContainer);
        if (open == null) view.openContainer = null;
        boxId = open?.id;
        var mine = Inventory.yourGrids(state, s);
        var theirs = open is { } c ? Inventory.openGrids(state, c.id) : new List<string>();
        string newStructure = $"{s.id}|{string.Join(",", mine)}|{string.Join(",", theirs)}";
        if (newStructure != structure)
        {
            structure = newStructure;
            Rebuild(state, mine, theirs);
        }
        Refresh(state, s, open);
    }

    /// <summary>A search or a boot you started: when it's done (and you're still beside it), the screen opens on it.</summary>
    void WatchOpening(SimState state, Survivor? s)
    {
        var busy = state.control?.busy;
        if (busy != null && (busy.what == "search" || busy.what == "openBoot"))
        {
            opening = busy.targetId;
            return;
        }
        if (opening == null || s == null) return;
        var id = opening;
        opening = null;
        var f = FurnitureMod.getFurniture(state, id);
        var car = f == null ? Vehicles.vehiclesOf(state).FirstOrDefault((v) => v.id == id) : null;
        bool done = f != null ? f.kind == "crate" && f.loot <= 0 : car?.bootOpen == true;
        if (done && Inventory.containerOf(state, id) is { } c && Commands.besideYou(s, c.at)) Open(id);
    }

    // --- building the columns ---------------------------------------------------------------------

    void Rebuild(SimState state, List<string> mine, List<string> theirs)
    {
        foreach (var col in new[] { youCol, boxCol })
            foreach (var child in col.GetChildren()) { col.RemoveChild(child); child.QueueFree(); }
        var oldFocus = focus?.gridRef;
        grids.Clear();
        slots.Clear();
        quicks.Clear();

        youTitle = youCol.Add(Kit.Label("", Kit.Title, 13));
        youCol.Add(Kit.Label("Wearing and wielding", Kit.Dim, 11));
        var slotRow = youCol.Add(new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore });
        foreach (var slot in Inventory.SLOTS) slots.Add(slotRow.Add(new SlotTile(this, slot, SLOT_NAMES[slot])));
        youCol.Add(Kit.Label("Quick slots (1-9)", Kit.Dim, 11));
        var quickRow = youCol.Add(new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore });
        quickRow.AddThemeConstantOverride("separation", 2);
        for (int n = 0; n < Inventory.QUICK_SLOTS; n++) quicks.Add(quickRow.Add(new QuickTile(this, n)));
        carryLine = youCol.Add(Kit.Label("", Kit.Title, 12));
        carryMeter = youCol.Add(new Meter { CustomMinimumSize = new Vector2(Inventory.BAG_W * CELL, 5) });
        foreach (var gridRef in mine) AddGrid(youCol, gridRef, false);
        var youButtons = youCol.Add(new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore });
        youButtons.AddChild(Kit.Button("Arrange", () => Do(new Command { type = "arrange", containerId = Inventory.BAG }), tip: "Lay the bag out again, biggest first (Z)", size: 11));

        if (theirs.Count > 0)
        {
            boxTitle = boxCol.Add(Kit.Label("", Kit.Title, 12));
            boxMeter = boxCol.Add(new Meter { CustomMinimumSize = new Vector2(Inventory.BAG_W * CELL, 5) });
            foreach (var gridRef in theirs) AddGrid(boxCol, gridRef, false);
            var boxButtons = boxCol.Add(new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore });
            boxButtons.AddChild(Kit.Button("← Take everything", () => { if (boxId != null) Do(new Command { type = "takeAll", containerId = boxId }); }, size: 11));
            boxButtons.AddChild(Kit.Button("Stash everything →", () => { if (boxId != null) Do(new Command { type = "stashAll", containerId = boxId }); }, size: 11));
            boxButtons.AddChild(Kit.Button("Arrange", () => { if (boxId != null) Do(new Command { type = "arrange", containerId = boxId }); }, size: 11));
        }
        else
        {
            boxTitle = null!;
            boxMeter = null!;
        }
        AddGrid(boxCol, Inventory.GROUND, true);
        focus = grids.FirstOrDefault((g) => g.gridRef == oldFocus) ?? grids[0];
        cx = Math.Min(cx, focus.w - 1);
        cy = Math.Min(cy, Math.Max(0, focus.rows - 1));
    }

    void AddGrid(VBoxContainer col, string gridRef, bool ground)
    {
        var g = new GridView(this, gridRef, ground);
        g.caption = col.Add(Kit.Label("", Kit.Dim, 11));
        col.Add(g);
        grids.Add(g);
    }

    /// <summary>Every frame: each grid's layout, the weights, the slots.</summary>
    void Refresh(SimState state, Survivor s, (string id, string title, double max)? open)
    {
        double weight = Inventory.packWeight(state, s);
        youTitle.Text = s.name;
        carryLine.Text = $"Carrying {kg(weight)} / {kg(Inventory.PACK_MAX)} kg";
        carryMeter.Set(weight / Inventory.PACK_MAX, Ctx.Css(weight > Inventory.PACK_MAX * 0.85 ? "#ffb347" : "#c8c8a0"));
        if (open is { } c && boxTitle != null)
        {
            double load = Inventory.load(state, c.id);
            bool limited = !double.IsInfinity(c.max);
            boxTitle.Text = limited ? $"{c.title}  {kg(load)} / {kg(c.max)} kg" : $"{c.title}  {kg(load)} kg";
            boxMeter.Set(limited ? load / c.max : 0, Ctx.Css("#c8c8a0"));
        }
        foreach (var g in grids)
        {
            if (g.ground)
            {
                var near = state.items.Where((i) => i.pos != null && Commands.besideYou(s, i.pos)).ToList();
                var stacks = Inventory.packLoose(near, GROUND_W);
                g.Set(stacks, GROUND_W, 2, Math.Max(2, Inventory.rowsOf(state, stacks, Inventory.GROUND)));
                g.caption.Text = near.Count == 0 ? "The ground beside you · drop things here" : $"The ground beside you · {near.Count} thing{(near.Count == 1 ? "" : "s")}";
                continue;
            }
            var size = Inventory.gridSize(state, g.gridRef) ?? (1, 1);
            var laid = Inventory.layout(state, s, g.gridRef);
            g.Set(laid, size.w, size.h, Inventory.rowsOf(state, laid, g.gridRef));
            int used = laid.Sum((p) => Math.Max(0, Math.Min(p.y + p.h, size.h) - p.y) * p.w);
            int free = size.w * size.h - used;
            string over = g.rows > size.h ? " · overstuffed" : "";
            g.caption.Text = $"{Inventory.gridName(state, g.gridRef)} · {free} of {size.w * size.h} free{over}";
        }
        foreach (var t in slots)
        {
            var id = Js.GetProp<string?>(s, t.slot);
            t.item = id != null ? Items.getItem(state, id) : null;
            t.QueueRedraw();
        }
        foreach (var q in quicks)
        {
            q.item = Inventory.quickItem(state, s, q.n);
            q.QueueRedraw();
        }
        if (held != null && held.keyboard) AimCell(focus, cx - held.grabX, cy - held.grabY);
        hint.Text = HintText();
    }

    string HintText()
    {
        if (held == null)
        {
            var at = focus != null ? focus.StackAtCell(cx, cy) : null;
            return at != null ? $"{Label(at.items)} · Enter: use · Space: pick up · F: across · G: drop · X: split · R: turn · 1-9: quick slot" : "";
        }
        var name = Label(held.items);
        if (aim is not { } a) return $"Holding {name} · R turns it · Esc lets go · off the panels: drop it";
        if (a.g.ground) return $"Holding {name} · drop it here";
        if (a.why == null) return $"Holding {name} · it fits";
        if (a.swap != null) return $"Holding {name} · swap with {Items.nameOf(a.swap)}";
        return $"Holding {name} · {a.why}";
    }

    // --- input ---------------------------------------------------------------------------------------

    public override void _Input(InputEvent e)
    {
        if (!Visible || state == null || you == null) return;
        if (testDevice != 0 && e is InputEventMouse && e.Device != testDevice) return;
        if (menu.Visible || quickMenu.Visible) return;
        switch (e)
        {
            case InputEventMouseMotion mm:
                OnMotion(mm.Position);
                break;
            case InputEventMouseButton mb:
                if (OnButton(mb)) GetViewport().SetInputAsHandled();
                break;
            case InputEventKey k when k.Pressed && !k.Echo:
                if (OnKey(k)) GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>What's under a point on screen: a grid and the stack there, a slot, or a quick slot.</summary>
    (GridView? g, Placed? p, SlotTile? slot, QuickTile? quick) HitAt(Vector2 pos)
    {
        foreach (var g in grids)
            if (g.IsVisibleInTree() && g.GetGlobalRect().HasPoint(pos))
            {
                var (x, y) = g.CellAt(pos);
                return (g, g.StackAtCell(x, y), null, null);
            }
        foreach (var t in slots)
            if (t.GetGlobalRect().HasPoint(pos)) return (null, null, t, null);
        foreach (var q in quicks)
            if (q.GetGlobalRect().HasPoint(pos)) return (null, null, null, q);
        return (null, null, null, null);
    }

    bool OnButton(InputEventMouseButton mb)
    {
        var pos = mb.Position;
        var hit = HitAt(pos);
        if (mb.ButtonIndex == MouseButton.Left && mb.Pressed)
        {
            if (held != null)
            {
                // Holding something from the keyboard: a click puts it down where the pointer is.
                Aim(pos);
                PutDown(pos);
                return true;
            }
            var item = hit.p?.first ?? hit.slot?.item;
            if (hit.g != null)
            {
                focus = hit.g;
                (cx, cy) = hit.g.CellAt(pos);
            }
            if (item == null) return hit.g != null || hit.slot != null || hit.quick != null;
            pressAt = pos;
            pressItem = item;
            pressPlaced = hit.p;
            pressGrid = hit.g;
            pressShift = mb.ShiftPressed;
            pressCtrl = mb.CtrlPressed;
            return true;
        }
        if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
        {
            if (held != null)
            {
                Aim(pos);
                PutDown(pos);
                pressAt = null;
                return true;
            }
            if (pressAt == null) return false;
            pressAt = null;
            if (pressItem != null && (pressShift || pressCtrl)) QuickMove(pressItem, pressCtrl ? 1 : null);
            return true;
        }
        if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
        {
            if (held != null)
            {
                held = null;
                aim = null;
                return true;
            }
            var item = hit.p?.first ?? hit.slot?.item;
            if (item == null) return false;
            ShowMenu(item, hit.slot?.slot, pos);
            return true;
        }
        return false;
    }

    void OnMotion(Vector2 pos)
    {
        lastPointer = pos;
        if (held == null && pressAt is { } from && pressItem != null && from.DistanceTo(pos) > 5)
        {
            int gx = 0, gy = 0;
            if (pressPlaced != null && pressGrid != null)
            {
                var (x, y) = pressGrid.CellAt(from);
                gx = Math.Clamp(x - pressPlaced.x, 0, pressPlaced.w - 1);
                gy = Math.Clamp(y - pressPlaced.y, 0, pressPlaced.h - 1);
            }
            PickUp(pressItem, pressPlaced, gx, gy, false);
        }
        if (held != null && !held.keyboard) Aim(pos);
    }

    void PickUp(Item item, Placed? placed, int grabX, int grabY, bool keyboard)
    {
        var stack = placed != null && placed.items.Contains(item) ? new List<Item>(placed.items) : new List<Item> { item };
        if (stack[0] != item) { stack.Remove(item); stack.Insert(0, item); }
        held = new Held { items = stack, turned = placed?.turned ?? false, grabX = grabX, grabY = grabY, keyboard = keyboard };
    }

    /// <summary>Where what you hold would land with the pointer at `pos`.</summary>
    void Aim(Vector2 pos)
    {
        aim = null;
        if (held == null) return;
        foreach (var g in grids)
        {
            if (!g.IsVisibleInTree() || !g.GetGlobalRect().HasPoint(pos)) continue;
            var (x, y) = g.CellAt(pos);
            AimCell(g, x - held.grabX, y - held.grabY, g.StackAtCell(x, y));
            return;
        }
    }

    void AimCell(GridView? g, int x, int y, Placed? under = null)
    {
        aim = null;
        if (held == null || g == null || state == null || you == null) return;
        if (g.ground)
        {
            aim = (g, x, y, null, null);
            return;
        }
        under ??= g.StackAtCell(x + held.grabX, y + held.grabY);
        var why = Inventory.whyNot(state, you, held.items, g.gridRef, x, y, held.turned, out _);
        Item? swap = why is "something's in the way" or "that stack is full" && under != null && !held.items.Contains(under.first) ? under.first : null;
        aim = (g, x, y, why, swap);
    }

    /// <summary>Lets go of what you hold at `pos` (or the keyboard cursor): into a cell, a swap, onto a slot, onto the ground.</summary>
    void PutDown(Vector2? pos)
    {
        var h = held;
        var a = aim;
        held = null;
        aim = null;
        if (h == null || state == null || you == null) return;
        var first = h.items[0];
        if (a is { } t)
        {
            if (t.g.ground) Do(new Command { type = "moveItem", itemId = first.id, containerId = Inventory.GROUND, count = h.items.Count });
            else if (t.why == null) Do(new Command { type = "moveItem", itemId = first.id, containerId = t.g.gridRef, x = t.x, y = t.y, turned = h.turned, count = h.items.Count });
            else if (t.swap != null) Do(new Command { type = "swapItems", itemId = first.id, id = t.swap.id });
            else Say(t.why);
            return;
        }
        if (pos is not { } p) return;
        var hit = HitAt(p);
        if (hit.slot != null) { Wear(first, hit.slot.slot); return; }
        if (hit.quick != null) { Do(new Command { type = "setQuick", slot = hit.quick.n, itemId = first.id }); return; }
        if (panels.Any((panel) => panel.GetGlobalRect().HasPoint(p))) return;
        // Off the panels: onto the ground at your feet.
        Do(new Command { type = "moveItem", itemId = first.id, containerId = Inventory.GROUND, count = h.items.Count });
    }

    bool OnKey(InputEventKey k)
    {
        var key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
        if (held != null)
        {
            switch (key)
            {
                case Key.R:
                    held.turned = !held.turned;
                    (held.grabX, held.grabY) = (held.grabY, held.grabX);
                    if (held.keyboard) AimCell(focus, cx - held.grabX, cy - held.grabY);
                    else if (lastPointer is { } p) Aim(p);
                    return true;
                case Key.Escape:
                    held = null;
                    aim = null;
                    return true;
            }
            if (!held.keyboard) return false;
        }
        switch (key)
        {
            case Key.Left: MoveCursor(-1, 0); return true;
            case Key.Right: MoveCursor(1, 0); return true;
            case Key.Up: MoveCursor(0, -1); return true;
            case Key.Down: MoveCursor(0, 1); return true;
            case Key.Q: CycleFocus(-1); return true;
            case Key.E: CycleFocus(1); return true;
        }
        if (held != null)
        {
            if (key is Key.Space or Key.Enter or Key.KpEnter)
            {
                AimCell(focus, cx - held.grabX, cy - held.grabY);
                PutDown(null);
                return true;
            }
            return true; // holding from the keyboard: nothing else
        }
        var at = focus?.StackAtCell(cx, cy);
        var item = at?.first;
        switch (key)
        {
            case Key.Space:
                if (item != null) PickUp(item, at, cx - at!.x, cy - at.y, true);
                return true;
            case Key.Enter or Key.KpEnter:
                if (item != null) Use(item);
                return true;
            case Key.F:
                if (item != null) QuickMove(item, k.ShiftPressed ? 1 : null);
                return true;
            case Key.G:
                if (item != null) Do(new Command { type = "moveItem", itemId = item.id, containerId = Inventory.GROUND, count = k.ShiftPressed ? at!.items.Count : 1 });
                return true;
            case Key.X:
                if (item != null) Do(new Command { type = "splitStack", itemId = item.id });
                return true;
            case Key.R:
                if (item != null) Turn(item, at!);
                return true;
            case Key.Z:
                if (focus != null && !focus.ground) Do(new Command { type = "arrange", containerId = focus.gridRef });
                return true;
            case >= Key.Key1 and <= Key.Key9:
                if (item == null || you == null || !Inventory.yours(you, item)) return false;
                Do(new Command { type = "setQuick", slot = (int)(key - Key.Key1), itemId = item.id });
                return true;
        }
        return false;
    }

    void MoveCursor(int dx, int dy)
    {
        if (focus == null) { focus = grids.FirstOrDefault(); return; }
        int nx = cx + dx, ny = cy + dy;
        if (ny < 0 || ny >= focus.rows || nx < 0 || nx >= focus.w)
        {
            // Off the edge: the next grid that way (down/right) or back (up/left).
            int i = grids.IndexOf(focus) + ((dx + dy) > 0 ? 1 : -1);
            if (i < 0 || i >= grids.Count) return;
            focus = grids[i];
            cx = Math.Clamp(nx, 0, focus.w - 1);
            cy = (dx + dy) > 0 && dy != 0 ? 0 : Math.Clamp(ny, 0, focus.rows - 1);
            if (dy < 0) cy = focus.rows - 1;
            return;
        }
        cx = nx;
        cy = ny;
    }

    void CycleFocus(int by)
    {
        if (grids.Count == 0) return;
        int i = focus != null ? grids.IndexOf(focus) : 0;
        focus = grids[((i + by) % grids.Count + grids.Count) % grids.Count];
        cx = Math.Min(cx, focus.w - 1);
        cy = Math.Min(cy, Math.Max(0, focus.rows - 1));
    }

    // --- what the moves do ----------------------------------------------------------------------------

    /// <summary>Shift-click, ctrl-click, F: yours to the open container; the container's or the ground's into yours; what you wear off into the bag.</summary>
    void QuickMove(Item item, int? count)
    {
        if (state == null || you == null) return;
        if (Inventory.slotOf(you, item.id) != null) { Do(new Command { type = "unequip", itemId = item.id }); return; }
        if (Inventory.yours(you, item))
        {
            if (boxId == null) { Say("nothing open to put it in"); return; }
            Do(new Command { type = "quickMove", itemId = item.id, containerId = boxId, count = count });
            return;
        }
        Do(new Command { type = "quickMove", itemId = item.id, count = count });
    }

    /// <summary>Eat, use, wear or wield (taking it first if it isn't yours); open something with pockets that isn't yours.</summary>
    void Use(Item item)
    {
        if (state == null || you == null) return;
        if (Verb(you, item.kind) == null)
        {
            if (Inventory.hasGrid(item.kind) && !Inventory.yours(you, item)) { view.openContainer = item.id; return; }
            Say("nothing to do with it here");
            return;
        }
        if (Inventory.slotOf(you, item.id) != null) { Do(new Command { type = "unequip", itemId = item.id }); return; }
        if (!Inventory.yours(you, item) && !Do(new Command { type = "take", itemId = item.id })) return;
        Do(new Command { type = "useItem", itemId = item.id });
    }

    void Wear(Item item, string slot)
    {
        if (Inventory.slotFor(item.kind) != slot)
        {
            Say($"{SLOT_NAMES[slot]}: that doesn't go there");
            return;
        }
        if (you != null && !Inventory.yours(you, item) && !Do(new Command { type = "take", itemId = item.id })) return;
        Do(new Command { type = "useItem", itemId = item.id });
    }

    void Turn(Item item, Placed at)
    {
        if (state == null || you == null || Inventory.gridOf(state, you, item) is not { } gridRef) return;
        Do(new Command { type = "moveItem", itemId = item.id, containerId = gridRef, x = at.x, y = at.y, turned = !at.turned, count = at.items.Count });
    }

    // --- the right-click menu -----------------------------------------------------------------------

    const int M_USE = 1, M_ACROSS = 2, M_ONE_ACROSS = 3, M_OPEN = 4, M_SPLIT = 5, M_TURN = 6, M_DROP = 7, M_DROP_ALL = 8, M_OFF = 9, M_QUICK = 100, M_UNQUICK = 120;

    void ShowMenu(Item item, string? slot, Vector2 at)
    {
        if (state == null || you == null) return;
        menuItem = item;
        menuSlot = slot;
        menu.Clear();
        quickMenu.Clear();
        var stack = slot == null ? Inventory.stackOf(state, you, item) : new List<Item> { item };
        bool mine = Inventory.yours(you, item);
        bool worn = Inventory.slotOf(you, item.id) != null;
        if (Verb(you, item.kind) is { } verb && !worn) menu.AddItem(Capitalise(verb), M_USE);
        if (worn) menu.AddItem("Take off", M_OFF);
        if (Inventory.hasGrid(item.kind) && !mine) menu.AddItem(item.kind == "corpse" ? "Search the body" : "Open its pockets", M_OPEN);
        if (!worn)
        {
            string across = mine ? (boxId != null ? $"Put in the {Inventory.gridName(state, boxId).ToLowerInvariant()}" : "") : "Take";
            if (across != "")
            {
                menu.AddItem(stack.Count > 1 ? $"{across} (all {stack.Count})" : across, M_ACROSS);
                if (stack.Count > 1) menu.AddItem($"{across} (one)", M_ONE_ACROSS);
            }
        }
        if (stack.Count > 1 && Inventory.gridOf(state, you, item) != null) menu.AddItem($"Split ({stack.Count / 2} off)", M_SPLIT);
        var (w, h, _) = Inventory.sizeOf(item);
        if (w != h && Inventory.gridOf(state, you, item) != null) menu.AddItem("Turn (R)", M_TURN);
        if (item.pos == null) menu.AddItem("Drop", M_DROP);
        if (item.pos == null && stack.Count > 1) menu.AddItem($"Drop all {stack.Count}", M_DROP_ALL);
        if (mine)
        {
            for (int n = 0; n < Inventory.QUICK_SLOTS; n++)
                quickMenu.AddItem($"{n + 1}{(Inventory.quickItem(state, you, n) is { } q ? $"  ({Items.nameOf(q)})" : "")}", M_QUICK + n);
            quickMenu.AddItem("None", M_UNQUICK);
            menu.AddSubmenuNodeItem("Quick slot", quickMenu);
        }
        menu.Position = new Vector2I((int)at.X, (int)at.Y);
        menu.ResetSize();
        menu.Popup();
    }

    void OnMenu(int id)
    {
        var item = menuItem;
        if (item == null || state == null || you == null) return;
        var stack = menuSlot == null ? Inventory.stackOf(state, you, item) : new List<Item> { item };
        switch (id)
        {
            case M_USE: Use(item); break;
            case M_OFF: Do(new Command { type = "unequip", itemId = item.id }); break;
            case M_OPEN: view.openContainer = item.id; break;
            case M_ACROSS: QuickMove(item, null); break;
            case M_ONE_ACROSS: QuickMove(item, 1); break;
            case M_SPLIT: Do(new Command { type = "splitStack", itemId = item.id }); break;
            case M_TURN:
                if (Inventory.gridOf(state, you, item) is { } gridRef && Inventory.layout(state, you, gridRef).FirstOrDefault((p) => p.items.Contains(item)) is { } at) Turn(item, at);
                break;
            case M_DROP: Do(new Command { type = "moveItem", itemId = item.id, containerId = Inventory.GROUND, count = 1 }); break;
            case M_DROP_ALL: Do(new Command { type = "moveItem", itemId = item.id, containerId = Inventory.GROUND, count = stack.Count }); break;
            case M_UNQUICK:
                if (you.quick?.IndexOf(item.id) is int q && q >= 0) Do(new Command { type = "setQuick", slot = q });
                break;
            case >= M_QUICK and < M_QUICK + 9: Do(new Command { type = "setQuick", slot = id - M_QUICK, itemId = item.id }); break;
        }
    }

    bool Do(Command c)
    {
        var result = command(c);
        if (!result.ok && result.reason != null) Say(result.reason);
        return result.ok;
    }

    void Say(string text)
    {
        said.Text = text;
        saidUntil = Godot.Time.GetTicksMsec() + 3000;
    }

    // --- what things are called and look like -------------------------------------------------------

    internal static string NameOf(Item item) => Items.nameOf(item)
        + (Clothing.isGarment(item.kind) && item.wear > 0 ? $" {Math.Max(0, 100 - Math.Round(100 * item.wear / Clothing.GARMENT_LIFE[item.kind]))}%" : "");

    /// <summary>A stack's label: its name (or its kind's word, if the names differ) and how many.</summary>
    internal static string Label(List<Item> stack) => stack.Count > 1 ? $"{Items.stackName(stack)} ×{stack.Count}" : NameOf(stack[0]);

    /// <summary>What "use" means for a kind, or null if it has no use from the bag.</summary>
    string? Verb(Survivor s, string kind)
    {
        if (kind is "food" or "meal") return "eat";
        if (kind is "bandage" or "medicine" or "medkit") return Medicine.remediesFor(s).includes(kind) ? "use" : null;
        return Inventory.slotFor(kind) switch { "weapon" => "wield", "tool" => "hold", null => null, _ => "wear" };
    }

    /// <summary>A tile's colour by what it's for: food, medicine, weapons and tools, clothing, materials, books.</summary>
    internal static Color KindColour(string kind) => Ctx.Css(kind switch
    {
        "food" or "meal" => "#5e5230",
        "bandage" or "medicine" or "medkit" or "sample" => "#5e3434",
        "weapon" or "gun" or "ammo" or "axe" or "hammer" => "#434a55",
        "coat" or "armour" or "boots" or "cloth" => "#3b5240",
        "wood" or "scrap" or "fuel" or "cart" => "#54473a",
        "book" => "#463c55",
        "corpse" => "#3a3030",
        _ => "#4a4a4a",
    });

    internal static string kg(double tenths) => (tenths / 10).ToString("0.0");
    static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // --- self-test hooks -------------------------------------------------------------------------------

    GridView? GridOf(string gridRef) => grids.FirstOrDefault((g) => g.gridRef == gridRef && IsInstanceValid(g));

    /// <summary>The middle of a grid's cell on screen, or null if that grid isn't shown.</summary>
    public Vector2? CellCentre(string gridRef, int x, int y) => GridOf(gridRef) is { } g ? g.GetGlobalRect().Position + new Vector2((x + 0.5f) * CELL, (y + 0.5f) * CELL) : null;

    /// <summary>The middle of a thing's tile on screen (in a grid, on the ground, or in a slot), or null.</summary>
    public Vector2? ItemCentre(string itemId)
    {
        foreach (var g in grids)
            if (g.stacks.FirstOrDefault((p) => p.items.Any((i) => i.id == itemId)) is { } p)
                return g.GetGlobalRect().Position + new Vector2((p.x + p.w / 2f) * CELL, (p.y + p.h / 2f) * CELL);
        return slots.FirstOrDefault((t) => t.item?.id == itemId)?.GetGlobalRect().GetCenter();
    }

    /// <summary>The middle of a slot's tile ("body"), on screen.</summary>
    public Vector2 SlotCentre(string slot) => slots.First((t) => t.slot == slot).GetGlobalRect().GetCenter();

    /// <summary>A point on the world behind the screen, clear of the panels: a drop there is a drop at your feet.</summary>
    public Vector2 GroundPoint() => new(20, GetViewportRect().Size.Y - 20);

    /// <summary>The names on the tiles shown now.</summary>
    public IEnumerable<string> ShownNames() => grids.SelectMany((g) => g.stacks.Select((p) => Items.nameOf(p.first)));

    /// <summary>The keyboard cursor's grid and cell.</summary>
    public (string? grid, int x, int y) Cursor => (focus?.gridRef, cx, cy);
}

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
