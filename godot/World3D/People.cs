// People in first person: survivors, residents, raiders, the dead and Others.cs's stand-ins (wanderers, traders,
// prisoners, caravans, scavenging parties) near you as the "pz" character
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
// Looks, picked by a hash of the body's id (stable, no RNG, nothing saved: Characters/Looks.cs): the dead an
// outfit (the tee with one of the five faces, a long-sleeved shirt, a jacket, a hoodie) in a top and trousers
// of their own colours; the living a clothes set (painted, jacket, hoodie); raiders a jacket and a buzz cut.
// Hair, beards, hats and the dead's colours are the dress, put on a model apart from its body: models are
// pooled by body (and lost limbs), and one handed to a body that wears something else is re-dressed, a few a
// frame. The colours are shared materials per file and colour pair (no instance uniforms). Lost limbs (the pose intent's lostLimbs: a survivor's amputation, a crawler's legs) are
// part of the look: the model is cut and capped at the stump (Characters/StumpCut.cs).
//
// The dormant dead (state.dormant: bare positions till someone comes within Herds.WAKE_RADIUS) are drawn
// too, so they don't pop into being as they wake: most lying still, some standing (a hash of the id), from
// the sim's bucketed index (Dormant.near: the cells round you, not the whole list). When one wakes, the
// real zombie that gets up where it lay takes over its model (same look, same facing): it gets up.
//
// The class in parts: People.cs (this: which bodies get models, their looks and LOD, the dormant dead, footfalls),
// People.Clips.cs (choosing each body's clip), People.You.cs (your own body), People.Leap.cs (your leaps),
// People.Climb.cs (the dead over cars and barricades), People.Hold.cs (what a body holds), People.Swim.cs (your body
// swimming, seen from outside).

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LoonieRoo.Game.Characters;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;
using static LoonieRoo.Game.Nodes;
using static LoonieRoo.Game.IdHash;

namespace LoonieRoo.Game.World3D;

public sealed partial class People
{
    /// <summary>The options' Character models (Ui/Graphics.cs).</summary>
    public static int MAX_MODELS = 40;
    const int MAKES_PER_FRAME = 3, DRESSES_PER_FRAME = 8;
    /// <summary>Further than this (m), a body stays a capsule even under budget.</summary>
    const float MODEL_RANGE = 40;
    /// <summary>Auto LOD as the look lab's pz ladder: the lowest screen height (px) each LOD is shown at
    /// (about 4 m and 10 m away at 720p).</summary>
    static readonly double[] LOD_AT = { 260, 110, 0 };
    const double FOV = 60;

    static readonly Looks.Outfit RAIDER = new(new Looks.Hair("buzz", 0, "none", 0, false, false), -1, -1);
    // How far each face's jaw hangs at rest (the look lab's).
    static readonly Dictionary<string, double> SLACK = new() { ["slack"] = 0.12, ["gaunt"] = 0.22, ["bloated"] = 0.06, ["torn"] = 0.18, ["feral"] = 0.32 };

