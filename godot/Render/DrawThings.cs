// Drawing rooms, furniture, items and blueprints (src/render/drawThings.ts). Reads sim state only.

using System;
using System.Collections.Generic;
using System.Linq;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.Render;

public sealed partial class Painter
{
    static readonly double[] NoDash = Array.Empty<double>();

    /// <summary>Soft tint over designated rooms, by purpose.</summary>
    void drawRoomTints(SimState state, TileRange range)
    {
        if (state.roomDesignations.Count == 0) return;
        var labels = Rooms.getRoomMap(state).labels;
        var purposeByLabel = new Dictionary<int, string>();
        foreach (var d in state.roomDesignations)
        {
            int label = (int)Rooms.labelAt(state, d.x, d.y);
            if (label >= 0) purposeByLabel[label] = d.purpose;
        }
        int width = state.grid.width;
        for (int y = range.y0; y <= range.y1; y++)
            for (int x = range.x0; x <= range.x1; x++)
            {
                if (!purposeByLabel.TryGetValue((int)labels[y * width + x], out var purpose)) continue;
                var r = tileRect(x, y);
                ctx.fillStyle = COLOURS.room[purpose];
                ctx.fillRect(r.x, r.y, r.w, r.h);
            }
    }

    /// <summary>Debug: outline every room (designated or not) where it meets something else.</summary>
    void drawRoomOutlines(SimState state, TileRange range)
    {
        var labels = Rooms.getRoomMap(state).labels;
        int width = state.grid.width, height = state.grid.height;
        int at(int x, int y) => x < 0 || y < 0 || x >= width || y >= height ? -99 : (int)labels[y * width + x];
        ctx.strokeStyle = COLOURS.roomOutline;
        ctx.lineWidth = 1;
        ctx.setLineDash(new double[] { 2, 3 });
        ctx.beginPath();
        for (int y = range.y0; y <= range.y1; y++)
            for (int x = range.x0; x <= range.x1; x++)
            {
                int label = at(x, y);
                if (label < 0) continue;
                var r = tileRect(x, y);
                const double inset = 2.5;
                if (at(x, y - 1) != label) { ctx.moveTo(r.x, r.y + inset); ctx.lineTo(r.x + r.w, r.y + inset); }
                if (at(x, y + 1) != label) { ctx.moveTo(r.x, r.y + r.h - inset); ctx.lineTo(r.x + r.w, r.y + r.h - inset); }
                if (at(x - 1, y) != label) { ctx.moveTo(r.x + inset, r.y); ctx.lineTo(r.x + inset, r.y + r.h); }
                if (at(x + 1, y) != label) { ctx.moveTo(r.x + r.w - inset, r.y); ctx.lineTo(r.x + r.w - inset, r.y + r.h); }
            }
        ctx.stroke();
        ctx.setLineDash(NoDash);
    }

    /// <summary>Purpose and status, centred at the top of each designated room. Incomplete rooms name their first unmet requirement.</summary>
    void drawRoomLabels(SimState state, double size)
    {
        int px = (int)Math.Round(Math.Max(10, Math.Min(13, size * 0.5)));
        ctx.font(px);
        ctx.textAlign = "center";
        ctx.textBaseline = "top";
        var placed = new List<(double x0, double y0, double x1, double y1)>();
        foreach (var d in state.roomDesignations)
        {
            var room = Rooms.roomAt(state, d.x, d.y);
            var status = Rooms.evaluateRoom(state, room, d.purpose);
            var missing = status.requirements.FirstOrDefault((r) => !r.met);
            int inUse = bedsInUse(state, room);
            string detail = missing != null
                ? $"⚠ needs: {missing.label}"
                : d.purpose is "housing" or "infirmary" or "barracks"
                    ? $"✓ {status.beds} bed{(status.beds == 1 ? "" : "s")}{(inUse > 0 ? $", {inUse} in use" : "")}"
                    : d.purpose == "messhall"
                        ? $"✓ {status.tables} table{(status.tables == 1 ? "" : "s")}"
                        : $"✓ {status.size} tiles";
            // Too wide for the room on screen: just the name and a tick or warning sign.
            double roomPx = room != null ? (room.maxX - room.minX + 1) * size : double.PositiveInfinity;
            string full = $"{PURPOSE_NAMES[d.purpose]}  {detail}";
            string text = ctx.measureText(full) + 8 <= roomPx ? full : $"{PURPOSE_NAMES[d.purpose]} {(missing != null ? "⚠" : "✓")}";
            // On the room's top wall, so it never covers what's inside.
            var p = room != null ? ToScreen((room.minX + room.maxX + 1) / 2, room.minY - 0.85) : ToScreen(d.x + 0.5, d.y + 0.15);
            double w = ctx.measureText(text) + 8;
            double h = px + 4;
            // Step down past any label already drawn where this one would go.
            bool hits(double y) => placed.Any((r) => p.x - w / 2 < r.x1 && p.x + w / 2 > r.x0 && y < r.y1 && y + h > r.y0);
            for (int tries = 0; tries < 4 && hits(p.y); tries++) p.y += h + 2;
            placed.Add((p.x - w / 2, p.y, p.x + w / 2, p.y + h));
            ctx.fillStyle = COLOURS.labelBg;
            ctx.fillRect(p.x - w / 2, p.y, w, h);
            ctx.fillStyle = missing != null ? COLOURS.incomplete : COLOURS.roomLabel[d.purpose];
            ctx.fillText(text, p.x, p.y + 2);
        }
    }

