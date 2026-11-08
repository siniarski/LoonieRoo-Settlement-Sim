// Sound in first person (2026-11-09): the dead's moans and blows landing, each from where it
// happens, heard through the camera. Reads state only (rule 5), by comparing what it sees now
// with what it saw last frame; nothing is added to the sim and nothing in state holds a delegate.
//   notice  a zombie's targetId turns to you: it has seen you, and snarls
//   idle    the near dead groan now and then (more often while they hunt someone): a stateless
//           hashRoll of the zombie's id and the second, so the same moment moans the same way
//   hit     a zombie's hurtTick moves: a blow landed on it
//   kill    a zombie is gone and someone's kills went up: the killing blow, where it stood
// A pool of VOICES players; each follows the body it speaks for until it's done. Sounds come
// from Synth (made by code); a file in res://assets/sounds/ named like "moan_idle_0.ogg"
// replaces the made ones of its kind, so a recorded pack can drop in later.

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;
using LoonieRoo.Game.Render;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.Audio;

public sealed partial class Sounds : Node3D
{
    /// <summary>Players at once; a moan with none free waits for the next chance.</summary>
    const int VOICES = 10;
    /// <summary>The dead further than this (m) aren't heard at all.</summary>
    const float RANGE = 40;
    /// <summary>Idle groans: one roll per zombie per second within this range (m), at these chances.</summary>
    const float IDLE_RANGE = 24;
    const double IDLE_CHANCE = 0.04, HUNT_CHANCE = 0.12;
    /// <summary>A jump of more ticks than this between looks (a load, time away on the map) is taken as a new start, not as events.</summary>
    const int GAP = 30;
    /// <summary>The height (m) a zombie's voice comes from, over the ground under it.</summary>
    const float MOUTH = 1.55f, CHEST = 1.2f;

    readonly Dictionary<string, AudioStream[]> kinds = new();
    readonly Voice[] voices = new Voice[VOICES];
    readonly Dictionary<string, (string? target, double hurt, P at)> seen = new();
    readonly Dictionary<string, double> kills = new();
    SimState? last;
    int lastTick = -1;

