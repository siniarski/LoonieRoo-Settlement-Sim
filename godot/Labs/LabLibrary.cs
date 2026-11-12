// The lab's Library tab: clips retargeted from a third-party library onto our rig
// (art/scripts/retarget.py; docs/characters.md "Where the clips come from") played on the mannequin,
// to judge the retarget by eye. A picked clip replaces everything the tree plays (a Blend2 at the
// top, after the lab's own layers); the character stands still meanwhile (the clips are in place).
//
//   --clip=ual_walk          start playing that clip
//   --clipat=SECONDS         seek there and hold the pose (for screenshots)

using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using static LoonieRoo.Game.Nodes;

namespace LoonieRoo.Game.Characters;

public partial class Lab
{
    const string LibraryClips = "res://assets/characters/clips_ual.glb";
    const string LibraryName = "lib";

    readonly List<string> libClips = new();
    OptionButton libPick = null!;
    string? libClip;
    double libRate = 1;
    bool libHold;

    bool LibraryOn => libClip != null;

    // After the lab's layers: lib (Blend2) = [layers, seek -> rate -> clip].
    string BuildLibraryLayer(AnimationNodeBlendTree root, string last)
    {
        // Never left without a clip: an AnimationNodeAnimation with none stops the whole tree (the
        // character stood in its rest pose) even at blend 0. "idle" until a library clip is picked.
        root.AddNode("libClip", new AnimationNodeAnimation { Animation = "idle" });
        root.AddNode("libRate", new AnimationNodeTimeScale());
        root.AddNode("libSeek", new AnimationNodeTimeSeek());
        root.AddNode("lib", new AnimationNodeBlend2());
        root.ConnectNode("libRate", 0, "libClip");
        root.ConnectNode("libSeek", 0, "libRate");
        root.ConnectNode("lib", 0, last);
        root.ConnectNode("lib", 1, "libSeek");
        return "lib";
    }

    void SetupLibrary(Dictionary<string, string> args)
    {
        if (!ResourceLoader.Exists(LibraryClips)) return;
        var scene = GD.Load<PackedScene>(LibraryClips).Instantiate<Node3D>();
        var lib = Find<AnimationPlayer>(scene)!.GetAnimationLibrary("");
        var loops = new HashSet<string>();
        var meta = FileAccess.GetFileAsString(LibraryClips.Replace(".glb", ".json"));
        if (meta.Length > 0)
            foreach (var c in JsonDocument.Parse(meta).RootElement.GetProperty("clips").EnumerateArray())
                if (c.GetProperty("loop").GetBoolean()) loops.Add(c.GetProperty("name").GetString()!);
        foreach (var name in lib.GetAnimationList())
        {
            lib.GetAnimation(name).LoopMode = loops.Contains(name) ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
            libClips.Add(name);
        }
        libClips.Sort(StringComparer.Ordinal);
        tree.AddAnimationLibrary(LibraryName, lib);
        scene.Free();
        if (args.TryGetValue("clip", out var clip)) PlayLibrary(clip);
        if (args.TryGetValue("clipat", out var at) && libClip != null)
        {
            libHold = true;
            tree.Set("parameters/libSeek/seek_request", Num(at));
        }
    }

    void PlayLibrary(string? clip)
    {
        libClip = clip != null && libClips.Contains(clip) ? clip : null;
        var node = (AnimationNodeAnimation)((AnimationNodeBlendTree)tree.TreeRoot).GetNode("libClip");
        if (libClip != null)
        {
            node.Animation = LibraryName + "/" + libClip;
            tree.Set("parameters/libSeek/seek_request", 0.0);
        }
        tree.Set("parameters/lib/blend_amount", libClip != null ? 1.0 : 0.0);
    }

    void BuildLibraryTab(VBoxContainer box)
    {
        box.AddChild(new Label { Text = libClips.Count > 0 ? $"{libClips.Count} clips retargeted from the Universal Animation\nLibrary (Quaternius, CC0) onto our rig." : "No library clips (art/scripts/retarget.py).", Modulate = new Color(1, 1, 1, 0.6f) });
        libPick = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        libPick.AddItem("(off: the lab's own clips)");
        foreach (var c in libClips) libPick.AddItem(c);
        libPick.Selected = libClip == null ? 0 : libClips.IndexOf(libClip) + 1;
        libPick.ItemSelected += i => { libHold = false; PlayLibrary(i == 0 ? null : libClips[(int)i - 1]); };
        box.AddChild(libPick);
        var row = new HBoxContainer();
        row.AddChild(Btn("Prev", () => Step(-1)));
        row.AddChild(Btn("Next", () => Step(1)));
        row.AddChild(Btn("Restart", () => { libHold = false; PlayLibrary(libClip); }));
        box.AddChild(row);
        Slider(box, "Clip speed", 0, 2, 0.05, libRate, v => libRate = v);

        void Step(int d)
        {
            if (libClips.Count == 0) return;
            int i = libClip == null ? (d > 0 ? 0 : libClips.Count - 1) : (libClips.IndexOf(libClip) + d + libClips.Count) % libClips.Count;
            libHold = false;
            PlayLibrary(libClips[i]);
            libPick.Selected = i + 1;
        }
    }

    /// <summary>A library clip blended over the lab's tree by an amount (the car's seated clip), without restarting it.</summary>
    void LibraryBlend(string clip, double amount)
    {
        if (libClip != clip) PlayLibrary(clip);
        libHold = false;
        tree.Set("parameters/lib/blend_amount", amount);
    }

    void UpdateLibrary() => tree.Set("parameters/libRate/scale", libHold ? 0.0 : libRate);

    string LibraryReadout() => libClip == null ? "" : $"clip     {libClip}" + (libHold ? " (held)" : "");
}