    /// <summary>Tilled soil with furrows; a sprout that grows with the crop; gold when ripe.</summary>
    void drawPlot(FarmPlot plot)
    {
        var r = tileRect(plot.x, plot.y);
        ctx.fillStyle = COLOURS.soil;
        ctx.fillRect(r.x + 1, r.y + 1, r.w - 2, r.h - 2);
        ctx.fillStyle = COLOURS.furrow;
        foreach (var t in new[] { 0.3, 0.55, 0.8 }) ctx.fillRect(r.x + 2, Math.Round(r.y + r.h * t), r.w - 4, Math.Max(1, Math.Round(r.h * 0.06)));
        if (!plot.planted) return;
        bool ripe = Farming.isRipe(plot);
        double grown = ripe ? 1 : plot.growth / Farming.growTicks(plot);
        double h = Math.Max(2, r.h * (0.15 + 0.55 * grown));
        double w = Math.Max(2, r.w * (0.12 + 0.3 * grown));
        ctx.fillStyle = ripe ? (plot.crop == "wheat" ? "#e8c860" : plot.crop == "herb" ? "#7ad7a0" : COLOURS.ripe) : COLOURS.sprout;
        ctx.fillRect(r.x + r.w / 2 - w / 2, r.y + r.h * 0.85 - h, w, h);
    }

    /// <summary>
    /// Generated fittings and containers (buildings.ts): a shape and colour per look. Containers
    /// also show their finds as pips, dim when searched out, and the marked outline.
    /// </summary>
    void drawFixture(Furniture f, double size)
    {
        var look = f.look ?? "crate";
        var style = LOOKS.TryGetValue(look, out var s) ? s : LOOKS["crate"];
        var r = tileRect(f.x, f.y);
        double w = r.w * style.w, h = r.h * style.h;
        double x = r.x + (r.w - w) / 2, y = r.y + (r.h - h) / 2;
        bool container = f.kind == "crate";
        ctx.globalAlpha = container && f.loot == 0 ? 0.55 : 1;
        ctx.fillStyle = style.colour;
        ctx.fillRect(x, y, w, h);
        ctx.strokeStyle = "rgba(0, 0, 0, 0.45)";
        ctx.lineWidth = 1;
        ctx.strokeRect(x + 0.5, y + 0.5, w - 1, h - 1);
        if (size >= 10) drawDetail(look, x, y, w, h);
        ctx.globalAlpha = 1;
        if (!container) return;
        if (f.scavenge)
        {
            ctx.strokeStyle = COLOURS.crateMarked;
            ctx.lineWidth = 1.5;
            ctx.setLineDash(new double[] { 3, 2 });
            ctx.strokeRect(r.x + 1.5, r.y + 1.5, r.w - 3, r.h - 3);
            ctx.setLineDash(NoDash);
        }
        if (f.loot > 0 && size >= MIN_GRID_LINE_PX)
        {
            double pip = Math.Max(1.5, size * 0.06);
            ctx.fillStyle = COLOURS.lootPip;
            for (int i = 0; i < Math.Min(f.loot, 5); i++) ctx.fillRect(x + 2 + i * (pip + 1.5), y + h - pip - 2, pip, pip);
        }
    }

