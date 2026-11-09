// Every sound the game has, by kind, and making them (2026-11-09). The made ones are worked out on a
// thread pool thread at startup (pure arithmetic: Synth, SynthWorld) and turned into Godot streams on
// the main thread once done, so starting the game doesn't wait on them; until then nothing plays.
// A file in res://assets/sounds/ named "<kind>_<n>.ogg|wav" replaces the made ones of its kind (a CC0
// pack can drop in, with the player's OK).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LoonieRoo.Game.Audio;

public sealed class Bank
{
    /// <summary>A kind: how many variants, how to make variant i, and whether it loops.</summary>
    public readonly record struct Kind(string name, int count, Func<int, float[]> make, bool loop = false);

    /// <summary>The kinds; the steps, one kind a surface (step_grass ... step_water), last.</summary>
    public static readonly Kind[] KINDS = new Kind[]
    {
        new("moan_idle", 6, (i) => Synth.Moan(i, false)),
        new("moan_notice", 4, (i) => Synth.Moan(i, true)),
        new("hit", 3, (i) => Synth.Hit(i, false)),
        new("kill", 2, (i) => Synth.Hit(i, true)),
        new("scream", 2, Synth.Scream),
        new("swing", 3, SynthWorld.Swing),
        new("gunshot", 3, SynthWorld.Gunshot),
        new("glass", 3, SynthWorld.Glass),
        new("thunder", 2, SynthWorld.Thunder),
        new("wind", 1, SynthWorld.Wind, true),
        new("rain", 1, SynthWorld.Rain, true),
        new("night", 1, SynthWorld.Night, true),
        new("engine", 1, SynthWorld.Engine, true),
        new("generator", 1, SynthWorld.Generator, true),
        new("alarm", 1, SynthWorld.Alarm, true),
        new("helicopter", 1, SynthWorld.Helicopter, true),
    }.Concat(Array.ConvertAll(SynthSteps.SURFACES, (g) => new Kind("step_" + g, 4, (i) => SynthSteps.Step(i, g)))).ToArray();

    readonly Dictionary<string, AudioStream[]> streams = new();
    Task<Dictionary<string, float[][]>>? making;

    /// <summary>How long making the sounds took on the worker (ms), and turning them into streams on the main thread.</summary>
    public double MakeMs { get; private set; }
    public double InstallMs { get; private set; }
    public bool Ready { get; private set; }

    public Bank()
    {
        // Kinds with files are loaded here (the resource loader wants the main thread); the rest are made off it.
        var toMake = new List<Kind>();
        foreach (var k in KINDS)
        {
            var files = Files(k.name);
            if (files.Length > 0) streams[k.name] = files;
            else toMake.Add(k);
        }
        making = Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            var made = new Dictionary<string, float[][]>();
            foreach (var k in toMake)
            {
                var set = new float[k.count][];
                for (int i = 0; i < k.count; i++) set[i] = k.make(i);
                made[k.name] = set;
            }
            MakeMs = clock.Elapsed.TotalMilliseconds;
            return made;
        });
    }

    /// <summary>Once the worker is done, the streams are made (main thread). True when every kind is there.</summary>
    public bool Poll()
    {
        if (Ready) return true;
        if (making == null || !making.IsCompleted) return false;
        var clock = Stopwatch.StartNew();
        foreach (var (name, set) in making.Result)
        {
            bool loop = Array.Find(KINDS, (k) => k.name == name).loop;
            var arr = new AudioStream[set.Length];
            for (int i = 0; i < set.Length; i++) arr[i] = Stream(set[i], loop);
            streams[name] = arr;
        }
        InstallMs = clock.Elapsed.TotalMilliseconds;
        making = null;
        Ready = true;
        GD.Print($"Sounds: {KINDS.Length} kinds made in {MakeMs:0} ms off the main thread, {InstallMs:0.0} ms to hand to Godot");
        return true;
    }

    /// <summary>Waits for the sounds (the self-test; a few hundred ms at most).</summary>
    public void Wait()
    {
        making?.Wait();
        Poll();
    }

    public AudioStream[] Get(string kind) => streams[kind];

    static AudioStream[] Files(string kind)
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
        return files.ToArray();
    }

    public static AudioStreamWav Stream(float[] samples, bool loop = false) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        MixRate = Synth.RATE,
        Stereo = false,
        Data = Synth.Pcm16(samples),
        LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
        LoopBegin = 0,
        LoopEnd = loop ? samples.Length : 0,
    };
}
