// The style lineup: one scene that puts the survivor, the dead, a wolf, a rabbit, trees and items side by
// side at five levels of detail, from blocky low poly to what the game draws now, in the game's daylight and
// at first person's eye (1.65 m, 60 degrees vertical), and times a whole first-person scene at each.
//
// The tiers (columns), all from the models we already have (art/scripts; nothing new is made here):
//   A Blocky   pz ladder's bottom (~600 tris), flat-shaded, its colour map shrunk to 128 px; animals and trees at far LOD
//   B Zomboid  pz 2.5k, smooth, its 512 px painted map, no normal map; animals and trees at mid LOD
//   C Mid      plus ladder's 6k, 2k colour and normal maps; animals and trees at near LOD (their top)
//   D Near     plus 12k
//   E Now      plus 30k: what People.cs draws at arm's length today
// Items have one level (36-252 tris) and are the same in every tier.
//
// Run:  Godot --path godot res://Labs/StyleLineup.tscn -- --shots=DIR     the comparison pictures, then quit
//       Godot --path godot res://Labs/StyleLineup.tscn -- --perf=DIR      the scene timing per tier (perf.md), then quit
//       (no arguments: the vignettes, cycled with the number keys 1-5)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using LoonieRoo.Game.World3D;
using static LoonieRoo.Game.Nodes;

namespace LoonieRoo.Game.Characters;

public partial class StyleLineup : Node3D
{
    sealed record Tier(string Key, string Name, string Look, int TopLod, int AnimalLod, int TreeLod, bool Flat, int Shrink, string Note);

    static readonly Tier[] Tiers =
    {
        new("A", "Blocky", "pz", 2, 2, 2, true, 2, "flat-shaded, 128 px colour"),
        new("B", "Zomboid", "pz", 0, 0, 1, false, 0, "smooth, 512 px painted, no normal map"),
        new("C", "Mid", "plus", 2, 0, 0, false, 0, "2k colour + normal map"),
        new("D", "Near", "plus", 1, 0, 0, false, 0, "2k colour + normal map"),
        new("E", "Now", "plus", 0, 0, 0, false, 0, "2k colour + normal map (the game today)"),
    };

    /// <summary>Screen heights (px) each LOD of a ladder is shown from (the look lab's, People.cs's, Animals.cs's).</summary>
    static readonly double[] PlusAt = { 700, 340, 170, 85, 40, 0 }, PzAt = { 260, 110, 0 }, AnimalAt = { 110, 36, 0 };

    const string Flat = @"
shader_type spatial;
render_mode cull_back, specular_disabled;
uniform sampler2D tex : source_color, filter_linear_mipmap;
uniform bool use_tex = true;
uniform bool use_vcol = false;
uniform vec4 tint : source_color = vec4(1.0);
void fragment() {
    NORMAL = normalize(cross(dFdy(VERTEX), dFdx(VERTEX)));
    vec3 c = tint.rgb;
    if (use_tex) c *= texture(tex, UV).rgb;
    if (use_vcol) c *= COLOR.rgb;
    ALBEDO = c;
    ROUGHNESS = 0.92;
}";

    sealed class Body
    {
        public Node3D Root = null!;
        public MeshInstance3D[][] Lods = Array.Empty<MeshInstance3D[]>();
        public int[] Tris = Array.Empty<int>();
        public AnimationPlayer Player = null!;
        public double[] At = Array.Empty<double>();
        public int Top;
        public float Height = 1.75f;
        public int Shown = -1;
    }

    Camera3D cam = null!;
    Label caption = null!;
    AnimationLibrary ual = null!;
    Trees trees = null!;
    ItemModels items = null!;
    readonly Dictionary<string, Texture2D> small = new();
    readonly Dictionary<string, Material> flatMats = new();
    Shader flatShader = null!;
    readonly List<Body> bodies = new();
    Node3D stage = null!;
    bool freeze = true;

