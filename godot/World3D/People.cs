// People in first person: survivors, residents, raiders and the dead near you as the "pz" character
// models (3k / 1.2k / 600 triangles, a 512 painted map, no normal map: picked 2026-11-09 from the style
// lineup, docs/characters.md "The look"; art/scripts/build_lookdev.py --only pz), animated from the sim's
// pose intents (PoseIntents.cs: stance, speed, heading) and LOD'd by height on screen as the look lab
// does. Reads state only.
//
// Budget (rule 14): the nearest MAX_MODELS are models, the rest stay placeholder capsules (Bodies.cs).
// A model is kept by the same body while it stays near (so its walk doesn't jump), and models are
// pooled by look: a body leaving hands its model to the next one that needs that look. A few new ones
// are made a frame at most, so a crowd turning up doesn't stall a frame.
//
// Looks, picked by a hash of the body's id (stable, no RNG): the dead get one of the five faces with
// matted hair; the living a clothes set (painted, jacket, hoodie), a hair style and colour; raiders a
// jacket and a buzz cut.
//
// The dormant dead (state.dormant: bare positions till someone comes within Herds.WAKE_RADIUS) are drawn
// too, so they don't pop into being as they wake: most lying still, some standing (a hash of the id), from
// the sim's bucketed index (Dormant.near: the cells round you, not the whole list). When one wakes, the
// real zombie that gets up where it lay takes over its model (same look, same facing): it gets up.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.World3D;

public sealed class People
{
    public const int MAX_MODELS = 40;
    const int MAKES_PER_FRAME = 3;
    /// <summary>Further than this (m), a body stays a capsule even under budget.</summary>
    const float MODEL_RANGE = 40;
    /// <summary>Auto LOD as the look lab's pz ladder: the lowest screen height (px) each LOD is shown at
    /// (about 4 m and 10 m away at 720p).</summary>
    static readonly double[] LOD_AT = { 260, 110, 0 };
    /// <summary>The hair meshes are on the plus ladder (30k ... 600): the pz ladder's 3k / 1.2k / 600 are its 3 / 4 / 5.</summary>
    const int HAIR_LOD_OFFSET = 3;
    const double FOV = 60;

    static readonly string[] ZOMBIE_FACES = { "feral", "feral", "slack", "gaunt", "bloated", "torn" };
    static readonly string[] CLOTHES = { "painted", "jacket", "hoodie" };
    static readonly string[] HAIR = { "buzz", "crop", "sidepart", "long", "receding", "ponytail", "none" };
    static readonly Color[] HAIR_COLOURS =
    {
        new(0.10f, 0.09f, 0.085f), new(0.34f, 0.24f, 0.16f), new(0.78f, 0.64f, 0.42f), new(0.58f, 0.27f, 0.13f), new(0.64f, 0.62f, 0.60f),
    };
    // How far each face's jaw hangs at rest (the look lab's).
    static readonly Dictionary<string, double> SLACK = new() { ["slack"] = 0.12, ["gaunt"] = 0.22, ["bloated"] = 0.06, ["torn"] = 0.18, ["feral"] = 0.32 };

    sealed class Model
    {
        public Node3D root = null!;
        public MeshInstance3D[][] lods = Array.Empty<MeshInstance3D[]>();
        public AnimationPlayer player = null!;
        public string look = "", variant = "", clip = "";
        public string? bodyId;
        public double speed, angry, phase;
        public float yaw;
        public bool placed;
    }

    readonly Node3D parent;
    readonly AnimationLibrary mannequinLib, ualLib;
    readonly Dictionary<string, PackedScene> scenes = new();
    readonly Dictionary<string, StandardMaterial3D> hairMats = new();
    readonly Dictionary<string, List<Model>> free = new();
    readonly Dictionary<string, Model> byBody = new();
    double time;