    sealed class Model
    {
        public Node3D root = null!;
        public MeshInstance3D[][] lods = Array.Empty<MeshInstance3D[]>();   // the body's and the dress's, by LOD
        public MeshInstance3D[][] body = Array.Empty<MeshInstance3D[]>();   // the body's own pieces, by LOD
        public readonly List<MeshInstance3D> dressed = new();               // the hair, beard and hat on it
        public string dress = "", path = "";
        public AnimationPlayer player = null!;
        public string look = "", variant = "", clip = "";
        public string? bodyId;
        public double speed, angry, phase;
        public float yaw;
        public bool placed;
        /// <summary>A clip that overrides the stance's (a leap's: People.Leap.cs), and where in it (s; below 0: played on).</summary>
        public string? forced;
        public double forcedAt;
        /// <summary>Swimming: the swim clip it plays instead of the stance's (People.Swim.cs), and at what rate.</summary>
        public string? swim;
        /// <summary>Over a car: its hands on it (People.Climb.cs), the part of the leg it is in, and whether its crawl wants its own phase.</summary>
        public ClimbHands? climb;
        public string climbPhase = "";
        public bool climbSeek;
        public float swimRate = 1;
        public Skeleton3D? skel;
        /// <summary>What it holds, drawn in its hand (People.Hold.cs).</summary>
        public string heldKind = "";
        public MeshInstance3D? holdMesh;
        public bool holdLoad;
        /// <summary>How fast it turns (rad/s, smoothed): standing, a quick turn steps round.</summary>
        public double turn, since, riseAt;
        /// <summary>The get-up clip of one of the dead that woke where it lay, while it plays.</summary>
        public string? rise;
        /// <summary>A one-shot over the stance's clip (a hit, the dead's lunge, a flinch), how far in (s), how long.</summary>
        public string? shot;
        public double shotAt, shotFor;
        /// <summary>What the sim said of this body last: when it was hurt (tick), its last stagger, whether it was winding up a
        /// bite or startled, the side the last blow came from; and (once its body has gone) how long its death has left.</summary>
        public double lastHurt = -1, lastStagger, dying;
        public bool attacking, startled, wasZombie;
        public string lastSide = "front";
        /// <summary>The near tier's foot IK (made once it first comes near), and whether it's on now.</summary>
        public FootIK? ik;
        public bool ikOn, own;
        /// <summary>The near dead's head turned to its target (made when first needed), eased in and out.</summary>
        public HeadLook? headLook;
        public double lookW;
        public readonly int[] feet = { -1, -1 }, hands = { -1, -1 };
        public readonly Footfalls.Watch watch = new();
    }

    readonly Node3D parent;
    readonly AnimationLibrary mannequinLib, ualLib, moreLib;
    readonly Dictionary<string, PackedScene> scenes = new();
    readonly Dictionary<string, List<Model>> free = new();
    readonly Dictionary<string, Model> byBody = new();
    readonly Dictionary<string, Footfalls.Marks> marks;
    readonly List<int> crossed = new();
    double time;

    /// <summary>The feet that came down this frame (Update clears it, UpdateYou adds yours): Sounds plays them.</summary>
    public readonly List<Footfall> Steps = new();
    /// <summary>Your last steps, for the self-test and STEP_TRACE: the clip and the playhead (s), the foot, the clip's
    /// ankle over its lowest there (m), and the drawn ankles over your feet's ground, this foot and the other (m).</summary>
    public readonly List<YourStep> YourSteps = new();
    public readonly record struct YourStep(ulong ms, string clip, double at, int foot, float clipH, float drawnH, float otherH);

    public People(Node3D parent)
    {
        this.parent = parent;
        var m = GD.Load<PackedScene>("res://assets/characters/test_mannequin.glb").Instantiate<Node3D>();
        mannequinLib = Find<AnimationPlayer>(m)!.GetAnimationLibrary("");
        m.Free();
        var u = GD.Load<PackedScene>("res://assets/characters/clips_ual.glb").Instantiate<Node3D>();
        ualLib = Find<AnimationPlayer>(u)!.GetAnimationLibrary("");
        marks = Footfalls.Find(ualLib, u, Find<Skeleton3D>(u)!);
        u.Free();
        var more = GD.Load<PackedScene>("res://assets/characters/clips_more.glb").Instantiate<Node3D>();
        moreLib = Find<AnimationPlayer>(more)!.GetAnimationLibrary("");
        var moreSkel = Find<Skeleton3D>(more)!;
        Gaits.Measure(moreLib, "more/", moreSkel, Gaits.ToScene(more, moreSkel));
        foreach (var (k, mk) in Footfalls.Find(moreLib, more, moreSkel, "more/")) marks[k] = mk;
        foreach (var name in moreLib.GetAnimationList())
            if (Gaits.Looping(name)) moreLib.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
        more.Free();
        foreach (var n in new[] { "ual_idle", "ual_walk", "ual_jog_fwd", "ual_sprint", "ual_crouch_fwd", "ual_crouch_idle", "ual_driving" })
            if (ualLib.HasAnimation(n)) ualLib.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
        foreach (var n in new[] { "lie_idle", "zombie_crawl", "prone_idle" })
            if (mannequinLib.HasAnimation(n)) mannequinLib.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
    }

