// Sound in first person (2026-11-09): each sound from where it happens, heard through the camera,
// muffled by the walls and doors between (Muffle). Reads state only (rule 5), by comparing what it
// sees now with what it saw last frame; nothing is added to the sim and nothing in state holds a delegate.
// The dead:
//   notice  a zombie's targetId turns to you: it has seen you, and snarls
//   idle    the near dead groan now and then (more often while they hunt someone): a stateless
//           hashRoll of the zombie's id and the second, so the same moment moans the same way
//   hit     a zombie's hurtTick moves: a blow landed on it
//   kill    a zombie is gone and someone's kills went up: the killing blow, where it stood
//   scream  a screamer's `screamed` turns true
// The world (WorldNoises below): gunshots (a gun's wear going down, a turret's rounds), glass (a
// window edge turning broken), the far-off shots and screams and lightning (asked of the sim's pure
// reads, TimelineMod.farNoiseAt and Seasons.lightningAt, for each tick run). Loops.cs: alarms,
// engines, generators, the helicopter. Ambience.cs: wind, rain, the night. Yours.cs: your steps and swing.
// A pool of VOICES players for the one-shots; each follows the body it speaks for until it's done.
// The sounds are Bank's (made off the main thread at startup; nothing plays until they're ready).

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;
using LoonieRoo.Game.Render;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.Audio;

public sealed partial class Sounds : Node3D
{
    /// <summary>Players at once; a sound with none free (and none less important playing) isn't heard.</summary>
    const int VOICES = 12;
    /// <summary>The dead further than this (m) aren't heard at all.</summary>
    const float RANGE = 40;
    /// <summary>Idle groans: one roll per zombie per second within this range (m), at these chances.</summary>
    const float IDLE_RANGE = 24;
    const double IDLE_CHANCE = 0.04, HUNT_CHANCE = 0.12;
    /// <summary>A jump of more ticks than this between looks (a load, time away on the map) is taken as a new start, not as events.</summary>
    public const int GAP = 30;
    /// <summary>The height (m) a zombie's voice comes from, over the ground under it.</summary>
    const float MOUTH = 1.55f, CHEST = 1.2f;
    /// <summary>A playing voice looks again at what's between it and you this often (frames).</summary>
    const int MUFFLE_EVERY = 8;

    /// <summary>How a kind is heard: full loudness within `unit` m (then inverse distance), nothing past `range` m,
    /// its level (dB), and how much it matters when voices run short.</summary>
    public readonly record struct Spec(float unit, float range, float db, int priority);

    public static readonly Dictionary<string, Spec> SPECS = new()
    {
        ["moan_idle"] = new(4, RANGE, -4, 1),
        ["moan_notice"] = new(4, RANGE, 0, 2),
        ["hit"] = new(4, RANGE, 0, 3),
        ["kill"] = new(4, RANGE, 0, 3),
        ["scream"] = new(14, 120, 0, 3),
        ["gunshot"] = new(25, 250, 0, 4),
        ["glass"] = new(6, 70, -2, 3),
    };

    public readonly Bank bank = new();
    readonly Voice[] voices = new Voice[VOICES];
    readonly Dictionary<string, (string? target, double hurt, bool screamed, P at)> seen = new();
    readonly Dictionary<string, double> kills = new();
    readonly WorldNoises noises;
    public readonly Loops loops;
    public readonly Ambience ambience;
    public readonly Yours yours;
    SimState? last;
    int lastTick = -1;
    int frame;
    ulong updatedFrame;

    /// <summary>How many of each sound have played (the self-test reads it).</summary>
    public readonly Dictionary<string, int> Played = new();
    /// <summary>Where the last sound was played from (m), what it was, and how much stood in the way, for the self-test.</summary>
    public Vector3 LastAt { get; private set; }
    public string LastKind { get; private set; } = "";
    public float LastMuffle { get; private set; }

    sealed class Voice
    {
        public readonly AudioStreamPlayer3D player = new()
        {
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            UnitSize = 4,
            MaxDistance = RANGE,
            MaxDb = 3,
            AttenuationFilterCutoffHz = 6000,
            AttenuationFilterDb = -18,
            DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
        };
        public string? follows;
        public float height, db;
        public int priority;
        public P at;
    }

    public Sounds()
    {
        _ = Volume.Level; // sets the master bus from the settings file
        Muffle.Buses();
        foreach (var key in new[] { "notice", "idle", "hit", "kill", "scream", "gunshot", "glass" }) Played[key] = 0;
        for (int i = 0; i < VOICES; i++)
        {
            voices[i] = new Voice();
            AddChild(voices[i].player);
        }
        noises = new WorldNoises(this);
        loops = new Loops(this);
        ambience = new Ambience(this);
        yours = new Yours(this);
    }

    /// <summary>The streams of a kind (the self-test writes the made ones out to listen to).</summary>
    public AudioStream[] Kind(string kind) => bank.Get(kind);