    public People(Node3D parent)
    {
        this.parent = parent;
        var m = GD.Load<PackedScene>("res://assets/characters/test_mannequin.glb").Instantiate<Node3D>();
        mannequinLib = Find<AnimationPlayer>(m)!.GetAnimationLibrary("");
        m.Free();
        var u = GD.Load<PackedScene>("res://assets/characters/clips_ual.glb").Instantiate<Node3D>();
        ualLib = Find<AnimationPlayer>(u)!.GetAnimationLibrary("");
        u.Free();
        foreach (var n in new[] { "ual_idle", "ual_walk", "ual_jog_fwd", "ual_sprint", "ual_crouch_fwd", "ual_crouch_idle", "ual_driving" })
            if (ualLib.HasAnimation(n)) ualLib.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
        foreach (var n in new[] { "lie_idle", "zombie_crawl", "prone_idle" })
            if (mannequinLib.HasAnimation(n)) mannequinLib.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
    }

    public int Shown => byBody.Count;

    /// <summary>One body this frame: who, what look, where, and how it's moving.</summary>
    readonly record struct Wanted(string id, string look, Body body, PoseIntent pose, double dist2, bool angry, string action);

    /// <summary>Places models for the nearest people; returns the ids drawn, so Bodies leaves them out.</summary>
    public HashSet<string> Update(SimState state, double alpha, Vector3 eye, string youId, double delta, float vpHeight)
    {
        time += delta;
        var wanted = new List<Wanted>();
        float range2 = MODEL_RANGE * MODEL_RANGE;
        void Add(string id, string look, Body b, PoseIntent pose, bool angry, string action)
        {
            var p = worldPos(b, alpha);
            double d2 = (p.x - eye.X) * (p.x - eye.X) + (p.y - eye.Z) * (p.y - eye.Z);
            if (d2 <= range2) wanted.Add(new Wanted(id, look, b, pose, d2, angry, action));
        }
        foreach (var s in state.survivors)
            if (s.id != youId && Vehicles.vehicleOf(state, s) == null) Add(s.id, LivingLook(s.id), s, PoseIntents.ofSurvivor(state, s), false, "");
        foreach (var r in state.residents) Add(r.id, LivingLook(r.id), r, Pose(r.at, r.was), false, "");
        foreach (var z in state.zombies)
        {
            bool raider = z.kind == "raider";
            string action = state.tick < z.staggerUntil ? "hit" : z.windup > 0 ? "attack" : "";
            Add(z.id, raider ? "survivor/jacket/buzz/0" : DeadLook(z.id), z, PoseIntents.ofZombie(state, z), !raider && z.targetId != null, action);
        }
        var dormantNow = new Dictionary<string, Point>();
        foreach (var d in Dormant.near(state, eye.X, eye.Z, MODEL_RANGE, false))
        {
            var b = DormantBody(d);
            dormantNow[d.id] = new Point(d.x, d.y);
            Add(d.id, DeadLook(d.id), b, DormantLies(d.id) ? LYING : STANDING, false, "");
        }
        wanted.Sort((a, b) => a.dist2.CompareTo(b.dist2));
        if (wanted.Count > MAX_MODELS) wanted.RemoveRange(MAX_MODELS, wanted.Count - MAX_MODELS);

        // One that woke: the zombie standing where it lay takes its model over, so it gets up rather than swapping.
        foreach (var (id, at) in lastDormant)
        {
            if (dormantNow.ContainsKey(id) || !byBody.TryGetValue(id, out var m)) continue;
            foreach (var w in wanted)
            {
                if (w.body is not Zombie z || byBody.ContainsKey(z.id) || Math.Max(Math.Abs(z.pos.x - at.x), Math.Abs(z.pos.y - at.y)) > 1) continue;
                byBody.Remove(id);
                m.bodyId = z.id;
                byBody[z.id] = m;
                break;
            }
        }
        lastDormant = dormantNow;
        foreach (var id in dormantBodies.Keys.Where((id) => !dormantNow.ContainsKey(id)).ToList()) dormantBodies.Remove(id);

        // Hand back the models of bodies that left.
        var keep = new HashSet<string>(wanted.Select((w) => w.id));
        foreach (var id in byBody.Keys.Where((id) => !keep.Contains(id)).ToList())
        {
            var m = byBody[id];
            byBody.Remove(id);
            m.bodyId = null;
            m.root.Visible = false;
            Free(m.look).Add(m);
        }

        var drawn = new HashSet<string>();
        int made = 0;
        foreach (var w in wanted)
        {
            if (!byBody.TryGetValue(w.id, out var m))
            {
                var pool = Free(w.look);
                if (pool.Count > 0) { m = pool[^1]; pool.RemoveAt(pool.Count - 1); }
                else if (made < MAKES_PER_FRAME) { m = Make(w.look); made++; }
                else continue; // a capsule this frame
                m.bodyId = w.id;
                m.placed = false;
                m.clip = "";
                m.phase = Hash01(w.id, 3);
                m.root.Visible = true;
                byBody[w.id] = m;
            }
            Pose(m, w, alpha, delta, Math.Sqrt(w.dist2), vpHeight);
            drawn.Add(w.id);
        }
        return drawn;
    }

