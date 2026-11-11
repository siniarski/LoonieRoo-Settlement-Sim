// Top-down view (src/render/draw.ts). Reads sim state only; never mutates it.

using System;
using System.Collections.Generic;
using System.Linq;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.Render;

public sealed class DrawOptions
{
    public Point? hover;
    /// <summary>An edge tool's border under the cursor, previewed ("n" or "w").</summary>
    public (int x, int y, string side)? hoverEdge;
    /// <summary>Armed build tool, or null in select mode.</summary>
    public string? tool;
    /// <summary>Fraction of the next tick elapsed, for interpolating movement.</summary>
    public double alpha;
    public string? selectedId;
    /// <summary>Debug overlay: path previews for every survivor, everyone drawn through the fog.</summary>
    public bool showDebug;
    /// <summary>Box being dragged out by an area tool, if any.</summary>
    public (Point from, Point to)? areaDrag;
}

public struct R { public double x, y, w, h; }

public sealed partial class Painter
{
    public static readonly Dictionary<string, string> PURPOSE_NAMES = new()
    {
        ["housing"] = "Housing", ["storage"] = "Storage", ["armory"] = "Armory", ["messhall"] = "Mess hall", ["recroom"] = "Rec room", ["infirmary"] = "Infirmary",
        ["barracks"] = "Barracks", ["workshop"] = "Workshop", ["graveyard"] = "Graveyard", ["greenhouse"] = "Greenhouse", ["cell"] = "Cells",
    };

    /// <summary>How dark full night gets (overlay opacity).</summary>
    const double NIGHT_MAX_ALPHA = 0.55;
    /// <summary>Below this tile size, grid lines are just noise.</summary>
    public const double MIN_GRID_LINE_PX = 10;
    /// <summary>Below this tile size, survivor names are unreadable.</summary>
    public const double MIN_LABEL_PX = 18;

    static readonly string[] CAR_COLOURS = { "#8a2f2a", "#2f4f7a", "#c9c2b0", "#3d5a3a", "#6b6b70", "#b08a2a" };

    readonly Ctx ctx;
    Cam camera;
    Vport viewport;

    public Painter(Ctx ctx) { this.ctx = ctx; }

    P ToScreen(double wx, double wy) => worldToScreen(camera, viewport, wx, wy);

    /// <summary>Integer pixel rect for a tile, so neighbouring tiles share edges without seams.</summary>
    public R tileRect(double x, double y)
    {
        var a = ToScreen(x, y);
        var b = ToScreen(x + 1, y + 1);
        double x0 = Math.Round(a.x), y0 = Math.Round(a.y);
        return new R { x = x0, y = y0, w = Math.Round(b.x) - x0, h = Math.Round(b.y) - y0 };
    }