    public int Shown => byBody.Count;
    readonly List<Model> dying = new();
    /// <summary>How long one of the dead takes to fall when it's killed (s), before the corpse the sim left is all there is.</summary>
    const double DEATH_FOR = 2.4;

    /// <summary>One body this frame: who, what look, where, and how it's moving.</summary>
    readonly record struct Wanted(string id, string look, Looks.Outfit dress, Body body, PoseIntent pose, double dist2, bool angry, string action);

    /// <summary>Places models for the nearest people; returns the ids drawn, so Bodies leaves them out.</summary>
    public HashSet<string> Update(SimState state, double alpha, Vector3 eye, string youId, double delta, float vpHeight, IReadOnlyList<Others.StandIn> others)
    {
        time += delta;
        Steps.Clear();
        iks = 0;
        climbHands = 0;
        var wanted = new List<Wanted>();
        float range2 = MODEL_RANGE * MODEL_RANGE;
        void Add(string id, string look, Looks.Outfit dress, Body b, PoseIntent pose, bool angry, string action)
        {
            var p = worldPos(b, alpha);
            double d2 = (p.x - eye.X) * (p.x - eye.X) + (p.y - eye.Z) * (p.y - eye.Z);
            if (d2 <= range2) wanted.Add(new Wanted(id, look, dress, b, pose, d2, angry, action));
        }
        foreach (var s in state.survivors)
            if (s.id != youId && Vehicles.vehicleOf(state, s) == null)
            {
                var pose = PoseIntents.ofSurvivor(state, s);
                Add(s.id, LivingLook(s.id) + Lost(pose), Looks.LivingOutfit(s.id), s, pose, false, "");
            }
        foreach (var r in state.residents) Add(r.id, LivingLook(r.id), Looks.LivingOutfit(r.id), r, Pose(r.at, r.was), false, "");
        // Those the sim keeps only as a tile (wanderers, traders, prisoners, caravans, runs): Others.cs's stand-ins.
        foreach (var o in others) Add(o.id, LivingLook(o.id), Looks.LivingOutfit(o.id), o.body, o.pose, false, "");
        foreach (var z in state.zombies)
        {
            bool raider = z.kind == "raider";
            string action = state.tick < z.staggerUntil ? "hit" : z.windup > 0 ? "attack" : "";
            var zp = PoseIntents.ofZombie(state, z);
            zp.lift = Crossings.lift(state, z, alpha); // over a car or a barricade: at this frame, not the tick's end (People.Climb.cs)
            Add(z.id, (raider ? "survivor/jacket" : DeadLook(z.id)) + Lost(zp), raider ? RAIDER : Looks.DeadOutfit(z.id), z, zp, !raider && z.targetId != null, action);
        }
        var dormantNow = new Dictionary<string, Point>();
        foreach (var d in Dormant.near(state, eye.X, eye.Z, MODEL_RANGE, false))
        {
            var b = DormantBody(d);
            dormantNow[d.id] = new Point(d.x, d.y);
            Add(d.id, DeadLook(d.id), Looks.DeadOutfit(d.id), b, DormantLies(d.id) ? LYING : STANDING, false, "");
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
                if (DormantLies(id) && z.variant != "crawler") { m.rise = DormantFront(id) ? "more/dead_rise_front" : "more/dead_rise_back"; m.riseAt = 0; }
                break;
            }
        }
        lastDormant = dormantNow;
        foreach (var id in dormantBodies.Keys.Where((id) => !dormantNow.ContainsKey(id)).ToList()) dormantBodies.Remove(id);