    // --- you: your own body under the camera (OwnBody.cs) ---

    /// <summary>How far behind the eye the body stands (m), so looking down the camera is over the chest, not in the neck.</summary>
    const float OWN_BACK = 0.22f;
    /// <summary>Most the chest turns from the legs (radians), and how far you look round before the legs follow, standing.</summary>
    const float OWN_TWIST = 1.2f, OWN_TURN_AT = 0.9f;
    Model? own, ownShadow;
    OwnBody? ownMod;
    string ownLook = "";
    float ownYaw = float.NaN;
    double ownHeading;

    /// <summary>Your own body: shown standing or sneaking, posed like everyone's, its legs the way you walk and its
    /// chest the way you look (camYaw, FirstPerson's: the look along (cos, sin) on the grid); its head hidden unless
    /// you look from outside (fromOutside: --eye).</summary>
    public void UpdateYou(SimState state, Survivor you, double alpha, double camYaw, double delta, bool show, bool fromOutside, bool rightHeld = false, bool leftHeld = false)
    {
        var pose = PoseIntents.ofSurvivor(state, you);
        show &= pose.stance is "stand" or "sneak";
        string look = LivingLook(you.id);
        if (show && look != ownLook)
        {
            own?.root.QueueFree();
            ownShadow?.root.QueueFree();
            own = Make(look);
            ownShadow = Make(look);
            foreach (var mi in FindAll<MeshInstance3D>(own.root)) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            foreach (var mi in FindAll<MeshInstance3D>(ownShadow.root)) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
            ownMod = new OwnBody();
            Find<Skeleton3D>(own.root)!.AddChild(ownMod);
            own.phase = ownShadow.phase = 0;
            ownLook = look;
            ownYaw = float.NaN;
        }
        if (own == null || ownShadow == null || ownMod == null) return;
        own.root.Visible = ownShadow.root.Visible = show;
        if (!show) return;

        // The legs: the way you walk (backing off: facing you, the walk run backwards); standing, they follow
        // the look once it's turned far enough. The chest makes up the rest, up to OWN_TWIST.
        float lookYaw = (float)Math.Atan2(Math.Cos(camYaw), Math.Sin(camYaw));
        var w = new Wanted(you.id, look, you, pose, 0, false, "");
        Pose(own, w, alpha, delta, 0.5, 720);
        Pose(ownShadow, w, alpha, delta, 0.5, 720);
        bool moving = own.speed > 0.3;
        bool back = false;
        float want;
        // The way you walk: the last heading you moved along (a tick that didn't move you says 0, which isn't a way).
        if (pose.speed > 0.15) ownHeading = pose.heading;
        if (moving)
        {
            float walk = (float)Math.Atan2(Math.Cos(ownHeading), Math.Sin(ownHeading));
            back = Math.Abs(Mathf.AngleDifference(lookYaw, walk)) > 1.75f;
            float legs = back ? walk + Mathf.Pi : walk;
            // The legs turn at most OWN_TWIST from the look (the chest makes up the rest): strafing walks them half
            // sideways rather than crossing the chest over.
            want = lookYaw + Math.Clamp(Mathf.AngleDifference(lookYaw, legs), -OWN_TWIST, OWN_TWIST);
        }
        else want = float.IsNaN(ownYaw) || Math.Abs(Mathf.AngleDifference(ownYaw, lookYaw)) > OWN_TURN_AT ? lookYaw : ownYaw;
        ownYaw = float.IsNaN(ownYaw) ? want : Mathf.LerpAngle(ownYaw, want, (float)Math.Min(1, delta * 8));
        float twist = Math.Clamp(Mathf.AngleDifference(ownYaw, lookYaw), -OWN_TWIST, OWN_TWIST);
        ownMod.Twist = twist;
        ownMod.HideHead = !fromOutside;
        ownMod.HideRightArm = rightHeld && !fromOutside;
        ownMod.HideLeftArm = leftHeld && !fromOutside;
        var behind = new Vector3((float)-Math.Cos(camYaw), 0, (float)-Math.Sin(camYaw)) * (fromOutside ? 0 : OWN_BACK);
        foreach (var m in new[] { own, ownShadow })
        {
            m.yaw = ownYaw;
            m.root.Rotation = new Vector3(0, ownYaw, 0);
            m.root.Position += behind;
            if (back) m.player.SpeedScale = -Math.Abs(m.player.SpeedScale);
        }
    }