    public void draw(SimState state, Cam cam, Vport vp, DrawOptions options)
    {
        camera = cam;
        viewport = vp;
        var grid = state.grid;
        ctx.fillStyle = COLOURS.background;
        ctx.fillRect(0, 0, vp.width, vp.height);

        double size = tileSize(camera);
        var range = visibleTiles(camera, viewport, grid.width, grid.height);
        // On screen and explored: everything else is skipped (big maps, fog of war).
        bool shown(int x, int y) => x >= range.x0 && x <= range.x1 && y >= range.y0 && y <= range.y1 && Grids.exploredAt(grid, y * grid.width + x) == 1;

        for (int y = range.y0; y <= range.y1; y++)
        {
            for (int x = range.x0; x <= range.x1; x++)
            {
                int i = y * grid.width + x;
                var tile = Grids.tileAt(grid, i);
                if (Grids.exploredAt(grid, i) != 1)
                {
                    var r = tileRect(x, y);
                    ctx.fillStyle = COLOURS.fog;
                    ctx.fillRect(r.x, r.y, r.w, r.h);
                }
                else drawTile(x, y, tile, Grids.groundAt(grid, i), size, tile == "door" && grid.locked.Contains(i), tile == "door" && grid.open.Contains(i));
                if (state.settings.winter && Climate.isRadioactive(state, x, y))
                {
                    var r = tileRect(x, y);
                    ctx.fillStyle = COLOURS.radiation;
                    ctx.fillRect(r.x, r.y, r.w, r.h);
                }
            }
        }

        drawRoomTints(state, range);

        for (int y = range.y0; y <= range.y1; y++)
            for (int x = range.x0; x <= range.x1; x++)
            {
                int i = y * grid.width + x;
                double damage = Grids.damageAt(grid, i);
                var tile = Grids.tileAt(grid, i);
                if (damage > 0 && (tile == "wall" || tile == "door")) drawDamage(x, y, damage / Breach.structureHealth(grid, i));
            }

        for (int y = range.y0; y <= range.y1; y++)
            for (int x = range.x0; x <= range.x1; x++)
                if (Grids.dirtAt(grid, y * grid.width + x) == 1) drawDirt(x, y, size);

        if (size >= MIN_GRID_LINE_PX) drawGridLines(range);
        drawMapEdge(grid.width, grid.height);
        drawEdges(grid, range, size);
        if (options.hoverEdge is { } he)
        {
            // The edge tool's target: a pale bar on the border.
            var r = tileRect(he.x, he.y);
            double t = Math.Max(2, size * 0.2);
            ctx.fillStyle = "rgba(240, 220, 160, 0.6)";
            if (he.side == "n") ctx.fillRect(r.x - t / 2, r.y - t / 2, r.w + t, t);
            else ctx.fillRect(r.x - t / 2, r.y - t / 2, t, r.h + t);
        }
        if (options.showDebug) drawRoomOutlines(state, range);

        foreach (var plot in state.farm) drawPlot(plot);
        foreach (var f in state.furniture)
        {
            if (!shown(f.x, f.y)) continue;
            if (f.kind == "bed") drawBed(f.x, f.y);
            else if (f.kind == "table") drawTable(f.x, f.y);
            else if (f.kind == "workbench") drawWorkbench(f, size);
            else if (f.kind == "stove") drawStove(f);
            else if (f.kind == "tree") drawTree(f, size);
            else if (f.kind == "trap") drawTrap(f);
            else if (f.kind == "decor" || (f.kind == "crate" && f.look != null && f.look != "crate")) drawFixture(f, size);
            else if (COLOURS.FURNITURE_GLYPHS.TryGetValue(f.kind, out var g)) drawGlyph(f.x + 0.5, f.y + 0.5, size * 1.7, g);
            else drawCrate(f, size);
        }
        foreach (var bp in state.blueprints) drawBlueprint(bp, size);
        foreach (var f in state.fires) if (shown(f.x, f.y)) drawFire(f.x, f.y, state.tick);
        // Lamplight: a warm pool round each lit lamp.
        foreach (var l in Power.litLamps(state))
        {
            if (!shown(l.x, l.y)) continue;
            var c = ToScreen(l.x + 0.5, l.y + 0.5);
            ctx.radialGlow(c.x, c.y, size * (Power.LAMP_LIGHT + 0.5), new Godot.Color(1f, 220 / 255f, 140 / 255f, 0.30f));
        }
        foreach (var item in state.items)
            if (item.pos != null && shown(item.pos.x, item.pos.y)) drawItem(item.kind, item.pos.x + 0.5, item.pos.y + 0.5, size);

        // Weather: a cool wash for rain, a pale one for snow.
        if (state.weather != "clear")
        {
            ctx.fillStyle = state.weather switch
            {
                "rain" => "rgba(90, 120, 160, 0.12)",
                "storm" => "rgba(60, 70, 110, 0.22)",
                "snow" => "rgba(230, 236, 245, 0.10)",
                "blizzard" => "rgba(235, 240, 250, 0.28)",
                "heatwave" => "rgba(255, 170, 80, 0.12)",
                _ => "rgba(0, 0, 0, 0)",
            };
            ctx.fillRect(0, 0, vp.width, vp.height);
        }

        // Night falls over the world, but not over survivors, previews, or labels, so play stays readable.
        double dark = Time.darkness(state.tick);
        if (dark > 0)
        {
            ctx.fillStyle = COLOURS.night;
            ctx.globalAlpha = dark * NIGHT_MAX_ALPHA;
            ctx.fillRect(0, 0, vp.width, vp.height);
            ctx.globalAlpha = 1;
        }

        if (options.areaDrag is { } box && options.tool != null) drawAreaBox(box.from, box.to, COLOURS.tools[options.tool]);
        else if (options.hover != null) drawHover(options.hover, options.tool, grid.width, grid.height);

        foreach (var s in state.survivors)
        {
            if (options.showDebug)
            {
                drawPath(s, options.alpha);
                drawJobLink(state, s, options.alpha);
            }
            if (s.id == options.selectedId && s.goal != null) drawGoal(s.goal, size);
        }
        foreach (var t in state.tasks) drawTask(t, size);
        foreach (var a in state.exploreAreas) drawAreaBox(new Point(a.x0, a.y0), new Point(a.x1, a.y1), COLOURS.tools["explore"]);
        // Line of sight (Sight.cs): what's explored but out of view now is dimmed, and the people,
        // the dead and animals in it aren't drawn (the debug overlay shows them all).
        var seen = Sight.visibleNow(state);
        bool inView(int x, int y) => shown(x, y) && (options.showDebug || seen[y * grid.width + x] == 1);
        ctx.fillStyle = COLOURS.unseen;
        for (int y = range.y0; y <= range.y1; y++)
            for (int x = range.x0; x <= range.x1; x++)
            {
                int i = y * grid.width + x;
                if (Grids.exploredAt(grid, i) != 1 || seen[i] == 1) continue;
                var r = tileRect(x, y);
                ctx.fillRect(r.x, r.y, r.w, r.h);
            }
        // Caravans (Caravans.cs): a cart and its people, where you can see them.
        foreach (var c in Caravans.caravansOf(state))
        {
            if (!inView(c.x, c.y)) continue;
            var p = ToScreen(c.x + 0.5, c.y + 0.5);
            ctx.fillStyle = "#8a6a3a";
            ctx.fillRect(p.x - size * 0.45, p.y - size * 0.25, size * 0.9, size * 0.5);
            ctx.strokeStyle = "#2a2018";
            ctx.lineWidth = 1;
            ctx.strokeRect(p.x - size * 0.45, p.y - size * 0.25, size * 0.9, size * 0.5);
            ctx.fillStyle = "#e0c9a0";
            for (int k = 0; k < c.people; k++)
            {
                ctx.beginPath();
                ctx.arc(p.x - size * 0.4 + k * size * 0.4, p.y + size * 0.5, Math.Max(2, size * 0.14), 0, Math.PI * 2);
                ctx.fill();
            }
        }
        // Neighbours' scavengers (Runs.cs): a few people on foot, packs on.
        foreach (var r in Runs.runsOf(state))
        {
            if (!inView(r.x, r.y)) continue;
            var p = ToScreen(r.x + 0.5, r.y + 0.5);
            for (int k = 0; k < r.people; k++)
            {
                double x = p.x - size * 0.3 + k * size * 0.3;
                ctx.fillStyle = "#e0c9a0";
                ctx.beginPath();
                ctx.arc(x, p.y, Math.Max(2, size * 0.14), 0, Math.PI * 2);
                ctx.fill();
                ctx.fillStyle = "#6a5a3a";
                ctx.fillRect(x - size * 0.08, p.y + size * 0.1, size * 0.16, size * 0.16);
            }
        }
        foreach (var h in state.herds)
        {
            bool onScreen = h.x >= range.x0 && h.x <= range.x1 && h.y >= range.y0 && h.y <= range.y1;
            if (onScreen && (h.heard || shown(h.x, h.y))) drawHerd(h, size);
        }
        foreach (var w in state.wanderers) if (inView(w.x, w.y)) drawWanderer(w.x, w.y, w.name, size);
        // Neighbours' people in their compounds: a wanderer's look, marked with their settlement's first letter.
        foreach (var r in state.residents)
        {
            if (!inView(r.pos.x, r.pos.y)) continue;
            var n = state.settlements.FirstOrDefault((x) => x.id == r.settlementId);
            var at = worldPos(r, options.alpha);
            drawWanderer(at.x - 0.5, at.y - 0.5, r.name, size, n != null ? n.name[..1] : "?", n != null && Settlements.isHostile(n));
        }
        foreach (var t in state.traders) if (inView(t.x, t.y)) drawWanderer(t.x, t.y, "Trader", size, "$");
        foreach (var z in state.zombies) if (inView(z.pos.x, z.pos.y)) drawZombie(state, z, options, size);
        foreach (var a in state.animals)
        {
            if (!inView(a.pos.x, a.pos.y)) continue;
            var w = worldPos(a, options.alpha);
            drawAnimal(a, ToScreen(w.x, w.y), size);
        }
        foreach (var q in state.prisoners)
        {
            if (!shown(q.x, q.y)) continue;
            var p = ToScreen(q.x + 0.5, q.y + 0.5);
            ctx.fillStyle = "#e0a080";
            ctx.strokeStyle = "#3a2a20";
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.arc(p.x, p.y, Math.Max(3, size * 0.3), 0, Math.PI * 2);
            ctx.fill();
            ctx.stroke();
        }
        foreach (var d in state.dogs) if (shown(d.pos.x, d.pos.y)) drawDog(d, options.alpha, size);
        foreach (var s in state.survivors) drawSurvivor(state, s, options, size);
        // Cars (Vehicles.cs), over whoever is in them: a body turned to its heading, a windscreen at the front.
        foreach (var v in Vehicles.vehiclesOf(state))
        {
            if (!shown(v.pos.x, v.pos.y)) continue;
            var w = worldPos(v.at, v.was, options.alpha);
            var p = ToScreen(w.x, w.y);
            ctx.save();
            ctx.translate(p.x, p.y);
            ctx.rotate(v.heading / Vehicles.HEADINGS * Math.PI * 2);
            bool truck = v.kind == "truck";
            // The size of its body in the sim (VehicleBody.cs), which is the size of the model first person draws.
            var shape = Vehicles.shapeOf(v);
            double len = size * 2 * shape.halfLength / Motion.SUB;
            double wid = size * 2 * shape.halfWidth / Motion.SUB;
            ctx.fillStyle = v.health <= 0 ? "#4a4440" : truck ? "#5a6338" : CAR_COLOURS[(int)(double.TryParse(v.id[1..], out var num) ? num : 0) % CAR_COLOURS.Length];
            ctx.fillRect(-len / 2, -wid / 2, len, wid);
            ctx.fillStyle = "rgba(170, 210, 235, 0.8)";
            ctx.fillRect(len * 0.12, -wid * 0.38, len * 0.16, wid * 0.76);
            ctx.fillStyle = "rgba(170, 210, 235, 0.5)";
            ctx.fillRect(-len * 0.38, -wid * 0.34, len * 0.12, wid * 0.68);
            ctx.strokeStyle = "#141614";
            ctx.lineWidth = 1;
            ctx.strokeRect(-len / 2, -wid / 2, len, wid);
            ctx.restore();
        }
        // The world winding down (TimelineMod.cs): alarms ringing (seen or not: you hear them), the helicopter overhead.
        if (state.timeline != null)
        {
            foreach (var a in state.timeline.alarms)
            {
                var p = ToScreen(a.x + 0.5, a.y + 0.5);
                double pulse = state.tick % 10 / 10.0;
                ctx.strokeStyle = $"rgba(255, 70, 50, {0.9 - pulse * 0.7:0.###})";
                ctx.lineWidth = 2;
                ctx.beginPath();
                ctx.arc(p.x, p.y, size * (0.4 + pulse * 1.2), 0, Math.PI * 2);
                ctx.stroke();
            }
            var heli = state.timeline.heli;
            if (heli != null)
            {
                var p = ToScreen(heli.x + 0.5, heli.y + 0.5);
                ctx.fillStyle = "rgba(0, 0, 0, 0.35)";
                ctx.beginPath();
                ctx.ellipse(p.x + size * 0.6, p.y + size * 0.8, size * 1.1, size * 0.5, 0, 0, Math.PI * 2);
                ctx.fill();
                ctx.fillStyle = "#3c4a3a";
                ctx.fillRect(p.x - size * 0.9, p.y - size * 0.35, size * 1.8, size * 0.7);
                ctx.fillRect(p.x + size * 0.8, p.y - size * 0.1, size * 1.2, size * 0.2);
                ctx.strokeStyle = "rgba(220, 220, 220, 0.6)";
                ctx.lineWidth = 2;
                double a = state.tick * 0.9;
                ctx.beginPath();
                ctx.moveTo(p.x - Math.Cos(a) * size * 1.6, p.y - Math.Sin(a) * size * 1.6);
                ctx.lineTo(p.x + Math.Cos(a) * size * 1.6, p.y + Math.Sin(a) * size * 1.6);
                ctx.stroke();
            }
        }
        drawRoomLabels(state, size);
    }