    /// <summary>Colour, width and height (shares of a tile) per look.</summary>
    static readonly Dictionary<string, (string colour, double w, double h)> LOOKS = new()
    {
        ["counter"] = ("#6b5a48", 0.9, 0.55), ["icebox"] = ("#9aa8b0", 0.7, 0.8), ["shelf"] = ("#5e4a36", 0.9, 0.4), ["wardrobe"] = ("#5a4030", 0.9, 0.55),
        ["dresser"] = ("#6a4c34", 0.7, 0.5), ["cabinet"] = ("#7a8a8c", 0.6, 0.5), ["toolbox"] = ("#9a3a2a", 0.6, 0.4), ["locker"] = ("#5a6a7a", 0.55, 0.85),
        ["desk"] = ("#6a5038", 0.85, 0.55), ["pallet"] = ("#8a7050", 0.85, 0.85), ["sack"] = ("#a89060", 0.6, 0.6), ["crate"] = (COLOURS.crate, 0.72, 0.72),
        ["sofa"] = ("#5a4a6a", 0.9, 0.5), ["bed"] = ("#7a7a90", 0.7, 0.9), ["toilet"] = ("#c8cccc", 0.45, 0.55), ["bath"] = ("#b8bcc0", 0.85, 0.55),
        ["table"] = ("#5e4632", 0.75, 0.75), ["oven"] = ("#3a3a3c", 0.7, 0.7), ["tv"] = ("#1e1e22", 0.7, 0.25), ["car"] = ("#5a2a24", 0.95, 0.95),
        ["pew"] = ("#5a4028", 0.95, 0.35), ["altar"] = ("#c8b070", 0.8, 0.5), ["hay"] = ("#b8a050", 0.85, 0.85), ["tractor"] = ("#8a3a20", 0.9, 0.9),
        ["pump"] = ("#a0302a", 0.4, 0.6), ["bench"] = ("#6a5a40", 0.8, 0.3), ["cot"] = ("#7a8a7a", 0.6, 0.9), ["register"] = ("#4a4a4a", 0.5, 0.4),
        ["machine"] = ("#4a5058", 0.9, 0.9), ["silo"] = ("#8a9098", 1, 1),
        ["sink"] = ("#a8b4b8", 0.75, 0.5), ["bin"] = ("#3e4a40", 0.4, 0.45), ["nightstand"] = ("#6a4c34", 0.45, 0.45), ["bookcase"] = ("#4e3a28", 0.9, 0.4),
        ["shower"] = ("#9ab0b8", 0.75, 0.75), ["armchair"] = ("#6a4a4a", 0.55, 0.55), ["washer"] = ("#d0d2d0", 0.65, 0.65),
    };