    /// <summary>How many of each sound have played (the self-test reads it).</summary>
    public readonly Dictionary<string, int> Played = new() { ["notice"] = 0, ["idle"] = 0, ["hit"] = 0, ["kill"] = 0 };
    /// <summary>Where the last sound was played from (m), for the self-test.</summary>
    public Vector3 LastAt { get; private set; }

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
        public float height;
        public int priority;
    }

    public Sounds()
    {
        _ = Volume.Level; // sets the master bus from the settings file
        kinds["moan_idle"] = Make("moan_idle", 6, (i) => Synth.Moan(i, false));
        kinds["moan_notice"] = Make("moan_notice", 4, (i) => Synth.Moan(i, true));
        kinds["hit"] = Make("hit", 3, (i) => Synth.Hit(i, false));
        kinds["kill"] = Make("kill", 2, (i) => Synth.Hit(i, true));
        for (int i = 0; i < VOICES; i++)
        {
            voices[i] = new Voice();
            AddChild(voices[i].player);
        }
    }

    /// <summary>The variants of a kind: the files in res://assets/sounds/ if there are any, else made.</summary>
    static AudioStream[] Make(string kind, int count, Func<int, float[]> make)
    {
        var files = new List<AudioStream>();
        for (int i = 0; ; i++)
        {
            string? path = null;
            foreach (var ext in new[] { "ogg", "wav" })
                if (ResourceLoader.Exists($"res://assets/sounds/{kind}_{i}.{ext}")) path = $"res://assets/sounds/{kind}_{i}.{ext}";
            if (path == null) break;
            if (GD.Load<AudioStream>(path) is { } s) files.Add(s);
        }
        if (files.Count > 0) return files.ToArray();
        var made = new AudioStream[count];
        for (int i = 0; i < count; i++) made[i] = Stream(make(i));
        return made;
    }

    public static AudioStreamWav Stream(float[] samples) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        MixRate = Synth.RATE,
        Stereo = false,
        Data = Synth.Pcm16(samples),
    };

    /// <summary>The made streams of a kind (the self-test writes them out to listen to).</summary>
    public AudioStream[] Kind(string kind) => kinds[kind];

    public void Update(SimState state, double alpha, Survivor you)
    {
        var zombies = new Dictionary<string, Zombie>();
        foreach (var z in state.zombies) zombies[z.id] = z;
        Follow(state, alpha, zombies);

        bool fresh = !ReferenceEquals(state, last) || state.tick < lastTick || state.tick - lastTick > GAP;
        bool newSecond = !fresh && state.tick / 10 != lastTick / 10;
        var me = worldPos(you, alpha);
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
                if (d > RANGE) continue;
                if (seen.TryGetValue(z.id, out var was))
                {
                    if (z.hurtTick != was.hurt && z.hurtTick >= 0) Play("hit", z.id, p, CHEST, 3, Hash.idNumber(z.id) + z.hurtTick);
                    if (z.targetId == you.id && was.target != you.id) Play("moan_notice", z.id, p, MOUTH, 2, Hash.idNumber(z.id) + state.tick);
                }
                if (newSecond && d <= IDLE_RANGE && !Speaking(z.id))
                {
                    double chance = z.targetId != null ? HUNT_CHANCE : IDLE_CHANCE;
                    if (Hash.hashRoll(Hash.idNumber(z.id), state.tick / 10, 0x5eed) < chance)
                        Play("moan_idle", z.id, p, MOUTH, 1, Hash.idNumber(z.id) * 31 + state.tick / 10);
                }
            }
            // The killing blows: a zombie gone, near someone whose kills went up.
            foreach (var (id, was) in seen)
            {
                if (zombies.ContainsKey(id) || Dist(was.at, me) > RANGE) continue;
                foreach (var s in killers)
                {
                    if (Dist(was.at, worldPos(s, alpha)) > 3) continue;
                    Play("kill", null, was.at, CHEST, 3, Hash.idNumber(id));
                    killers.Remove(s);
                    break;
                }
            }
        }
        seen.Clear();
        foreach (var z in state.zombies) seen[z.id] = (z.targetId, z.hurtTick, worldPos(z, alpha));
        last = state;
        lastTick = state.tick;
    }

    bool Speaking(string id)
    {
        foreach (var v in voices) if (v.follows == id && v.player.Playing) return true;
        return false;
    }

    /// <summary>Plays a variant of `kind` from `at`, following body `follows` if given. Takes a free voice, else
    /// the one playing the least important sound if it's less important than this.</summary>
    void Play(string kind, string? follows, P at, float height, int priority, double pick)
    {
        Voice? use = null;
        foreach (var v in voices)
        {
            if (!v.player.Playing) { use = v; break; }
            if (v.priority < priority && (use == null || v.priority < use.priority)) use = v;
        }
        if (use == null) return;
        var set = kinds[kind];
        use.player.Stop();
        use.player.Stream = set[(int)(Math.Abs(pick) % set.Length)];
        use.player.PitchScale = 0.92f + 0.16f * (float)Hash.hashRoll(pick, 0x9172);
        use.player.VolumeDb = kind == "moan_idle" ? -4 : 0;
        use.follows = follows;
        use.height = height;
        use.priority = priority;
        use.player.Position = Place(at, height);
        use.player.Play();
        LastAt = use.player.Position;
        Played[kind == "moan_idle" ? "idle" : kind == "moan_notice" ? "notice" : kind]++;
    }

    /// <summary>Voices speaking for a body move with it; a body gone leaves its voice where it was.</summary>
    void Follow(SimState state, double alpha, Dictionary<string, Zombie> zombies)
    {
        World3D.Terrain.Use(state);
        foreach (var v in voices)
        {
            if (!v.player.Playing) { v.follows = null; continue; }
            if (v.follows != null && zombies.TryGetValue(v.follows, out var z)) v.player.Position = Place(worldPos(z, alpha), v.height);
        }
    }

    static Vector3 Place(P at, float height) => new((float)at.x, World3D.Terrain.Height((float)at.x, (float)at.y) + height, (float)at.y);

    static double Dist(P a, P b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
}
