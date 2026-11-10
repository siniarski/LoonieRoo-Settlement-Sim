// The HUD (src/ui/hud.ts, card.ts, bag.ts, overview.ts): the top bar, the status line, alerts, the
// toast, the colony's panels (wanderers, traders, crafting, expeditions, neighbours, prisoners, a
// survivor's story, succession, the helicopter), the survivor table, the character card and its
// conditions, the overview, the debug panel and the paused banner. Reads state;
// changes go out as sim commands or view-state changes.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Control = Godot.Control;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Game.Ui;

/// <summary>What the HUD asks of the game.</summary>
public interface IGameFiles
{
    void OpenMenu();
    void NewGamePlus();
    bool Command(Command cmd);
}

public sealed partial class Hud : Control
{
    static readonly Dictionary<string, string> JOB_LABELS = new()
    {
        ["guard"] = "Guard", ["firefight"] = "Fire", ["doctor"] = "Doc", ["farm"] = "Farm", ["build"] = "Build", ["haul"] = "Haul", ["craft"] = "Craft",
        ["research"] = "Lab", ["cook"] = "Cook", ["scout"] = "Scout", ["scavenge"] = "Scav", ["hunt"] = "Hunt", ["clean"] = "Clean",
    };
    const int DEBUG_JOB_LINES = 12;
    /// <summary>Most recent events shown in the feed under the status line.</summary>
    const int FEED_LINES = 4;
    const int MAX_ALERTS = 6;
    static readonly Dictionary<string, string> LEVEL_COLOUR = new() { ["danger"] = "#ff7a6a", ["warn"] = "#ffd166", ["info"] = "#9fc3ff" };

    readonly View view;
    readonly IGameFiles files;
    static string J(object o) => JsonSerializer.Serialize(o);

    // top bar
    Label clock = null!;
    Button buildButton = null!, prioritiesButton = null!, overviewButton = null!, colonyButton = null!, bagButton = null!;
    readonly List<(int speed, Button button)> speedButtons = new();
    // left column
    readonly Label status = Kit.Label("", Kit.Dim, 12);
    readonly UiPanel controlLine = new("#f0c850");
    /// <summary>First person with a gun in hand: its rounds, the bag's, and what to do (cs/Sim/Systems/WeaponTable.cs).</summary>
    readonly UiPanel ammoReadout = new("#f0c850", "rgba(20, 23, 21, 0.85)", 14);
    readonly UiPanel alerts = new("#5a4a3a", "rgba(20, 23, 21, 0.85)");
    readonly UiPanel toast = new("rgba(140, 240, 164, 0.95)", "rgba(140, 240, 164, 0.95)", 13);
    readonly UiPanel succession = new("#c9b88a");
    readonly UiPanel helicopter = new("#c9b88a");
    readonly UiPanel story = new("#c9b88a");
    readonly UiPanel room = new("#333a35", "rgba(20, 23, 21, 0.92)");
    readonly UiPanel wanderers = new("#c9b88a");
    readonly UiPanel traders = new("#c9b88a");
    readonly UiPanel crafting = new("#be9664");
    readonly UiPanel expeditions = new("#be9664");
    readonly UiPanel neighbours = new("#c9b88a");
    readonly UiPanel prisoners = new("#c9b88a");
    readonly UiPanel feed = new("rgba(20, 23, 21, 0.85)", "rgba(20, 23, 21, 0.85)");
    // elsewhere
    readonly UiPanel priorities = new("#333a35", "rgba(20, 23, 21, 0.92)");
    readonly UiPanel card = new("#3a423c", "rgba(20, 23, 21, 0.94)");
    readonly UiPanel conditions = new("#3a423c", "rgba(20, 23, 21, 0.9)");
    readonly UiPanel overview = new("#6c9a78", "rgba(20, 23, 21, 0.97)");
    readonly UiPanel debug = new("#333a35", "rgba(10, 12, 11, 0.85)", 11);
    readonly UiPanel pausedBanner = new("rgba(255, 196, 92, 0.95)", "rgba(255, 196, 92, 0.95)", 13);
    public readonly BuildBar buildBar;
    public readonly Minimap minimap;
    string? lastControlled;
    int frames;
    /// <summary>First person: the dot in the middle of the screen.</summary>
    readonly Crosshair crosshair = new();
    int overviewFrame;

    public Hud(View view, IGameFiles files)
    {
        this.view = view;
        this.files = files;
        buildBar = new BuildBar(view);
        minimap = new Minimap(view);
        Theme = Kit.MakeTheme();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Top left: the bar, then the info column.
        var left = new VBoxContainer { Position = new Vector2(12, 12), MouseFilter = MouseFilterEnum.Ignore };
        left.AddThemeConstantOverride("separation", 6);
        AddChild(left);
        left.AddChild(MakeBar());
        var info = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        info.AddThemeConstantOverride("separation", 6);
        left.AddChild(info);
        var statusBox = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        statusBox.AddThemeStyleboxOverride("panel", Kit.Box("rgba(20, 23, 21, 0.85)", "rgba(20, 23, 21, 0.85)", 4, 3));
        statusBox.AddChild(status);
        foreach (var c in new Control[] { statusBox, succession, helicopter, controlLine, alerts, toast, story, room, wanderers, traders, crafting, expeditions, neighbours, prisoners, feed })
        {
            // Each panel as wide as what it says, like the browser's.
            c.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            info.AddChild(c);
        }
        priorities.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        card.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        foreach (var c in new Control[] { minimap }) c.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;

        // Bottom left: the survivor table over the character card.
        var bottomLeft = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, GrowVertical = GrowDirection.Begin };
        bottomLeft.SetAnchorsPreset(LayoutPreset.BottomLeft);
        bottomLeft.Position = new Vector2(12, -12);
        bottomLeft.AddThemeConstantOverride("separation", 6);
        bottomLeft.AddChild(priorities);
        bottomLeft.AddChild(card);
        AddChild(bottomLeft);
        Pin(bottomLeft, 0, 1, 12, -12, GrowDirection.End, GrowDirection.Begin);

        // Top right: conditions (under the M screen's tabs); bottom right: the minimap (the bag and containers are the inventory screen, Inventory.cs).
        Pin(conditions, 1, 0, -12, 52, GrowDirection.Begin, GrowDirection.End);
        AddChild(conditions);
        var bottomRight = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        bottomRight.AddThemeConstantOverride("separation", 6);
        bottomRight.AddChild(minimap);
        AddChild(bottomRight);
        Pin(bottomRight, 1, 1, -12, -12, GrowDirection.Begin, GrowDirection.Begin);

        // Right: the debug panel; middle top: the overview; bottom middle: the build bar and the paused banner.
        AddChild(debug);
        Pin(debug, 1, 0, -12, 96, GrowDirection.Begin, GrowDirection.End);
        AddChild(overview);
        Pin(overview, 0.5f, 0, 0, 58, GrowDirection.Both, GrowDirection.End);
        var bottom = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        bottom.AddThemeConstantOverride("separation", 6);
        var bannerRow = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        bannerRow.AddChild(pausedBanner);
        bottom.AddChild(bannerRow);
        bottom.AddChild(buildBar);
        AddChild(bottom);
        Pin(bottom, 0.5f, 1, 0, -12, GrowDirection.Both, GrowDirection.Begin);