    void drawTile(int x, int y, string tile, int ground, double size, bool locked, bool open)
    {
        var r = tileRect(x, y);
        ctx.fillStyle = tile == "floor" || tile == "door" ? COLOURS.ground[ground >= 0 && ground < Grids.GROUNDS.Length ? Grids.GROUNDS[ground] : "grass"] : COLOURS.tiles[tile];
        ctx.fillRect(r.x, r.y, r.w, r.h);
        if (tile == "barricade")
        {
            // Two crossed planks.
            ctx.strokeStyle = COLOURS.plankEdge;
            ctx.lineWidth = Math.Max(2, r.w * 0.22);
            ctx.beginPath();
            ctx.moveTo(r.x + r.w * 0.15, r.y + r.h * 0.15);
            ctx.lineTo(r.x + r.w * 0.85, r.y + r.h * 0.85);
            ctx.moveTo(r.x + r.w * 0.85, r.y + r.h * 0.15);
            ctx.lineTo(r.x + r.w * 0.15, r.y + r.h * 0.85);
            ctx.stroke();
            ctx.strokeStyle = COLOURS.plank;
            ctx.lineWidth = Math.Max(1, r.w * 0.14);
            ctx.stroke();
        }
        if (tile == "water" && size >= 8)
        {
            ctx.fillStyle = COLOURS.waterRipple;
            ctx.fillRect(r.x + r.w * ((x * 7 + y * 3) % 5) * 0.15, r.y + r.h * 0.45, r.w * 0.3, Math.Max(1, r.h * 0.08));
        }

        if (tile == "wall")
        {
            // A lighter top edge so wall runs read as solid blocks.
            ctx.fillStyle = COLOURS.wallTop;
            ctx.fillRect(r.x, r.y, r.w, Math.Max(1, Math.Round(size * 0.15)));
        }
        else if (tile == "door" && locked)
        {
            ctx.fillStyle = COLOURS.doorFrame;
            ctx.fillRect(r.x, r.y, r.w, r.h);
            ctx.fillStyle = "#c9a227";
            ctx.fillRect(r.x + r.w * 0.35, r.y + r.h * 0.4, r.w * 0.3, r.h * 0.3);
            ctx.strokeStyle = "#c9a227";
            ctx.lineWidth = Math.Max(1, r.w * 0.06);
            ctx.strokeRect(r.x + r.w * 0.4, r.y + r.h * 0.22, r.w * 0.2, r.h * 0.2);
        }
        else if (tile == "door" && open)
        {
            // Open: the frame's edges, and the leaf swung back along one side.
            double edge = Math.Max(1, Math.Round(size * 0.12));
            ctx.fillStyle = COLOURS.doorFrame;
            ctx.fillRect(r.x, r.y, edge, r.h);
            ctx.fillRect(r.x + r.w - edge, r.y, edge, r.h);
            ctx.fillStyle = COLOURS.tiles["door"];
            ctx.fillRect(r.x + edge, r.y, Math.Max(1, Math.Round(size * 0.18)), r.h);
        }
        else if (tile == "door")
        {
            double inset = Math.Round(size * 0.12);
            ctx.fillStyle = COLOURS.doorFrame;
            ctx.fillRect(r.x, r.y, r.w, r.h);
            ctx.fillStyle = COLOURS.tiles["door"];
            ctx.fillRect(r.x + inset, r.y + inset, r.w - inset * 2, r.h - inset * 2);
        }
    }

