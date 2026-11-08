// Sounds that go on while something does (2026-11-09), each from where it is, muffled as the rest:
//   alarm       each ringing alarm (state.timeline.alarms), a bell
//   engine      each car with someone at the wheel and fuel in it; faster as it goes faster
//   generator   each generator burning fuel (its `loot` counter moved in the last RUN_TICKS ticks)
//   helicopter  the helicopter (state.timeline.heli), high up
// The nearest LOOPS sources get a player; a source keeps its player while it's heard, so nothing restarts.

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;
using LoonieRoo.Game.Render;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.Audio;

public sealed class Loops
{
    const int LOOPS = 6;
    /// <summary>A generator that counted fuel within this many ticks is running.</summary>
    const int RUN_TICKS = 20;

    public static readonly Dictionary<string, Sounds.Spec> SPECS = new()
    {
        ["alarm"] = new(10, 110, -2, 0),
        ["engine"] = new(5, 60, -4, 0),
        ["generator"] = new(3, 40, -6, 0),
        ["helicopter"] = new(40, 300, 0, 0),
    };

    sealed class Player
    {
        public readonly AudioStreamPlayer3D p = new()
        {
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            MaxDb = 3,
            AttenuationFilterCutoffHz = 5000,
            AttenuationFilterDb = -18,
            DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
        };
        public string? key;
        public float db;
    }

    readonly Sounds sounds;
    readonly Player[] players = new Player[LOOPS];
    readonly Dictionary<string, (double loot, int tick)> generators = new();

    /// <summary>What's heard now, by key (the self-test reads it).</summary>
    public readonly List<string> Heard = new();

    public Loops(Sounds sounds)
    {
        this.sounds = sounds;
        for (int i = 0; i < LOOPS; i++)
        {
            players[i] = new Player();
            sounds.AddChild(players[i].p);
        }
    }

    readonly record struct Source(string key, string kind, P at, float height, float pitch, double dist);

    public void Update(SimState state, double alpha, P me, int frame)
    {
        var found = new List<Source>();
        void Add(string key, string kind, P at, float height, float pitch = 1)
        {
            double d = Sounds.Dist(at, me);
            if (d <= SPECS[kind].range) found.Add(new Source(key, kind, at, height, pitch, d));
        }
        var t = state.timeline;
        if (t != null)
        {
            foreach (var a in t.alarms) Add($"alarm {a.x},{a.y}", "alarm", new P(a.x + 0.5, a.y + 0.5), 2.6f);
            if (t.heli is { } heli) Add("helicopter", "helicopter", new P(heli.x + 0.5, heli.y + 0.5), 45);
        }
        foreach (var v in state.vehicles ?? new())
            if (v.driverId != null && v.fuel > 0 && v.health > 0)
                Add($"car {v.id}", "engine", worldPos(v.at, v.was, alpha), 0.6f, 0.85f + 1.3f * (float)Math.Min(1, Math.Abs(v.speed) / Vehicles.MAX_SPEED));
        foreach (var f in state.furniture)
        {
            if (f.kind != "generator") continue;
            // First seen: not known to be running until its counter moves.
            if (!generators.TryGetValue(f.id, out var g)) generators[f.id] = g = (f.loot, int.MinValue / 2);
            else if (g.loot != f.loot) generators[f.id] = g = (f.loot, state.tick);
            if (f.stored > 0 && state.tick - g.tick <= RUN_TICKS) Add($"generator {f.id}", "generator", new P(f.x + 0.5, f.y + 0.5), 0.5f);
        }
        found.Sort((a, b) => a.dist.CompareTo(b.dist));
        if (found.Count > LOOPS) found.RemoveRange(LOOPS, found.Count - LOOPS);

        Heard.Clear();
        var keep = new HashSet<string>();
        foreach (var s in found) keep.Add(s.key);
        foreach (var pl in players)
            if (pl.key != null && !keep.Contains(pl.key)) { pl.p.Stop(); pl.key = null; }
        for (int i = 0; i < found.Count; i++)
        {
            var s = found[i];
            Player? pl = Array.Find(players, (x) => x.key == s.key);
            bool start = pl == null;
            pl ??= Array.Find(players, (x) => x.key == null);
            if (pl == null) continue;
            var spec = SPECS[s.kind];
            if (start)
            {
                var set = sounds.bank.Get(s.kind);
                pl.key = s.key;
                pl.p.Stream = set[0];
                pl.p.UnitSize = spec.unit;
                pl.p.MaxDistance = spec.range;
                pl.db = spec.db;
            }
            pl.p.Position = Sounds.Place(s.at, s.height);
            pl.p.PitchScale = s.pitch;
            if (start || (frame + i) % 8 == 0) Sounds.Muffled(pl.p, pl.db, Muffle.Between(state.grid, s.at.x, s.at.y, me.x, me.y));
            if (start) pl.p.Play();
            Heard.Add(s.key);
        }
    }

    public void Silence()
    {
        foreach (var pl in players) if (pl.key != null) { pl.p.Stop(); pl.key = null; }
        Heard.Clear();
    }
}
