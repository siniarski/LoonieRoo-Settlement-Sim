// The character lab, part three: a crowd for stress testing, with the LOD tiers of
// docs/characters.md section 7 (and the numbers from the anim bench, experiments/anim-bench):
//   near  the 14-mesh region build with an AnimationTree, updated every frame
//   mid   one mesh with an AnimationPlayer, updated at the chosen rate; dressed in the same random
//         outfit as near, merged into that one mesh (OutfitMerge.cs), or bare (--midbare)
//         (every frame, or 30/20/15 fps staggered across frames)
//   auto  near within the near radius (at most the near budget, nearest first), mid beyond;
//         re-sorted four times a second, with a metre of hysteresis so nobody flickers
// The crowd stands in rings round the lab's character (from 4.5 m out), walking or jogging in
// place, facing every which way. No foot IK on the crowd (that's near-tier polish for later).
//
//   --crowd=N --lod=near|mid|auto --rate=60|30|20|15

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static LoonieRoo.Game.Nodes;

namespace LoonieRoo.Game.Characters;

public partial class Lab
{
    const string MergedMannequin = "res://assets/characters/test_mannequin_merged.glb";
    static readonly string[] LodModes = { "near", "mid", "auto" };
    static readonly int[] Rates = { 60, 30, 20, 15 };

    sealed class Member
    {
        public Node3D? Node;
        public AnimationPlayer? Player;
        public int Tier = -1;  // 0 near, 1 mid
        public Vector3 Position;
        public float Heading, Gait, Phase;
    }

    readonly List<Member> crowd = new();
    bool midDressed = true;
    int crowdCount, lodMode = 2, rateIndex = 2, nearBudget = 40, ratePhase;
    double nearRadius = 8, crowdRebuildAt = -1, lodTimer, frameMsSmooth;
    PackedScene? regionScene, mergedScene;
    AnimationNodeBlendTree? crowdTreeRoot;
    Label crowdStats = null!;

    void BuildCrowdTab(VBoxContainer box)
    {
        Slider(box, "Humanoids", 0, 2000, 10, crowdCount, v => { crowdCount = (int)v; crowdRebuildAt = Time.GetTicksMsec() + 400; });
        var lodRow = new HBoxContainer();
        lodRow.AddChild(new Label { Text = "LOD", CustomMinimumSize = new Vector2(60, 0) });
        var lodPick = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach (var m in LodModes) lodPick.AddItem(m);
        lodPick.Selected = lodMode;
        lodPick.ItemSelected += v => { lodMode = (int)v; crowdRebuildAt = Time.GetTicksMsec(); };
        lodRow.AddChild(lodPick);
        lodRow.AddChild(new Label { Text = "  mid rate" });
        var ratePick = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach (var r in Rates) ratePick.AddItem(r == 60 ? "every frame" : $"{r} fps");
        ratePick.Selected = rateIndex;
        ratePick.ItemSelected += v => { rateIndex = (int)v; foreach (var m in crowd) SetRate(m); };
        lodRow.AddChild(ratePick);
        box.AddChild(lodRow);
        Slider(box, "Near budget", 0, 200, 1, nearBudget, v => nearBudget = (int)v);
        var dressBox = new CheckBox { Text = "Dress the mid tier (one merged mesh)", ButtonPressed = midDressed };
        dressBox.Toggled += on => { midDressed = on; crowdRebuildAt = Time.GetTicksMsec(); };
        box.AddChild(dressBox);
        Slider(box, "Near radius m", 2, 40, 0.5, nearRadius, v => nearRadius = v);
        crowdStats = new Label();
        crowdStats.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Consolas", "Courier New", "monospace" } });
        crowdStats.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(crowdStats);
        box.AddChild(new Label { Text = "near: 14 meshes + clothes + AnimationTree, every frame\nmid: 1 merged mesh + AnimationPlayer at the mid rate\nZoom out (wheel) to see them all.", Modulate = new Color(1, 1, 1, 0.6f) });
    }

    void SetupCrowd(Dictionary<string, string> args)
    {
        if (args.TryGetValue("crowd", out var c)) crowdCount = int.Parse(c);
        if (args.TryGetValue("lod", out var l)) lodMode = Math.Max(0, Array.IndexOf(LodModes, l));
        if (args.ContainsKey("midbare")) midDressed = false;
        if (args.TryGetValue("rate", out var r)) rateIndex = Math.Max(0, Array.IndexOf(Rates, int.Parse(r)));
        if (crowdCount > 0) RebuildCrowd();
    }

    void RebuildCrowd()
    {
        crowdRebuildAt = -1;
        foreach (var m in crowd) m.Node?.QueueFree();
        crowd.Clear();
        regionScene ??= GD.Load<PackedScene>(Mannequin);
        mergedScene ??= GD.Load<PackedScene>(MergedMannequin);
        // Rings of places from 4.5 m out, nearest first, 1.3 m apart.
        var places = new List<Vector3>();
        int reach = 4;
        while (places.Count < crowdCount)
        {
            places.Clear();
            for (int x = -reach; x <= reach; x++)
                for (int z = -reach; z <= reach; z++)
                {
                    var p = new Vector3(x * 1.3f, 0, z * 1.3f);
                    if (p.Length() >= 4.5f) places.Add(p);
                }
            reach += 4;
        }
        places = places.OrderBy(p => p.Length()).Take(crowdCount).ToList();
        for (int i = 0; i < places.Count; i++)
        {
            var p = places[i];
            p.Y = FootIK.Ground(actor, p).y;
            crowd.Add(new Member
            {
                Position = p,
                Heading = (float)(i * 2.39996 % (Math.PI * 2)),  // golden angle: every which way
                Gait = (float)(0.9 + 1.6 * (i * 0.618 % 1.0)),
                Phase = (float)(i * 0.37 % 1.0),
            });
        }
        AssignTiers(force: true);
    }

