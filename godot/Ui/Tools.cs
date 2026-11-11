// Build tools, grouped into the categories of the build bar (B) (src/ui/tools.ts, buildMode.ts). A
// tool turns a tile into a sim command; it never touches state directly (architecture rule 5).
// Outside build mode there is no tool: left-click selects, right-click moves the selected survivor.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Control = Godot.Control;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Ui;

public sealed class Tool
{
    public string id = "", label = "", category = "";
    /// <summary>Painting tools apply on every tile a drag crosses; others only on click.</summary>
    public bool paints;
    /// <summary>Area tools drag out a box instead, applied to every tile in it on release.</summary>
    public bool area;
    /// <summary>The auto-planner: a click asks the planner to furnish the room.</summary>
    public bool furnish;
    /// <summary>Whether Shift (instant) does anything for this tool.</summary>
    public bool hasInstant;
    /// <summary>Builds on the tile border nearest the cursor, not on a tile (edge walls and doors, Grid.cs).</summary>
    public bool edge;
    /// <summary>(x, y, instant, side) to a command; null for tools that don't map to a single command.</summary>
    public Func<int, int, bool, string?, Command?> command = (_, _, _, _) => null;
    /// <summary>Area tools only: one command for the whole box instead of one per tile.</summary>
    public Func<Point, Point, Command>? boxCommand;
}

public static class Tools
{
    public static readonly (string id, string label)[] CATEGORIES =
    {
        ("structure", "Structure"), ("furniture", "Furniture"), ("utilities", "Utilities"), ("rooms", "Rooms"), ("land", "Land"), ("orders", "Orders"),
    };

    static Command Build(int x, int y, string structure, string? side = null) => new() { type = "designateBuild", x = x, y = y, structure = structure, side = side };
    static Command Room(int x, int y, string purpose) => new() { type = "designateRoom", x = x, y = y, purpose = purpose };

    static Tool Simple(string id, string label, string category, bool paints = false) =>
        new() { id = id, label = label, category = category, paints = paints, command = (x, y, _, _) => Build(x, y, id) };

    static Tool RoomTool(string id, string label, string category = "rooms") =>
        new() { id = id, label = label, category = category, command = (x, y, _, _) => Room(x, y, id) };

    public static readonly List<Tool> TOOLS = new List<Tool>
    {
        new() { id = "wall", label = "Wall", category = "structure", paints = true, hasInstant = true, edge = true,
            command = (x, y, instant, side) => instant ? new Command { type = "placeWall", x = x, y = y, side = side } : Build(x, y, "wall", side) },
        new() { id = "door", label = "Door", category = "structure", paints = true, hasInstant = true, edge = true,
            command = (x, y, instant, side) => instant ? new Command { type = "placeDoor", x = x, y = y, side = side } : Build(x, y, "door", side) },
        new() { id = "barricade", label = "Barricade", category = "structure", paints = true, edge = true, command = (x, y, _, side) => Build(x, y, "barricade", side) },
        Simple("trap", "Spike trap", "structure", paints: true),
        Simple("tower", "Watchtower", "structure"),
        Simple("reinforce", "Reinforce (scrap)", "structure", paints: true),
        Simple("turret", "Turret (ammo)", "structure"),
        Simple("lure", "Lure", "structure"),
        Simple("pit", "Pit trap", "structure"),
        new() { id = "bed", label = "Bed", category = "furniture", paints = true, hasInstant = true,
            command = (x, y, instant, _) => instant ? new Command { type = "placeBed", x = x, y = y } : Build(x, y, "bed") },
        new() { id = "table", label = "Table", category = "furniture", paints = true, hasInstant = true,
            command = (x, y, instant, _) => instant ? new Command { type = "placeTable", x = x, y = y } : Build(x, y, "table") },
        new() { id = "workbench", label = "Workbench", category = "furniture", hasInstant = true,
            command = (x, y, instant, _) => instant ? new Command { type = "placeWorkbench", x = x, y = y } : Build(x, y, "workbench") },
        Simple("stove", "Stove", "furniture"),
        Simple("well", "Well", "utilities"),
        Simple("collector", "Rain collector", "utilities"),
        Simple("generator", "Generator", "utilities"),
        Simple("lamp", "Lamp", "utilities"),
        Simple("fridge", "Fridge", "utilities"),
        Simple("heater", "Heater", "utilities"),
        Simple("radio", "Radio", "utilities"),
        Simple("lab", "Lab bench", "utilities"),
        Simple("beacon", "Beacon", "utilities"),
        RoomTool("housing", "Housing"),
        RoomTool("storage", "Storage"),
        RoomTool("armory", "Armory"),
        RoomTool("messhall", "Mess hall"),
        RoomTool("recroom", "Rec room"),
        RoomTool("infirmary", "Infirmary"),
        RoomTool("barracks", "Barracks"),
        RoomTool("workshop", "Workshop"),
        RoomTool("graveyard", "Graveyard"),
        RoomTool("cell", "Cells"),
        new() { id = "furnish", label = "Furnish", category = "orders", furnish = true },
        new() { id = "scavenge", label = "Scavenge", category = "orders", area = true, command = (x, y, _, _) => new Command { type = "designateScavenge", x = x, y = y } },
        new() { id = "remove", label = "Remove", category = "orders", paints = true, hasInstant = true,
            command = (x, y, instant, _) => instant ? new Command { type = "clearTile", x = x, y = y } : new Command { type = "designateRemove", x = x, y = y } },
        new() { id = "cancel", label = "Cancel", category = "orders", paints = true, command = (x, y, _, _) => new Command { type = "cancelDesignation", x = x, y = y } },
        new() { id = "explore", label = "Explore", category = "orders", area = true,
            boxCommand = (a, b) => new Command { type = "designateExplore", x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y } },
        new() { id = "lock", label = "Lock door", category = "orders", edge = true, command = (x, y, _, side) => new Command { type = "toggleLock", x = x, y = y, side = side } },
        new() { id = "burn", label = "Burn", category = "orders", command = (x, y, _, _) => new Command { type = "ignite", x = x, y = y } },
        new() { id = "farm", label = "Potatoes", category = "land", area = true, command = (x, y, _, _) => new Command { type = "designateFarm", x = x, y = y } },
        new() { id = "wheat", label = "Wheat", category = "land", area = true, command = (x, y, _, _) => new Command { type = "designateFarm", x = x, y = y, crop = "wheat" } },
        new() { id = "herb", label = "Herbs", category = "land", area = true, command = (x, y, _, _) => new Command { type = "designateFarm", x = x, y = y, crop = "herb" } },
        RoomTool("greenhouse", "Greenhouse", "land"),
        Simple("coop", "Chicken coop", "land"),
        new() { id = "hunt", label = "Hunt", category = "land", area = true,
            boxCommand = (a, b) => new Command { type = "designateHunt", x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y } },
        new() { id = "perimeter", label = "Wall in", category = "land", area = true,
            boxCommand = (a, b) => new Command { type = "planPerimeter", x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y } },
    };