        AddChild(crosshair);
        crosshair.SetAnchorsPreset(LayoutPreset.Center);
        AddChild(ammoReadout);
        Pin(ammoReadout, 0.5f, 1, 0, -64, GrowDirection.Both, GrowDirection.Begin);
    }

    /// <summary>The gun in your hand: its rounds of its magazine, the bag's, and what to do about an empty one.</summary>
    void UpdateAmmo(SimState state)
    {
        var you = ControlMod.controlled(state);
        var gun = (you != null && view.firstPerson && !view.showInventory && view.menu == null) ? Weapons.heldGun(state, you) : null;
        string text = "";
        if (gun != null && you != null)
        {
            var w = Weapons.specOf(gun)!;
            double bag = 0;
            foreach (var id in you.pack)
                if (Sim.Items.getItem(state, id) is { } box && box.kind == w.ammo) bag += Weapons.rounds(box);
            var loaded = Weapons.loadedGun(state, you);
            string tail = state.control?.busy?.what == "reload" ? "   reloading..."
                : loaded == null ? (gun.wear > 0 ? "   (hands full)" : bag > 0 ? "   X: reload" : "   out of ammunition")
                : gun.wear < w.magazine && bag > 0 && gun.wear <= w.magazine / 3 ? "   X: reload" : "";
            text = $"{Sim.Items.nameOf(gun)}   {gun.wear:0} / {w.magazine}   ({bag:0} in your bag){tail}";
        }
        ammoReadout.Show(text != "", text, (r) => r.Text(text, Kit.Gold, 480));
    }

    static void Pin(Control c, float ax, float ay, float x, float y, Control.GrowDirection gx, Control.GrowDirection gy)
    {
        c.AnchorLeft = c.AnchorRight = ax;
        c.AnchorTop = c.AnchorBottom = ay;
        c.OffsetLeft = c.OffsetRight = x;
        c.OffsetTop = c.OffsetBottom = y;
        c.GrowHorizontal = gx;
        c.GrowVertical = gy;
    }

    Control MakeBar()
    {
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        var bar = new HBoxContainer();
        panel.AddChild(bar);
        clock = Kit.Label("", Ctx.Css("#f0d39a"));
        clock.CustomMinimumSize = new Vector2(150, 0);
        bar.AddChild(clock);
        foreach (var speed in new[] { 1, 2, 4 })
        {
            var b = Kit.Button($"{speed}x", () => { view.speed = speed; view.paused = false; });
            bar.AddChild(b);
            speedButtons.Add((speed, b));
        }
        bar.AddChild(new VSeparator());
        buildButton = Kit.Button("B Build", () => BuildBar.toggleBuild(view));
        prioritiesButton = Kit.Button("P Priorities", () => view.showPriorities = !view.showPriorities);
        var menuButton = Kit.Button("☰ Menu", () => files.OpenMenu(), tip: "Saves, loading, a new game (Esc)");
        overviewButton = Kit.Button("O Overview", () => view.showOverview = !view.showOverview);
        colonyButton = Kit.Button("M Map", () => view.openTab("map"), tip: "The map, and the colony's management in its Colony tab (M)");
        bagButton = Kit.Button("Tab Bag", () => view.showInventory = !view.showInventory, tip: "Your bag, what you wear, and the container beside you (Tab)");
        foreach (var b in new[] { buildButton, prioritiesButton, menuButton, overviewButton, colonyButton, bagButton }) bar.AddChild(b);
        return panel;
    }

    void Command(Command cmd) => files.Command(cmd);

    public void Update(SimState state, string debugPerf)
    {
        bool night = Time.isNight(state.tick);
        string weather = state.weather == "clear" ? "" : $", {state.weather}";
        clock.Text = $"Day {Time.dayOf(state.tick)} · {Time.clockText(state.tick)} {(night ? "☾" : "☀")} · {Seasons.seasonOf(state.tick)}{weather}";
        clock.AddThemeColorOverride("font_color", Ctx.Css(night ? "#9cb4ff" : "#f0d39a"));

        var you = ControlMod.controlled(state);
        // Taking control of someone starts in survivor mode.
        var nowControlled = you?.id;
        bool seenFrame = lastControlled != null || frames++ > 0;
        if (nowControlled != lastControlled)
        {
            if (nowControlled != null && seenFrame) view.closeMScreen(); // into first person (not on the first frame: the command line may open a tab)
            lastControlled = nowControlled;
        }
        bool solo = view.survivorMode(you != null);
        colonyButton.Visible = view.firstPerson;
        bagButton.Visible = !view.firstPerson && you != null && view.colonyMode && view.menu == null;
        Kit.SetActive(bagButton, view.showInventory);
        foreach (var b in new[] { buildButton, prioritiesButton, overviewButton }) b.Visible = !solo;

        string controlText = you != null
            ? view.showKeys
                ? view.firstPerson
                    ? "mouse look · WASD walk (in a car: W go, S brake, A/D steer) · Shift run · Space jump, vault, hop · C sneak · E get in or out of a car · U boot · I bonnet · click swing or shoot · X reload · Q next weapon · right-click/E use · R work · V follow me · H hold · L loot (where you look) · T talk · Tab inventory · 1-9 use from bag · M map and colony · F let go · K hide"
                    : "M or Esc: back to first person · Tab your bag · F on someone selected: play them instead · K hide"
                : "K: keys"
            : view.selectedId != null ? "F: take control of the selected survivor" : "";
        controlLine.Show(controlText != "", controlText, (r) => r.Text(controlText, Kit.Gold, 600));
        string toastText = view.toast != null && Godot.Time.GetTicksMsec() < view.toastUntil ? view.toast : "";
        toast.Show(toastText != "", toastText, (r) => r.Text(toastText, Ctx.Css("#12160f"), 520));
        string feedText = string.Join("\n", state.events.TakeLast(FEED_LINES).Select((e) => $"Day {Time.dayOf(e.tick)} {Time.clockText(e.tick)}  {e.message}"));
        feed.Show(feedText != "", feedText, (r) => r.Text(feedText, Ctx.Css("#ffb3a8")));

        // No pause key: the map and the menu hold the world still; --paused (a debug option) until a speed button.
        string pausedText = view.showMap ? "PAUSED · the map is open" : view.paused ? "PAUSED · + or a speed button plays" : "";
        pausedBanner.Show(pausedText != "", pausedText, (r) => r.Text(pausedText, Ctx.Css("#1a1408")));
        Kit.SetActive(buildButton, view.buildOpen);
        buildBar.Update(state);
        foreach (var (speed, button) in speedButtons) Kit.SetActive(button, !view.halted && speed == view.speed);
        Kit.SetActive(prioritiesButton, view.showPriorities);
        Kit.SetActive(overviewButton, view.showOverview);
        UpdateAlerts(state);
        UpdateOverview(state, solo);
        minimap.Update(state);

        status.Text = statusText(state, solo);
        var roomLines = roomText(state, view.hover);
        room.Show(roomLines != "", roomLines, (r) => r.Text(roomLines));

        UpdatePriorities(state, solo);
        UpdateConditions(state);
        UpdateCard(state);
        UpdateAmmo(state);
        bool stowed = view.firstPerson && !view.showInventory;
        if (stowed) card.Visible = false;
        crosshair.Visible = view.firstPerson && !view.showInventory && view.menu == null;
        UpdateWanderers(state, solo);
        UpdateCrafting(state, solo);
        UpdateExpeditions(state, solo);
        UpdateTraders(state, solo);
        UpdateNeighbours(state, solo);
        UpdatePrisoners(state, solo);
        UpdateStory(state);
        UpdateSuccession(state);
        UpdateHelicopter(state);

        if (view.showDebug)
        {
            var text = debugText(state, debugPerf);
            debug.Show(true, text, (r) => r.Text(text));
        }
        else debug.Visible = false;
    }

    // --- the status line -------------------------------------------------------------------------

    /// <summary>Behind the wheel: speed, fuel, and how the car is holding up.</summary>
    static string drivingText(SimState state)
    {
        var you = ControlMod.controlled(state);
        var car = you != null ? Vehicles.vehicleOf(state, you) : null;
        if (car == null) return "";
        double tiles = Math.Round(Math.Abs(car.speed) * 10 / Motion.SUB);
        return $"driving {(car.speed < 0 ? "backwards " : "")}{tiles} tiles/s · fuel {Math.Round(car.fuel * 100 / Vehicles.FUEL_MAX)}% · car {Math.Round(car.health * 100 / Vehicles.VEHICLE_HEALTH)}%";
    }

    string statusText(SimState state, bool solo)
    {
        var hover = view.hover;
        var grid = state.grid;
        string? tile = hover != null && hover.x >= 0 && hover.y >= 0 && hover.x < grid.width && hover.y < grid.height ? Grids.tileAt(grid, hover.y * grid.width + hover.x) : null;
        var selected = state.survivors.FirstOrDefault((s) => s.id == view.selectedId);
        int wood = state.items.Count((i) => i.kind == "wood");
        int food = state.items.Count((i) => i.kind is "food" or "meal");
        string mode = view.tool != null ? $"building: {Tools.getTool(view.tool).label}" : view.buildOpen ? "build: pick a tool" : "";
        bool isYou = state.control?.survivorId == selected?.id;
        string place = hover != null && tile != null
            ? Grids.exploredAt(grid, hover.y * grid.width + hover.x) == 1
                ? $"({hover.x}, {hover.y}) {tile}{furnitureText(state, hover)}{(state.world != null ? $" · {Climate.climateAt(state, hover.x, hover.y)}{(Climate.isRadioactive(state, hover.x, hover.y) ? " · RADIOACTIVE" : "")}" : "")}"
                : $"({hover.x}, {hover.y}) unexplored"
            : "";
        var stores = new[] { solo ? "" : $"wood {wood} · food {food} · jobs {state.jobs.Count}", state.timeline != null ? $"water {(TimelineMod.waterOn(state) ? "on" : "off")} · power {(TimelineMod.powerOn(state) ? "on" : "off")}" : "" }.Where((x) => x != "");
        return string.Join("  ·  ", new[] { mode, selected != null && !isYou ? $"{selected.name}: right-click to move" : "", place, drivingText(state), string.Join(" · ", stores) }.Where((x) => x != ""));
    }

    /// <summary>" · crate (3 left, marked)" etc. for the hovered tile's furniture, or "".</summary>
    static string furnitureText(SimState state, Point p)
    {
        var plot = state.farm.FirstOrDefault((x) => x.x == p.x && x.y == p.y);
        if (plot != null)
            return $" · {plot.crop} ({(!plot.planted ? "empty, needs sowing" : Farming.isRipe(plot) ? "ripe" : $"growing {Math.Floor(100 * plot.growth / Farming.growTicks(plot))}%")})";
        var f = state.furniture.FirstOrDefault((x) => x.x == p.x && x.y == p.y);
        if (f == null) return "";
        if (f.kind == "bed")
        {
            var owner = f.owner != null ? state.survivors.FirstOrDefault((s) => s.id == f.owner)?.name : null;
            return $" · {(owner != null ? $"{owner}'s bed" : "bed")}{(f.occupant != null ? " (in use)" : "")}";
        }
        if (f.kind == "table") return " · table";
        if (f.kind == "trap") return $" · spike trap ({f.loot} left)";
        if (f.kind == "tree") return $" · tree ({f.loot} wood{(f.scavenge ? ", marked for chopping" : "")})";
        if (f.kind == "workbench") return $" · workbench{(f.stored > 0 ? $" ({f.stored} {f.storedKind ?? "wood"} in)" : "")}";
        if (f.kind == "stove") return $" · stove{(f.stored > 0 ? $" ({f.stored} food in)" : "")}";
        if (f.kind == "decor") return f.look != null ? $" · {FurnitureMod.LOOK_NAMES[f.look]}" : "";
        if (f.kind != "crate") return $" · {f.kind}";
        var name = FurnitureMod.containerName(f);
        return f.loot == 0 ? $" · {name} (searched)" : $" · {name} ({f.loot} left{(f.scavenge ? ", marked" : "")})";
    }

    /// <summary>Requirements checklist for the room (or outdoor room designation) under the cursor.</summary>
    static string roomText(SimState state, Point? hover)
    {
        if (hover == null) return "";
        var r = Rooms.roomAt(state, hover.x, hover.y);
        var designation = r != null ? Rooms.designationOf(state, r) : state.roomDesignations.FirstOrDefault((d) => d.x == hover.x && d.y == hover.y);
        if (r == null && designation == null) return "";
        string header = r != null ? $"Room · {r.tiles.Count} tiles · {r.doors.Count} door{(r.doors.Count == 1 ? "" : "s")}" : "Not a room (open to the outside)";
        if (designation == null) return $"{header}\nno purpose · assign one with a Rooms tool (B)";
        var st = Rooms.evaluateRoom(state, r, designation.purpose);
        return string.Join("\n", new[] { header, $"{Painter.PURPOSE_NAMES[designation.purpose]} · {(st.complete ? "complete" : "INCOMPLETE")}" }.Concat(st.requirements.Select((q) => $" {(q.met ? "✓" : "✗")} {q.label}")));
    }

    // --- alerts and the overview (overview.ts) -----------------------------------------------------

    void UpdateAlerts(SimState state)
    {
        var list = Alerts.alerts(state);
        alerts.Show(list.Count > 0, J(list.Take(MAX_ALERTS).Select((a) => new[] { a.level, a.text, a.at?.x.ToString() ?? "", a.at?.y.ToString() ?? "" })), (r) =>
        {
            foreach (var a in list.Take(MAX_ALERTS))
            {
                string text = $"{(a.level == "danger" ? "▲" : a.level == "warn" ? "●" : "·")} {a.text}";
                if (a.at is { } at)
                {
                    // Click an alert to look at where it is.
                    var b = r.Button(text, () => view.camera = new Cam(at.x + 0.5, at.y + 0.5, view.camera.zoom), tip: $"Look at ({at.x}, {at.y})");
                    b.Flat = true;
                    b.Alignment = HorizontalAlignment.Left;
                    b.AddThemeColorOverride("font_color", Ctx.Css(LEVEL_COLOUR[a.level]));
                }
                else r.Text(text, Ctx.Css(LEVEL_COLOUR[a.level]));
            }
            if (list.Count > MAX_ALERTS) r.Text($"… {list.Count - MAX_ALERTS} more (O: overview)");
        });
    }

    static string pct(double v, double max) => $"{Math.Round(100 * v / max)}%";

    /// <summary>O: the whole colony on one panel. Rebuilt a few times a second at most.</summary>
    void UpdateOverview(SimState state, bool solo)
    {
        if (!view.showOverview || solo) { overview.Visible = false; return; }
        if (overviewFrame++ % 15 != 0 && overview.Visible) return;
        overview.Invalidate();
        overview.Show(true, "x", (r) =>
        {
            r.Text($"Colony overview (O to close) · threat x{Director.threatScale(state):0.0}", Kit.Title);
            var grid = r.Add(new GridContainer { Columns = 9, MouseFilter = MouseFilterEnum.Ignore });
            grid.AddThemeConstantOverride("h_separation", 12);
            var cells = new Rows(grid, 12);
            foreach (var h in new[] { "Name", "Age", "Health", "Mood", "Food", "Water", "Wearing", "Doing", "" }) cells.Text(h, Kit.Dim);
            foreach (var s in state.survivors)
            {
                var job = s.jobId != null ? Jobs.getJob(state, s.jobId) : null;
                string doing = s.sleep != null ? "asleep" : s.meal != null ? "eating" : s.drink != null ? "drinking" : s.reading != null ? "reading" : job != null ? job.type : s.childUntil != null ? "playing" : "idle";
                var weapon = s.weapon != null ? state.items.FirstOrDefault((i) => i.id == s.weapon)?.kind ?? "" : "";
                string wearing = string.Join(", ", new[] { Clothing.describeClothing(state, s), weapon, s.cart != null ? "cart" : "" }.Where((x) => x != ""));
                foreach (var c in new[]
                {
                    s.name + (s.infection != null ? " (infected)" : s.sick != null ? " (sick)" : ""),
                    s.childUntil != null ? $"{s.age} (child)" : $"{s.age}",
                    pct(s.health, Health.HEALTH_MAX), $"{Mood.mood(state, s)}", pct(s.satiety, Food.SATIETY_MAX),
                    state.harsh ? pct(s.hydration, Water.HYDRATION_MAX) : "—", wearing == "" ? "—" : wearing, doing,
                }) cells.Text(c);
                if (Wounds.canAmputate(s)) cells.Button($"Amputate {s.bitten}", () => Command(new Command { type = "amputate", survivorId = s.id }));
                else cells.Text("");
            }
            var away = state.expeditions.SelectMany((x) => x.members.Select((m) => m.name)).ToList();
            r.Text($"People: {state.survivors.Count}{(away.Count > 0 ? $", away: {string.Join(", ", away)}" : "")}{(state.dogs.Count > 0 ? $", dogs: {string.Join(", ", state.dogs.Select((d) => d.name))}" : "")}", Kit.Title);

            double days = Alerts.foodDays(state);
            r.Text($"Stock (food for {(double.IsFinite(days) ? days.ToString("0.0") : "—")} days)", Kit.Title);
            var stock = string.Join(" · ", Alerts.stockCounts(state).Select((kv) => $"{kv.Item1} {kv.Item2}"));
            r.Text(stock == "" ? "nothing" : stock, Ctx.Css("#cccccc"), 860);

            var stores = state.roomDesignations.Where((d) => d.purpose == "storage").ToList();
            if (stores.Count > 0)
            {
                r.Text("Storage rooms (click to take or refuse)", Kit.Title);
                foreach (var d in stores)
                {
                    var row = r.Row();
                    row.Text($"at ({d.x}, {d.y}){(Rooms.designationStatus(state, d).complete ? "" : " (incomplete)")}");
                    foreach (var c in Rooms.STORE_CATEGORIES)
                    {
                        bool takes = d.refuses == null || !d.refuses.Contains(c);
                        row.Button($"{(takes ? "✓" : "✗")} {c}", () => Command(new Command { type = "setStorageFilter", x = d.x, y = d.y, category = c, accept = !takes }), active: takes);
                    }
                }
            }

            var leader = state.survivors.FirstOrDefault((s) => s.id == state.leaderId);
            r.Text($"Policies · leader: {(leader != null ? leader.name : "nobody yet")}", Kit.Title);
            var prow = r.Row();
            foreach (var p in Policies.POLICIES)
            {
                bool on = state.policies.Contains(p);
                prow.Button($"{(on ? "✓" : "✗")} {p}", () => Command(new Command { type = "setPolicy", policy = p, on = !on }), active: on, tip: Policies.POLICY_INFO[p]);
            }

            var eg = state.endgame;
            r.Text("Endgame", Kit.Title);
            r.Text(eg.won != null
                ? $"Won: {(eg.won == "cure" ? "the cure" : "evacuated")}{(eg.escaped > 0 ? $" ({eg.escaped} flown out)" : "")}"
                : $"Cure: {eg.cure}/{EndgameMod.CURE_SAMPLES} samples studied (lab bench, powered) · Evacuation: {(eg.evacuation == null ? $"build a beacon and a radio, both powered (they answer from day {EndgameMod.EVAC_FROM_DAY})" : $"helicopter at dawn on day {Time.dayOf(eg.evacuation.Value)}")}", Ctx.Css("#cccccc"), 860);
            var known = state.knowledge.Select((k) => $"{k} ({Books.KNOWLEDGE[k]})").ToList();
            r.Text("Known", Kit.Title);
            r.Text(known.Count > 0 ? string.Join("; ", known) : "nothing from books yet", Ctx.Css("#cccccc"));
            if (state.settlements.Count > 0)
            {
                r.Text("Neighbours", Kit.Title);
                r.Text(string.Join(" · ", state.settlements.Select((n) => $"{n.name} {n.reputation}")), Ctx.Css("#cccccc"), 860);
            }
            var all = Alerts.alerts(state);
            r.Text($"Alerts ({all.Count})", Kit.Title);
            foreach (var a in all) r.Text(a.text, Ctx.Css(LEVEL_COLOUR[a.level]));
        });
    }

    // --- the colony's panels -----------------------------------------------------------------------

    /// <summary>The helicopter is down: who goes?</summary>
    void UpdateHelicopter(SimState state)
    {
        bool shown = state.endgame.helicopter;
        helicopter.Show(shown, $"{state.survivors.Count}|{state.control?.survivorId}", (r) =>
        {
            r.Text($"The helicopter is down until dusk. {(state.endgame.escaped > 0 ? $"({state.endgame.escaped} flown out so far.) " : "")}Who goes?", Kit.Title);
            r.Button("Everyone (a new start elsewhere)", () => files.NewGamePlus(), tip: "A new game+: a fresh world with the same settings, and you in it alone, with what you carry, your skills and your story.");
            if (state.control != null && state.survivors.Count > 1) r.Button("The others; I stay", () => Command(new Command { type = "escape", who = "others" }), tip: "Everyone but you flies out. You stay, and can call again for anyone else you find.");
            r.Button("Wave it off", () => Command(new Command { type = "escape", who = "none" }), tip: "Nobody goes. You can call again.");
        });
    }

    string lastSuccession = "";

    /// <summary>When the one you controlled dies: who carries on? The game waits for the choice (Game.AwaitingHeir).</summary>
    void UpdateSuccession(SimState state)
    {
        var s = state.succession;
        var ready = s != null ? SuccessionMod.heirs(state) : new List<Survivor>();
        string key = s != null ? J(new object[] { s.name, s.cause, ready.Select((h) => $"{h.id}:{h.resolve}") }) : "";
        lastSuccession = key;
        succession.Show(s != null, key, (r) =>
        {
            r.Text($"{s!.name} died of {s.cause}. Who carries on?", Kit.Title);
            void choose(string label, string choice, string about) => r.Button(label, () =>
            {
                Command(new Command { type = "succeed", choice = choice });
                view.selectedId = state.control?.survivorId ?? view.selectedId;
            }, tip: about);
            foreach (var h in ready) choose($"{h.name} (resolve {h.resolve})", h.id, $"{h.name} takes over: you play as them from here.");
            if (ready.Count == 0 && state.survivors.Count > 0) r.Text($"Nobody left has the resolve to step up ({SuccessionMod.STEP_UP}+).", Ctx.Css("#dddddd"));
            choose("Start anew, far from here", "anew", "A new survivor, alone, somewhere else in this world, with a weapon and a bandage.");
            if (state.survivors.Count > 0) choose("Just watch the colony", "none", "Nobody under your control; the colony runs itself.");
        });
    }

    /// <summary>The selected survivor's story so far, what they've asked of you, and a Talk button.</summary>
    void UpdateStory(SimState state)
    {
        var s = state.survivors.FirstOrDefault((x) => x.id == view.selectedId && x.id != state.control?.survivorId);
        var told = s != null ? StoryMod.storyLines(s.story).Take((int)s.story.told).ToList() : new List<string>();
        var asks = s != null ? Talk.describeTasks(state, s) : new List<string>();
        string wish = s?.wish != null ? $"{Wishes.describeWish(state, s.wish)}|{s.wish.promised}|{Math.Ceiling((s.wish.until - state.tick) / Time.TICKS_PER_HOUR)}" : "";
        story.Show(s != null, J(new object?[] { s?.id, told.Count, asks, wish }), (r) =>
        {
            r.Text($"{s!.name}'s story", Kit.Title);
            foreach (var line in told.Count > 0 ? told.Select((l) => $"\"{l}\"") : new[] { "You don't know much about them yet." }) r.Text(line, Ctx.Css("#dddddd"), 420);
            foreach (var ask in asks) r.Text($"{s.name} {ask}", Kit.Gold, 420);
            if (s.wish != null)
            {
                var row = r.Row();
                double hours = Math.Ceiling((s.wish.until - state.tick) / Time.TICKS_PER_HOUR);
                row.Text($"Wants {Wishes.describeWish(state, s.wish)} ({hours}h left{(s.wish.promised ? ", promised" : "")})", s.wish.promised ? Kit.Gold : null);
                if (!s.wish.promised) row.Button("Promise", () => Command(new Command { type = "promiseWish", survivorId = s.id }), tip: "See it done for a bigger lift, and they think better of you. Let it run out and they are let down.");
            }
            r.Button($"Talk to {s.name}", () => Command(new Command { type = "talk", survivorId = s.id }), tip: "Once every couple of hours. While you control someone, they need to be next to them (or press T).");
        });
    }

    /// <summary>Prisoners in the cells: talk them round, or let them go.</summary>
    void UpdatePrisoners(SimState state, bool solo)
    {
        var rows = state.prisoners.Select((p) => (p, ready: state.tick - p.since >= Prisoners.RECRUIT_DAYS * Time.DAY_LENGTH)).ToList();
        prisoners.Show(rows.Count > 0 && !solo, J(rows.Select((x) => $"{x.p.id}:{x.ready}")), (r) =>
        {
            r.Text("Prisoners (each eats a food a day)", Kit.Title);
            foreach (var (p, ready) in rows)
            {
                var row = r.Row();
                row.Text($"{p.name} at ({p.x}, {p.y})");
                row.Button(ready ? "Recruit" : "Recruit (not yet)", () => Command(new Command { type = "recruitPrisoner", prisonerId = p.id }), disabled: !ready);
                row.Button("Let go", () => Command(new Command { type = "releasePrisoner", prisonerId = p.id }));
            }
        });
    }

    /// <summary>" · allies: X · feuding: Y" for a settlement, or "".</summary>
    static string standing(SimState state, Settlement n)
    {
        var others = state.settlements.Where((o) => o != n && !Colonies.isFallen(o)).ToList();
        var allies = others.Where((o) => Settlements.relation(n, o) == "allies").Select((o) => o.name).ToList();
        var feuds = others.Where((o) => Settlements.relation(n, o) == "feud").Select((o) => o.name).ToList();
        return $"{(allies.Count > 0 ? $" · allies: {string.Join(", ", allies)}" : "")}{(feuds.Count > 0 ? $" · feuding: {string.Join(", ", feuds)}" : "")}";
    }

    readonly Dictionary<string, ulong> raidArmed = new();

    /// <summary>Neighbouring settlements: what they think of you, and a gift button.</summary>
    void UpdateNeighbours(SimState state, bool solo)
    {
        string key = J(new object[] { state.settlements.Select((n) => $"{n.id}{n.reputation}{n.people}{n.food}{n.defence}{J(n.ties)}"), state.requests.Count, state.items.Count, Errands.errandsOf(state).Count, Time.dayOf(state.tick), raidArmed.Count });
        neighbours.Show(state.settlements.Count > 0 && !solo, key, (r) =>
        {
            r.Text("Neighbours", Kit.Title);
            int food = state.items.Count((i) => i.kind == "food" && i.pos != null && i.reservedBy == null);
            foreach (var n in state.settlements)
            {
                var row = r.Row();
                string mood = n.reputation <= Settlements.HOSTILE ? "hostile" : n.reputation >= Settlements.FRIENDLY ? "friendly" : "wary";
                row.Text(Colonies.isFallen(n)
                    ? $"{n.name} ({n.x}, {n.y}): fallen, nobody left"
                    : $"{n.name} ({n.x}, {n.y}): {mood} {n.reputation} · {n.people} people, {Colonies.hungerOf(n)}, walls {n.defence}/5{standing(state, n)}");
                if (Colonies.isFallen(n)) continue;
                row.Button($"Gift {Settlements.GIFT_FOOD} food", () => Command(new Command { type = "giftSettlement", settlementId = n.id }), disabled: food < Settlements.GIFT_FOOD);
                // The browser asks to confirm; here the first click arms it, a second within 3 s raids.
                bool armed = raidArmed.TryGetValue(n.id, out var t) && Godot.Time.GetTicksMsec() < t;
                row.Button(armed ? "Raid: sure?" : "Raid", () =>
                {
                    if (armed) { raidArmed.Remove(n.id); Command(new Command { type = "raidSettlement", settlementId = n.id }); }
                    else raidArmed[n.id] = Godot.Time.GetTicksMsec() + 3000;
                    neighbours.Invalidate();
                }, tip: "Send everyone armed to take their food. They will count you an enemy, win or lose.");
            }
            foreach (var e in Errands.errandsOf(state))
            {
                var n = state.settlements.FirstOrDefault((s) => s.id == e.settlementId);
                double days = Math.Max(0, Math.Ceiling((e.until - state.tick) / Time.DAY_LENGTH));
                r.Text($"{n?.name ?? "?"} asks: {(e.kind == "nest" ? "clear the dead" : $"find {e.who}")} near ({e.x}, {e.y}), {days} day{(days == 1 ? "" : "s")} left (on your map)");
            }
            foreach (var q in state.requests)
            {
                var n = state.settlements.FirstOrDefault((s) => s.id == q.settlementId);
                int have = state.items.Count((i) => i.kind == q.kind && i.pos != null && i.reservedBy == null);
                var row = r.Row();
                row.Text($"{n?.name ?? "?"} asks for {q.count} {q.kind} (you have {have})");
                row.Button("Send", () => Command(new Command { type = "fulfilRequest", requestId = q.id }), disabled: have < q.count);
            }
        });
    }

    /// <summary>A trader's offers, each with a Trade button.</summary>
    void UpdateTraders(SimState state, bool solo)
    {
        traders.Show(state.traders.Count > 0 && !solo, J(new object[] { state.traders.Select((t) => $"{t.id}{t.traded}{t.offers.Count}"), state.items.Count }), (r) =>
        {
            foreach (var t in state.traders)
            {
                var from = state.settlements.FirstOrDefault((n) => n.id == t.from);
                r.Text($"Trader{(from != null ? $" from {from.name}" : "")} at ({t.x}, {t.y})", Kit.Title);
                for (int i = 0; i < t.offers.Count; i++)
                {
                    var o = t.offers[i];
                    int have = state.items.Count((it) => it.kind == o.want && it.pos != null && it.reservedBy == null);
                    var row = r.Row();
                    row.Text($"{o.giveCount} {o.give} for {o.wantCount} {o.want} (you have {have})");
                    int index = i;
                    row.Button("Trade", () => Command(new Command { type = "trade", traderId = t.id, offer = index }), disabled: have < o.wantCount);
                }
            }
        });
    }

    /// <summary>Known ruins to send the selected survivor to, and who's away.</summary>
    void UpdateExpeditions(SimState state, bool solo)
    {
        var selected = state.survivors.FirstOrDefault((s) => s.id == view.selectedId);
        var away = state.expeditions.Select((x) => (x, hours: Math.Ceiling((x.returnTick - state.tick) / Time.TICKS_PER_HOUR))).ToList();
        string key = J(new object?[] { selected?.id, state.pointsOfInterest.Select((p) => p.id), away.Select((a) => $"{a.x.id}:{a.hours}") });
        expeditions.Show((state.pointsOfInterest.Count > 0 || away.Count > 0) && !solo, key, (r) =>
        {
            r.Text("Expeditions", Kit.Title);
            foreach (var p in state.pointsOfInterest.Take(5))
            {
                var row = r.Row();
                row.Text($"{p.label} at ({p.x}, {p.y})");
                row.Button(selected != null ? $"Send {selected.name}" : "Select someone to send", () => { if (selected != null) Command(new Command { type = "sendExpedition", survivorIds = new() { selected.id }, poiId = p.id }); }, disabled: selected == null);
                row.Button("Outpost", () => { if (selected != null) Command(new Command { type = "sendExpedition", survivorIds = new() { selected.id }, poiId = p.id, outpost = true }); }, disabled: selected == null, tip: "Send them to stay there: supplies every dawn, at some risk");
            }
            foreach (var (x, hours) in away)
            {
                var row = r.Row();
                row.Text($"{string.Join(", ", x.members.Select((s) => s.name))} → {x.target.label} ({x.target.x}, {x.target.y}){(x.outpost ? ", holding it as an outpost" : $", back in about {hours}h")}");
                if (x.outpost) row.Button("Recall", () => Command(new Command { type = "recallOutpost", expeditionId = x.id }));
            }
        });
    }

    /// <summary>Stock targets for the workbenches: shown once there is a workbench (or a plan for one).</summary>
    void UpdateCrafting(SimState state, bool solo)
    {
        int benches = state.furniture.Count((f) => f.kind == "workbench");
        bool planned = state.blueprints.Any((b) => b.target == "workbench");
        var rows = Crafting.ORDERABLE.Select((k) => (k, have: Crafting.craftStock(state, k), want: state.craftTargets[k])).ToList();
        crafting.Show((benches > 0 || planned) && !solo, $"{benches}|{planned}|{J(rows.Select((x) => $"{x.k}{x.have}/{x.want}"))}", (r) =>
        {
            r.Text(benches == 0 ? "Workbench orders (once the bench is built)" : $"Workbench orders · {benches} bench{(benches == 1 ? "" : "es")}", Kit.Title);
            foreach (var (k, have, want) in rows)
            {
                var recipe = Crafting.RECIPES[k];
                var row = r.Row();
                row.Text($"{recipe.label} ({recipe.count} {recipe.input}{(recipe.workshopOnly == true ? ", workshop" : "")}): have {have}, keep");
                row.Button("−", () => Command(new Command { type = "setCraftTarget", kind = k, count = want - 1 }), disabled: want == 0);
                row.Text($"{want}", Kit.Title);
                row.Button("+", () => Command(new Command { type = "setCraftTarget", kind = k, count = want + 1 }), disabled: want >= Crafting.MAX_CRAFT_TARGET);
            }
        });
    }

    /// <summary>People waiting at the edge, each with Take in / Turn away.</summary>
    void UpdateWanderers(SimState state, bool solo)
    {
        double spare = Wanderers.spareBeds(state);
        var rows = state.wanderers.Select((w) => (w, left: (int)(Math.Ceiling(Wanderers.patienceLeft(state, w) / (Time.TICKS_PER_HOUR / 6.0)) * 10))).ToList();
        wanderers.Show(rows.Count > 0 && !solo, $"{spare}|{string.Join(",", rows.Select((x) => $"{x.w.id}:{x.left}"))}", (r) =>
        {
            r.Text($"Wanderers waiting · {(spare > 0 ? $"{spare} spare bed{(spare == 1 ? "" : "s")}" : "no spare beds")}", Kit.Title);
            foreach (var (w, left) in rows)
            {
                var row = r.Row();
                string traits = w.traits.Count > 0 ? $" ({string.Join(", ", w.traits.Select((t) => Skills.TRAIT_INFO[t].label))})" : "";
                row.Text($"{w.name}{traits} at ({w.x}, {w.y}) · leaves in {left / 60}h {left % 60:00}m");
                row.Button("Take in", () => Command(new Command { type = "acceptWanderer", wandererId = w.id }));
                row.Button("Turn away", () => Command(new Command { type = "dismissWanderer", wandererId = w.id }));
            }
        });
    }

    /// <summary>Click cycles 1 → 2 → 3 → 4 → off → 1; right-click goes the other way.</summary>
    public static double cyclePriority(double p, bool backwards)
    {
        double[] order = { 1, 2, 3, 4, 0 };
        int i = Array.IndexOf(order, p);
        return order[(i + (backwards ? order.Length - 1 : 1)) % order.Length];
    }

    void UpdatePriorities(SimState state, bool solo)
    {
        // Build mode takes the bottom of the screen: the panel steps aside until it closes.
        bool shown = view.showPriorities && !view.buildOpen && !solo;
        string key = shown ? J(new object?[] { view.selectedId, view.roster, state.survivors.Select((s) => J(new object?[] { s.id, s.name, s.resolve, s.social, s.comfort, s.shift, s.order?.kind, J(s.priorities), Card.activity(state, s), Card.moodTitle(state, s) })) }) : "";
        priorities.Show(shown, key, (r) =>
        {
            bool full = view.roster == "full";
            var head = r.Row();
            head.Text($"Survivors ({state.survivors.Count})", Ctx.Css("#c9cfc9"));
            if (view.roster != "folded") head.Button(full ? "Less" : "Jobs & needs", () => view.roster = full ? "short" : "full");
            head.Button(view.roster == "folded" ? "▸" : "▾", () => view.roster = view.roster == "folded" ? "short" : "folded", tip: view.roster == "folded" ? "Show the list" : "Fold the list away");
            if (view.roster == "folded") return;
            var heads = new List<string> { "Survivor", "Mood" };
            if (full) heads.AddRange(new[] { "Resolve", "Social", "Comfort", "Shift" });
            heads.Add("Order");
            if (full) heads.AddRange(JobTypes.JOB_TYPES.Select((t) => JOB_LABELS[t]));
            heads.Add("Doing");
            var grid = r.Add(new GridContainer { Columns = heads.Count, MouseFilter = MouseFilterEnum.Ignore });
            grid.AddThemeConstantOverride("h_separation", 6);
            var cells = new Rows(grid, 12);
            foreach (var h in heads) cells.Text(h, Kit.Dim);
            Color tone(bool low, bool high) => low ? Ctx.Css("#ff8a7a") : high ? Ctx.Css("#8cf0a4") : Kit.Text;
            foreach (var s in state.survivors)
            {
                var name = cells.Button(s.name, () => view.selectedId = s.id, active: s.id == view.selectedId,
                    tip: string.Join(" · ", new[] { $"Select · {Skills.describeSkills(s)}", Clothing.describeClothing(state, s), Relationships.describeRelations(state, s) }.Where((x) => x != "")));
                name.Flat = s.id != view.selectedId;
                double m = Mood.mood(state, s);
                cells.Text($"{m}", tone(Mood.isMiserable(state, s), m >= 60)).TooltipText = Card.moodTitle(state, s);
                if (full)
                {
                    cells.Text($"{s.resolve}", tone(Resolve.isBreaking(s), Resolve.isSteeled(s)));
                    cells.Text($"{s.social}", tone(s.social < Wellbeing.LOW, s.social > Wellbeing.HIGH));
                    cells.Text($"{s.comfort}", tone(s.comfort < Wellbeing.LOW, s.comfort > Wellbeing.HIGH));
                    cells.Button(s.shift == "night" ? "☾" : "☀", () => Command(new Command { type = "setShift", survivorId = s.id, shift = s.shift == "night" ? "day" : "night" }),
                        tip: $"{s.name} works the {s.shift} shift (sleeps {(s.shift == "night" ? "by day" : "at night")}). Click to switch.");
                }
                string orderGlyph = s.order == null ? "⌂" : s.order.kind == "follow" ? "→" : s.order.kind == "loot" ? "⚲" : "■";
                string next = s.order == null ? "follow" : s.order.kind == "follow" ? "hold" : "settle";
                cells.Button(orderGlyph, () => Command(new Command { type = "setOrder", survivorId = s.id, order = next }), tip: $"{s.name} is {Orders.describeOrder(s.order)}. Click: settle (colony jobs) → follow you → hold where they stand.");
                if (full)
                    foreach (var type in JobTypes.JOB_TYPES)
                    {
                        double p = s.priorities[type];
                        var b = cells.Button(p == 0 ? "–" : $"{p}", () => Command(new Command { type = "setPriority", survivorId = s.id, jobType = type, priority = cyclePriority(p, Input.IsKeyPressed(Key.Shift)) }),
                            tip: $"{JOB_LABELS[type]} priority for {s.name}. Click: higher number, Shift-click: back. – is off.");
                        b.AddThemeColorOverride("font_color", Ctx.Css(p == 1 ? "#8cf0a4" : p == 2 ? "#cfe8a0" : p == 3 ? "#e8d9a8" : p == 4 ? "#b0a890" : "#666e68"));
                    }
                cells.Text(Card.activity(state, s), Kit.Dim);
            }
        });
    }

    // --- the character card (card.ts) ------------------------------------------------------------

    Survivor? Subject(SimState state)
    {
        var id = state.control?.survivorId ?? view.selectedId;
        return id != null ? state.survivors.FirstOrDefault((s) => s.id == id) : null;
    }

    void UpdateConditions(SimState state)
    {
        var s = Subject(state);
        var list = s != null ? Card.conditionsOf(state, s) : new List<Condition>();
        conditions.Show(list.Count > 0, string.Join("|", list.Select((c) => c.id + c.label)), (r) =>
        {
            foreach (var c in list)
            {
                var l = r.Text($"{c.glyph}  {c.label}", Ctx.Css(c.tone == "bad" ? "#ff7a6a" : c.tone == "mild" ? "#ffd166" : "#8cf0a4"));
                l.TooltipText = c.about;
                l.MouseFilter = MouseFilterEnum.Stop;
            }
        });
    }

    static readonly Dictionary<string, string> BAR_COLOURS = new()
    {
        ["health"] = "#7ee08a", ["food"] = "#ffb35c", ["water"] = "#5ab0ff", ["rest"] = "#7fb2ff", ["endurance"] = "#c8c8a0", ["warmth"] = "#e68a4a",
    };

    void UpdateCard(SimState state)
    {
        var s = Subject(state);
        if (s == null) { card.Visible = false; return; }
        var bars = Card.barsOf(state, s);
        bool isYou = state.control?.survivorId == s.id;
        int jobIndex = (int)s.story.job;
        string job = jobIndex >= 0 && jobIndex < StoryMod.STORY_JOBS.Count ? StoryMod.STORY_JOBS[jobIndex] : "";
        string now = Card.activity(state, s);
        string wish = s.wish != null ? $"wants {Wishes.describeWish(state, s.wish)}{(s.wish.promised ? " (promised)" : "")}" : "";
        double m = Mood.mood(state, s);
        string key = string.Join("|", s.name, isYou, job, s.age, s.resolve, now, wish, m, s.social, s.comfort, s.panic, string.Join(",", bars.Select((b) => b.pct)));
        card.Show(true, key, (r) =>
        {
            var head = r.Row();
            head.Text(s.name + (isYou ? " (you)" : ""), Kit.Title).AddThemeFontSizeOverride("font_size", 14);
            head.Text($"{System.Text.RegularExpressions.Regex.Replace(job, "^an? ", "")}, {s.age}", Ctx.Css("#8f998f"));
            string doing = string.Join(" · ", new[] { now, wish }.Where((x) => x != ""));
            if (doing != "") r.Text(doing, Ctx.Css("#b9c2b9"), 320);
            var grid = r.Add(new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore });
            grid.AddThemeConstantOverride("h_separation", 14);
            foreach (var b in bars)
            {
                var cell = new VBoxContainer { CustomMinimumSize = new Vector2(150, 0), MouseFilter = MouseFilterEnum.Ignore };
                cell.AddThemeConstantOverride("separation", 1);
                var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                top.AddChild(Kit.Label(b.label, Kit.Dim, 12));
                top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
                top.AddChild(Kit.Label($"{b.pct}", Kit.Text, 12));
                cell.AddChild(top);
                string colour = b.pct < b.crit ? "#ff6a5c" : b.pct < b.low ? "#ffb347" : BAR_COLOURS[b.kind];
                cell.AddChild(new BarFill(b.pct / 100.0, Ctx.Css(colour)));
                grid.AddChild(cell);
            }
            var minds = r.Row();
            void stat(string label, double value, bool bad, string? title = null)
            {
                var l = minds.Text($"{label} {value}", bad ? Ctx.Css("#ff8a7a") : Kit.Dim);
                if (title != null) { l.TooltipText = title; l.MouseFilter = MouseFilterEnum.Stop; }
            }
            stat("Resolve", s.resolve, Resolve.isBreaking(s), $"{Resolve.describeResolve(s)}: the will to keep going. Drifts with mood each dawn.");
            stat("Mood", m, Mood.isMiserable(state, s), Card.moodTitle(state, s));
            stat("Social", s.social, s.social < Wellbeing.LOW);
            stat("Comfort", s.comfort, s.comfort < Wellbeing.LOW);
            stat("Panic", s.panic, s.panic > BodyMod.PANICKING);
        });
    }

    // --- the debug panel -----------------------------------------------------------------------------

    string debugText(SimState state, string perf)
    {
        var problems = Invariants.checkInvariants(state);
        int open = state.jobs.Count((j) => j.reservedBy == null);
        var lines = new List<string>
        {
            perf,
            $"zombies/people {state.zombies.Count}/{state.survivors.Count + state.residents.Count}",
            $"tick           {state.tick}",
            $"speed          {(view.halted ? "paused" : $"{view.speed}x")}",
            $"map            {state.grid.width}x{state.grid.height}",
            $"walkability v  {state.walkabilityVersion}",
            $"commands       {state.commandLog.Count}",
            $"camera         {view.camera.x:0.0}, {view.camera.y:0.0} @ {view.camera.zoom:0.00}x",
            $"rng            {state.rng}",
            $"invariants     {(problems.Count == 0 ? "ok" : $"{problems.Count} PROBLEM(S): {problems[0]}")}",
            $"rooms          {Rooms.getRoomMap(state).rooms.Count} enclosed, {(state.roomDesignations.Count == 0 ? "none designated" : string.Join(", ", state.roomDesignations.Select((d) => $"{d.purpose}{(Rooms.designationStatus(state, d).complete ? "" : " (incomplete)")}")))}",
            "",
        };
        foreach (var s in state.survivors) lines.AddRange(survivorLines(state, s, s.id == view.selectedId));
        lines.Add("");
        foreach (var z in state.zombies.Take(20))
        {
            var target = state.survivors.FirstOrDefault((s) => s.id == z.targetId);
            lines.Add($"  zombie {z.id} at ({z.pos.x}, {z.pos.y}) hp {z.health}  ·  {(target != null ? $"chasing {target.name}" : "wandering")}");
        }
        if (state.zombies.Count > 20) lines.Add($"  … {state.zombies.Count - 20} more");
        lines.Add($"jobs {state.jobs.Count} ({open} open)");
        foreach (var j in state.jobs.Take(DEBUG_JOB_LINES))
        {
            var who = j.reservedBy != null ? state.survivors.FirstOrDefault((s) => s.id == j.reservedBy)?.name ?? j.reservedBy : "open";
            lines.Add($"  {j.id,-5} {j.type,-5} ({j.x}, {j.y}){(j.type == "clean" && j.progress > 0 ? $" {j.progress}" : "")}  ← {who}");
        }
        if (state.jobs.Count > DEBUG_JOB_LINES) lines.Add($"  … {state.jobs.Count - DEBUG_JOB_LINES} more");
        return string.Join("\n", lines);
    }

    /// <summary>"Why is she just standing there?" in four lines.</summary>
    static IEnumerable<string> survivorLines(SimState state, Survivor s, bool selected)
    {
        static string at(Point p) => $"({p.x}, {p.y})";
        string moving = s.move != null ? $"moving {at(s.move.from)}→{at(s.move.to)} {s.move.progress}/{s.move.duration}" : "still";
        int remaining = s.path.Count + (s.move != null ? 1 : 0);
        string cache = s.pathVersion == -1 ? "no path" : s.pathVersion == state.walkabilityVersion ? $"path v{s.pathVersion} fresh" : $"path v{s.pathVersion} STALE";
        var job = s.jobId != null ? Jobs.getJob(state, s.jobId) : null;
        var p = s.priorities;
        string carrying = s.carrying != null ? $"  carrying {state.items.FirstOrDefault((i) => i.id == s.carrying)?.kind ?? s.carrying}" : "";
        yield return $"{(selected ? "▶" : " ")} {s.name} [{s.id}] at {at(s.pos)}  {moving}{carrying}";
        yield return $"    health {Math.Round(100 * s.health / Health.HEALTH_MAX)}%{(Health.isCritical(s.health) ? " CRITICAL" : Health.isHurt(s.health) ? " HURT" : "")}{(s.infection != null ? $" INFECTED {Math.Round(100 * s.infection.Value / Infection.INFECTION_TURN)}%" : "")}  ·  rest {Math.Round(100 * s.rest / Rest.REST_MAX)}%{(s.rest < Rest.TIRED ? " TIRED" : "")}  ·  food {Math.Round(100 * s.satiety / Food.SATIETY_MAX)}%{(s.satiety < Food.HUNGRY ? " HUNGRY" : "")}  ·  {Card.activity(state, s)}";
        yield return $"    job {(job != null ? $"{job.id} {job.type} ({job.x}, {job.y})" : s.sleep != null || s.meal != null ? $"none: {Card.activity(state, s)}" : $"none: {JobChoice.explainIdle(state, s)}")}  ·  prio g{p["guard"]} b{p["build"]} f{p["farm"]} h{p["haul"]} s{p["scavenge"]} c{p["clean"]}";
        yield return $"    goal {(s.goal != null ? $"{at(s.goal)}, {remaining} tiles left" : "—")}  ·  {cache}";
        yield return $"    {Card.moodTitle(state, s)}  ·  {Skills.describeSkills(s)}  ·  wearing {(Clothing.describeClothing(state, s) is var c && c != "" ? c : "nothing much")}";
        yield return $"    {s.note ?? ""}";
    }
}

/// <summary>First person: a small dot where you're looking.</summary>
public sealed partial class Crosshair : Control
{
    public Crosshair() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, 3.2f, new Color(0, 0, 0, 0.6f));
        DrawCircle(Vector2.Zero, 2f, new Color(0.95f, 0.92f, 0.85f, 0.85f));
    }
}

/// <summary>A thin progress bar (the card's need bars, the bag's weight).</summary>
public sealed partial class BarFill : Control
{
    double fraction;
    Color colour;

    public BarFill() { }

    public BarFill(double fraction, Color colour)
    {
        this.fraction = Math.Clamp(fraction, 0, 1);
        this.colour = colour;
        CustomMinimumSize = new Vector2(150, 5);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Ctx.Css("#2a302c"));
        DrawRect(new Rect2(Vector2.Zero, new Vector2((float)(Size.X * fraction), Size.Y)), colour);
    }
}