    /// <summary>A few strokes per look so each reads as what it is (doors, drawers, a basin, a seat), over its block of colour.</summary>
    void drawDetail(string look, double x, double y, double w, double h)
    {
        void line(double x0, double y0, double x1, double y1)
        {
            ctx.beginPath();
            ctx.moveTo(x + x0 * w, y + y0 * h);
            ctx.lineTo(x + x1 * w, y + y1 * h);
            ctx.stroke();
        }
        void blob(double cx, double cy, double rx, double ry, string fill)
        {
            ctx.fillStyle = fill;
            ctx.beginPath();
            ctx.ellipse(x + cx * w, y + cy * h, rx * w, ry * h, 0, 0, Math.PI * 2);
            ctx.fill();
        }
        void box(double x0, double y0, double x1, double y1, string fill)
        {
            ctx.fillStyle = fill;
            ctx.fillRect(x + x0 * w, y + y0 * h, (x1 - x0) * w, (y1 - y0) * h);
        }
        ctx.strokeStyle = "rgba(0, 0, 0, 0.5)";
        ctx.lineWidth = 1;
        switch (look)
        {
            case "icebox": // freezer door over the fridge door, and a handle
                line(0, 0.35, 1, 0.35);
                line(0.82, 0.12, 0.82, 0.25);
                line(0.82, 0.48, 0.82, 0.75);
                break;
            case "counter": // a worktop edge and cupboard doors
                box(0, 0, 1, 0.25, "rgba(255, 255, 255, 0.18)");
                line(0.5, 0.25, 0.5, 1);
                break;
            case "sink": // a worktop with a basin and a tap
                blob(0.5, 0.55, 0.28, 0.3, "#5a6a70");
                box(0.47, 0.05, 0.53, 0.25, "#d8dcdc");
                break;
            case "oven": // four rings
                foreach (var (cx, cy) in new[] { (0.3, 0.3), (0.7, 0.3), (0.3, 0.7), (0.7, 0.7) }) blob(cx, cy, 0.13, 0.13, "#5a5a5e");
                break;
            case "bin": // a lid
                box(0, 0, 1, 0.22, "rgba(255, 255, 255, 0.2)");
                break;
            case "wardrobe": // two doors
                line(0.5, 0, 0.5, 1);
                line(0.42, 0.4, 0.42, 0.6);
                line(0.58, 0.4, 0.58, 0.6);
                break;
            case "dresser":
            case "nightstand": // drawers
                line(0, 0.33, 1, 0.33);
                line(0, 0.66, 1, 0.66);
                break;
            case "cabinet": // a door and a handle
                line(0.75, 0.3, 0.75, 0.7);
                break;
            case "shelf":
            case "bookcase": // shelves (a bookcase with spines on them)
                line(0, 0.5, 1, 0.5);
                if (look == "bookcase")
                {
                    var spines = new[] { "#8a3a2a", "#3a5a7a", "#6a7a3a" };
                    for (int i = 0; i < 6; i++) box(0.06 + i * 0.15, 0.08, 0.13 + i * 0.15, 0.45, spines[i % 3]);
                }
                break;
            case "locker": // vents
                foreach (var v in new[] { 0.15, 0.25, 0.35 }) line(0.2, v, 0.8, v);
                break;
            case "toolbox": // a handle
                line(0.3, 0.2, 0.7, 0.2);
                break;
            case "toilet": // the cistern and the bowl
                box(0, 0, 1, 0.3, "rgba(0, 0, 0, 0.12)");
                blob(0.5, 0.65, 0.38, 0.32, "#e0e4e4");
                break;
            case "bath": // the tub
                blob(0.5, 0.5, 0.42, 0.34, "#dfe6ea");
                break;
            case "shower": // a tray and its drain
                ctx.strokeRect(x + 0.12 * w, y + 0.12 * h, 0.76 * w, 0.76 * h);
                blob(0.5, 0.5, 0.07, 0.07, "#4a5a60");
                break;
            case "washer": // the door
                blob(0.5, 0.55, 0.3, 0.3, "#6a7a88");
                break;
            case "bed":
            case "cot": // a pillow
                box(0.12, 0.06, 0.88, 0.24, "#d8d8d0");
                break;
            case "sofa":
            case "armchair": // the back and the arms
                box(0, 0, 1, 0.3, "rgba(0, 0, 0, 0.2)");
                box(0, 0, 0.15, 1, "rgba(0, 0, 0, 0.2)");
                box(0.85, 0, 1, 1, "rgba(0, 0, 0, 0.2)");
                break;
            case "tv": // the screen
                box(0.08, 0.15, 0.92, 0.85, "#38424a");
                break;
            case "desk": // a drawer
                line(0.65, 0, 0.65, 1);
                break;
            case "crate":
            case "pallet": // slats
                line(0, 0.33, 1, 0.33);
                line(0, 0.66, 1, 0.66);
                break;
        }
    }

    /// <summary>A slatted crate: dull when searched out, yellow dashed outline when marked, pips for finds left.</summary>
    void drawCrate(Furniture f, double size)
    {
        var r = tileRect(f.x, f.y);
        double pad = Math.Max(1, Math.Round(r.w * 0.14));
        double x = r.x + pad, y = r.y + pad, w = r.w - pad * 2, h = r.h - pad * 2;
        ctx.fillStyle = f.loot > 0 ? COLOURS.crate : COLOURS.crateEmpty;
        ctx.fillRect(x, y, w, h);
        ctx.strokeStyle = COLOURS.crateSlat;
        ctx.lineWidth = 1;
        ctx.beginPath();
        foreach (var t in new[] { 1 / 3.0, 2 / 3.0 })
        {
            ctx.moveTo(x, Math.Round(y + h * t) + 0.5);
            ctx.lineTo(x + w, Math.Round(y + h * t) + 0.5);
        }
        ctx.stroke();
        ctx.strokeRect(x + 0.5, y + 0.5, w - 1, h - 1);
        if (f.scavenge)
        {
            ctx.strokeStyle = COLOURS.crateMarked;
            ctx.lineWidth = 1.5;
            ctx.setLineDash(new double[] { 3, 2 });
            ctx.strokeRect(r.x + 1.5, r.y + 1.5, r.w - 3, r.h - 3);
            ctx.setLineDash(NoDash);
        }
        if (f.loot > 0 && size >= MIN_GRID_LINE_PX)
        {
            double pip = Math.Max(1.5, size * 0.06);
            ctx.fillStyle = COLOURS.lootPip;
            for (int i = 0; i < f.loot; i++) ctx.fillRect(x + 2 + i * (pip + 1.5), y + h - pip - 2, pip, pip);
        }
    }