    public void Update(SimState state, double alpha, Survivor you)
    {
        updatedFrame = Engine.GetProcessFrames();
        if (!bank.Poll()) return;
        frame++;
        var zombies = new Dictionary<string, Zombie>();
        foreach (var z in state.zombies) zombies[z.id] = z;
        var me = worldPos(you, alpha);
        Follow(state, alpha, zombies, me);

        bool fresh = !ReferenceEquals(state, last) || state.tick < lastTick || state.tick - lastTick > GAP;
        bool newSecond = !fresh && state.tick / 10 != lastTick / 10;
        // Who killed something since the last look.
        var killers = new List<Survivor>();
        foreach (var s in state.survivors)
        {
            if (!fresh && kills.TryGetValue(s.id, out var k) && s.kills > k) killers.Add(s);
            kills[s.id] = s.kills;
        }
        if (!fresh)
        {
            foreach (var z in state.zombies)
            {
                var p = worldPos(z, alpha);
                double d = Dist(p, me);
                if (d > SPECS["scream"].range) continue;
                if (seen.TryGetValue(z.id, out var was))
                {
                    if (z.screamed && !was.screamed) Play(state, "scream", z.id, p, MOUTH, me, Hash.idNumber(z.id));
                    if (d > RANGE) continue;
                    if (z.hurtTick != was.hurt && z.hurtTick >= 0) Play(state, "hit", z.id, p, CHEST, me, Hash.idNumber(z.id) + z.hurtTick);
                    if (z.targetId == you.id && was.target != you.id) Play(state, "moan_notice", z.id, p, MOUTH, me, Hash.idNumber(z.id) + state.tick);
                }
                if (newSecond && d <= IDLE_RANGE && !Speaking(z.id))
                {
                    double chance = z.targetId != null ? HUNT_CHANCE : IDLE_CHANCE;
                    if (Hash.hashRoll(Hash.idNumber(z.id), state.tick / 10, 0x5eed) < chance)
                        Play(state, "moan_idle", z.id, p, MOUTH, me, Hash.idNumber(z.id) * 31 + state.tick / 10);
                }
            }
            // The killing blows: a zombie gone, near someone whose kills went up.
            foreach (var (id, was) in seen)
            {
                if (zombies.ContainsKey(id) || Dist(was.at, me) > RANGE) continue;
                foreach (var s in killers)
                {
                    if (Dist(was.at, worldPos(s, alpha)) > 3) continue;
                    Play(state, "kill", null, was.at, CHEST, me, Hash.idNumber(id));
                    killers.Remove(s);
                    break;
                }
            }
        }
        seen.Clear();
        foreach (var z in state.zombies) seen[z.id] = (z.targetId, z.hurtTick, z.screamed, worldPos(z, alpha));
        noises.Update(state, alpha, you, me, fresh, lastTick);
        loops.Update(state, alpha, me, frame);
        ambience.Update(state, you, fresh, lastTick);
        yours.Update(state, you, fresh);
        last = state;
        lastTick = state.tick;
    }

    /// <summary>Away from first person (the map, the Colony tab), the loops and the weather go quiet.</summary>
    public override void _Process(double delta)
    {
        if (Engine.GetProcessFrames() - updatedFrame > 2 || !IsVisibleInTree())
        {
            loops.Silence();
            ambience.Silence();
        }
    }

    bool Speaking(string id)
    {
        foreach (var v in voices) if (v.follows == id && v.player.Playing) return true;
        return false;
    }

    /// <summary>Plays a variant of `kind` from `at` (tile units), `height` m over the ground, following body `follows` if given.
    /// Takes a free voice, else the one playing the least important sound if it's less important than this.</summary>
    public void Play(SimState state, string kind, string? follows, P at, float height, P listener, double pick, float extraDb = 0)
    {
        var spec = SPECS[kind];
        if (Dist(at, listener) > spec.range) return;
        Voice? use = null;
        foreach (var v in voices)
        {
            if (!v.player.Playing) { use = v; break; }
            if (v.priority < spec.priority && (use == null || v.priority < use.priority)) use = v;
        }
        if (use == null) return;
        var set = bank.Get(kind);
        use.player.Stop();
        use.player.Stream = set[(int)(Math.Abs(pick) % set.Length)];
        use.player.PitchScale = 0.92f + 0.16f * (float)Hash.hashRoll(pick, 0x9172);
        use.player.UnitSize = spec.unit;
        use.player.MaxDistance = spec.range;
        use.db = spec.db + extraDb;
        use.follows = follows;
        use.height = height;
        use.priority = spec.priority;
        use.at = at;
        use.player.Position = Place(at, height);
        float muffle = Muffle.Between(state.grid, at.x, at.y, listener.x, listener.y);
        Muffled(use.player, use.db, muffle);
        use.player.Play();
        LastAt = use.player.Position;
        LastKind = kind;
        LastMuffle = muffle;
        string key = kind == "moan_idle" ? "idle" : kind == "moan_notice" ? "notice" : kind;
        Played[key] = Count(key) + 1;
    }

    /// <summary>How many of a kind have played.</summary>
    public int Count(string key) => Played.TryGetValue(key, out var n) ? n : 0;

    public static void Muffled(AudioStreamPlayer3D p, float db, float muffle)
    {
        p.VolumeDb = db + Muffle.DbFor(muffle);
        string bus = Muffle.BusFor(muffle);
        if (p.Bus != bus) p.Bus = bus;
    }

    /// <summary>Voices speaking for a body move with it; a body gone leaves its voice where it was. Each looks again
    /// at what's between it and you now and then (you walk round a corner, a door shuts).</summary>
    void Follow(SimState state, double alpha, Dictionary<string, Zombie> zombies, P me)
    {
        World3D.Terrain.Use(state);
        for (int i = 0; i < voices.Length; i++)
        {
            var v = voices[i];
            if (!v.player.Playing) { v.follows = null; continue; }
            if (v.follows != null && zombies.TryGetValue(v.follows, out var z))
            {
                v.at = worldPos(z, alpha);
                v.player.Position = Place(v.at, v.height);
            }
            if ((frame + i) % MUFFLE_EVERY == 0) Muffled(v.player, v.db, Muffle.Between(state.grid, v.at.x, v.at.y, me.x, me.y));
        }
    }

    public static Vector3 Place(P at, float height) => new((float)at.x, World3D.Terrain.Height((float)at.x, (float)at.y) + height, (float)at.y);

    public static double Dist(P a, P b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
}
