// Which locomotion clip a body plays at a speed, and how fast (2026-11-10, the animation pass). The clips come
// from art/scripts/derive_clips.py (assets/characters/clips_more.glb, library "more"): the library's walk, jog and
// sprint stride-warped to a slow walk, a walk, a brisk walk, a jog and a run, each with its ground speed at rate 1
// measured from its planted foot (clips_more.json). The rate is the speed over the clip's: the feet keep pace with
// the ground at any speed, and the clip is picked so the cadence stays a human one (a band per clip, a little
// overlap so a speed on the edge doesn't flick between two).
// Render-side only: People.cs plays what this picks; AnimTest.cs measures it.

using System;
using System.Collections.Generic;
using Godot;

namespace LoonieRoo.Game.Characters;

public static class Gaits
{
    /// <summary>A clip and its playback rate.</summary>
    public readonly record struct Pick(string clip, float rate);

    /// <summary>One gait: its clip ("more/walk"), the speeds it covers (m/s), its ground speed at rate 1.</summary>
    public sealed record Band(string clip, double from, double to, double speed);

    static readonly Dictionary<string, double> speeds = new();

    /// <summary>A gait clip's ground speed at rate 1 (m/s), as Measure found it; 0 for one that doesn't move.</summary>
    public static double SpeedOf(string clip) => speeds.TryGetValue(clip, out var v) ? v : 0;

    /// <summary>The rig's ankle and ball (the toes bone's head) over the sole (art/scripts/rig.py), and how near the
    /// ground a sole point counts as down (m): AnimTest.cs measures the drawn feet the same way.</summary>
    public const float HEEL_UP = 0.09f, BALL_UP = 0.02f, DOWN = 0.025f;
    const int SAMPLES_PER_SECOND = 120;

    /// <summary>Measures each looping clip of `lib` (played as prefix + name) in place, by forward kinematics through
    /// its own tracks from skeleton `skel` (toScene: the skeleton's place in the scene): each foot's contact point is
    /// the lower of its heel and its ball, down within DOWN of the ground; the clip's ground speed is how fast that
    /// point travels back while down (the median: a toe scuffing forward as it lifts doesn't count). Once at load (People.cs, AnimTest.cs); clips_more.json has Blender's figure.</summary>
    public static void Measure(AnimationLibrary lib, string prefix, Skeleton3D skel, Transform3D toScene)
    {
        foreach (var name in lib.GetAnimationList())
        {
            var a = lib.GetAnimation(name);
            var fk = new Fk(a, skel, toScene);
            int n = Math.Max(2, (int)(a.Length * SAMPLES_PER_SECOND));
            var v = new List<double>();
            for (int foot = 0; foot < 2; foot++)
            {
                string s = foot == 0 ? "Left" : "Right";
                Vector3 lastAt = default; int lastPt = -1;
                for (int i = 0; i <= n; i++)
                {
                    double t = a.Length * i / n;
                    var heel = fk.Head(s + "Foot", t) - new Vector3(0, HEEL_UP, 0);
                    var ball = fk.Head(s + "Toes", t) - new Vector3(0, BALL_UP, 0);
                    int pt = heel.Y <= ball.Y ? 0 : 1;
                    var at = pt == 0 ? heel : ball;
                    bool down = at.Y < DOWN;
                    if (down && lastPt == pt) v.Add((lastAt.Z - at.Z) / (a.Length / n));
                    lastPt = down ? pt : -1;
                    lastAt = at;
                }
            }
            v.RemoveAll((x) => x <= 0.05);  // a foot dragged forward along the ground isn't the ground's pace
            v.Sort();
            double median = v.Count > 0 ? v[v.Count / 2] : 0;
            speeds[prefix + name] = median > 0.02 ? median : 0;
        }
    }

    static HashSet<string>? looping;

    /// <summary>Whether a derived clip loops (clips_more.json says).</summary>
    public static bool Looping(string name)
    {
        if (looping == null)
        {
            looping = new();
            using var f = FileAccess.Open("res://assets/characters/clips_more.json", FileAccess.ModeFlags.Read);
            if (f != null)
                foreach (var c in Json.ParseString(f.GetAsText()).AsGodotDictionary()["clips"].AsGodotArray())
                {
                    var clip = c.AsGodotDictionary();
                    if (clip["loop"].AsBool()) looping.Add(clip["name"].AsString());
                }
        }
        return looping.Contains(name);
    }

    /// <summary>A skeleton's place in the scene `root` heads (an Armature node can turn it).</summary>
    public static Transform3D ToScene(Node root, Skeleton3D skel)
    {
        var xf = Transform3D.Identity;
        for (Node n = skel; n != null && n != root; n = n.GetParent())
            if (n is Node3D n3) xf = n3.Transform * xf;
        return xf;
    }

    /// <summary>Forward kinematics through a clip's own bone tracks (the rest where a bone has none).</summary>
    public sealed class Fk
    {
        readonly Animation a;
        readonly Skeleton3D skel;
        readonly Transform3D toScene;
        readonly Dictionary<string, int> pos = new(), rot = new();