    public override void _Ready()
    {
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        var args = new Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            args[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        flatShader = new Shader { Code = Flat };
        var u = GD.Load<PackedScene>("res://assets/characters/clips_ual.glb").Instantiate<Node3D>();
        ual = Find<AnimationPlayer>(u)!.GetAnimationLibrary("");
        foreach (var n in new[] { "ual_idle", "ual_walk" }) ual.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
        u.Free();
        trees = new Trees(this);
        items = new ItemModels(this);
        BuildWorld();
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        GetViewport().MeshLodThreshold = 0;  // the ladders' own LODs, not Godot's
        if (args.TryGetValue("shots", out var sd)) _ = RunShots(sd);
        else if (args.TryGetValue("perf", out var pd)) _ = RunPerf(pd);
        else Vignette(Tiers[4]);
    }

    void BuildWorld()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.52f, 0.56f, 0.58f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.58f, 0.6f),
            AmbientLightEnergy = 0.5f,
            FogEnabled = true,
            FogDensity = 0.018f,
            FogLightColor = new Color(0.52f, 0.56f, 0.58f),
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        AddChild(new WorldEnvironment { Environment = env });
        // The game's sun at about two in the afternoon, from behind your right shoulder.
        AddChild(new DirectionalLight3D { ShadowEnabled = true, DirectionalShadowMaxDistance = 40, LightEnergy = 1.18f, LightColor = new Color(1, 0.95f, 0.86f), RotationDegrees = new Vector3(-48, 35, 0) });
        AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(400, 400) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.27f, 0.18f), Roughness = 1 } });
        cam = new Camera3D { Fov = 60, Position = new Vector3(0, 1.65f, 0), Near = 0.05f, Far = 300 };
        AddChild(cam);
        var layer = new CanvasLayer();
        AddChild(layer);
        caption = new Label { Position = new Vector2(16, 10) };
        caption.AddThemeFontSizeOverride("font_size", 22);
        caption.AddThemeColorOverride("font_outline_color", Colors.Black);
        caption.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(caption);
    }

    // ------------------------------------------------------------------ bodies

    Body Person(Tier t, string subj, Node parent, string clip = "ual_idle", double phase = 0.3)
    {
        var root = GD.Load<PackedScene>($"res://assets/characters/look/look_{t.Look}_{subj}.glb").Instantiate<Node3D>();
        parent.AddChild(root);
        var groups = FindAll<MeshInstance3D>(root).GroupBy(mi => LodIndex(mi.Name)).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        // Hair on the skeleton, its LOD matched to the body's (the pz ladder's 2.5k / 1.2k / 600 are the plus ladder's 3 / 4 / 5).
        var skel = Find<Skeleton3D>(root)!;
        var hair = GD.Load<PackedScene>($"res://assets/characters/look/hair/hair_{(subj == "zombie" ? "matted" : "sidepart")}.glb").Instantiate<Node3D>();
        var tint = subj == "zombie" ? new Color(0.2f, 0.17f, 0.14f) : new Color(0.34f, 0.24f, 0.16f);
        foreach (var mi in FindAll<MeshInstance3D>(hair).ToList())
        {
            int lod = LodIndex(mi.Name) - (t.Look == "pz" ? 3 : 0);
            if (lod < 0 || lod >= groups.Count) continue;
            mi.GetParent().RemoveChild(mi);
            mi.Owner = null;
            skel.AddChild(mi);
            mi.Skeleton = new NodePath("..");
            var m = (StandardMaterial3D)((BaseMaterial3D)mi.Mesh.SurfaceGetMaterial(0)).Duplicate();
            m.AlbedoColor = tint;
            mi.MaterialOverride = m;
            groups[lod].Add(mi);
        }
        hair.QueueFree();
        var b = new Body { Root = root, Lods = groups.Select(g => g.ToArray()).ToArray(), Top = t.TopLod, At = t.Look == "pz" ? PzAt : PlusAt };
        b.Tris = b.Lods.Select(g => g.Sum(Tris)).ToArray();
        if (t.Flat) Flatten(b, t.Shrink);
        b.Player = new AnimationPlayer();
        root.AddChild(b.Player);
        b.Player.AddAnimationLibrary("ual", ual);
        Pose(b, "ual/" + clip, phase, subj == "zombie" ? 0.7f : 1f);
        bodies.Add(b);
        return b;
    }

    Body Animal(Tier t, string kind, Node parent, string clip = "idle", double phase = 0.3)
    {
        var root = GD.Load<PackedScene>($"res://assets/characters/animals/{kind}.glb").Instantiate<Node3D>();
        parent.AddChild(root);
        var b = new Body
        {
            Root = root, Top = t.AnimalLod, At = AnimalAt, Height = kind == "rabbit" ? 0.3f : 0.8f,
            Lods = FindAll<MeshInstance3D>(root).Where(mi => !mi.Name.ToString().Contains("antlers")).GroupBy(mi => LodIndex(mi.Name)).OrderBy(g => g.Key).Select(g => g.ToArray()).ToArray(),
        };
        b.Tris = b.Lods.Select(g => g.Sum(Tris)).ToArray();
        if (t.Flat) Flatten(b, 0);
        b.Player = FindAll<AnimationPlayer>(root).First();
        foreach (var n in new[] { "idle", "walk", "run" })
            if (b.Player.HasAnimation(n)) b.Player.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
        Pose(b, clip, phase, 1);
        bodies.Add(b);
        return b;
    }

    void Pose(Body b, string clip, double phase, float rate)
    {
        b.Player.Play(clip);
        b.Player.Seek(phase * b.Player.CurrentAnimationLength, true);
        b.Player.SpeedScale = freeze ? 0 : rate;
    }

    /// <summary>Every piece in the flat material: its colour map shrunk (by 2^shrink), or its vertex colours.</summary>
    void Flatten(Body b, int shrink)
    {
        foreach (var mi in b.Lods.SelectMany(g => g))
        {
            var src = (mi.MaterialOverride ?? mi.Mesh.SurfaceGetMaterial(0)) as BaseMaterial3D;
            if (src == null) continue;
            string key = src.GetInstanceId() + "/" + shrink;
            if (!flatMats.TryGetValue(key, out var mat))
            {
                var sm = new ShaderMaterial { Shader = flatShader };
                sm.SetShaderParameter("tint", src.AlbedoColor);
                sm.SetShaderParameter("use_vcol", src.VertexColorUseAsAlbedo);
                sm.SetShaderParameter("use_tex", src.AlbedoTexture != null);
                if (src.AlbedoTexture != null) sm.SetShaderParameter("tex", Shrunk(src.AlbedoTexture, shrink));
                flatMats[key] = mat = sm;
            }
            mi.MaterialOverride = mat;
        }
    }

    Texture2D Shrunk(Texture2D tex, int shrink)
    {
        string key = tex.GetRid() + "/" + shrink;
        if (small.TryGetValue(key, out var have)) return have;
        var img = (Image)tex.GetImage().Duplicate();
        if (img.IsCompressed()) img.Decompress();
        if (img.HasMipmaps()) img.ClearMipmaps();
        img.Resize(Math.Max(8, img.GetWidth() >> shrink), Math.Max(8, img.GetHeight() >> shrink), Image.Interpolation.Lanczos);
        img.GenerateMipmaps();
        return small[key] = ImageTexture.CreateFromImage(img);
    }

    void ShowLod(Body b, int lod)
    {
        lod = Math.Clamp(lod, 0, b.Lods.Length - 1);
        if (lod == b.Shown) return;
        for (int i = 0; i < b.Lods.Length; i++)
            foreach (var p in b.Lods[i]) p.Visible = i == lod;
        b.Shown = lod;
    }

    /// <summary>The LOD by height on screen, never above the tier's top.</summary>
    void AutoLod(Body b, double vpH)
    {
        double d = Math.Max(0.3, cam.GlobalPosition.DistanceTo(b.Root.GlobalPosition + new Vector3(0, b.Height / 2, 0)));
        double h = b.Height / (2 * d * Math.Tan(Mathf.DegToRad(30))) * vpH;
        int lod = Array.FindIndex(b.At, t => h >= t);
        if (lod < 0) lod = b.At.Length - 1;
        ShowLod(b, Math.Max(lod, b.Top));
    }

    MultiMeshInstance3D Multi(Mesh mesh, List<Transform3D> xf, bool custom, Node parent)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = custom, Mesh = mesh, InstanceCount = xf.Count };
        for (int i = 0; i < xf.Count; i++)
        {
            mm.SetInstanceTransform(i, xf[i]);
            if (custom) mm.SetInstanceCustomData(i, new Color(0.95f, 1f, 0.93f, (float)H(i, 5)));
        }
        var node = new MultiMeshInstance3D { Multimesh = mm };
        parent.AddChild(node);
        return node;
    }

    void Tree(int species, int lod, Vector3 at, float size, Node parent) =>
        Multi(trees.TreeMesh(species, lod), new() { new Transform3D(new Basis(Vector3.Up, (float)(H((int)(at.X * 7 + at.Z * 13), 2) * 6.28)).Scaled(Vector3.One * size), at) }, true, parent);

    static readonly string[] ItemSet = { "food", "weapon_v1", "medkit", "gun", "book_v1", "fuel", "axe", "food_v2", "wood", "boots" };

    void Item(string name, Vector3 at, float yaw, Node parent)
    {
        var mesh = items.MeshNamed(name);
        if (mesh != null) Multi(mesh, new() { new Transform3D(new Basis(Vector3.Up, yaw), at) }, false, parent);
    }

    // ------------------------------------------------------------------ the pictures

    void Clear()
    {
        stage?.QueueFree();
        bodies.Clear();
        stage = new Node3D();
        AddChild(stage);
    }

    Label3D Tag(string text, Vector3 at, float pixel)
    {
        var l = new Label3D { Text = text, Position = at, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 44, OutlineSize = 12, PixelSize = pixel, Modulate = new Color(1, 1, 0.92f) };
        stage.AddChild(l);
        return l;
    }

    void Look(Vector3 from, Vector3 at)
    {
        cam.Position = from;
        cam.LookAt(at, Vector3.Up);
    }

    static string K(int n) => n >= 1000 ? $"{n / 1000.0:0.#}k" : n.ToString();

    /// <summary>One subject in a row across the tiers, each labelled with its tier and triangles.</summary>
    void Row(string subject)
    {
        Clear();
        caption.Text = "";
        float spacing = subject switch { "wolf" => 1.7f, "rabbit" => 0.6f, "tree" => 6.5f, _ => 1.05f };
        float z = subject switch { "wolf" => -4.6f, "rabbit" => -2.2f, "tree" => -17, _ => -3.7f };
        for (int i = 0; i < Tiers.Length; i++)
        {
            var t = Tiers[i];
            var col = new Node3D { Position = new Vector3((i - 2) * spacing, 0, z) };
            stage.AddChild(col);
            int tris;
            float top;
            if (subject == "tree")
            {
                // A spruce and a broadleaf, the two shapes most of a forest is.
                Tree(0, t.TreeLod, col.Position + new Vector3(-1.4f, 0, 0), 1f, stage);
                Tree(2, t.TreeLod, col.Position + new Vector3(1.6f, 0, 1.5f), 0.95f, stage);
                tris = Tris(trees.TreeMesh(0, t.TreeLod)) + Tris(trees.TreeMesh(2, t.TreeLod));
                top = 12;
            }
            else
            {
                var b = subject is "wolf" or "rabbit" ? Animal(t, subject, col) : Person(t, subject, col);
                b.Root.RotationDegrees = new Vector3(0, subject is "wolf" or "rabbit" ? 55 : 15, 0);
                ShowLod(b, b.Top);
                tris = b.Tris[b.Shown];
                top = subject switch { "wolf" => 1.15f, "rabbit" => 0.42f, _ => 1.95f };
            }
            float px = -z * 0.00055f;
            Tag($"{t.Key}  {t.Name}\n{K(tris)} tris", col.Position + new Vector3(0, top, 0), px);
        }
        float lookY = subject switch { "wolf" => 0.45f, "rabbit" => 0.1f, "tree" => 5f, _ => 1.05f };
        Look(new Vector3(0, 1.65f, 0), new Vector3(0, lookY, z));
    }

    /// <summary>A tier's first-person moment: the dead coming, a wolf, a rabbit, a survivor, the forest edge, things on the ground.</summary>
    void Vignette(Tier t)
    {
        Clear();
        foreach (var (x, z, ph) in new[] { (-1.1f, -2.6f, 0.1), (1.6f, -5.2f, 0.55), (-3.2f, -9.5f, 0.8), (2.4f, -15f, 0.3), (-0.5f, -20f, 0.65) })
        {
            var b = Person(t, "zombie", stage, "ual_walk", ph);
            b.Root.Position = new Vector3(x, 0, z);
            b.Root.RotationDegrees = new Vector3(0, (float)(Math.Atan2(-x, -z) * 180 / Math.PI), 0);
        }
        var s = Person(t, "survivor", stage);
        s.Root.Position = new Vector3(4.2f, 0, -7.5f);
        s.Root.RotationDegrees = new Vector3(0, -60, 0);
        var w = Animal(t, "wolf", stage, "walk", 0.4);
        w.Root.Position = new Vector3(-3.6f, 0, -5.8f);
        w.Root.RotationDegrees = new Vector3(0, 70, 0);
        var r = Animal(t, "rabbit", stage);
        r.Root.Position = new Vector3(1.5f, 0, -3.3f);
        r.Root.RotationDegrees = new Vector3(0, -120, 0);
        // The forest edge behind them, at the game's tree LOD ranges (0-24 m near, 24-44 mid, past it far), the tier's top.
        for (int i = 0; i < 60; i++)
        {
            double a = (H(i, 1) - 0.5) * 2.0, d = 11 + H(i, 3) * 40;
            var at = new Vector3((float)(Math.Sin(a) * d), 0, (float)(-Math.Cos(a) * d));
            int lod = Math.Max(t.TreeLod, d < 24 ? 0 : d < 44 ? 1 : 2);
            int sp = (int)(H(i, 4) * 5) % 5;
            Tree(sp, lod, at, 0.75f + (float)H(i, 6) * 0.4f, stage);
        }
        Item("weapon_v1", new Vector3(0.35f, 0, -1.3f), 0.6f, stage);
        Item("food", new Vector3(-0.45f, 0, -1.7f), 1.9f, stage);
        Item("medkit", new Vector3(0.8f, 0, -2.2f), 0.2f, stage);
        Item("book_v1", new Vector3(-0.2f, 0, -2.6f), 2.4f, stage);
        cam.Position = new Vector3(0, 1.65f, 0);
        cam.RotationDegrees = new Vector3(-9, 0, 0);
        double vpH = GetViewport().GetVisibleRect().Size.Y;
        foreach (var b in bodies) AutoLod(b, vpH);
        caption.Text = $"{t.Key}  {t.Name}: {t.Note}";
    }

    /// <summary>A tier's dead at arm's length (1 m), the closest first person gets.</summary>
    void Close(Tier t, string subj)
    {
        Clear();
        caption.Text = "";
        var b = Person(t, subj, stage);
        b.Root.Position = new Vector3(0, 0, -0.95f);
        b.Root.RotationDegrees = new Vector3(0, 20, 0);
        ShowLod(b, b.Top);
        Tag($"{t.Key}  {t.Name}  {K(b.Tris[b.Shown])}", new Vector3(0, 1.98f, -0.95f), 0.0006f);
        Look(new Vector3(0, 1.65f, 0), new Vector3(0, 1.45f, -0.95f));
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async Task<Image> Grab()
    {
        await Frames(6);
        var img = GetViewport().GetTexture().GetImage();
        img.Convert(Image.Format.Rgba8);
        return img;
    }

    async Task RunShots(string dir)
    {
        DirAccess.MakeDirRecursiveAbsolute(dir);
        await Frames(3);
        foreach (var subject in new[] { "survivor", "zombie", "wolf", "rabbit", "tree" })
        {
            Row(subject);
            (await Grab()).SavePng($"{dir}/row_{subject}.png");
        }
        // Arm's length: the middle 512 px of each tier's shot side by side.
        foreach (var subj in new[] { "zombie", "survivor" })
        {
            var strip = Image.CreateEmpty(512 * Tiers.Length, 720, false, Image.Format.Rgba8);
            for (int i = 0; i < Tiers.Length; i++)
            {
                Close(Tiers[i], subj);
                strip.BlitRect(await Grab(), new Rect2I(384, 0, 512, 720), new Vector2I(512 * i, 0));
            }
            strip.SavePng($"{dir}/close_{subj}.png");
        }
        // The vignettes, each on its own and as a 3 x 2 sheet at half size.
        var sheet = Image.CreateEmpty(1920, 720, false, Image.Format.Rgba8);
        sheet.Fill(new Color(0.08f, 0.08f, 0.08f));
        for (int i = 0; i < Tiers.Length; i++)
        {
            Vignette(Tiers[i]);
            var img = await Grab();
            img.SavePng($"{dir}/scene_{Tiers[i].Key}.png");
            img.Resize(640, 360, Image.Interpolation.Lanczos);
            sheet.BlitRect(img, new Rect2I(0, 0, 640, 360), new Vector2I(640 * (i % 3), 360 * (i / 3)));
        }
        sheet.SavePng($"{dir}/scenes.png");
        // The items, the same in every tier.
        Clear();
        caption.Text = "Items: one level in every tier";
        for (int i = 0; i < ItemSet.Length; i++)
        {
            var at = new Vector3((i % 5 - 2) * 0.55f, 0, -1.6f - (i / 5) * 0.6f);
            Item(ItemSet[i], at, 0.5f + i, stage);
            var mesh = items.MeshNamed(ItemSet[i]);
            if (mesh != null) Tag($"{ItemSet[i]} {Tris(mesh)}", at + new Vector3(0, 0.32f, 0), 0.0009f);
        }
        Look(new Vector3(0, 1.65f, 0), new Vector3(0, 0, -1.9f));
        (await Grab()).SavePng($"{dir}/items.png");
        GD.Print($"style lineup: shots in {dir}");
        GetTree().Quit();
    }

    // ------------------------------------------------------------------ timing

    /// <summary>A tier's whole first-person scene: N of the dead walking round you (the game's model cap is 40), 40 animals,
    /// ~900 trees at the game's LOD ranges, 300 items; the camera turns a full circle. Each LOD by height on screen,
    /// capped at the tier's top.</summary>
    void PerfScene(Tier t, int people)
    {
        freeze = false;
        Clear();
        for (int i = 0; i < people; i++)
        {
            double a = H(i, 11) * Math.Tau, d = 3 + Math.Sqrt(H(i, 12)) * 37;
            var b = Person(t, i % 4 == 3 ? "survivor" : "zombie", stage, "ual_walk", H(i, 13));
            b.Root.Position = new Vector3((float)(Math.Sin(a) * d), 0, (float)(Math.Cos(a) * d));
            b.Root.RotationDegrees = new Vector3(0, (float)(H(i, 14) * 360), 0);
        }
        for (int i = 0; i < 40; i++)
        {
            double a = H(i, 21) * Math.Tau, d = 4 + Math.Sqrt(H(i, 22)) * 40;
            var b = Animal(t, i % 2 == 0 ? "wolf" : "rabbit", stage, "walk", H(i, 23));
            b.Root.Position = new Vector3((float)(Math.Sin(a) * d), 0, (float)(Math.Cos(a) * d));
            b.Root.RotationDegrees = new Vector3(0, (float)(H(i, 24) * 360), 0);
        }
        // Trees: one MultiMesh per species and range band (the game: one per chunk and LOD).
        var bands = new Dictionary<(int sp, int lod), List<Transform3D>>();
        for (int i = 0; i < 900; i++)
        {
            double a = H(i, 31) * Math.Tau, d = 8 + Math.Sqrt(H(i, 32)) * 52;
            int lod = Math.Max(t.TreeLod, d < 24 ? 0 : d < 44 ? 1 : 2), sp = (int)(H(i, 33) * 5) % 5;
            if (!bands.TryGetValue((sp, lod), out var list)) bands[(sp, lod)] = list = new();
            list.Add(new Transform3D(new Basis(Vector3.Up, (float)(H(i, 34) * 6.28)).Scaled(Vector3.One * (0.75f + (float)H(i, 35) * 0.4f)), new Vector3((float)(Math.Sin(a) * d), 0, (float)(Math.Cos(a) * d))));
        }
        foreach (var ((sp, lod), list) in bands) Multi(trees.TreeMesh(sp, lod), list, true, stage);
        var byItem = new Dictionary<string, List<Transform3D>>();
        for (int i = 0; i < 300; i++)
        {
            double a = H(i, 41) * Math.Tau, d = 1.5 + Math.Sqrt(H(i, 42)) * 38;
            string name = ItemSet[i % ItemSet.Length];
            if (!byItem.TryGetValue(name, out var list)) byItem[name] = list = new();
            list.Add(new Transform3D(new Basis(Vector3.Up, (float)(H(i, 43) * 6.28)), new Vector3((float)(Math.Sin(a) * d), 0, (float)(Math.Cos(a) * d))));
        }
        foreach (var (name, list) in byItem)
            if (items.MeshNamed(name) is Mesh m) Multi(m, list, false, stage);
        caption.Text = $"{t.Key}  {t.Name}: {people} people";
    }

    double yaw;

    public override void _Process(double delta)
    {
        if (perfRunning)
        {
            yaw += delta * 360 / 5.0;  // a full turn in 5 s
            cam.Position = new Vector3(0, 1.65f, 0);
            cam.RotationDegrees = new Vector3(-4, (float)yaw, 0);
            double vpH = GetViewport().GetVisibleRect().Size.Y;
            foreach (var b in bodies) AutoLod(b, vpH);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } k && k.Keycode >= Key.Key1 && k.Keycode <= Key.Key5)
            Vignette(Tiers[(int)(k.Keycode - Key.Key1)]);
    }

    bool perfRunning;

    async Task RunPerf(string dir)
    {
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        DirAccess.MakeDirRecursiveAbsolute(dir);
        var sb = new StringBuilder();
        sb.AppendLine("# Style lineup: a first-person scene per tier");
        sb.AppendLine();
        sb.AppendLine($"Godot {Engine.GetVersionInfo()["string"]}, {OS.GetProcessorName()}, {RenderingServer.GetVideoAdapterName()}, 1280x720, day, shadows on, vsync off. People walking round you (3-40 m; a quarter survivors), 40 animals (wolves and rabbits, walking), 900 trees (8-60 m, the game's LOD ranges capped at the tier's top), 300 items; a full turn in 5 s. Each LOD by height on screen, capped at the tier's top. Means over 300 frames after 60.");
        sb.AppendLine();
        sb.AppendLine("| tier | people | frame ms | fps | gpu ms | render cpu ms | primitives | draw calls | texture MB (character looks) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        perfRunning = true;
        foreach (var people in new[] { 40, 200 })
            foreach (var t in Tiers)
            {
                PerfScene(t, people);
                await Frames(60);
                double frameMs = 0, gpu = 0, cpu = 0, prims = 0, draws = 0;
                const int N = 300;
                ulong start = Godot.Time.GetTicksUsec();
                for (int i = 0; i < N; i++)
                {
                    await Frames(1);
                    var rid = GetViewport().GetViewportRid();
                    gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
                    cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid);
                    prims += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
                    draws += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
                }
                frameMs = (Godot.Time.GetTicksUsec() - start) / 1000.0 / N;
                double tex = TextureBytes() / 1048576.0;
                string line = $"| {t.Key} {t.Name} | {people} | {frameMs:0.00} | {1000 / frameMs:0} | {gpu / N:0.00} | {cpu / N:0.00} | {prims / N:N0} | {draws / N:0} | {tex:0.0} |";
                sb.AppendLine(line);
                GD.Print(line);
            }
        perfRunning = false;
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "perf.md"), sb.ToString());
        GD.Print($"style lineup: {dir}/perf.md");
        GetTree().Quit();
    }

    /// <summary>The colour and normal maps the people on stage use (each once), as loaded, with mipmaps.</summary>
    long TextureBytes()
    {
        long bytes = 0;
        var seen = new HashSet<Rid>();
        foreach (var b in bodies)
            foreach (var mi in b.Lods.SelectMany(g => g))
            {
                var texs = new List<Texture2D?>();
                if (mi.MaterialOverride is ShaderMaterial sm) texs.Add(sm.GetShaderParameter("tex").As<Texture2D>());
                else if ((mi.MaterialOverride ?? mi.Mesh.SurfaceGetMaterial(0)) is BaseMaterial3D bm) { texs.Add(bm.AlbedoTexture); texs.Add(bm.NormalTexture); }
                foreach (var tx in texs)
                    if (tx != null && seen.Add(tx.GetRid()) && tx.GetImage() is Image img) bytes += img.GetData().Length;
            }
        return bytes;
    }

    // ------------------------------------------------------------------ helpers

    static double H(int i, int salt)
    {
        uint h = 2166136261;
        h = (h ^ (uint)i) * 16777619;
        h = (h ^ (uint)salt) * 16777619;
        h ^= h >> 15; h *= 2246822519; h ^= h >> 13;
        return (h % 100003) / 100003.0;
    }

    static int Tris(MeshInstance3D mi) => Tris(mi.Mesh);

    static int Tris(Mesh mesh)
    {
        int n = 0;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            var idx = arrays[(int)Mesh.ArrayType.Index];
            n += (idx.VariantType != Variant.Type.Nil ? idx.AsInt32Array().Length : arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length) / 3;
        }
        return n;
    }

    static int LodIndex(StringName name)
    {
        // "body_lod1", or "body_lod1_001" / "body_lod1.001" (Blender's duplicate suffix): the digits after "lod".
        var s = name.ToString();
        int i = s.LastIndexOf("lod", StringComparison.Ordinal);
        if (i < 0) return 0;
        var digits = new string(s[(i + 3)..].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }
}