    public static Tool getTool(string id) => TOOLS.First((t) => t.id == id);

    public static List<Tool> toolsIn(string category) => TOOLS.Where((t) => t.category == category).ToList();

    /// <summary>Every tile in the box spanned by two corners, row by row (the order commands are applied).</summary>
    public static IEnumerable<Point> tilesInBox(Point a, Point b)
    {
        for (int y = Math.Min(a.y, b.y); y <= Math.Max(a.y, b.y); y++)
            for (int x = Math.Min(a.x, b.x); x <= Math.Max(a.x, b.x); x++) yield return new Point(x, y);
    }

    /// <summary>Number keys pick tools within the open category: 1 is the first, 0 the tenth.</summary>
    public static Tool? toolForKey(string category, int n)
    {
        var list = toolsIn(category);
        return n >= 1 && n <= list.Count ? list[n - 1] : null;
    }

    /// <summary>Next (or previous) category, wrapping around.</summary>
    public static string stepCategory(string category, int direction)
    {
        int i = Array.FindIndex(CATEGORIES, (c) => c.id == category);
        return CATEGORIES[(i + direction + CATEGORIES.Length) % CATEGORIES.Length].id;
    }

    /// <summary>Tiles on the line from a to b, inclusive (Bresenham), so a fast drag paints every tile it crosses.</summary>
    public static List<Point> tilesOnLine(Point a, Point b)
    {
        var points = new List<Point>();
        int dx = Math.Abs(b.x - a.x), dy = -Math.Abs(b.y - a.y);
        int sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1;
        int err = dx + dy, x = a.x, y = a.y;
        for (; ; )
        {
            points.Add(new Point(x, y));
            if (x == b.x && y == b.y) return points;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }

    /// <summary>The tile border nearest a world point: the tile, and its north ("n") or west ("w") side.</summary>
    public static (int x, int y, string side) edgeNear(double wx, double wy)
    {
        int tx = (int)Math.Floor(wx), ty = (int)Math.Floor(wy);
        double fx = wx - tx, fy = wy - ty;
        double near = Math.Min(Math.Min(fx, 1 - fx), Math.Min(fy, 1 - fy));
        if (near == fy) return (tx, ty, "n");
        if (near == 1 - fy) return (tx, ty + 1, "n");
        if (near == fx) return (tx, ty, "w");
        return (tx + 1, ty, "w");
    }

    // --- build mode ---

    /// <summary>Closing build mode also drops the armed tool, back to select mode.</summary>
    public static void closeBuild(View view)
    {
        view.buildOpen = false;
        disarm(view);
    }

    public static void chooseCategory(View view, string category)
    {
        if (view.buildCategory != category) view.tool = null;
        view.buildCategory = category;
        view.buildOpen = true;
    }

    /// <summary>Arms a tool (opening build mode on its category). Choosing the armed tool again disarms it.</summary>
    public static void chooseTool(View view, string tool, string category)
    {
        view.buildOpen = true;
        view.buildCategory = category;
        view.tool = view.tool == tool ? null : tool;
    }

    /// <summary>Right-click or Escape with a tool armed: put the tool down (abandoning any box being dragged), stay in build mode.</summary>
    public static void disarm(View view)
    {
        view.tool = null;
        view.areaDrag = null;
    }

    /// <summary>
    /// Keys that mean something in or for build mode. Returns true if handled: B toggles build mode;
    /// in build mode 1-9 and 0 arm a tool, Tab/E and Shift+Tab/Q change category; Escape disarms,
    /// then closes build mode, then clears the selection.
    /// </summary>
    public static bool handleBuildKey(View view, Key key, bool shift)
    {
        if (key == Key.B)
        {
            if (view.buildOpen) closeBuild(view);
            else view.buildOpen = true;
            return true;
        }
        if (key == Key.Escape)
        {
            if (view.tool != null) disarm(view);
            else if (view.buildOpen) closeBuild(view);
            else view.selectedId = null;
            return true;
        }
        if (!view.buildOpen) return false;
        if (key is Key.Tab or Key.E or Key.Q)
        {
            bool back = key == Key.Q || (key == Key.Tab && shift);
            chooseCategory(view, stepCategory(view.buildCategory, back ? -1 : 1));
            return true;
        }
        if (key >= Key.Key0 && key <= Key.Key9)
        {
            int n = key == Key.Key0 ? 10 : (int)(key - Key.Key0);
            var tool = toolForKey(view.buildCategory, n);
            if (tool != null)
            {
                chooseTool(view, tool.id, tool.category);
                return true;
            }
        }
        return false;
    }
}

/// <summary>The build bar (B): category tabs, the tools in the open one, and a hint.</summary>
public sealed partial class BuildBar : PanelContainer
{
    /// <summary>Tools whose action isn't obvious from "click or drag to place".</summary>
    static readonly Dictionary<string, string> TOOL_HINTS = new()
    {
        ["scavenge"] = "Drag a box over a ruin or trees to mark crates and trees",
        ["explore"] = $"Drag a box into the dark: a scout goes and looks (up to {Fog.MAX_EXPLORE_SIDE} tiles a side)",
        ["farm"] = "Drag a box over open ground to lay out plots",
        ["furnish"] = "Click a room: the planner adds what it needs",
        ["remove"] = "Drag over walls, doors, or furniture to take down",
        ["cancel"] = "Drag to cancel plans, unmark crates and trees, or clear room purposes",
    };

