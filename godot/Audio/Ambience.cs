// The weather and the hour, all round you (2026-11-09): not from anywhere, so plain players, not 3D.
//   wind     always, stronger in rain and snow, strongest in a storm or a blizzard
//   rain     in rain and storms
//   night    insects after dark, not in winter or in the wet
//   thunder  when lightning strikes (Seasons.lightningAt for each tick run), a moment later
// Indoors (Rooms.isIndoors under you) the wind and rain go through the "Muffled" bus and drop: rain
// on the roof, the wind outside. Levels ease towards their target (EASE dB a second), so weather
// turning and going in and out don't jump.

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Audio;

public sealed class Ambience
{
    /// <summary>Below this (dB) a loop is stopped.</summary>
    const float OFF = -60;
    /// <summary>How fast levels move (dB a second).</summary>
    const float EASE = 15;
    const float INDOOR_DB = -9;

    sealed class Bed
    {
        public readonly AudioStreamPlayer p = new() { VolumeDb = OFF };
        public float level = OFF, target = OFF;
    }

    readonly Sounds sounds;
    readonly Dictionary<string, Bed> beds = new();
    readonly AudioStreamPlayer thunder = new();
    readonly List<(ulong at, double pick)> claps = new();
    ulong lastMs;

    /// <summary>The level each bed is heading for (dB; OFF for silent), and thunder heard (the self-test reads them).</summary>
    public float Target(string kind) => beds[kind].target;
    public int Thunders { get; private set; }

    public Ambience(Sounds sounds)
    {
        this.sounds = sounds;
        foreach (var kind in new[] { "wind", "rain", "night" })
        {
            beds[kind] = new Bed();
            sounds.AddChild(beds[kind].p);
        }
        sounds.AddChild(thunder);
    }

    /// <summary>What each bed wants now (dB), from the weather, the hour, the season and whether you're under a roof.</summary>
    public static (float wind, float rain, float night) Targets(string weather, bool night, bool winter, bool indoors)
    {
        float wind = weather switch { "storm" => -6, "blizzard" => -3, "rain" => -15, "snow" => -14, "heatwave" => -28, _ => -21 };
        float rain = weather switch { "storm" => -3, "rain" => -8, _ => OFF };
        bool wet = Seasons.isWet(weather);
        float insects = night && !winter && !wet ? -15 : OFF;
        if (indoors)
        {
            wind += INDOOR_DB;
            if (rain > OFF) rain += INDOOR_DB / 2;
            if (insects > OFF) insects += INDOOR_DB * 1.5f;
        }
        return (wind, rain, insects);
    }

    public void Update(SimState state, Survivor you, bool fresh, int lastTick)
    {
        ulong now = Godot.Time.GetTicksMsec();
        float dt = lastMs == 0 ? 0 : Math.Min(0.25f, (now - lastMs) / 1000f);
        lastMs = now;
        bool indoors = Rooms.isIndoors(state, you.pos.x, you.pos.y);
        bool inCar = Vehicles.vehicleOf(state, you) != null;
        var (wind, rain, night) = Targets(state.weather, Sim.Time.darkness(state.tick) > 0.5, Seasons.seasonOf(state.tick) == "winter", indoors || inCar);
        Set("wind", wind, indoors || inCar, dt, fresh);
        Set("rain", rain, indoors || inCar, dt, fresh);
        Set("night", night, indoors || inCar, dt, fresh);

        // Thunder: a strike on any tick run since the last look, heard a moment later (the strike is somewhere near).
        if (fresh) claps.Clear();
        else
            for (int t = lastTick + 1; t <= state.tick; t++)
                if (Seasons.lightningAt(state, t)) claps.Add((now + (ulong)(400 + Hash.hashRoll(t, 0x7d0) * 2600), t));
        for (int i = claps.Count - 1; i >= 0; i--)
        {
            if (claps[i].at > now) continue;
            var set = sounds.bank.Get("thunder");
            thunder.Stream = set[(int)(claps[i].pick % set.Length)];
            thunder.PitchScale = 0.85f + 0.3f * (float)Hash.hashRoll(claps[i].pick, 0x7d1);
            thunder.VolumeDb = indoors ? -4 : 0;
            thunder.Bus = indoors ? Muffle.MUFFLED : "Master";
            thunder.Play();
            Thunders++;
            claps.RemoveAt(i);
        }
    }

    void Set(string kind, float target, bool muffled, float dt, bool fresh)
    {
        var b = beds[kind];
        b.target = target;
        b.level = fresh ? target : b.level + Math.Clamp(target - b.level, -EASE * dt, EASE * dt);
        string bus = muffled ? Muffle.MUFFLED : "Master";
        if (b.p.Bus != bus) b.p.Bus = bus;
        if (b.level <= OFF + 0.5f)
        {
            if (b.p.Playing) b.p.Stop();
            return;
        }
        b.p.VolumeDb = b.level;
        if (!b.p.Playing)
        {
            b.p.Stream = sounds.bank.Get(kind)[0];
            b.p.Play();
        }
    }

    public void Silence()
    {
        foreach (var b in beds.Values)
        {
            if (b.p.Playing) b.p.Stop();
            b.level = OFF;
        }
        lastMs = 0;
    }
}