        public Fk(Animation a, Skeleton3D skel, Transform3D toScene)
        {
            this.a = a; this.skel = skel; this.toScene = toScene;
            for (int i = 0; i < a.GetTrackCount(); i++)
            {
                string bone = a.TrackGetPath(i).GetConcatenatedSubNames();
                if (a.TrackGetType(i) == Animation.TrackType.Position3D) pos[bone] = i;
                else if (a.TrackGetType(i) == Animation.TrackType.Rotation3D) rot[bone] = i;
            }
        }

        /// <summary>Bone `name`'s head at time t, in the scene.</summary>
        public Vector3 Head(string name, double t)
        {
            var chain = new List<int>();
            for (int c = skel.FindBone(name); c >= 0; c = skel.GetBoneParent(c)) chain.Insert(0, c);
            var xf = toScene;
            foreach (int c in chain)
            {
                string bn = skel.GetBoneName(c);
                var rest = skel.GetBoneRest(c);
                var o = pos.TryGetValue(bn, out var pt) ? a.PositionTrackInterpolate(pt, t) : rest.Origin;
                var q = rot.TryGetValue(bn, out var rt) ? a.RotationTrackInterpolate(rt, t) : rest.Basis.GetRotationQuaternion();
                xf *= new Transform3D(new Basis(q), o);
            }
            return xf.Origin;
        }
    }

    /// <summary>Under this (m/s) a body stands; over it, it steps.</summary>
    public const double STILL = 0.25;
    /// <summary>A band holds a body this much (share of its edge) past its edge before the next takes over.</summary>
    const double HOLD = 0.08;

    /// <summary>The living on their feet, walking (or running: `running` lets the run take over sooner).</summary>
    static readonly Band[] WALKING =
    {
        new("more/walk_slow", 0, 1.05, 0), new("more/walk", 1.05, 1.6, 0), new("more/walk_brisk", 1.6, 2.9, 0),
        new("more/jog", 2.9, 3.9, 0), new("more/run", 3.9, 99, 0),
    };
    static readonly Band[] RUNNING = { new("more/jog", 0, 3.6, 0), new("more/run", 3.6, 99, 0) };
    static readonly Band[] SNEAKING = { new("more/sneak", 0, 99, 0) };

    /// <summary>The clip for one of the living moving at `speed` (m/s), given the one it plays now (for the overlap).</summary>
    public static Pick Living(double speed, string stance, bool running, string current)
    {
        var bands = stance == "sneak" ? SNEAKING : running && speed > 2.6 ? RUNNING : WALKING;
        return InBands(bands, speed, current);
    }

    /// <summary>The dead's gaits, one per body by a hash of its id (stable: nothing saved): two in five shamble, one lurches,
    /// two drag a leg (one each side). Runners run (shambling when slow), brutes lurch.</summary>
    static readonly string[] DEAD_GAITS = { "more/dead_shamble", "more/dead_shamble", "more/dead_lurch", "more/dead_drag_l", "more/dead_drag_r" };
    /// <summary>The crawl's ground speed at rate 1 (m/s): derive_clips.py's CRAWL_SPEED (its hands, not its feet, are planted).</summary>
    public const double CRAWL_SPEED = 1.0;

    /// <summary>One of the dead (variant: the sim's walker, runner, brute, screamer; h: a hash of its id, 0..1) at a speed.</summary>
    public static Pick Dead(string variant, double h, double speed)
    {
        // Standing about: the hunched idle, each at its own pace.
        if (speed < STILL) return new Pick("more/dead_idle", (float)(0.75 + 0.5 * h));
        string clip = variant == "runner" && speed > 1.6 ? "more/dead_run" : variant == "brute" ? "more/dead_lurch"
            : DEAD_GAITS[Math.Min(DEAD_GAITS.Length - 1, (int)(h * DEAD_GAITS.Length))];
        double at1 = SpeedOf(clip);
        return new Pick(clip, at1 > 0 ? (float)Math.Clamp(speed / at1, 0.3, 3.0) : 1f);
    }

    /// <summary>A crawler dragging itself along (or lying still, barely stirring).</summary>
    public static Pick Crawl(double speed) => speed < STILL ? new Pick("more/dead_crawl", 0.12f) : new Pick("more/dead_crawl", (float)Math.Clamp(speed / CRAWL_SPEED, 0.3, 3.0));

    /// <summary>One clip at the rate that matches a speed.</summary>
    public static (string clip, float rate, double blend) InBandsPick(string clip, double speed)
    {
        double at1 = SpeedOf(clip);
        return (clip, at1 > 0 ? (float)Math.Clamp(speed / at1, 0.3, 3.0) : 1f, 0.2);
    }

    /// <summary>The band for a speed (the current clip's while within HOLD of its edges), at the rate that matches it.</summary>
    public static Pick InBands(Band[] bands, double speed, string current)
    {
        Band? pick = null;
        foreach (var b in bands)
            if (b.clip == current && speed >= b.from * (1 - HOLD) && speed < b.to * (1 + HOLD)) { pick = b; break; }
        if (pick == null)
            foreach (var b in bands)
                if (speed >= b.from && speed < b.to) { pick = b; break; }
        pick ??= bands[^1];
        double at1 = pick.speed > 0 ? pick.speed : SpeedOf(pick.clip);
        return new Pick(pick.clip, at1 > 0 ? (float)Math.Clamp(speed / at1, 0.3, 3.0) : 1f);
    }
}