    void drawGridLines(TileRange range)
    {
        ctx.strokeStyle = COLOURS.gridLine;
        ctx.lineWidth = 1;
        ctx.beginPath();
        double top = ToScreen(0, range.y0).y;
        double bottom = ToScreen(0, range.y1 + 1).y;
        double left = ToScreen(range.x0, 0).x;
        double right = ToScreen(range.x1 + 1, 0).x;
        for (int x = range.x0; x <= range.x1 + 1; x++)
        {
            double sx = Math.Round(ToScreen(x, 0).x) + 0.5;
            ctx.moveTo(sx, top);
            ctx.lineTo(sx, bottom);
        }
        for (int y = range.y0; y <= range.y1 + 1; y++)
        {
            double sy = Math.Round(ToScreen(0, y).y) + 0.5;
            ctx.moveTo(left, sy);
            ctx.lineTo(right, sy);
        }
        ctx.stroke();
    }

    void drawMapEdge(int w, int h)
    {
        var a = ToScreen(0, 0);
        var b = ToScreen(w, h);
        ctx.strokeStyle = COLOURS.mapEdge;
        ctx.lineWidth = 2;
        ctx.strokeRect(Math.Round(a.x) - 1, Math.Round(a.y) - 1, Math.Round(b.x - a.x) + 2, Math.Round(b.y - a.y) + 2);
    }

