// Your own sounds (2026-11-09): footsteps and the swing, heard as yours (plain players, not 3D).
//   steps  one every STRIDE m you've moved (shorter sneaking, longer running), on what's underfoot:
//          soft (grass, dirt, field, forest, water's edge), hard (road, paving, concrete, tiles, a bridge),
//          boards; quiet sneaking, louder running. None in a car or asleep.
//   swing  control.swungTick moved: a swing through the air (a blow landing is the hit's own thud).
// Your gunshots are WorldNoises' (a gun's wear going down), from where you stand.

using System;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Audio;

public sealed class Yours
{
    const double STRIDE = 0.75, STRIDE_RUN = 1.05, STRIDE_SNEAK = 0.55;
    const float STEP_DB = -11, STEP_RUN_DB = -6, STEP_SNEAK_DB = -22, SWING_DB = -7;
    /// <summary>A move of more than this (m) in one look is a jump (a teleport, getting out of a car), not steps.</summary>
    const double JUMP = 2;

    readonly Sounds sounds;
    readonly AudioStreamPlayer[] feet = { new(), new() };
    readonly AudioStreamPlayer swing = new();
    double walked, lastX = double.NaN, lastY;
    int foot, steps;
    double? lastSwung;

    /// <summary>Steps and swings heard (the self-test reads them), and the surface of the last step.</summary>
    public int Steps => steps;
    public int Swings { get; private set; }
    public string LastSurface { get; private set; } = "";

    public Yours(Sounds sounds)
    {
        this.sounds = sounds;
        foreach (var f in feet) sounds.AddChild(f);
        sounds.AddChild(swing);
    }

    /// <summary>The kind of step for the ground of tile (x, y).</summary>
    public static string Surface(Grid grid, int x, int y)
    {
        if (!Grids.inBounds(grid, x, y)) return "step_soft";
        int i = y * grid.width + x;
        var g = Grids.GROUNDS[Math.Clamp(grid.ground[i], 0, Grids.GROUNDS.Length - 1)];
        return g switch
        {
            "road" or "paving" or "concrete" or "tiles" or "bridge" => "step_hard",
            "boards" => "step_wood",
            _ => "step_soft",
        };
    }

    public void Update(SimState state, Survivor you, bool fresh)
    {
        var c = state.control;
        double x = you.at.x / (double)Motion.SUB, y = you.at.y / (double)Motion.SUB;
        bool still = Vehicles.vehicleOf(state, you) != null || you.sleep?.asleep == true;
        double d = double.IsNaN(lastX) ? 0 : Math.Sqrt((x - lastX) * (x - lastX) + (y - lastY) * (y - lastY));
        lastX = x; lastY = y;
        if (fresh || still || d > JUMP) { walked = 0; d = 0; }
        walked += d;
        bool sneaking = c?.sneaking == true, running = c?.running == true && !sneaking;
        double stride = sneaking ? STRIDE_SNEAK : running ? STRIDE_RUN : STRIDE;
        if (walked >= stride)
        {
            walked -= stride;
            if (walked > stride) walked = 0;
            var kind = Surface(state.grid, you.pos.x, you.pos.y);
            var set = sounds.bank.Get(kind);
            var p = feet[foot];
            foot ^= 1;
            p.Stream = set[(int)(Hash.hashRoll(steps, 0x57e9) * set.Length) % set.Length];
            p.PitchScale = (running ? 1.05f : 1) * (0.94f + 0.12f * (float)Hash.hashRoll(steps, 0x57ea));
            p.VolumeDb = sneaking ? STEP_SNEAK_DB : running ? STEP_RUN_DB : STEP_DB;
            p.Play();
            steps++;
            LastSurface = kind;
        }

        var swung = c?.swungTick;
        if (!fresh && swung != null && swung != lastSwung)
        {
            var set = sounds.bank.Get("swing");
            swing.Stream = set[(int)(swung.Value % set.Length)];
            swing.PitchScale = 0.92f + 0.16f * (float)Hash.hashRoll(swung.Value, 0x5a19);
            swing.VolumeDb = SWING_DB;
            swing.Play();
            Swings++;
        }
        lastSwung = swung;
    }
}
