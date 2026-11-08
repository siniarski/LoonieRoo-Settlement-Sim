// The inventory screen (Tab in first person): what you carry and what you wear, the bag's weight, and
// use / drop / wear / take off. Beside a container (a crate, a shelf, a fridge, a car's boot) it stands
// next to yours, and things move between the two: click a row for one, shift-click for the whole stack
// (every one of a kind), or drag a row across (shift while dragging: the stack). Using a container
// opens the screen on it: a searched one at once (Game's use), an unsearched one when the search is
// done, a car's boot when it's open (U).
//
// Reads state only; every move is a sim command (inventory.ts: take, takeAll, stash, stashAll, drop,
// useItem, unequip) through the game's command path. What's open is view state (View.openContainer).

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
    readonly View view;
    readonly Func<Command, CommandResult> command;
    readonly DropPanel you, box;
    readonly Label said = Kit.Label("", Ctx.Css("#ffb347"), 12);
    string key = "\0";
    /// <summary>A container being searched, or a boot being opened: the screen opens on it when that's done.</summary>
    string? opening;
    ulong saidUntil;

    static readonly Dictionary<string, string> SLOT_NAMES = new() { ["weapon"] = "In hand", ["body"] = "Body", ["feet"] = "Feet", ["tool"] = "Tool" };
    const int NAME_W = 120;

    public InventoryScreen(View view, Func<Command, CommandResult> command)
    {
        this.view = view;
        this.command = command;
        Theme = Kit.MakeTheme();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        // The world and the HUD dimmed behind; the two columns a little above the middle.
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var outer = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        outer.AddThemeConstantOverride("separation", 6);
        outer.AnchorLeft = outer.AnchorRight = 0.5f;
        outer.AnchorTop = outer.AnchorBottom = 0.16f;
        outer.GrowHorizontal = GrowDirection.Both;
        AddChild(outer);
        var title = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        var titleBox = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        titleBox.AddThemeStyleboxOverride("panel", Kit.Box("rgba(20, 23, 21, 0.9)", "#c9b88a", 4, 5));
        titleBox.AddChild(Kit.Label("Inventory · Tab or Esc to close · click: one · shift-click: the stack · drag across · 1-9 use", Kit.Title, 12));
        title.AddChild(titleBox);
        outer.AddChild(title);
        var columns = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        columns.AddThemeConstantOverride("separation", 10);
        outer.AddChild(columns);
        you = new DropPanel("bag", (from) => from == "box", (data) => Move(data));
        box = new DropPanel("box", (from) => from == "bag", (data) => Move(data));
        columns.AddChild(you);
        columns.AddChild(box);
        var saidRow = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        saidRow.AddChild(said);
        outer.AddChild(saidRow);
    }

    /// <summary>The container open beside you: the one you used, else a car's boot or a container beside you. Null if none.</summary>
    public static (string id, string title, double max)? Container(SimState state, Survivor s, string? open)
    {
        if (Vehicles.vehicleOf(state, s) != null) return null;
        if (open != null && Inventory.containerOf(state, open) is { } o && Commands.besideYou(s, o.at)) return (open, TitleOf(state, open), o.max);
        var car = Vehicles.vehicleNear(state, s.at, Motion.SUB * 1.5);
        if (car != null && Inventory.containerOf(state, car.id) is { } b && Commands.besideYou(s, b.at)) return (car.id, TitleOf(state, car.id), b.max);
        Furniture? searched = null;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var f = FurnitureMod.furnitureAt(state, s.pos.x + dx, s.pos.y + dy);
                if (f?.kind != "crate") continue;
                if (state.items.Any((i) => i.inside == f.id)) return (f.id, TitleOf(state, f.id), Inventory.CRATE_HOLD);
                if (f.loot <= 0) searched ??= f;
            }
        return searched != null ? (searched.id, TitleOf(state, searched.id), Inventory.CRATE_HOLD) : null;
    }

    static string TitleOf(SimState state, string id)
    {
        var f = FurnitureMod.getFurniture(state, id);
        if (f != null) return Capitalise(FurnitureMod.containerName(f));
        var v = Vehicles.vehiclesOf(state).FirstOrDefault((x) => x.id == id);
        return v?.kind == "truck" ? "Truck's boot" : "Car's boot";
    }

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
        var s = ControlMod.controlled(state);
        WatchOpening(state, s);
        Visible = view.firstPerson && view.showInventory && s != null;
        if (!view.showInventory) view.openContainer = null;
        if (!Visible || s == null) return;
        if (saidUntil != 0 && Godot.Time.GetTicksMsec() > saidUntil) { said.Text = ""; saidUntil = 0; }
        var open = Container(state, s, view.openContainer);
        if (open == null) view.openContainer = null;
        var inside = open is { } o ? state.items.Where((i) => i.inside == o.id).ToList() : new List<Item>();
        var worn = Inventory.SLOTS.Select((slot) => Js.GetProp<string?>(s, slot) ?? "-");
        string newKey = $"{s.id}|{string.Join(",", s.pack)}|{string.Join(",", worn)}|{s.carrying}|{s.cart}|{open?.id}|{string.Join(",", inside.Select((i) => i.id))}|{Medicine.remediesFor(s).join(",")}";
        if (newKey == key) return;
        key = newKey;
        BuildYou(state, s, open != null);
        box.Visible = open != null;
        if (open is { } c) BuildBox(state, c, inside);
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

    // --- the two columns -------------------------------------------------------------------------

    void BuildYou(SimState state, Survivor s, bool hasBox)
    {
        var r = you.Clear();
        double weight = Inventory.packWeight(state, s);
        r.Text($"{s.name}", Kit.Title);
        r.Text("Wearing", Kit.Dim);
        foreach (var slot in Inventory.SLOTS)
        {
            var id = Js.GetProp<string?>(s, slot);
            var item = id != null ? Items.getItem(state, id) : null;
            var row = r.Row();
            row.Text(SLOT_NAMES[slot], Kit.Dim).CustomMinimumSize = new Vector2(70, 0);
            row.Text(item != null ? NameOf(item) : "-", item != null ? Kit.Text : Ctx.Css("#5f6862")).CustomMinimumSize = new Vector2(NAME_W, 0);
            if (item == null) continue;
            row.Text(kg(Inventory.ITEM_WEIGHT[item.kind]), Kit.Dim).CustomMinimumSize = new Vector2(36, 0);
            row.Button("take off", () => Do(new Command { type = "unequip", itemId = item.id }), tip: "Into your bag");
            row.Button("drop", () => Do(new Command { type = "drop", itemId = item.id }), tip: "At your feet");
        }
        if (s.carrying != null && Items.getItem(state, s.carrying) is { } held) r.Text($"Carrying  {NameOf(held)}", Kit.Dim);
        if (s.cart != null) r.Text("Pulling a cart", Kit.Dim);
        r.Text("");
        r.Text($"Bag  {kg(weight)} / {kg(Inventory.PACK_MAX)} kg", Kit.Title);
        r.Add(new BarFill(Math.Min(1, weight / Inventory.PACK_MAX), Ctx.Css(weight > Inventory.PACK_MAX * 0.85 ? "#ffb347" : "#c8c8a0")) { CustomMinimumSize = new Vector2(300, 5) });
        foreach (var stack in Stacks(state, s.pack))
        {
            var first = stack[0];
            // The quick slots (1-9) are the bag's first nine items: show where each stack's first one is.
            int quick = s.pack.IndexOf(first.id);
            var row = you.AddStack(r, first.kind, stack.Select((i) => i.id).ToList(), hasBox, (all) => Stash(stack, all));
            row.Text(quick < 9 ? $"{quick + 1}" : " ", Kit.Dim);
            row.Text(Label(first, stack.Count)).CustomMinimumSize = new Vector2(NAME_W, 0);
            row.Text(kg(stack.Count * Inventory.ITEM_WEIGHT[first.kind]), Kit.Dim).CustomMinimumSize = new Vector2(36, 0);
            if (Verb(state, s, first.kind) is { } verb) row.Button(verb, () => Do(new Command { type = "useItem", itemId = first.id }));
            row.Button("drop", () => Do(new Command { type = "drop", itemId = first.id }), tip: "One, at your feet");
            if (hasBox)
            {
                row.Button("stash", () => Stash(stack, false), tip: "One into the container");
                if (stack.Count > 1) row.Button("all", () => Stash(stack, true), tip: "The whole stack into the container");
            }
        }
        if (s.pack.Count == 0) r.Text("empty: pick things up, or search a container", Ctx.Css("#8f998f"));
        else if (hasBox) r.Button("Stash everything →", () => Do(new Command { type = "stashAll", containerId = box.containerId }));
    }

    void BuildBox(SimState state, (string id, string title, double max) c, List<Item> inside)
    {
        box.containerId = c.id;
        var r = box.Clear();
        double load = Inventory.load(state, c.id);
        r.Text($"{c.title}  {kg(load)} / {kg(c.max)} kg", Kit.Title);
        r.Add(new BarFill(Math.Min(1, load / c.max), Ctx.Css("#c8c8a0")) { CustomMinimumSize = new Vector2(300, 5) });
        foreach (var stack in Stacks(state, inside.Select((i) => i.id).ToList()))
        {
            var first = stack[0];
            var row = box.AddStack(r, first.kind, stack.Select((i) => i.id).ToList(), true, (all) => Take(c.id, stack, all));
            row.Text(Label(first, stack.Count)).CustomMinimumSize = new Vector2(NAME_W, 0);
            row.Text(kg(stack.Count * Inventory.ITEM_WEIGHT[first.kind]), Kit.Dim).CustomMinimumSize = new Vector2(36, 0);
            row.Button("take", () => Take(c.id, stack, false), tip: "One into your bag");
            if (stack.Count > 1) row.Button("all", () => Take(c.id, stack, true), tip: "The whole stack into your bag");
        }
        if (inside.Count == 0) r.Text("empty: stash things from your bag", Ctx.Css("#8f998f"));
        else r.Button("← Take everything", () => Do(new Command { type = "takeAll", containerId = c.id }));
    }

    // --- moves ---------------------------------------------------------------------------------

    void Stash(List<Item> stack, bool all)
    {
        if (box.containerId is not { } id || !box.Visible) return;
        Do(all ? new Command { type = "stashAll", containerId = id, kind = stack[0].kind } : new Command { type = "stash", itemId = stack[0].id, containerId = id });
    }

    void Take(string containerId, List<Item> stack, bool all) =>
        Do(all ? new Command { type = "takeAll", containerId = containerId, kind = stack[0].kind } : new Command { type = "take", itemId = stack[0].id });

    /// <summary>A row dropped on the other column: "bag|kind|all" or "box|kind|one".</summary>
    void Move(string data)
    {
        var parts = data.Split('|');
        if (parts.Length != 3 || box.containerId is not { } id) return;
        bool all = parts[2] == "all";
        if (parts[0] == "bag")
            Do(all ? new Command { type = "stashAll", containerId = id, kind = parts[1] } : new Command { type = "stash", itemId = you.FirstOf(parts[1]), containerId = id });
        else
            Do(all ? new Command { type = "takeAll", containerId = id, kind = parts[1] } : new Command { type = "take", itemId = box.FirstOf(parts[1]) });
    }

    void Do(Command c)
    {
        var result = command(c);
        if (!result.ok && result.reason != null)
        {
            said.Text = result.reason;
            saidUntil = Godot.Time.GetTicksMsec() + 3000;
        }
        key = "\0";
    }

    // --- what things are called --------------------------------------------------------------------

    /// <summary>The ids as stacks: things of a kind together, in the order the first of each comes.</summary>
    static List<List<Item>> Stacks(SimState state, List<string> ids)
    {
        var stacks = new List<List<Item>>();
        foreach (var id in ids)
        {
            var item = Items.getItem(state, id);
            if (item == null) continue;
            var stack = stacks.FirstOrDefault((st) => st[0].kind == item.kind);
            if (stack != null) stack.Add(item);
            else stacks.Add(new List<Item> { item });
        }
        return stacks;
    }

    static string NameOf(Item item) => item.kind switch
    {
        "weapon" => "knife",
        "gun" => "pistol",
        "book" when item.tag != null => $"book ({item.tag})",
        _ => item.kind,
    } + (Clothing.isGarment(item.kind) && item.wear > 0 ? $" {Math.Max(0, 100 - Math.Round(100 * item.wear / Clothing.GARMENT_LIFE[item.kind]))}%" : "");

    static string Label(Item first, int count) => count > 1 ? $"{(first.kind == "book" ? "book" : NameOf(first))} ×{count}" : NameOf(first);

    /// <summary>What "use" means for a kind in the bag, or null if it has no use here.</summary>
    static string? Verb(SimState state, Survivor s, string kind)
    {
        if (kind is "food" or "meal") return "eat";
        if (kind is "bandage" or "medicine" or "medkit") return Medicine.remediesFor(s).includes(kind) ? "use" : null;
        return Inventory.slotFor(kind) switch { "weapon" => "wield", "tool" => "hold", null => null, _ => "wear" };
    }

    static string kg(double tenths) => (tenths / 10).ToString("0.0");
    static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // --- self-test hooks -----------------------------------------------------------------------------

    /// <summary>The middle of the first stack row of `kind` in the bag ("bag") or the container ("box"), on screen, or null.</summary>
    public Vector2? RowCentre(string side, string kind)
    {
        var panel = side == "bag" ? you : box;
        var row = panel.rows.FirstOrDefault((r) => r.kind == kind && IsInstanceValid(r) && !r.IsQueuedForDeletion());
        return row != null ? row.GetGlobalRect().GetCenter() : null;
    }

    public Vector2 PanelCentre(string side) => (side == "bag" ? you : box).GetGlobalRect().GetCenter();

    /// <summary>A button in the row of a slot you wear things in ("body"), or null.</summary>
    public Button? SlotButton(string slot, string text) =>
        you.FindChildren("*", "HBoxContainer", true, false).OfType<HBoxContainer>()
            .Where((h) => h.GetChildren().OfType<Label>().FirstOrDefault()?.Text == SLOT_NAMES[slot])
            .SelectMany((h) => h.GetChildren().OfType<Button>()).FirstOrDefault((b) => b.Text == text);
}