    // --- the dormant dead ---

    static readonly PoseIntent LYING = new() { stance = "lie" }, STANDING = new();
    Dictionary<string, Point> lastDormant = new();
    readonly Dictionary<string, Zombie> dormantBodies = new();

    /// <summary>Most of the dormant lie where they fell; some stand (a hash of the id, stable between runs).</summary>
    public static bool DormantLies(string id) => Hash01(id, 17) < 0.65;

    /// <summary>A stand-in body (not in state) for a dormant one, so it's placed and posed like the rest.</summary>
    Zombie DormantBody(DormantZombie d)
    {
        if (!dormantBodies.TryGetValue(d.id, out var z))
        {
            var placed = Motion.placedAt(new Point(d.x, d.y));
            dormantBodies[d.id] = z = new Zombie { id = d.id, pos = new Point(d.x, d.y), at = placed.at, was = placed.was };
        }
        return z;
    }

    List<Model> Free(string look)
    {
        if (!free.TryGetValue(look, out var l)) free[look] = l = new();
        return l;
    }

    static PoseIntent Pose(Point at, Point was)
    {
        double dx = at.x - was.x, dy = at.y - was.y;
        return new PoseIntent { speed = Math.Sqrt(dx * dx + dy * dy) / Motion.SUB * PoseIntents.TICKS_PER_SECOND, heading = Math.Atan2(dy, dx) };
    }

    // --- looks ---

    static double Hash01(string id, int salt)
    {
        uint h = 2166136261;
        foreach (char c in id) h = (h ^ c) * 16777619;
        h = (h ^ (uint)salt) * 16777619;
        h ^= h >> 13;
        return (h % 10007) / 10007.0;
    }

    static string Pick(string[] from, string id, int salt) => from[(int)(Hash01(id, salt) * from.Length) % from.Length];

    static string DeadLook(string id) => $"zombie/{Pick(ZOMBIE_FACES, id, 1)}/matted/4";

    static string LivingLook(string id) =>
        $"survivor/{Pick(CLOTHES, id, 1)}/{Pick(HAIR, id, 2)}/{(int)(Hash01(id, 4) * HAIR_COLOURS.Length) % HAIR_COLOURS.Length}";