        // Hand back the models of bodies that left; one of the dead killed (hurt as it went: the sim removes it and
        // leaves a corpse at once) falls first, where it stood (DEATH_FOR s: backwards, or on its face if struck from behind).
        var keep = new HashSet<string>(wanted.Select((w) => w.id));
        foreach (var id in byBody.Keys.Where((id) => !keep.Contains(id)).ToList())
        {
            var m = byBody[id];
            byBody.Remove(id);
            m.bodyId = null;
            if (m.wasZombie && m.lastHurt >= state.tick - 3)
            {
                m.dying = DEATH_FOR;
                m.shot = null;
                if (m.ik != null && m.ikOn) m.ik.Enabled = m.ikOn = false;
                Play(m, m.lastSide == "back" ? "more/dead_fall_front" : "ual/ual_death01", 1, 0.1);
                dying.Add(m);
                continue;
            }
            m.root.Visible = false;
            Free(m.look).Add(m);
        }
        for (int i = dying.Count - 1; i >= 0; i--)
        {
            var m = dying[i];
            m.dying -= delta;
            m.player.Advance(delta);
            if (m.dying > 0) continue;
            dying.RemoveAt(i);
            m.root.Visible = false;
            m.wasZombie = false;
            m.lastHurt = -1;
            Free(m.look).Add(m);
        }

