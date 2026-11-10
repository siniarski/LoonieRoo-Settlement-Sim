// The game's text outputs (see the command line in Game.cs and godot/README.md "Which to use when"): what an agent
// reads instead of a picture. Read only: they look at the sim state after each tick and print.
//   --trace[=K]       one line per tick (every Kth) for the survivor you control: tick, where (tiles, two decimals),
//                     tile, facing, what they're doing, health. --trace-near=R adds the movers within R tiles
//   --textmap=R       DebugText's map of the 2R+1 tiles square round you at the end of the run
//   --textmap-at=T,.. ... and when the tick is T
// With --ticks=N the N ticks run tick by tick here in _Ready (headless works: no window, no frames), so every
// tick is seen; with --shot-at the ticks between the pictures are seen too. Everything goes to Godot's stdout.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    /// <summary>--trace[=K]: print every Kth tick (0: off). --trace-near=R: and the movers within R tiles (negative: not).</summary>
    int traceEvery;
    double traceNear = -1;
    /// <summary>--textmap=R: the square of R tiles round you (null: off); and --textmap-at the ticks to print it at, besides the end.</summary>
    int? textMapR;
    readonly HashSet<int> textMapAt = new();
    /// <summary>The tick the last trace line and the last map were printed at (-1: none yet).</summary>
    int tracedTick = -1, mappedTick = -1, traceStart;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const char NL = '\n';

    void ParseTextOptions(Dictionary<string, string> args)
    {
        if (args.TryGetValue("trace", out var tr)) traceEvery = tr == "" ? 1 : Math.Max(1, int.Parse(tr, Inv));
        if (args.TryGetValue("trace-near", out var tn)) traceNear = double.Parse(tn, Inv);
        if (args.TryGetValue("textmap", out var tm)) textMapR = tm == "" ? 8 : int.Parse(tm, Inv);
        if (args.TryGetValue("textmap-at", out var ta))
            foreach (var p in ta.Split(',', StringSplitOptions.RemoveEmptyEntries)) textMapAt.Add(int.Parse(p, Inv));
        if (textMapAt.Count > 0) textMapR ??= 8;
    }

    bool TextRun => traceEvery > 0 || textMapR != null;

    /// <summary>Where first person looks at the start (and so which way a held key walks): --look, else the way you face.</summary>
    void InitYaw()
    {
        var c = state.control;
        if (c == null) return;
        var d = c.aim ?? c.facing;
        view.yaw = lookArg ?? Math.Atan2(d.y, d.x);
    }

    /// <summary>The ticks to run before showing the game, one at a time so each is seen (--ticks with --trace or --textmap).</summary>
    void TextRunTicks(int n)
    {
        InitYaw();
        traceStart = state.tick;
        TextTick(true);
        for (int i = 0; i < n; i++) StepOne();
        if (tracedTick != state.tick && traceEvery > 0) TraceLine(); // the last tick, whatever K is
    }

    /// <summary>One tick, then what the text options print for it. The held keys (--hold) are applied first, as the frame would.</summary>
    void StepOne()
    {
        HeldInput();
        Tick.step(state, 1);
        TextTick(false);
    }

    /// <summary>--hold's keys as commands (the sync run has no frames to do it): steer, and jump again on each landing.</summary>
    void HeldInput()
    {
        var c = state.control;
        var you = ControlMod.controlled(state);
        if (holdKeys.Length == 0 || c == null || you == null) return;
        int dx = 0, dy = 0;
        bool shift = false;
        foreach (var k in holdKeys)
        {
            if (k == "w") dy -= 1; else if (k == "s") dy += 1; else if (k == "a") dx -= 1; else if (k == "d") dx += 1; else if (k == "shift") shift = true;
        }
        if (holdKeys.Contains("space") && !Leaps.airborne(c) && Vehicles.vehicleOf(state, you) == null) Command(new Command { type = "jump" });
        FirstPersonInput(c, dx, -dy, shift);
    }

    void TextTick(bool first)
    {
        if (traceEvery > 0 && (state.tick - traceStart) % traceEvery == 0) TraceLine();
        if (textMapAt.Contains(state.tick) && textMapR is { } r) PrintTextMap(r);
    }

    /// <summary>The end of the run: the final map, once.</summary>
    void TextFinish()
    {
        if (textMapR is { } r && mappedTick != state.tick) PrintTextMap(r);
    }

    static string F(double v) => v.ToString("0.00", Inv);

    /// <summary>t=12 @64.93,64.50 tile 64,64 face 0 stand v1.4 hp 100 [run] [steer 64,0] [act swing .35]</summary>
    void TraceLine()
    {
        tracedTick = state.tick;
        var you = ControlMod.controlled(state);
        var c = state.control;
        if (you == null || c == null) { GD.Print($"t={state.tick} (nobody under your control)"); return; }
        var pose = PoseIntents.ofSurvivor(state, you);
        var d = c.aim ?? c.facing;
        double face = Math.Round(Math.Atan2(d.y, d.x) * 180 / Math.PI);
        if (face < 0) face += 360;
        var sb = new StringBuilder();
        sb.Append($"t={state.tick} @{F((double)you.at.x / Motion.SUB)},{F((double)you.at.y / Motion.SUB)} tile {you.pos.x},{you.pos.y} face {face:0} {pose.stance} v{pose.speed.ToString("0.0", Inv)} hp {you.health:0.#}");
        if (c.running == true) sb.Append(" run");
        if (Leaps.airborne(c)) sb.Append(" airborne");
        if (c.dx != 0 || c.dy != 0) sb.Append($" steer {c.dx:0},{c.dy:0}");
        if (pose.action != null) sb.Append($" act {pose.action} {pose.progress.ToString("0.00", Inv)}");
        GD.Print(sb.ToString());
        if (traceNear >= 0) PrintNear(you, traceNear);
    }

    /// <summary>The movers within r tiles of you, nearest first, one short line each: "  z z12 @66.10,64.40 d1.2 hp 40 ->you".</summary>
    void PrintNear(Survivor you, double r)
    {
        var found = new List<(double d, string line)>();
        void Add(string kind, string id, Point at, string rest)
        {
            double dx = (double)(at.x - you.at.x) / Motion.SUB, dy = (double)(at.y - you.at.y) / Motion.SUB;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= r) found.Add((d, $"  {kind} {id} @{F((double)at.x / Motion.SUB)},{F((double)at.y / Motion.SUB)} d{d.ToString("0.0", Inv)}{rest}"));
        }
        foreach (var z in state.zombies) Add(z.variant == "crawler" ? "z-crawl" : "z", z.id, z.at, $" hp {z.health:0.#}{(z.targetId == you.id ? " ->you" : z.targetId != null ? " ->" + z.targetId : "")}{(z.staggerUntil > state.tick ? " stagger" : "")}");
        foreach (var a in state.animals) Add(a.kind, a.id, a.at, a.health != null ? $" hp {a.health:0.#}{(a.targetId == you.id ? " ->you" : "")}" : "");
        foreach (var g in state.dogs) Add("dog", g.id, g.at, $" hp {g.health:0.#}");
        foreach (var s in state.survivors) if (s.id != you.id) Add("s", s.id, s.at, $" hp {s.health:0.#}");
        foreach (var rs in state.residents) Add("res", rs.id, rs.at, "");
        found.Sort((a, b) => a.d.CompareTo(b.d));
        const int CAP = 12;
        foreach (var f in found.Take(CAP)) GD.Print(f.line);
        if (found.Count > CAP) GD.Print($"  ... {found.Count - CAP} more within {r:0.#}");
    }

    /// <summary>DebugText's picture of the square of R tiles round you (clamped to the world), with you as @.</summary>
    void PrintTextMap(int r)
    {
        mappedTick = state.tick;
        var you = ControlMod.controlled(state);
        var at = you?.pos ?? new Point { x = state.grid.width / 2, y = state.grid.height / 2 };
        int w = state.grid.width, h = state.grid.height;
        int x0 = Math.Max(0, at.x - r), x1 = Math.Min(w - 1, at.x + r), y0 = Math.Max(0, at.y - r), y1 = Math.Min(h - 1, at.y + r);
        var text = DebugText.dumpText(state).Split('\n');
        // The first h lines are the tiles; after "edges:" the same map with the borders between tiles (2h+1 lines, tile x,y at 2y+1, 2x+1).
        int edgesAt = Array.IndexOf(text, "edges:");
        string Cell(string[] lines, int row, int col) => row < lines.Length && col < lines[row].Length ? lines[row][col].ToString() : " ";
        var sb = new StringBuilder();
        // The tiles, and (when the world has edge walls) the borders between them: used only if some stand in this square.
        var tiles = new StringBuilder();
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++) tiles.Append(Overlay(Cell(text, y, x), x, y, at, you != null));
            tiles.Append(NL);
        }
        var borders = new StringBuilder();
        bool anyEdge = false;
        if (edgesAt >= 0)
        {
            var e = text.Skip(edgesAt + 1).ToArray();
            for (int row = 2 * y0; row <= 2 * y1 + 2; row++)
            {
                for (int col = 2 * x0; col <= 2 * x1 + 2; col++)
                {
                    var ch = Cell(e, row, col);
                    if (ch == "+") anyEdge = true;
                    if (row % 2 == 1 && col % 2 == 1) ch = Overlay(ch, (col - 1) / 2, (row - 1) / 2, at, you != null);
                    borders.Append(ch);
                }
                borders.Append(NL);
            }
        }
        sb.Append($"map t={state.tick} you {at.x},{at.y} x {x0}..{x1} y {y0}..{y1}");
        sb.Append(anyEdge ? " (tiles at the odd columns and rows, the borders between them: - | wall, = door, o open door, % window, # boarded, : barricade, + corner)" : "");
        sb.Append(NL);
        sb.Append(anyEdge ? borders : tiles);
        sb.Append("@ you, s survivor, z zombie, u animal, g dog, Y tree, . ground, % water (a window, in the borders)");
        GD.Print(sb.ToString());
    }

    static string Overlay(string ch, int x, int y, Point you, bool haveYou) => haveYou && x == you.x && y == you.y ? "@" : ch;
}