    PackedScene Scene(string path)
    {
        if (!scenes.TryGetValue(path, out var s)) scenes[path] = s = GD.Load<PackedScene>(path);
        return s;
    }

    /// <summary>A new model of a look: "subject/variant/hair/colour".</summary>
    Model Make(string look)
    {
        var parts = look.Split('/');
        string subject = parts[0], variant = parts[1], hair = parts[2];
        int colour = int.Parse(parts[3]);
        string plain = $"res://assets/characters/look/look_pz_{subject}.glb";
        string path = variant is "feral" or "painted" ? plain : plain.Replace(".glb", $"_{variant}.glb");
        if (!ResourceLoader.Exists(path)) path = plain;
        var root = Scene(path).Instantiate<Node3D>();
        parent.AddChild(root);
        var m = new Model { root = root, look = look, variant = variant };
        var groups = FindAll<MeshInstance3D>(root).GroupBy((mi) => LodIndex(mi.Name)).OrderBy((g) => g.Key).Select((g) => g.ToList()).ToList();
        if (hair != "none") AttachHair(root, groups, hair, colour);
        m.lods = groups.Select((g) => g.ToArray()).ToArray();
        var player = new AnimationPlayer();
        root.AddChild(player); // its root node ".." is the instance, where "Armature/Skeleton3D:..." starts
        player.AddAnimationLibrary("", mannequinLib);
        player.AddAnimationLibrary("ual", ualLib);
        m.player = player;
        return m;
    }

    void AttachHair(Node3D root, List<List<MeshInstance3D>> groups, string style, int colour)
    {
        string path = $"res://assets/characters/look/hair/hair_{style}.glb";
        if (!ResourceLoader.Exists(path)) return;
        var skel = Find<Skeleton3D>(root);
        if (skel == null) return;
        var scene = Scene(path).Instantiate<Node3D>();
        foreach (var mi in FindAll<MeshInstance3D>(scene).ToList())
        {
            int lod = LodIndex(mi.Name) - HAIR_LOD_OFFSET;
            if (lod < 0) continue;  // the plus ladder's top levels (and their strand cards): not drawn
            mi.GetParent().RemoveChild(mi);
            mi.Owner = null;
            skel.AddChild(mi);
            mi.Skeleton = new NodePath("..");
            mi.MaterialOverride = HairMaterial(mi, colour);
            groups[Math.Min(lod, groups.Count - 1)].Add(mi);
        }
        scene.QueueFree();
    }

    StandardMaterial3D HairMaterial(MeshInstance3D mi, int colour)
    {
        var src = (BaseMaterial3D)mi.Mesh.SurfaceGetMaterial(0);
        string key = src.GetInstanceId() + "/" + colour;
        if (!hairMats.TryGetValue(key, out var mat))
        {
            mat = (StandardMaterial3D)src.Duplicate();
            mat.AlbedoColor = HAIR_COLOURS[colour];
            mat.Roughness = 0.6f;
            hairMats[key] = mat;
        }
        return mat;
    }

    // --- each frame ---