        var drawn = new HashSet<string>();
        int made = 0, redressed = 0;
        Holding = 0;
        foreach (var w in wanted)
        {
            if (!byBody.TryGetValue(w.id, out var m))
            {
                var pool = Free(w.look);
                // A free model of this body, best one already dressed the same (no re-dress).
                string dress = w.dress.ToString();
                int pick = pool.FindLastIndex((x) => x.dress == dress);
                if (pick < 0 && pool.Count > 0 && redressed < DRESSES_PER_FRAME) { pick = pool.Count - 1; redressed++; }
                if (pick >= 0) { m = pool[pick]; pool.RemoveAt(pick); }
                else if (made < MAKES_PER_FRAME) { m = Make(w.look); made++; }
                else continue; // a capsule this frame
                Dress(m, w.dress);
                m.bodyId = w.id;
                m.placed = false;
                m.clip = "";
                m.phase = Hash01(w.id, 3);
                m.turn = 0;
                m.rise = null;
                m.shot = null;
                m.attacking = m.startled = false;
                m.lastStagger = 0;
                if (m.ik != null && m.ikOn) m.ik.Enabled = m.ikOn = false;  // a new body: nothing held from the last
                m.watch.Reset();
                m.root.Visible = true;
                byBody[w.id] = m;
            }
            if (w.body is Zombie zb && !dormantNow.ContainsKey(w.id)) { m.wasZombie = true; m.lastHurt = zb.hurtTick; }
            else m.wasZombie = false;
            CrossPre(m, w, state, alpha);
            Pose(m, w, alpha, delta, Math.Sqrt(w.dist2), vpHeight);
            Hold(m, w);
            CrossPost(m, w);
            m.player.Advance(delta);
            Footfall(m, w.id, false, w.look.StartsWith("zombie"), delta);
            drawn.Add(w.id);
        }
        return drawn;
    }

    /// <summary>The feet of model m that came down since the last look (its clip's marks crossed): into Steps.</summary>
    void Footfall(Model m, string id, bool you, bool dead, double delta)
    {
        string clip = m.player.CurrentAnimation;
        double at = m.player.CurrentAnimationPosition;
        var mk = marks.GetValueOrDefault(clip);
        m.watch.Crossed(clip, at, delta, mk, crossed);
        if (crossed.Count == 0) return;
        if (!Bones(m)) return;
        var limbs = Footfalls.ON_HANDS.Contains(Footfalls.GAITS[clip]) ? m.hands : m.feet;
        var xf = m.skel!.GlobalTransform;
        foreach (int foot in crossed)
        {
            if (limbs[foot] < 0) continue;
            var ankle = xf * m.skel.GetBoneGlobalPose(limbs[foot]).Origin;
            Steps.Add(new Footfall(id, you, dead, ankle, Footfalls.GAITS[clip], foot));
            if (!you) continue;
            var other = xf * m.skel.GetBoneGlobalPose(m.feet[1 - foot]).Origin;
            float ground = m.root.GlobalPosition.Y;
            var step = new YourStep(Godot.Time.GetTicksMsec(), clip, at, foot, mk!.HeightAt(foot, at), ankle.Y - ground, other.Y - ground);
            YourSteps.Add(step);
            if (YourSteps.Count > 64) YourSteps.RemoveAt(0);
            // STEP_SHOT=prefix: a picture at your 4th to 6th steps (with --eye to watch yourself), to see the foot down.
            if (OS.GetEnvironment("STEP_SHOT") is { Length: > 0 } shot && YourSteps.Count is >= 4 and <= 6)
                parent.GetViewport().GetTexture().GetImage().SavePng($"{shot}-{YourSteps.Count}.png");
            if (OS.GetEnvironment("STEP_TRACE") != "")
                GD.Print($"step: {step.ms} ms {clip} at {at:0.000}/{mk.length:0.000} rate {m.player.SpeedScale:0.00} {(foot == 0 ? "L" : "R")} clip ankle +{step.clipH * 100:0.0} cm, drawn ankle {step.drawnH * 100:0.0} cm, other {step.otherH * 100:0.0} cm");
        }
    }

    // --- the dormant dead ---

    static readonly PoseIntent LYING = new() { stance = "lie" }, STANDING = new();
    Dictionary<string, Point> lastDormant = new();
    readonly Dictionary<string, Zombie> dormantBodies = new();

    /// <summary>Most of the dormant lie where they fell; some stand (a hash of the id, stable between runs).</summary>
    public static bool DormantLies(string id) => Hash01(id, 17) < 0.65;

    /// <summary>Of those lying, some lie face down (a hash of the id).</summary>
    public static bool DormantFront(string id) => Hash01(id, 21) < 0.4;

    /// <summary>How long the dead take to get up off the ground (the rise clips' length, s).</summary>
    const double RISE_FOR = 1.6;

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

    static string Pick(string[] from, string id, int salt) => Looks.Pick(from, id, salt);

    /// <summary>A look's lost limbs (the pose intent's lostLimbs): "|armLeft:elbow,legRight:knee", or "" for none.
    /// Part of the look, so a model cut for one stays with the looks cut the same (StumpCut.cs).</summary>
    static string Lost(PoseIntent pose) => pose.lostLimbs.Count == 0 ? "" : "|" + string.Join(",", pose.lostLimbs);

    /// <summary>A body: "subject/variant" (the model file), then any lost limbs. The hair is the dress (Looks).</summary>
    static string DeadLook(string id) => $"zombie/{Looks.DeadBody(id)}";

    static string LivingLook(string id) => $"survivor/{Looks.LivingClothes(id)}";

    PackedScene Scene(string path)
    {
        if (!scenes.TryGetValue(path, out var s)) scenes[path] = s = GD.Load<PackedScene>(path);
        return s;
    }

    /// <summary>A new model of a body: "subject/variant[|lost limbs]", undressed (Dress puts the hair on).</summary>
    Model Make(string look)
    {
        var cut = look.Split('|');
        var parts = cut[0].Split('/');
        string subject = parts[0], variant = parts[1];
        string plain = $"res://assets/characters/look/look_pz_{subject}.glb";
        string path = variant is "feral" or "painted" ? plain : plain.Replace(".glb", $"_{variant}.glb");
        if (!ResourceLoader.Exists(path)) path = plain;
        var root = Scene(path).Instantiate<Node3D>();
        parent.AddChild(root);
        // Lost limbs: the body and its clothes cut and capped (a pinned sleeve, a knotted trouser leg; the dead's torn), before the hair goes on.
        if (cut.Length > 1) StumpCut.Apply(root, cut[1].Split(','), subject == "zombie");
        // An arm lost: its stump held close, whatever the clip does with that arm (StumpHold.cs).
        if (cut.Length > 1 && cut[1].Contains("arm") && Find<Skeleton3D>(root) is Skeleton3D sk)
            sk.AddChild(new StumpHold { Name = "StumpHold", Left = cut[1].Contains("armLeft"), Right = cut[1].Contains("armRight") });
        var m = new Model { root = root, look = look, variant = Looks.FaceOf(variant), path = path };
        m.body = m.lods = FindAll<MeshInstance3D>(root).GroupBy((mi) => LodIndex(mi.Name)).OrderBy((g) => g.Key).Select((g) => g.ToArray()).ToArray();
        // Played on the world's time (Advance each frame with Update's delta: 0 while the world is held), not the frame's.
        var player = new AnimationPlayer { CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual };
        root.AddChild(player); // its root node ".." is the instance, where "Armature/Skeleton3D:..." starts
        player.AddAnimationLibrary("", mannequinLib);
        player.AddAnimationLibrary("ual", ualLib);
        player.AddAnimationLibrary("more", moreLib);
        m.player = player;
        return m;
    }

    /// <summary>Puts a person's hair, beard and hat on model m (Looks.Attach), taking off what it wore, and
    /// the colours of the dead's clothes (Looks.Recolour).</summary>
    static void Dress(Model m, Looks.Outfit outfit)
    {
        string key = outfit.ToString();
        if (m.dress == key) return;
        Looks.Recolour(m.body.SelectMany((g) => g), m.path, outfit.top, outfit.bottom);
        var hair = outfit.hair;
        foreach (var mi in m.dressed) { mi.GetParent()?.RemoveChild(mi); mi.QueueFree(); }
        m.dressed.Clear();
        var skel = Find<Skeleton3D>(m.root);
        if (skel == null) return;
        var groups = Looks.Attach(skel, hair, m.body.Length, Looks.HAIR_LOD_OFFSET);
        m.lods = m.body.Select((g, i) => g.Concat(groups[i]).ToArray()).ToArray();
        foreach (var g in groups) m.dressed.AddRange(g);
        m.dress = key;
    }

    // --- each frame ---

    void Pose(Model m, Wanted w, double alpha, double delta, double dist, float vpHeight)
    {
        var p = worldPos(w.body, alpha);
        m.root.Position = new Vector3((float)p.x, Terrain.Height((float)p.x, (float)p.y), (float)p.y);
        bool dead = w.look.StartsWith("zombie");
        // The model's front is +Z: a heading (cos h, sin h) on the grid's (x, z) is a turn of atan2(cos h, sin h).
        float was = m.yaw;
        bool placed = m.placed;
        if (w.pose.speed > 0.15 && m.rise == null && m.shot == null)
        {
            float want = (float)Math.Atan2(Math.Cos(w.pose.heading), Math.Sin(w.pose.heading));
            m.yaw = m.placed ? Mathf.LerpAngle(m.yaw, want, (float)Math.Min(1, delta * 10)) : want;
        }
        else if (!m.placed) m.yaw = (float)(Hash01(w.id, 9) * Math.PI * 2);
        m.placed = true;
        m.root.Rotation = new Vector3(0, m.yaw, 0);
        if (!m.own && placed && delta > 0) Turning(m, Mathf.AngleDifference(was, m.yaw) / delta, delta);

        string stance = w.pose.stance;
        if (m.forced != null) ForcedPlay(m);
        else if (m.swim != null) Play(m, m.swim, m.swimRate, 0.3);
        else Clip(m, w, stance, dead, delta);
        int lod = Lod(m, dist, vpHeight, w, dead, delta);
        if (dead) Look(m, w, lod == 0 && dist < LOOK_RANGE && m.rise == null, delta);
        FeetOnGround(m, (m.own || (lod == 0 && dist < IK_RANGE && iks < IK_MAX)) && m.forced == null && m.swim == null && stance is "stand" or "sneak" && OneLeg(w.pose) == ' ');
    }

    /// <summary>Model m's skeleton and the bones its footfalls are read from, found once; false if it has no skeleton.</summary>
    static bool Bones(Model m)
    {
        m.skel ??= Find<Skeleton3D>(m.root);
        if (m.skel == null) return false;
        if (m.feet[0] < 0)
        {
            m.feet[0] = m.skel.FindBone("LeftFoot");
            m.feet[1] = m.skel.FindBone("RightFoot");
            m.hands[0] = m.skel.FindBone("LeftHand");
            m.hands[1] = m.skel.FindBone("RightHand");
        }
        return true;
    }

    /// <summary>The foot IK (FootIK.cs: feet on the ground's slope, a planted foot held where it landed) for you and
    /// the nearest few (within IK_RANGE, at the near LOD, IK_MAX of them): the mid and far tiers never have it.</summary>
    const float IK_RANGE = 8;
    const int IK_MAX = 6;
    int iks;

    void FeetOnGround(Model m, bool on)
    {
        if (on && m.ik == null)
        {
            m.skel ??= Find<Skeleton3D>(m.root);
            if (m.skel == null) return;
            m.ik = new FootIK(m.root, m.skel, parent) { GroundAt = m.own ? OwnGroundAt : GroundAt };
            m.ik.Enabled = false;
        }
        if (on && !m.own) iks++;
        if (m.ik == null) return;
        // Steps (a held foot lifted and put down where the clip has it) only standing or turning on the spot.
        m.ik.Steps = m.clip is "more/idle" or "more/crouch_idle" or "more/shuffle";
        if (on == m.ikOn) return;
        m.ik.Enabled = m.ikOn = on;
    }

    /// <summary>The near dead turn their heads to what they're after (HeadLook.cs, PoseIntent.lookAt) within this (m).</summary>
    const float LOOK_RANGE = 15;

    void Look(Model m, Wanted w, bool near, double delta)
    {
        bool on = near && w.pose.lookAt != null;
        if (on && m.headLook == null)
        {
            m.skel ??= Find<Skeleton3D>(m.root);
            if (m.skel == null) return;
            m.skel.AddChild(m.headLook = new HeadLook { Name = "HeadLook" });
        }
        if (m.headLook == null) return;
        m.lookW = Math.Clamp(m.lookW + (on ? 1 : -1) * delta * 3, 0, 1);
        m.headLook.Weight = (float)(m.lookW * m.lookW * (3 - 2 * m.lookW));
        m.headLook.Active = m.lookW > 0;
        if (on)
        {
            var at = w.pose.lookAt!;
            float x = at.x / (float)Motion.SUB, z = at.y / (float)Motion.SUB;
            m.headLook.Target = new Vector3(x, Terrain.Height(x, z) + 1.55f, z);
        }
    }

    /// <summary>The ground under a point for the foot IK: the sim's heights (Terrain.cs) and their slope there.</summary>
    static (float y, Vector3 normal) GroundAt(Vector3 at)
    {
        const float E = 0.15f;
        float h = Terrain.Height(at.X, at.Z);
        float dx = Terrain.Height(at.X + E, at.Z) - Terrain.Height(at.X - E, at.Z);
        float dz = Terrain.Height(at.X, at.Z + E) - Terrain.Height(at.X, at.Z - E);
        return (h, new Vector3(-dx / (2 * E), 1, -dz / (2 * E)).Normalized());
    }


    /// <summary>The turn rate, smoothed (rad/s).</summary>
    static void Turning(Model m, double rate, double delta) => m.turn += (rate - m.turn) * Math.Min(1, delta * 10);


    int Lod(Model m, double dist, float vpHeight, Wanted w, bool dead, double delta)
    {
        // LOD by height on screen, as the look lab.
        double h = vpHeight * 1.75 / (2 * Math.Max(dist, 0.1) * Math.Tan(Mathf.DegToRad(FOV / 2)));
        int lod = Array.FindIndex(LOD_AT, (t) => h >= t);
        if (lod < 0) lod = LOD_AT.Length - 1;
        lod = Math.Min(lod, m.lods.Length - 1);
        for (int i = 0; i < m.lods.Length; i++)
            foreach (var piece in m.lods[i]) piece.Visible = i == lod;

        // The dead's face: angry once it's after someone, eased in over a quarter second.
        if (!dead) return lod;
        m.angry = Math.Clamp(m.angry + (w.angry ? 1 : -1) * delta / 0.25, 0, 1);
        var face = m.lods[lod].FirstOrDefault((mi) => mi.FindBlendShapeByName("angry") >= 0);
        if (face == null) return lod;
        double e = m.angry * m.angry * (3 - 2 * m.angry);
        double slack = SLACK.GetValueOrDefault(m.variant, 0.12) + 0.03 * Math.Sin(time * 1.3 + m.phase * 6);
        double growl = 0.75 + 0.20 * Math.Sin(time * 7.5 + m.phase * 6);
        Shape(face, "jaw_open", slack + (growl - slack) * e);
        Shape(face, "angry", e);
        return lod;
    }


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
}