/// <summary>One column of the inventory: takes drops from the other column.</summary>
public sealed partial class DropPanel : PanelContainer
{
    readonly VBoxContainer content = new() { MouseFilter = MouseFilterEnum.Ignore };
    readonly string side;
    readonly Func<string, bool> accepts;
    readonly Action<string> dropped;
    public readonly List<StackRow> rows = new();
    /// <summary>The container column: which container it shows.</summary>
    public string? containerId;

    public DropPanel() : this("", (_) => false, (_) => { }) { }

    public DropPanel(string side, Func<string, bool> accepts, Action<string> dropped)
    {
        this.side = side;
        this.accepts = accepts;
        this.dropped = dropped;
        AddThemeStyleboxOverride("panel", Kit.Box("rgba(20, 23, 21, 0.95)", "#3a423c", 4, 8));
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(330, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        AddChild(content);
    }

    public Rows Clear()
    {
        foreach (var c in content.GetChildren()) c.QueueFree();
        rows.Clear();
        return new Rows(content, 12);
    }

    /// <summary>A stack row: click moves one across, shift-click the stack, and it drags.</summary>
    public Rows AddStack(Rows into, string kind, List<string> ids, bool movable, Action<bool> clicked)
    {
        var row = new StackRow(this, side, kind, ids, movable, clicked);
        into.Add(row);
        rows.Add(row);
        return row.Rows;
    }

    public string? FirstOf(string kind) => rows.FirstOrDefault((r) => r.kind == kind)?.ids.FirstOrDefault();

    public bool CanTake(Variant data) => data.VariantType == Variant.Type.String && data.AsString().Split('|') is { Length: 3 } p && accepts(p[0]);
    public void Take(Variant data) => dropped(data.AsString());
    public override bool _CanDropData(Vector2 atPosition, Variant data) => CanTake(data);
    public override void _DropData(Vector2 atPosition, Variant data) => Take(data);
}

/// <summary>A stack in a column: what it is, how many, and its buttons.</summary>
public sealed partial class StackRow : PanelContainer
{
    readonly DropPanel panel;
    readonly string side;
    public readonly string kind;
    public readonly List<string> ids;
    readonly bool movable;
    readonly Action<bool> clicked;
    readonly HBoxContainer line = new() { MouseFilter = MouseFilterEnum.Ignore };
    public Rows Rows => new(line, 12);
    bool dragged;