    void Pose(Model m, Wanted w, double alpha, double delta, double dist, float vpHeight)
    {
        var p = worldPos(w.body, alpha);
        m.root.Position = new Vector3((float)p.x, Terrain.Height((float)p.x, (float)p.y), (float)p.y);
        bool dead = w.look.StartsWith("zombie");
        // Speed eased, so a tick without a step doesn't flick the walk to idle and back.
        m.speed += (w.pose.speed - m.speed) * Math.Min(1, delta * 8);
        // The model's front is +Z: a heading (cos h, sin h) on the grid's (x, z) is a turn of atan2(cos h, sin h).
        if (w.pose.speed > 0.15)
        {
            float want = (float)Math.Atan2(Math.Cos(w.pose.heading), Math.Sin(w.pose.heading));
            m.yaw = m.placed ? Mathf.LerpAngle(m.yaw, want, (float)Math.Min(1, delta * 10)) : want;
        }
        else if (!m.placed) m.yaw = (float)(Hash01(w.id, 9) * Math.PI * 2);
        m.placed = true;
        m.root.Rotation = new Vector3(0, m.yaw, 0);

        string stance = w.pose.stance;
        (string clip, float rate) = stance switch
        {
            "lie" or "dead" => ("lie_idle", 1f),
            "crawl" => ("zombie_crawl", 1f),
            "sit" => ("ual/ual_driving", 1f),
            _ when w.action == "attack" => ("ual/ual_punch_cross", 1.2f),
            _ when w.action == "hit" => ("ual/ual_hit_chest", 1f),
            _ when m.speed < 0.2 => ("ual/ual_idle", dead ? 0.6f : 1f),
            _ when stance == "sneak" => ("ual/ual_crouch_fwd", Rate(m.speed, 1.0)),
            _ when m.speed < 2.2 || dead => ("ual/ual_walk", Rate(m.speed, dead ? 1.0 : 1.4)),
            _ when m.speed < 3.8 => ("ual/ual_jog_fwd", Rate(m.speed, 3.0)),
            _ => ("ual/ual_sprint", Rate(m.speed, 5.0)),
        };
        if (!m.player.HasAnimation(clip)) clip = "ual/ual_idle";
        if (clip != m.clip)
        {
            bool first = m.clip == "";
            m.player.Play(clip, first ? 0 : 0.25);
            if (first) m.player.Seek(m.phase * m.player.CurrentAnimationLength, true);
            m.clip = clip;
        }
        m.player.SpeedScale = rate;

        // LOD by height on screen, as the look lab.
        double h = vpHeight * 1.75 / (2 * Math.Max(dist, 0.1) * Math.Tan(Mathf.DegToRad(FOV / 2)));
        int lod = Array.FindIndex(LOD_AT, (t) => h >= t);
        if (lod < 0) lod = LOD_AT.Length - 1;
        lod = Math.Min(lod, m.lods.Length - 1);
        for (int i = 0; i < m.lods.Length; i++)
            foreach (var piece in m.lods[i]) piece.Visible = i == lod;

        // The dead's face: angry once it's after someone, eased in over a quarter second.
        if (!dead) return;
        m.angry = Math.Clamp(m.angry + (w.angry ? 1 : -1) * delta / 0.25, 0, 1);
        var face = m.lods[lod].FirstOrDefault((mi) => mi.FindBlendShapeByName("angry") >= 0);
        if (face == null) return;
        double e = m.angry * m.angry * (3 - 2 * m.angry);
        double slack = SLACK.GetValueOrDefault(m.variant, 0.12) + 0.03 * Math.Sin(time * 1.3 + m.phase * 6);
        double growl = 0.75 + 0.20 * Math.Sin(time * 7.5 + m.phase * 6);
        Shape(face, "jaw_open", slack + (growl - slack) * e);
        Shape(face, "angry", e);
    }

    /// <summary>A clip's playback rate for a speed, against the speed it was made at.</summary>
    static float Rate(double speed, double clipSpeed) => (float)Math.Clamp(speed / clipSpeed, 0.5, 1.6);

    static void Shape(MeshInstance3D mi, string name, double v)
    {
        int i = mi.FindBlendShapeByName(name);
        if (i >= 0) mi.SetBlendShapeValue(i, (float)v);
    }

    static int LodIndex(StringName name)
    {
        var s = name.ToString();
        int i = s.LastIndexOf("lod", StringComparison.Ordinal);
        return i >= 0 && int.TryParse(s[(i + 3)..], out var n) ? n : 0;
    }

    static T? Find<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
            if (Find<T>(c) is T f) return f;
        return null;
    }

    static IEnumerable<T> FindAll<T>(Node n) where T : Node
    {
        if (n is T t) yield return t;
        foreach (var c in n.GetChildren())
            foreach (var f in FindAll<T>(c)) yield return f;
    }
}
