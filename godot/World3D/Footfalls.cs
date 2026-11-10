// Footsteps from the animation's own footfalls (2026-11-09, the playtest: steps on a timer were out of step
// with the walk and the run). Two halves:
//   Marks   once, at load: each locomotion clip's footfalls, found from the clip itself. The ankles' height is
//           worked out over the clip by forward kinematics through its own bone tracks (rests where a bone has
//           no track), and a foot touches down where its ankle comes down through FLOOR_BAND over its lowest;
//           it lifts where it goes back up through it.
//   Watch   each frame, per model: where its clip's playhead was and is now. Crossing a touch-down mark going
//           forward is a step; played backwards (backing off) the clip's lift-offs are the touch-downs, so those
//           are the steps. The rate doesn't matter (sped up, slowed down, either way round), nor the frame
//           rate (every mark crossed since the last look counts, at most a clip's length), nor the LOD.
// Why marks from the clip rather than reading the drawn feet each frame: the drawn feet are a blend while
// clips cross-fade and a frame samples the foot at whatever moment it falls on (a run's contact lasts a frame
// or two at 30 fps), so a per-frame height test misses or doubles steps; the marks are exact, cost nothing a
// frame, and follow the clips when retarget.py exports them again (no key frames to keep in step by hand).
// Render-side only: nothing goes to the sim.

using System;
using System.Collections.Generic;
using Godot;

namespace LoonieRoo.Game.World3D;

/// <summary>A foot coming down this frame: whose, where (tile units: x, height, y), the clip's gait
/// (walk, jog, sprint, crouch) and the foot (0 left, 1 right).</summary>
public readonly record struct Footfall(string bodyId, bool you, bool dead, Vector3 at, string gait, int foot);

public static class Footfalls
{
    /// <summary>The locomotion clips that step, and their gait.</summary>
    public static readonly Dictionary<string, string> GAITS = new()
    {
        ["ual/ual_walk"] = "walk", ["ual/ual_jog_fwd"] = "jog", ["ual/ual_sprint"] = "sprint", ["ual/ual_crouch_fwd"] = "crouch",
        // The derived gaits (art/scripts/derive_clips.py, 2026-11-10), the dead's, and the crawl (on its hands).
        ["more/walk_slow"] = "walk", ["more/walk"] = "walk", ["more/walk_brisk"] = "walk", ["more/jog"] = "jog",
        ["more/run"] = "sprint", ["more/sneak"] = "crouch", ["more/shuffle"] = "walk",
        ["more/dead_shamble"] = "walk", ["more/dead_lurch"] = "walk", ["more/dead_drag_l"] = "walk", ["more/dead_drag_r"] = "walk",
        ["more/dead_run"] = "jog", ["more/dead_crawl"] = "crawl", ["more/hop_l"] = "walk", ["more/hop_r"] = "walk",
        ["more/crawl_l"] = "crawl", ["more/crawl_r"] = "crawl", ["more/carry_walk"] = "walk", ["more/carry_brisk"] = "walk",
    };
    /// <summary>Gaits whose footfalls are the hands' (a crawl drags itself on them).</summary>
    public static readonly HashSet<string> ON_HANDS = new() { "crawl" };
    /// <summary>A foot is down within this (m) of its lowest in the clip, or this share of its lift, whichever is more.</summary>
    const float FLOOR_BAND = 0.015f, FLOOR_SHARE = 0.10f;
    /// <summary>A foot that came down is in the air again once it's above this share of its lift.</summary>
    const float AIR_SHARE = 0.35f;
    const int SAMPLES = 240;
    static readonly string[] FEET = { "LeftFoot", "RightFoot" };

    /// <summary>A clip's marks: touch-downs and lift-offs (seconds into the clip) per foot, and the ankles' height
    /// curve (for the trace and the self-test).</summary>
    public sealed class Marks
    {
        public double length;
        public readonly List<(double t, int foot)> down = new(), up = new();
        public float[][] height = { Array.Empty<float>(), Array.Empty<float>() };
        public float[] floor = new float[2];

        /// <summary>A foot's height over its lowest in the clip at time t (m).</summary>
        public float HeightAt(int foot, double t)
        {
            var h = height[foot];
            if (h.Length == 0 || length <= 0) return 0;
            double u = ((t % length) + length) % length / length * h.Length;
            int i = (int)u % h.Length, j = (i + 1) % h.Length;
            double f = u - Math.Floor(u);
            return (float)(h[i] + (h[j] - h[i]) * f) - floor[foot];
        }
    }

    /// <summary>The marks of every stepping clip in `lib` (named prefix + its name: "ual/", "more/"), from the skeleton
    /// `skel` of scene `root` (the instance the clips' track paths start at).</summary>
    public static Dictionary<string, Marks> Find(AnimationLibrary lib, Node root, Skeleton3D skel, string prefix = "ual/")
    {
        var all = new Dictionary<string, Marks>();
        // The skeleton's place in the scene (an Armature node can turn it): heights are taken along the scene's up.
        var toScene = Transform3D.Identity;
        for (Node n = skel; n != null && n != root; n = n.GetParent())
            if (n is Node3D n3) toScene = n3.Transform * toScene;
        foreach (var (key, gait) in GAITS)
        {
            if (!key.StartsWith(prefix)) continue;
            string name = key[prefix.Length..];
            if (!lib.HasAnimation(name)) continue;
            all[key] = Of(lib.GetAnimation(name), skel, toScene, ON_HANDS.Contains(gait) ? HANDS : FEET);
        }
        if (OS.GetEnvironment("STEP_TRACE") != "")
            foreach (var (k, m) in all)
                GD.Print($"footfalls: {k} {m.length:0.000}s down {string.Join(" ", m.down.ConvertAll((d) => $"{(d.foot == 0 ? "L" : "R")}@{d.t:0.000}"))}"
                    + $" up {string.Join(" ", m.up.ConvertAll((d) => $"{(d.foot == 0 ? "L" : "R")}@{d.t:0.000}"))} lift L {m.height[0].Max() - m.floor[0]:0.000} R {m.height[1].Max() - m.floor[1]:0.000} m");
        return all;
    }

