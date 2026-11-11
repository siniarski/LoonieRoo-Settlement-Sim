// Wild animals and dogs in first person: deer, rabbits, wolves, bears (Animals.cs in the sim) and dogs
// (Dogs.cs) as their own models (art/scripts/build_animals.py: godot/assets/characters/animals/<kind>.glb,
// a three-step LOD ladder, idle / walk / run clips and an attack for the biters). Reads state only.
// The class is AnimalModels so it doesn't hide the sim's Animals inside this namespace.
//
// What the sim gives: where each one is (at, was) and nothing about how it moves, so the clip is
// chosen by its speed (idle, walk, run, with some hysteresis; the rate scaled by the speed each clip
// was made at, from <kind>.json), facing the way it moves. Animals and dogs are free movers (the
// sim's Walkers.cs: they walk at any angle at a steady pace, and stand still to graze or wait), so the
// speed is eased only enough to hide a tick's jitter, the facing turns towards the velocity, and an
// animal that stops really stands (idle) until it walks on. Crossing a parked car (the sim's AnimalCars.cs) it is
// lifted by AnimalCars.lift (a deer's arc, a wolf up on the roof and down), and under one it keeps to the ground,
// crouched to the room beneath the body. A bite is when its nextBite moved on (wolves, bears) or its cooldown was reset
// (dogs): the attack plays once, turned to whoever it bit. Killed animals leave the sim at once (the
// meat goes to whoever brought it down), so there's no carcass to draw.
//
// Budget (rule 14): the nearest MAX_MODELS within RANGE, pooled by kind (one leaving hands its model
// to the next of its kind), a few new ones made a frame at most; LOD by height on screen.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.World3D;

public sealed class AnimalModels
{
    /// <summary>The options' Character models (Ui/Graphics.cs).</summary>
    public static int MAX_MODELS = 40;
    const int MAKES_PER_FRAME = 4;
    /// <summary>Further than this (m), an animal isn't drawn (as Bodies' range).</summary>
    const float RANGE = 48;
    /// <summary>The lowest height on screen (px) each LOD is shown at.</summary>
    static readonly double[] LOD_AT = { 110, 36, 0 };
    const double FOV = 60;
    static readonly string[] KINDS = { "deer", "rabbit", "wolf", "bear", "dog" };

    sealed class Kind
    {
        public PackedScene scene = null!;
        public double height;
        public double walkSpeed = 1, runSpeed = 3, attackLength = 0.8;
    }

    sealed class Model
    {
        public Node3D root = null!;
        public MeshInstance3D[][] lods = Array.Empty<MeshInstance3D[]>();
        public AnimationPlayer player = null!;
        public string kind = "", clip = "";
        public string? bodyId;
        public double speed, phase, lastBite = double.NaN;
        public float yaw;
        public bool placed, buck;
    }

    readonly Node3D parent;
    readonly Dictionary<string, Kind> kinds = new();
    readonly Dictionary<string, List<Model>> free = new();
    readonly Dictionary<string, Model> byBody = new();

    public AnimalModels(Node3D parent)
    {
        this.parent = parent;
        foreach (var k in KINDS)
        {
            string path = $"res://assets/characters/animals/{k}.glb";
            if (!ResourceLoader.Exists(path)) continue;
            var kind = new Kind { scene = GD.Load<PackedScene>(path) };
            var meta = Json.ParseString(FileAccess.GetFileAsString($"res://assets/characters/animals/{k}.json")).AsGodotDictionary();
            kind.height = meta["height"].AsDouble();
            var clips = meta["clips"].AsGodotDictionary();
            kind.walkSpeed = clips["walk"].AsGodotDictionary()["speed"].AsDouble();
            kind.runSpeed = clips["run"].AsGodotDictionary()["speed"].AsDouble();
            if (clips.ContainsKey("attack")) kind.attackLength = clips["attack"].AsGodotDictionary()["length"].AsDouble();
            kinds[k] = kind;
        }
    }

    public int Shown => byBody.Count;

    readonly record struct Wanted(string id, string kind, Point at, Point was, double dist2, double bite, Point? facing, CarCrossing? cross);