    readonly View view = null!;
    readonly HBoxContainer tabs = new();
    readonly HBoxContainer tools = new();
    readonly Label hint = Kit.Label("", Kit.Dim, 12);
    readonly List<(string id, Button button)> tabButtons = new();
    readonly List<(string id, Button button)> toolButtons = new();
    string? shownCategory;

    public BuildBar() { }

    public BuildBar(View view)
    {
        this.view = view;
        MouseFilter = MouseFilterEnum.Stop;
        var box = new VBoxContainer();
        AddChild(box);
        box.AddChild(tabs);
        box.AddChild(tools);
        box.AddChild(hint);
        foreach (var (id, label) in Tools.CATEGORIES)
        {
            var b = Kit.Button(label, () => Tools.chooseCategory(view, id));
            tabs.AddChild(b);
            tabButtons.Add((id, b));
        }
    }

    public static void toggleBuild(View view)
    {
        if (view.buildOpen) Tools.closeBuild(view);
        else view.buildOpen = true;
    }

    public void Update(SimState state)
    {
        Visible = view.buildOpen;
        if (!view.buildOpen) return;
        if (shownCategory != view.buildCategory)
        {
            foreach (var c in tools.GetChildren()) c.QueueFree();
            toolButtons.Clear();
            var list = Tools.toolsIn(view.buildCategory);
            for (int i = 0; i < list.Count; i++)
            {
                var tool = list[i];
                var b = Kit.Button($"{(i + 1) % 10} {tool.label}", () => Tools.chooseTool(view, tool.id, tool.category), tip: tool.hasInstant ? "Hold Shift while placing to build instantly" : null);
                tools.AddChild(b);
                toolButtons.Add((tool.id, b));
            }
            shownCategory = view.buildCategory;
        }
        foreach (var (id, b) in tabButtons) Kit.SetActive(b, id == view.buildCategory);
        foreach (var (id, b) in toolButtons) Kit.SetActive(b, id == view.tool);
        var t = view.tool != null ? Tools.getTool(view.tool) : null;
        hint.Text = t != null
            ? $"{(TOOL_HINTS.TryGetValue(t.id, out var h) ? h : t.paints ? "Click or drag to place" : "Click inside a room")}{(t.hasInstant ? " · Shift: instant" : "")} · Right-click: put down · Esc"
            : "Number keys: pick a tool · Tab / Q E: category · Esc or B: close";
    }
}