    static int bedsInUse(SimState state, Room? room)
    {
        if (room == null) return 0;
        return state.furniture.Count((f) => f.kind == "bed" && f.occupant != null && Rooms.labelAt(state, f.x, f.y) == room.index);
    }

    void drawBed(int x, int y)
    {
        var r = tileRect(x, y);
        double pad = Math.Max(1, Math.Round(r.w * 0.12));
        ctx.fillStyle = COLOURS.bedFrame;
        ctx.fillRect(r.x + pad, r.y + pad, r.w - pad * 2, r.h - pad * 2);
        ctx.fillStyle = COLOURS.bedSheet;
        ctx.fillRect(r.x + pad + 1, r.y + pad + Math.Round(r.h * 0.3), r.w - pad * 2 - 2, r.h - pad * 2 - Math.Round(r.h * 0.3) - 1);
        ctx.fillStyle = COLOURS.bedPillow;
        ctx.fillRect(r.x + pad + 2, r.y + pad + 2, r.w - pad * 2 - 4, Math.Max(2, Math.Round(r.h * 0.2)));
    }

    /// <summary>A long bench with a saw and hammer on it; pips for wood brought in.</summary>
    void drawWorkbench(Furniture f, double size)
    {
        var r = tileRect(f.x, f.y);
        double pad = Math.Max(1, Math.Round(r.w * 0.08));
        ctx.fillStyle = COLOURS.tableEdge;
        ctx.fillRect(r.x + pad, r.y + r.h * 0.25, r.w - pad * 2, r.h * 0.5);
        ctx.fillStyle = COLOURS.tableTop;
        ctx.fillRect(r.x + pad + 1, r.y + r.h * 0.25 + 1, r.w - pad * 2 - 2, r.h * 0.5 - 2);
        ctx.fillStyle = COLOURS.blade;
        ctx.fillRect(r.x + r.w * 0.2, r.y + r.h * 0.4, r.w * 0.35, Math.Max(1, r.h * 0.08));
        ctx.fillStyle = COLOURS.hilt;
        ctx.fillRect(r.x + r.w * 0.65, r.y + r.h * 0.33, Math.Max(1, r.w * 0.08), r.h * 0.3);
        double pip = Math.Max(2, Math.Round(size * 0.12));
        ctx.fillStyle = COLOURS.wood;
        for (int i = 0; i < f.stored; i++) ctx.fillRect(r.x + 2 + i * (pip + 1.5), r.y + r.h - pip - 2, pip, pip);
    }

    /// <summary>A round canopy on a trunk: thinner as it's chopped, a stump when bare, dashed when marked.</summary>
    void drawTree(Furniture f, double size)
    {
        var p = ToScreen(f.x + 0.5, f.y + 0.5);
        ctx.fillStyle = COLOURS.trunk;
        ctx.fillRect(p.x - size * 0.07, p.y - size * 0.05, size * 0.14, size * 0.38);
        if (f.loot > 0)
        {
            ctx.fillStyle = COLOURS.leaves;
            ctx.beginPath();
            ctx.arc(p.x, p.y - size * 0.08, size * (0.18 + 0.08 * f.loot), 0, Math.PI * 2);
            ctx.fill();
        }
        if (f.scavenge)
        {
            ctx.strokeStyle = COLOURS.crateMarked;
            ctx.lineWidth = 1.5;
            ctx.setLineDash(new double[] { 3, 3 });
            var r = tileRect(f.x, f.y);
            ctx.strokeRect(r.x + 1.5, r.y + 1.5, r.w - 3, r.h - 3);
            ctx.setLineDash(NoDash);
        }
    }

    /// <summary>Spikes, one row per charge left.</summary>
    void drawTrap(Furniture f)
    {
        var r = tileRect(f.x, f.y);
        ctx.fillStyle = COLOURS.spike;
        for (int row = 0; row < f.loot; row++)
        {
            double y = r.y + r.h * (0.3 + 0.22 * row);
            for (int k = 0; k < 3; k++)
            {
                double x = r.x + r.w * (0.22 + 0.28 * k);
                ctx.beginPath();
                ctx.moveTo(x - r.w * 0.08, y + r.h * 0.12);
                ctx.lineTo(x, y - r.h * 0.06);
                ctx.lineTo(x + r.w * 0.08, y + r.h * 0.12);
                ctx.fill();
            }
        }
    }