    /// <summary>The animals near the eye this frame; `delta` is the world's time (0 while it is held: they hold still).</summary>
    public void Update(SimState state, double alpha, Vector3 eye, double delta)
    {
        delta = Math.Min(0.25, delta);
        float vpHeight = parent.GetViewport().GetVisibleRect().Size.Y;

        var wanted = new List<Wanted>();
        void Add(string id, string kind, Point at, Point was, double bite, Point? facing, CarCrossing? cross)
        {
            if (!kinds.ContainsKey(kind)) return;
            var p = worldPos(at, was, alpha);
            double d2 = (p.x - eye.X) * (p.x - eye.X) + (p.y - eye.Z) * (p.y - eye.Z);
            if (d2 <= RANGE * RANGE) wanted.Add(new Wanted(id, kind, at, was, d2, bite, facing, cross));
        }
        foreach (var a in state.animals)
        {
            // The tick of its last bite: nextBite is set that many ticks on when it bites.
            double bite = double.NaN;
            if (a.kind is "wolf" or "bear" && a.nextBite is double nb && nb > 0)
                bite = nb - (a.kind == "bear" ? LoonieRoo.Sim.Animals.BEAR_BITE_EVERY : LoonieRoo.Sim.Animals.WOLF_BITE_EVERY);
            Add(a.id, a.kind, a.at, a.was, bite, Js.T(a.targetId) ? TargetAt(state, a.targetId!) : null, a.cross);
        }
        foreach (var d in state.dogs)
            Add(d.id, "dog", d.at, d.was, d.cooldown >= Dogs.DOG_BITE_EVERY - 1 ? state.tick - (Dogs.DOG_BITE_EVERY - d.cooldown) : double.NaN, null, d.cross);
        wanted.Sort((a, b) => a.dist2.CompareTo(b.dist2));
        if (wanted.Count > MAX_MODELS) wanted.RemoveRange(MAX_MODELS, wanted.Count - MAX_MODELS);

        var keep = new HashSet<string>(wanted.Select((w) => w.id));
        foreach (var id in byBody.Keys.Where((id) => !keep.Contains(id)).ToList())
        {
            var m = byBody[id];
            byBody.Remove(id);
            m.bodyId = null;
            m.root.Visible = false;
            Free(m.kind).Add(m);
        }

        int made = 0;
        foreach (var w in wanted)
        {
            if (!byBody.TryGetValue(w.id, out var m))
            {
                var pool = Free(w.kind);
                if (pool.Count > 0) { m = pool[^1]; pool.RemoveAt(pool.Count - 1); }
                else if (made < MAKES_PER_FRAME) { m = Make(w.kind); made++; }
                else continue; // next frame
                m.bodyId = w.id;
                m.placed = false;
                m.clip = "";
                m.speed = 0;
                m.lastBite = w.bite;
                m.phase = Hash01(w.id, 3);
                m.buck = w.kind == "deer" && Hash01(w.id, 5) < 0.5;
                m.root.Visible = true;
                byBody[w.id] = m;
            }
            Pose(m, w, state, alpha, delta, Math.Sqrt(w.dist2), vpHeight);
            m.player.Advance(delta); // played on the world's time, not the frame's (Make: manual)
        }
    }

    /// <summary>Where the one it's after stands: a survivor or another animal.</summary>
    static Point? TargetAt(SimState state, string id)
    {
        foreach (var s in state.survivors) if (s.id == id) return s.at;
        foreach (var o in state.animals) if (o.id == id) return o.at;
        return null;
    }

    List<Model> Free(string kind)
    {
        if (!free.TryGetValue(kind, out var l)) free[kind] = l = new();
        return l;
    }

    static double Hash01(string id, int salt)
    {
        uint h = 2166136261;
        foreach (char c in id) h = (h ^ c) * 16777619;
        h = (h ^ (uint)salt) * 16777619;
        h ^= h >> 13;
        return (h % 10007) / 10007.0;
    }

    Model Make(string kind)
    {
        var root = kinds[kind].scene.Instantiate<Node3D>();
        parent.AddChild(root);
        var m = new Model { root = root, kind = kind };
        m.lods = FindAll<MeshInstance3D>(root).GroupBy((mi) => LodIndex(mi.Name)).OrderBy((g) => g.Key).Select((g) => g.ToArray()).ToArray();
        foreach (var mi in m.lods.SelectMany((g) => g)) mi.MaterialOverride = Coat(mi.Mesh.SurfaceGetMaterial(0) as BaseMaterial3D);
        m.player = FindAll<AnimationPlayer>(root).First();
        m.player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        foreach (var n in new[] { "idle", "walk", "run" })
            if (m.player.HasAnimation(n)) m.player.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
        return m;
    }

    static ShaderMaterial? coat;

    /// <summary>The coats' material, one for every animal: the file's vertex colours times the kit's fur layer
    /// (docs/textures.md), laid by the body's own coordinates in UV2 (build_animals.py detail_uvs: it moves with
    /// the skin, so it never swims); the eyes and antlers say no layer.</summary>
    static ShaderMaterial Coat(BaseMaterial3D? src)
    {
        if (coat != null) return coat;
        bool srgb = src?.VertexColorIsSrgb ?? false;
        coat = new ShaderMaterial { Shader = Surfaces.Shader(COAT.Replace("SRGB", srgb ? "1" : "0")) };
        coat.SetShaderParameter("roughness", src?.Roughness ?? 0.92f);
        return coat;
    }

    const string COAT = @"
shader_type spatial;
" + Surfaces.INCLUDE + @"
uniform float roughness = 0.92;
void vertex() { SURFACE_BY_OBJECT }
void fragment() {
    vec3 c = COLOR.rgb;
#if SRGB
    c = mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
#endif
    ALBEDO = c * SURFACE_DETAIL;
    ROUGHNESS = roughness;
}
";