    void AssignTiers(bool force = false)
    {
        var focus = follow ? actor.GlobalPosition : Vector3.Zero;
        int nearLeft = nearBudget;
        foreach (var m in crowd.OrderBy(m => m.Position.DistanceSquaredTo(focus)))
        {
            int tier = lodMode switch
            {
                0 => 0,
                1 => 1,
                _ => nearLeft > 0 && m.Position.DistanceTo(focus) < nearRadius + (m.Tier == 0 ? 1 : 0) ? 0 : 1,
            };
            if (tier == 0) nearLeft--;
            if (tier != m.Tier || force) Spawn(m, tier);
        }
    }

    void Spawn(Member m, int tier)
    {
        m.Node?.QueueFree();
        var node = (tier == 0 ? regionScene! : mergedScene!).Instantiate<Node3D>();
        AddChild(node);
        node.Position = m.Position;
        node.Rotation = new Vector3(0, m.Heading, 0);
        var player = Find<AnimationPlayer>(node)!;
        var lib = player.GetAnimationLibrary("");
        foreach (var n in Looping) if (lib.HasAnimation(n)) lib.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
        string clip = m.Gait < 1.9 ? "walk" : "jog";
        if (tier == 0)
        {
            foreach (var mi in FindAll<MeshInstance3D>(node))
                if (mi.Name.ToString().StartsWith("stump_")) mi.Visible = false;
            DressCrowd(node, crowd.IndexOf(m));  // near: each in a random outfit
            crowdTreeRoot ??= CrowdTreeRoot();
            var tree = new AnimationTree { TreeRoot = crowdTreeRoot };
            tree.AddAnimationLibrary("", lib);
            node.AddChild(tree);
            tree.Active = true;
            tree.Set("parameters/space/blend_position", m.Gait);
            tree.Set("parameters/rate/scale", Lerp(StandRates, m.Gait));
            m.Player = null;
        }
        else
        {
            if (midDressed) DressMid(node, crowd.IndexOf(m));  // mid: the same random outfit, merged
            player.Play(clip);
            player.Seek(m.Phase, true);
            player.SpeedScale = (float)Lerp(StandRates, m.Gait);
            m.Player = player;
            SetRate(m);
        }
        m.Node = node;
        m.Tier = tier;
    }

    void SetRate(Member m)
    {
        if (m.Player == null) return;
        m.Player.CallbackModeProcess = Rates[rateIndex] == 60
            ? AnimationMixer.AnimationCallbackModeProcess.Idle : AnimationMixer.AnimationCallbackModeProcess.Manual;
    }

    static AnimationNodeBlendTree CrowdTreeRoot()
    {
        var space = new AnimationNodeBlendSpace1D { MinSpace = 0, MaxSpace = (float)MaxSpeed, Sync = true };
        foreach (var (clip, x) in new[] { ("idle", 0f), ("walk", 1.37f), ("jog", 2.4f), ("run", (float)MaxSpeed) })
            space.AddBlendPoint(new AnimationNodeAnimation { Animation = clip }, x);
        var root = new AnimationNodeBlendTree();
        root.AddNode("space", space);
        root.AddNode("rate", new AnimationNodeTimeScale());
        root.ConnectNode("rate", 0, "space");
        root.ConnectNode("output", 0, "rate");
        return root;
    }

    void UpdateCrowd(double delta)
    {
        if (crowdRebuildAt >= 0 && Time.GetTicksMsec() >= crowdRebuildAt) RebuildCrowd();
        lodTimer += delta;
        if (lodMode == 2 && lodTimer > 0.25) { lodTimer = 0; AssignTiers(); }
        // Mid players below 60: a 1/N share of them each frame, each advanced by N frames' worth.
        int every = Rates[rateIndex] == 60 ? 1 : (int)Math.Round(60.0 / Rates[rateIndex]);
        if (every > 1)
        {
            int i = 0;
            foreach (var m in crowd)
                if (m.Player != null && i++ % every == ratePhase) m.Player.Advance(delta * every);
            ratePhase = (ratePhase + 1) % every;
        }
        frameMsSmooth = frameMsSmooth * 0.95 + delta * 1000 * 0.05;
        if (trace && crowd.Count > 0 && (ulong)Time.GetTicksMsec() % 2000 < 17)
            GD.Print($"crowd: {crowd.Count} ({crowd.Count(m => m.Tier == 0)} near) frame {frameMsSmooth:0.0} ms, draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0}, merged {OutfitMerge.Merges} meshes in {OutfitMerge.MergeMs:0.0} ms (sources read once in {OutfitMerge.LoadMs:0} ms)");
        if (crowdStats != null)
        {
            int near = crowd.Count(m => m.Tier == 0), mid = crowd.Count - near;
            crowdStats.Text = $"humanoids  {crowd.Count + 1} ({near} near, {mid} mid, +you)\n" +
                              $"frame      {frameMsSmooth:0.0} ms ({Engine.GetFramesPerSecond():0} fps)\n" +
                              $"draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0}\n" +
                              $"merged     {OutfitMerge.Merges} meshes, {OutfitMerge.MergeMs:0.0} ms in all";
        }
    }
}