    /// <summary>A dark iron stove with a glowing door.</summary>
    void drawStove(Furniture f)
    {
        var r = tileRect(f.x, f.y);
        double pad = Math.Max(1, Math.Round(r.w * 0.12));
        ctx.fillStyle = "#3a3d42";
        ctx.fillRect(r.x + pad, r.y + pad, r.w - pad * 2, r.h - pad * 2);
        ctx.fillStyle = f.stored > 0 ? "#ff9a3b" : "#6b4a2e";
        ctx.fillRect(r.x + r.w * 0.3, r.y + r.h * 0.5, r.w * 0.4, r.h * 0.25);
    }

    /// <summary>A square wooden table top.</summary>
    void drawTable(int x, int y)
    {
        var r = tileRect(x, y);
        double pad = Math.Max(1, Math.Round(r.w * 0.15));
        ctx.fillStyle = COLOURS.tableTop;
        ctx.fillRect(r.x + pad, r.y + pad, r.w - pad * 2, r.h - pad * 2);
        ctx.strokeStyle = COLOURS.tableEdge;
        ctx.lineWidth = Math.Max(1, r.w * 0.06);
        ctx.strokeRect(r.x + pad, r.y + pad, r.w - pad * 2, r.h - pad * 2);
    }

    /// <summary>Battered wall or door: a red wash and cracks that grow with the damage (0..1).</summary>
    void drawDamage(int x, int y, double amount)
    {
        var r = tileRect(x, y);
        ctx.fillStyle = $"rgba(255, 70, 50, {0.12 + 0.4 * amount:0.###})";
        ctx.fillRect(r.x, r.y, r.w, r.h);
        ctx.strokeStyle = "rgba(20, 10, 10, 0.85)";
        ctx.lineWidth = Math.Max(1, r.w * 0.06);
        ctx.beginPath();
        ctx.moveTo(r.x + r.w * 0.5, r.y + r.h * 0.1);
        ctx.lineTo(r.x + r.w * 0.4, r.y + r.h * (0.1 + 0.4 * amount));
        ctx.lineTo(r.x + r.w * (0.4 + 0.3 * amount), r.y + r.h * (0.1 + 0.8 * amount));
        ctx.stroke();
    }

    static readonly (double x, double y)[] DirtSpeckles = { (0.2, 0.3), (0.55, 0.2), (0.75, 0.6), (0.35, 0.7), (0.6, 0.85) };

    void drawDirt(int x, int y, double size)
    {
        var r = tileRect(x, y);
        ctx.fillStyle = COLOURS.dirt;
        // A fixed speckle pattern per tile, so it doesn't shimmer.
        double dot = Math.Max(1, Math.Round(size * 0.1));
        foreach (var (fx, fy) in DirtSpeckles) ctx.fillRect(r.x + Math.Round(r.w * fx), r.y + Math.Round(r.h * fy), dot, dot);
    }

    void drawItem(string kind, double wx, double wy, double size)
    {
        if (kind == "food") drawFood(wx, wy, size);
        else if (kind == "meal") drawMeal(wx, wy, size);
        else if (kind == "medicine") drawMedicine(wx, wy, size);
        else if (kind == "bandage") drawBandage(wx, wy, size);
        else if (kind == "weapon") drawWeapon(wx, wy, size);
        else if (kind == "wood") drawWood(wx, wy, size);
        else drawGlyph(wx, wy, size, COLOURS.GLYPHS.TryGetValue(kind, out var g) ? g : ("#cccccc", "?"));
    }

    /// <summary>Flames: two flickering tongues (the flicker comes from the tick, so it's stable when paused).</summary>
    void drawFire(int x, int y, int tick)
    {
        var r = tileRect(x, y);
        double flicker = (tick + x * 7 + y * 13) % 6 / 6.0;
        ctx.fillStyle = "rgba(255, 90, 20, 0.55)";
        ctx.fillRect(r.x, r.y, r.w, r.h);
        ctx.fillStyle = $"rgba(255, {170 + Math.Round(60 * flicker)}, 60, 0.85)";
        ctx.beginPath();
        ctx.moveTo(r.x + r.w * 0.2, r.y + r.h * 0.9);
        ctx.lineTo(r.x + r.w * (0.35 + 0.1 * flicker), r.y + r.h * 0.2);
        ctx.lineTo(r.x + r.w * 0.5, r.y + r.h * 0.9);
        ctx.lineTo(r.x + r.w * (0.65 - 0.1 * flicker), r.y + r.h * 0.35);
        ctx.lineTo(r.x + r.w * 0.8, r.y + r.h * 0.9);
        ctx.closePath();
        ctx.fill();
    }