    void Pose(Model m, Wanted w, SimState state, double alpha, double delta, double dist, float vpHeight)
    {
        var kind = kinds[w.kind];
        var p = worldPos(w.at, w.was, alpha);
        // Over a parked car: up off the ground. Under one: crouched to fit the room beneath it (a little to spare).
        float lift = (float)AnimalCars.lift(w.cross, alpha);
        m.root.Position = new Vector3((float)p.x, Terrain.Height((float)p.x, (float)p.y) + lift, (float)p.y);
        var room = AnimalCars.room(w.cross);
        float squash = room is double r && kind.height > r ? (float)Math.Max(0.3, (r - 0.03) / kind.height) : 1f;
        m.root.Scale = new Vector3(1, squash, 1);
        double dx = w.at.x - w.was.x, dy = w.at.y - w.was.y;
        double speed = Math.Sqrt(dx * dx + dy * dy) / Motion.SUB * PoseIntents.TICKS_PER_SECOND;
        // Eased a little (a tick's jitter, a turn round a corner); a stop is a real stand.
        m.speed += (speed - m.speed) * Math.Min(1, delta * (speed > m.speed ? 10 : 6));
        // The model's front is +Z: a heading (cos h, sin h) on the grid's (x, z) is a turn of atan2(cos h, sin h).
        if (w.cross != null)
        {
            // Crossing a car: the way across (it can't turn until it is down).
            float want = (float)Math.Atan2(w.cross.to.x - w.cross.from.x, w.cross.to.y - w.cross.from.y);
            m.yaw = m.placed ? Mathf.LerpAngle(m.yaw, want, (float)Math.Min(1, delta * 12)) : want;
        }
        else if (speed > 0.15)
        {
            float want = (float)Math.Atan2(dx, dy);
            // Turn towards the way it's going: quicker the faster it goes (a bolting rabbit turns on the spot).
            m.yaw = m.placed ? Mathf.LerpAngle(m.yaw, want, (float)Math.Min(1, delta * (6 + 2 * speed))) : want;
        }
        else if (!m.placed) m.yaw = (float)(Hash01(w.id, 9) * Math.PI * 2);

        // A bite: once, from the start, turned to whoever it's after.
        bool bit = !double.IsNaN(w.bite) && w.bite != m.lastBite && state.tick - w.bite < kind.attackLength * PoseIntents.TICKS_PER_SECOND;
        if (!double.IsNaN(w.bite)) m.lastBite = w.bite;
        if (w.facing != null && (bit || m.clip == "attack"))
        {
            float want = (float)Math.Atan2(w.facing.x - w.at.x, w.facing.y - w.at.y);
            m.yaw = m.placed ? Mathf.LerpAngle(m.yaw, want, (float)Math.Min(1, delta * 12)) : want;
        }
        m.placed = true;
        m.root.Rotation = new Vector3(0, m.yaw, 0);

        if (bit && m.player.HasAnimation("attack"))
        {
            m.player.Play("attack", 0.1);
            m.player.Seek(0, true);
            m.player.SpeedScale = 1;
            m.clip = "attack";
        }
        else if (m.clip != "attack" || !m.player.IsPlaying())
        {
            // Idle, walk or run, with hysteresis so a speed near a threshold doesn't flick between them.
            double runAt = (kind.walkSpeed + kind.runSpeed) / 2;
            string clip = m.clip switch
            {
                "run" => m.speed < runAt * 0.85 ? (m.speed < 0.12 ? "idle" : "walk") : "run",
                "walk" => m.speed < 0.12 ? "idle" : m.speed > runAt * 1.1 ? "run" : "walk",
                _ => m.speed > runAt * 1.1 ? "run" : m.speed > 0.3 ? "walk" : "idle",
            };
            double clipSpeed = clip == "run" ? kind.runSpeed : kind.walkSpeed;
            float rate = clip == "idle" ? 1f : (float)Math.Clamp(m.speed / clipSpeed, 0.6, 1.8);
            if (clip != m.clip)
            {
                bool first = m.clip == "";
                m.player.Play(clip, first ? 0 : 0.25);
                if (first) m.player.Seek(m.phase * m.player.CurrentAnimationLength, true);
                m.clip = clip;
            }
            m.player.SpeedScale = rate;
        }

        // LOD by height on screen.
        double h = vpHeight * kind.height / (2 * Math.Max(dist, 0.1) * Math.Tan(Mathf.DegToRad(FOV / 2)));
        int lod = Array.FindIndex(LOD_AT, (t) => h >= t);
        if (lod < 0) lod = LOD_AT.Length - 1;
        lod = Math.Min(lod, m.lods.Length - 1);
        for (int i = 0; i < m.lods.Length; i++)
            foreach (var piece in m.lods[i])
                piece.Visible = i == lod && (m.buck || !piece.Name.ToString().Contains("antlers"));
    }

    static int LodIndex(StringName name)
    {
        var s = name.ToString();
        int i = s.LastIndexOf("lod", StringComparison.Ordinal);
        return i >= 0 && int.TryParse(s[(i + 3)..], out var n) ? n : 0;
    }

    static IEnumerable<T> FindAll<T>(Node n) where T : Node
    {
        if (n is T t) yield return t;
        foreach (var c in n.GetChildren())
            foreach (var f in FindAll<T>(c)) yield return f;
    }
}