    static readonly StyleBoxFlat plain = Kit.Box("rgba(0, 0, 0, 0)", "rgba(0, 0, 0, 0)", 3, 1);
    static readonly StyleBoxFlat lit = Kit.Box("rgba(60, 72, 62, 0.8)", "#4a544c", 3, 1);

    public StackRow() : this(null!, "", "", new(), false, (_) => { }) { }

    public StackRow(DropPanel panel, string side, string kind, List<string> ids, bool movable, Action<bool> clicked)
    {
        this.panel = panel;
        this.side = side;
        this.kind = kind;
        this.ids = ids;
        this.movable = movable;
        this.clicked = clicked;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", plain);
        AddChild(line);
        MouseEntered += () => AddThemeStyleboxOverride("panel", lit);
        MouseExited += () => AddThemeStyleboxOverride("panel", plain);
        TooltipText = movable ? "Click: move one across · Shift-click: the stack · or drag it" : "";
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } b) return;
        if (b.Pressed) { dragged = false; return; }
        if (dragged || !movable) return;
        AcceptEvent();
        clicked(b.ShiftPressed);
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!movable) return default;
        dragged = true;
        bool all = Input.IsKeyPressed(Key.Shift) && ids.Count > 1;
        var preview = new PanelContainer();
        preview.AddThemeStyleboxOverride("panel", Kit.Box("rgba(20, 23, 21, 0.95)", "#c9b88a", 4, 4));
        preview.AddChild(Kit.Label(all ? $"{kind} ×{ids.Count}" : kind, Kit.Title, 12));
        SetDragPreview(preview);
        return $"{side}|{kind}|{(all ? "all" : "one")}";
    }

    // A row in the other column takes the drop for its column.
    public override bool _CanDropData(Vector2 atPosition, Variant data) => panel.CanTake(data);
    public override void _DropData(Vector2 atPosition, Variant data) => panel.Take(data);
}