    /// <summary>A dog: a small brown body with a darker head, and its name when zoomed in.</summary>
    void drawDog(Dog d, double alpha, double size)
    {
        var w = worldPos(d, alpha);
        var p = ToScreen(w.x, w.y);
        double r = Math.Max(2, size * 0.2);
        ctx.fillStyle = "#8a5a2e";
        ctx.fillRect(p.x - r * 1.3, p.y - r * 0.6, r * 2.2, r * 1.2);
        ctx.fillStyle = "#5a3a1e";
        ctx.fillRect(p.x + r * 0.7, p.y - r, r, r);
        if (size >= MIN_LABEL_PX)
        {
            ctx.fillStyle = COLOURS.label;
            ctx.font((int)Math.Round(Math.Min(11, size * 0.35)));
            ctx.textAlign = "center";
            ctx.textBaseline = "bottom";
            ctx.fillText(d.name, p.x, p.y - r - 2);
        }
    }

    void drawGlyph(double wx, double wy, double size, (string colour, string letter) g)
    {
        var p = ToScreen(wx, wy);
        double s = size * 0.5;
        ctx.fillStyle = g.colour;
        ctx.fillRect(p.x - s / 2, p.y - s / 2, s, s);
        ctx.strokeStyle = "rgba(0, 0, 0, 0.5)";
        ctx.lineWidth = 1;
        ctx.strokeRect(p.x - s / 2 + 0.5, p.y - s / 2 + 0.5, s - 1, s - 1);
        if (s < 8) return;
        ctx.fillStyle = "#ffffff";
        ctx.font((int)Math.Round(s * 0.7), bold: true);
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillText(g.letter, p.x, p.y + 1);
    }

    /// <summary>A machete: a pale diagonal blade on a brown hilt.</summary>
    void drawWeapon(double wx, double wy, double size)
    {
        var p = ToScreen(wx, wy);
        double s = size * 0.3;
        ctx.strokeStyle = COLOURS.blade;
        ctx.lineWidth = Math.Max(2, size * 0.12);
        ctx.beginPath();
        ctx.moveTo(p.x - s * 0.3, p.y + s * 0.3);
        ctx.lineTo(p.x + s, p.y - s);
        ctx.stroke();
        ctx.strokeStyle = COLOURS.hilt;
        ctx.lineWidth = Math.Max(2, size * 0.14);
        ctx.beginPath();
        ctx.moveTo(p.x - s, p.y + s);
        ctx.lineTo(p.x - s * 0.3, p.y + s * 0.3);
        ctx.stroke();
    }

    /// <summary>A green bottle.</summary>
    void drawMedicine(double wx, double wy, double size)
    {
        var p = ToScreen(wx, wy);
        double s = size * 0.2;
        ctx.fillStyle = "#4caf6a";
        ctx.fillRect(p.x - s * 0.6, p.y - s * 0.4, s * 1.2, s * 1.4);
        ctx.fillRect(p.x - s * 0.25, p.y - s * 0.9, s * 0.5, s * 0.5);
        ctx.fillStyle = "#f4f1ea";
        ctx.fillRect(p.x - s * 0.4, p.y, s * 0.8, s * 0.35);
    }

    /// <summary>A steaming bowl.</summary>
    void drawMeal(double wx, double wy, double size)
    {
        var p = ToScreen(wx, wy);
        double r = size * 0.22;
        ctx.fillStyle = "#c9a26b";
        ctx.beginPath();
        ctx.arc(p.x, p.y, r, 0, Math.PI);
        ctx.fill();
        ctx.fillStyle = "#e8743b";
        ctx.fillRect(p.x - r, p.y - r * 0.15, r * 2, r * 0.3);
        ctx.strokeStyle = "rgba(240, 240, 240, 0.7)";
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(p.x - r * 0.3, p.y - r * 0.4);
        ctx.lineTo(p.x - r * 0.1, p.y - r * 1.1);
        ctx.moveTo(p.x + r * 0.3, p.y - r * 0.4);
        ctx.lineTo(p.x + r * 0.5, p.y - r * 1.1);
        ctx.stroke();
    }