    /// <summary>The box an area tool is dragging out, covering whole tiles.</summary>
    void drawAreaBox(Point from, Point to, string colour)
    {
        var a = tileRect(Math.Min(from.x, to.x), Math.Min(from.y, to.y));
        var b = tileRect(Math.Max(from.x, to.x), Math.Max(from.y, to.y));
        double x = a.x, y = a.y, w = b.x + b.w - a.x, h = b.y + b.h - a.y;
        ctx.globalAlpha = 0.15;
        ctx.fillStyle = colour;
        ctx.fillRect(x, y, w, h);
        ctx.globalAlpha = 1;
        ctx.strokeStyle = colour;
        ctx.lineWidth = 2;
        ctx.setLineDash(new double[] { 6, 4 });
        ctx.strokeRect(x + 1, y + 1, w - 2, h - 2);
        ctx.setLineDash(Array.Empty<double>());
    }

    void drawHover(Point hover, string? tool, int w, int h)
    {
        if (hover.x < 0 || hover.y < 0 || hover.x >= w || hover.y >= h) return;
        var r = tileRect(hover.x, hover.y);
        // Select mode gets a faint outline; an armed tool previews boldly in its colour.
        var colour = tool != null ? COLOURS.tools[tool] : COLOURS.selectHover;
        ctx.globalAlpha = tool != null ? 0.25 : 0.06;
        ctx.fillStyle = colour;
        ctx.fillRect(r.x, r.y, r.w, r.h);
        ctx.globalAlpha = tool != null ? 1 : 0.5;
        ctx.strokeStyle = colour;
        ctx.lineWidth = tool != null ? 2 : 1;
        ctx.strokeRect(r.x + 1, r.y + 1, r.w - 2, r.h - 2);
        ctx.globalAlpha = 1;
    }

