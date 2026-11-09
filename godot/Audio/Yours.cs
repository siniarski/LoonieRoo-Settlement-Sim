// Your own sounds (2026-11-09): footsteps and the swing, heard as yours (plain players, not 3D).
//   steps  one each time a foot of your own body comes down in its clip (People's footfalls: Footfalls.cs), so
//          they land with the walk, the run and the sneak you see, at any rate and backing off; on the ground
//          under that foot (Surface: each ground its own, shallow water by the bank); quiet sneaking, louder
//          running. None in a car or asleep (your body isn't walking then).
//   swing  control.swungTick moved: a swing through the air (a blow landing is the hit's own thud).
// Your gunshots are WorldNoises' (a gun's wear going down), from where you stand.

using System;
using Godot;
using System.Collections.Generic;
using LoonieRoo.Sim;
using LoonieRoo.Game.World3D;

namespace LoonieRoo.Game.Audio;

public sealed class Yours
{
    const float SWING_DB = -7;
    /// <summary>A foot this close (m) to a water tile is in the shallows at the bank.</summary>
    const double SHALLOWS = 0.3;

    /// <summary>A step's level (dB) and pitch by gait; others' steps (Sounds) start from the same levels.</summary>
    public static (float db, float pitch) Gait(string gait) => gait switch
    {
        "crouch" => (-22, 0.97f),
        "jog" => (-7, 1.03f),
        "sprint" => (-4, 1.06f),
        _ => (-11, 1),
    };

    readonly Sounds sounds;
    readonly AudioStreamPlayer[] feet = { new(), new() };
    readonly AudioStreamPlayer swing = new();
    int foot, steps;
    double? lastSwung;

    /// <summary>Steps and swings heard (the self-test reads them), and the surface of the last step.</summary>
    public int Steps => steps;
    public int Swings { get; private set; }
    public string LastSurface { get; private set; } = "";
    public string LastGait { get; private set; } = "";

    public Yours(Sounds sounds)
    {
        this.sounds = sounds;
        foreach (var f in feet) sounds.AddChild(f);
        sounds.AddChild(swing);
    }

    /// <summary>The kind of step (step_grass ... step_water) for a foot at (x, y) in tile units: the ground of the
    /// tile under it, or shallow water if that's water or a water tile is within SHALLOWS of the foot (a bridge is a bridge).</summary>
    public static string Surface(Grid grid, double x, double y)
    {
        int tx = (int)Math.Floor(x), ty = (int)Math.Floor(y);
        if (!Grids.inBounds(grid, tx, ty)) return "step_grass";
        int i = ty * grid.width + tx;
        var g = Grids.GROUNDS[Math.Clamp(grid.ground[i], 0, Grids.GROUNDS.Length - 1)];
        if (g == "bridge") return "step_bridge";
        if (grid.tiles[i] == Grids.WATER) return "step_water";
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = (int)Math.Floor(x + dx * SHALLOWS), ny = (int)Math.Floor(y + dy * SHALLOWS);
                if ((nx != tx || ny != ty) && Grids.getTile(grid, nx, ny) == Grids.WATER) return "step_water";
            }
        return "step_" + g;
    }

    public void Update(SimState state, Survivor you, bool fresh, IReadOnlyList<Footfall> footfalls)
    {
        var c = state.control;
        foreach (var f in footfalls)
        {
            if (!f.you || fresh) continue;
            var kind = Surface(state.grid, f.at.X, f.at.Z);
            var set = sounds.bank.Get(kind);
            var p = feet[foot];
            foot ^= 1;
            var (db, pitch) = Gait(f.gait);
            p.Stream = set[(int)(Hash.hashRoll(steps, 0x57e9) * set.Length) % set.Length];
            p.PitchScale = pitch * (0.94f + 0.12f * (float)Hash.hashRoll(steps, 0x57ea));
            p.VolumeDb = db;
            p.Play();
            steps++;
            LastSurface = kind;
            LastGait = f.gait;
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