    /// <summary>A white pad with a red cross.</summary>
    void drawBandage(double wx, double wy, double size)
    {
        var p = ToScreen(wx, wy);
        double s = size * 0.46;
        ctx.fillStyle = COLOURS.bandage;
        ctx.fillRect(p.x - s / 2, p.y - s / 2, s, s);
        ctx.fillStyle = COLOURS.bandageCross;
        double bar = Math.Max(1, s * 0.22);
        ctx.fillRect(p.x - bar / 2, p.y - s * 0.35, bar, s * 0.7);
        ctx.fillRect(p.x - s * 0.35, p.y - bar / 2, s * 0.7, bar);
    }

    /// <summary>A tin can: easy to tell apart from wood planks at a glance.</summary>
    void drawFood(double wx, double wy, double size)
    {
        var p = ToScreen(wx, wy);
        double w = size * 0.42, h = size * 0.5;
        ctx.fillStyle = COLOURS.foodCan;
        ctx.fillRect(p.x - w / 2, p.y - h / 2, w, h);
        ctx.fillStyle = COLOURS.foodLabel;
        ctx.fillRect(p.x - w / 2, p.y - h * 0.15, w, h * 0.3);
        ctx.strokeStyle = COLOURS.foodRim;
        ctx.lineWidth = 1;
        ctx.strokeRect(p.x - w / 2 + 0.5, p.y - h / 2 + 0.5, w - 1, h - 1);
    }

    void drawWood(double wx, double wy, double size)
    {
        var p = ToScreen(wx, wy);
        double w = size * 0.6, h = size * 0.16;
        ctx.fillStyle = COLOURS.wood;
        ctx.strokeStyle = COLOURS.woodGrain;
        ctx.lineWidth = 1;
        foreach (var dy in new[] { -h * 1.1, 0, h * 1.1 })
        {
            ctx.fillRect(p.x - w / 2, p.y + dy - h / 2, w, h);
            ctx.strokeRect(p.x - w / 2 + 0.5, p.y + dy - h / 2 + 0.5, w - 1, h - 1);
        }
    }

    void drawBlueprint(Blueprint bp, double size)
    {
        var r = tileRect(bp.x, bp.y);
        var edge = COLOURS.blueprintEdge[bp.target];
        bool waiting = bp.needsMaterial && !bp.delivered;
        if (bp.side != null)
        {
            // An edge wall or door to build: a ghost bar on the border (dashed until its material is in).
            double t = Math.Max(2, size * 0.2);
            double bx = r.x - t / 2, by = r.y - t / 2;
            double bw = bp.side == "n" ? r.w + t : t, bh = bp.side == "n" ? t : r.h + t;
            ctx.fillStyle = COLOURS.blueprint[bp.target];
            ctx.fillRect(bx, by, bw, bh);
            ctx.strokeStyle = edge;
            ctx.lineWidth = 1;
            ctx.setLineDash(waiting ? new double[] { 3, 3 } : NoDash);
            ctx.strokeRect(bx + 0.5, by + 0.5, bw - 1, bh - 1);
            ctx.setLineDash(NoDash);
            return;
        }
        ctx.fillStyle = COLOURS.blueprint[bp.target];
        ctx.fillRect(r.x, r.y, r.w, r.h);
        ctx.strokeStyle = edge;
        ctx.lineWidth = 1.5;
        // Dashed while still waiting for material, solid once it can be built.
        ctx.setLineDash(waiting ? new double[] { 3, 3 } : NoDash);
        ctx.strokeRect(r.x + 1.5, r.y + 1.5, r.w - 3, r.h - 3);
        ctx.setLineDash(NoDash);
        if (bp.target == "floor")
        {
            ctx.beginPath();
            ctx.moveTo(r.x + 3, r.y + 3);
            ctx.lineTo(r.x + r.w - 3, r.y + r.h - 3);
            ctx.moveTo(r.x + r.w - 3, r.y + 3);
            ctx.lineTo(r.x + 3, r.y + r.h - 3);
            ctx.stroke();
        }
        if (bp.workDone > 0)
        {
            double barH = Math.Max(2, Math.Round(size * 0.12));
            ctx.fillStyle = "rgba(0, 0, 0, 0.6)";
            ctx.fillRect(r.x + 2, r.y + r.h - barH - 2, r.w - 4, barH);
            ctx.fillStyle = COLOURS.progress;
            ctx.fillRect(r.x + 2, r.y + r.h - barH - 2, (r.w - 4) * bp.workDone / bp.workRequired, barH);
        }
    }
}