    static float Max(this float[] a) { float m = float.MinValue; foreach (var v in a) m = Math.Max(m, v); return m; }

    static readonly string[] HANDS = { "LeftHand", "RightHand" };

    static Marks Of(Animation a, Skeleton3D skel, Transform3D toScene, string[] limbs)
    {
        var m = new Marks { length = a.Length };
        // Each bone's tracks, by name.
        var pos = new Dictionary<string, int>();
        var rot = new Dictionary<string, int>();
        var scl = new Dictionary<string, int>();
        for (int i = 0; i < a.GetTrackCount(); i++)
        {
            string bone = a.TrackGetPath(i).GetConcatenatedSubNames();
            switch (a.TrackGetType(i))
            {
                case Animation.TrackType.Position3D: pos[bone] = i; break;
                case Animation.TrackType.Rotation3D: rot[bone] = i; break;
                case Animation.TrackType.Scale3D: scl[bone] = i; break;
            }
        }
        for (int f = 0; f < 2; f++)
        {
            int b = skel.FindBone(limbs[f]);
            var chain = new List<int>();
            for (int c = b; c >= 0; c = skel.GetBoneParent(c)) chain.Insert(0, c);
            var h = new float[SAMPLES];
            for (int s = 0; s < SAMPLES; s++)
            {
                double t = a.Length * s / SAMPLES;
                var xf = toScene;
                foreach (int c in chain)
                {
                    string bn = skel.GetBoneName(c);
                    var rest = skel.GetBoneRest(c);
                    var o = pos.TryGetValue(bn, out var pt) ? a.PositionTrackInterpolate(pt, t) : rest.Origin;
                    var q = rot.TryGetValue(bn, out var rt) ? a.RotationTrackInterpolate(rt, t) : rest.Basis.GetRotationQuaternion();
                    var sc = scl.TryGetValue(bn, out var st) ? a.ScaleTrackInterpolate(st, t) : rest.Basis.Scale;
                    xf *= new Transform3D(new Basis(q).Scaled(sc), o);
                }
                h[s] = xf.Origin.Y;
            }
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in h) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
            m.height[f] = h;
            m.floor[f] = lo;
            // Down within the band over its lowest; back in the air only once it's above `air` (so a foot shuffling
            // in the band, as the sprint's right foot does, comes down once). Scanned once round from its highest.
            float band = lo + Math.Max(FLOOR_BAND, FLOOR_SHARE * (hi - lo)), air = lo + AIR_SHARE * (hi - lo);
            int top = Array.IndexOf(h, hi);
            bool down = false;
            double lift = -1;
            for (int k = 1; k <= SAMPLES; k++)
            {
                int s0 = (top + k - 1) % SAMPLES, s1 = (top + k) % SAMPLES;
                float h0 = h[s0], h1 = h[s1];
                double Cross() => ((a.Length * (s0 + (h0 - band) / (h0 - h1)) / SAMPLES) % a.Length + a.Length) % a.Length;
                if (!down && h0 > band && h1 <= band) { m.down.Add((Cross(), f)); down = true; lift = -1; }
                else if (down && h0 <= band && h1 > band) lift = Cross();
                else if (down && lift >= 0 && h1 <= band) lift = -1;
                if (down && lift >= 0 && h1 > air) { m.up.Add((lift, f)); down = false; }
            }
        }
        m.down.Sort((x, y) => x.t.CompareTo(y.t));
        m.up.Sort((x, y) => x.t.CompareTo(y.t));
        return m;
    }

    /// <summary>For this long (s) after a clip changes no steps come: the clips cross-fade over it (People's 0.25 s),
    /// and the drawn feet are still mostly the old clip's (the new one starts at its own 0, often a mark).</summary>
    public const double BLEND = 0.25;

    /// <summary>One model's playhead between looks.</summary>
    public sealed class Watch
    {
        string clip = "";
        double last, since;

        /// <summary>The feet that came down since the last look at `clip` now at `now` (s): each mark crossed, going
        /// the way the playhead went. A new clip starts fresh (none for BLEND s); the move is taken the short way round the loop.</summary>
        public void Crossed(string clipNow, double now, double delta, Marks? marks, List<int> feet)
        {
            feet.Clear();
            bool same = clipNow == clip;
            since = same ? since + delta : 0;
            clip = clipNow;
            double was = last;
            last = now;
            if (!same || since < BLEND || marks == null || marks.length <= 0) return;
            double L = marks.length, d = now - was;
            if (d > L / 2) d -= L;
            else if (d < -L / 2) d += L;
            if (d == 0) return;
            // Forward over (was, was + d]; backward over [was + d, was): the lift-offs are touch-downs played backwards.
            var list = d > 0 ? marks.down : marks.up;
            foreach (var (t, foot) in list)
            {
                // t as an offset from `was` the way we went, in [0, L).
                double off = d > 0 ? t - was : was - t;
                off = ((off % L) + L) % L;
                if (off > 0 && off <= Math.Abs(d) || (off == 0 && d < 0)) feet.Add(foot);
            }
        }

        public void Reset() { clip = ""; }
    }
}