    /// <summary>
    /// Edge walls and doors (Grid.cs): a bar along the border between two tiles, a fifth of a tile
    /// thick, centred on it. A door is a wooden leaf in a gap; open, it's swung back along the frame.
    /// </summary>
    void drawEdges(Grid grid, TileRange range, double size)
    {
        double t = Math.Max(2, size * 0.2);
        for (int y = range.y0; y <= range.y1 + 1; y++)
        {
            for (int x = range.x0; x <= range.x1 + 1; x++)
            {
                if (x < 0 || y < 0 || x >= grid.width || y >= grid.height) continue;
                bool seen = Grids.exploredAt(grid, y * grid.width + x) == 1 || (y > 0 && Grids.exploredAt(grid, (y - 1) * grid.width + x) == 1) || (x > 0 && Grids.exploredAt(grid, y * grid.width + x - 1) == 1);
                if (!seen) continue;
                for (int side = 0; side <= 1; side++)
                {
                    int e = (y * grid.width + x) * 2 + side;
                    int kind = Grids.edgeKind(grid, e);
                    if (kind == 0) continue;
                    var r = tileRect(x, y);
                    // North edge: along the top of the tile; west edge: down its left side.
                    double bx = r.x - t / 2, by = r.y - t / 2;
                    double bw = side == 0 ? r.w + t : t, bh = side == 0 ? t : r.h + t;
                    if (kind == 4 || kind == 5)
                    {
                        // A window: the frame's ends as wall; glass a pale pane between, broken a gap with shards;
                        // boarded, planks across it.
                        double end = r.w * 0.15;
                        ctx.fillStyle = COLOURS.tiles["wall"];
                        if (side == 0)
                        {
                            ctx.fillRect(bx, by, end + t / 2, t);
                            ctx.fillRect(bx + bw - end - t / 2, by, end + t / 2, t);
                        }
                        else
                        {
                            ctx.fillRect(bx, by, t, end + t / 2);
                            ctx.fillRect(bx, by + bh - end - t / 2, t, end + t / 2);
                        }
                        double gx = side == 0 ? bx + end + t / 2 : bx + t * 0.25;
                        double gy = side == 0 ? by + t * 0.25 : by + end + t / 2;
                        double gw = side == 0 ? bw - 2 * end - t : t * 0.5;
                        double gh = side == 0 ? t * 0.5 : bh - 2 * end - t;
                        if (kind == 4)
                        {
                            ctx.fillStyle = "rgba(170, 210, 235, 0.85)";
                            ctx.fillRect(gx, gy, gw, gh);
                        }
                        else
                        {
                            ctx.fillStyle = "rgba(170, 210, 235, 0.7)";
                            foreach (var f in new[] { 0.1, 0.45, 0.8 })
                            {
                                if (side == 0) ctx.fillRect(gx + gw * f, gy, Math.Max(1, gw * 0.08), gh);
                                else ctx.fillRect(gx, gy + gh * f, gw, Math.Max(1, gh * 0.08));
                            }
                        }
                        if (grid.edgeBoarded.Contains(e))
                        {
                            ctx.fillStyle = COLOURS.tiles["door"];
                            foreach (var f in new[] { 0.15, 0.55 })
                            {
                                if (side == 0) ctx.fillRect(gx + gw * f, by - t * 0.2, gw * 0.3, t * 1.4);
                                else ctx.fillRect(bx - t * 0.2, gy + gh * f, t * 1.4, gh * 0.3);
                            }
                        }
                        continue;
                    }
                    if (kind == 3)
                    {
                        // A barricade: planks across the border, gaps between.
                        ctx.fillStyle = COLOURS.tiles["door"];
                        const int n = 4;
                        for (int k = 0; k < n; k++)
                        {
                            double f = (k + 0.15) / n;
                            if (side == 0) ctx.fillRect(bx + bw * f, by, bw * 0.7 / n, bh);
                            else ctx.fillRect(bx, by + bh * f, bw, bh * 0.7 / n);
                        }
                        continue;
                    }
                    if (kind == Grids.EDGE_WALL)
                    {
                        ctx.fillStyle = COLOURS.tiles["wall"];
                        ctx.fillRect(bx, by, bw, bh);
                        ctx.fillStyle = COLOURS.wallTop;
                        ctx.fillRect(bx + 1, by + 1, Math.Max(1, bw - 2), Math.Max(1, bh - 2));
                        continue;
                    }
                    // A door: the frame's ends as wall, the leaf in between (shut across the gap, or swung open).
                    bool open = grid.edgeOpen.Contains(e);
                    double end2 = r.w * 0.15;
                    ctx.fillStyle = COLOURS.tiles["wall"];
                    if (side == 0)
                    {
                        ctx.fillRect(bx, by, end2 + t / 2, t);
                        ctx.fillRect(bx + bw - end2 - t / 2, by, end2 + t / 2, t);
                    }
                    else
                    {
                        ctx.fillRect(bx, by, t, end2 + t / 2);
                        ctx.fillRect(bx, by + bh - end2 - t / 2, t, end2 + t / 2);
                    }
                    ctx.fillStyle = COLOURS.tiles["door"];
                    if (!open)
                    {
                        if (side == 0) ctx.fillRect(bx + end2 + t / 2, by + t * 0.2, bw - 2 * end2 - t, t * 0.6);
                        else ctx.fillRect(bx + t * 0.2, by + end2 + t / 2, t * 0.6, bh - 2 * end2 - t);
                    }
                    else if (side == 0) ctx.fillRect(r.x + end2, r.y, t * 0.5, r.h * 0.6); // swung in, along the jamb
                    else ctx.fillRect(r.x, r.y + end2, r.w * 0.6, t * 0.5);
                }
            }
        }
    }

