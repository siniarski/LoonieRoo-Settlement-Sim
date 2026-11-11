// Your own sounds (2026-11-09): footsteps and the swing, heard as yours (plain players, not 3D).
//   steps  one each time a foot of your own body comes down in its clip (People's footfalls: Footfalls.cs), so
//          they land with the walk, the run and the sneak you see, at any rate and backing off; on the ground
//          under that foot (Surface: each ground its own, shallow water by the bank); quiet sneaking, louder
//          running. None in a car or asleep (your body isn't walking then).
//   swing  control.swungTick moved: a swing through the air (a blow landing is the hit's own thud).
//   leaps  a jump, vault or hop (Leaps.cs): the push-off as you go up, a light scuff of the ground you leave; the
//          landing as the leap ends, both feet on the ground you land on, heavier and lower than a step (the second
//          foot a moment after the first). The steps' own sounds for that surface, pitched and levelled.
//   water  a stroke (Swim.cs) is one splash as it starts, louder and lower than a step; going out of your depth,
//          or jumping into water, one more.
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
    double? lastGunWear;
    readonly AudioStreamPlayer push = new(), land = new(), land2 = new();
    ControlBusy? leapBusy;
    ulong land2At;
    /// <summary>The push-off and the landing: level (dB) and pitch; the landing's second foot this much later (ms) and quieter.</summary>
    const float PUSH_DB = -10, PUSH_PITCH = 1.12f, LAND_DB = -3, LAND_PITCH = 0.8f, LAND2_DB = -8;
    const ulong LAND2_MS = 45;

    /// <summary>Leaps heard (the self-test reads them): push-offs and landings, and the landing's surface.</summary>
    public int Pushes { get; private set; }
    public int Landings { get; private set; }
    /// <summary>Splashes heard: strokes, and going into deep water.</summary>
    public int Splashes { get; private set; }
    int lastDepth;
    const float SPLASH_DB = -4, SPLASH_PITCH = 0.78f;
    public string LandSurface { get; private set; } = "";

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
        foreach (var p in new[] { push, land, land2 }) sounds.AddChild(p);
    }

    /// <summary>The kind of step (step_grass ... step_water) for a foot at (x, y) in tile units: the ground of the
    /// tile under it, or shallow water if that's water or a water tile is within SHALLOWS of the foot (a bridge is a bridge).</summary>
    public static string Surface(Grid grid, double x, double y)
    {
        int tx = (int)Math.Floor(x), ty = (int)Math.Floor(y);
        if (!Grids.inBounds(grid, tx, ty)) return "step_grass";
        int i = ty * grid.width + tx;
        var g = Grids.GROUNDS[Math.Clamp(Grids.groundAt(grid, i), 0, Grids.GROUNDS.Length - 1)];
        if (g == "bridge") return "step_bridge";
        if (Grids.tileAt(grid, i) == Grids.WATER) return "step_water";
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

        Leap(state, you, c, fresh);

        var swung = c?.swungTick;
        // A gun's report is the world's gunshot (WorldNoises.cs); only blows, and a crossbow's twang, whoosh.
        bool report = Weapons.specOf(Weapons.wielded(state, you)) is { isFirearm: true, cls: not "bow" } && Weapons.heldGun(state, you)?.wear < (lastGunWear ?? double.MaxValue);
        lastGunWear = Weapons.heldGun(state, you)?.wear;
        if (!fresh && swung != null && swung != lastSwung && !report)
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

    /// <summary>A leap's sounds: the landing when the one we heard go up ends (a new one may start the same frame, with
    /// Space held), the push-off when a new one starts.</summary>
    void Leap(SimState state, Survivor you, LoonieRoo.Sim.Control? c, bool fresh)
    {
        var b = c?.busy;
        bool leaping = b != null && Leaps.isLeap(b.what);
        var at = (x: you.at.x / (double)Motion.SUB, y: you.at.y / (double)Motion.SUB);
        int depth = Swim.depth(state.grid, you.pos);
        if (!fresh && depth > lastDepth && (depth == Swim.DEEP || leapBusy != null)) { Play(push, "step_water", Splashes, SPLASH_DB, SPLASH_PITCH); Splashes++; }
        lastDepth = depth;
        if (b?.what == "stroke" || leapBusy?.what == "stroke")
        {
            if (!fresh && b?.what == "stroke" && !ReferenceEquals(b, leapBusy)) { Play(push, "step_water", Splashes, SPLASH_DB, SPLASH_PITCH); Splashes++; }
            leapBusy = leaping ? b : null;
            return;
        }
        if (!fresh && leapBusy != null && !ReferenceEquals(b, leapBusy))
        {
            LandSurface = Surface(state.grid, at.x, at.y);
            Play(land, LandSurface, Landings * 2, LAND_DB, LAND_PITCH);
            land2At = Godot.Time.GetTicksMsec() + LAND2_MS;
            Landings++;
        }
        if (!fresh && leaping && !ReferenceEquals(b, leapBusy))
        {
            Play(push, Surface(state.grid, at.x, at.y), Pushes, PUSH_DB, PUSH_PITCH);
            Pushes++;
        }
        if (land2At != 0 && Godot.Time.GetTicksMsec() >= land2At)
        {
            Play(land2, LandSurface, Landings * 2 + 1, LAND2_DB, LAND_PITCH * 1.06f);
            land2At = 0;
        }
        leapBusy = leaping ? b : null;
    }

    void Play(AudioStreamPlayer p, string kind, int n, float db, float pitch)
    {
        var set = sounds.bank.Get(kind);
        p.Stream = set[(int)(Hash.hashRoll(n, 0x1ea9) * set.Length) % set.Length];
        p.PitchScale = pitch * (0.95f + 0.1f * (float)Hash.hashRoll(n, 0x1eaa));
        p.VolumeDb = db;
        p.Play();
    }
}