    /// <summary>An animal by kind: a deer with antlers, a small pale rabbit, a grey wolf, a big dark bear (red edge: marked to hunt).</summary>
    void drawAnimal(Animal a, P p, double size)
    {
        double scale = a.kind switch { "deer" => 0.24, "rabbit" => 0.12, "wolf" => 0.2, _ => 0.34 };
        string colour = a.kind switch { "deer" => "#b07a46", "rabbit" => "#cfc2a8", "wolf" => "#8a8f96", _ => "#4a3426" };
        double r = Math.Max(a.kind == "rabbit" ? 1.5 : 2, size * scale);
        ctx.fillStyle = colour;
        ctx.fillRect(p.x - r * 1.2, p.y - r * 0.5, r * 2.2, r);
        if (a.kind == "deer")
        {
            ctx.strokeStyle = "#e8d0a0";
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(p.x + r * 0.8, p.y - r * 0.5);
            ctx.lineTo(p.x + r * 1.1, p.y - r * 1.3);
            ctx.stroke();
        }
        else if (a.kind == "rabbit") ctx.fillRect(p.x + r * 0.6, p.y - r * 1.4, r * 0.3, r);
        else
        {
            // A head, with ears for wolves.
            ctx.fillRect(p.x + r * 0.8, p.y - r * 0.9, r * 0.8, r * 0.8);
            if (a.kind == "wolf") ctx.fillRect(p.x + r * 0.9, p.y - r * 1.4, r * 0.25, r * 0.5);
        }
        if (a.hunted)
        {
            ctx.strokeStyle = "#ff5a4a";
            ctx.lineWidth = 1;
            ctx.strokeRect(p.x - r * 1.3, p.y - r * 0.6, r * 2.4, r * 1.2);
        }
    }
}
