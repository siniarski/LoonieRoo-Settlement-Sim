// The character lab, part five: survivor states on the body and face (docs/characters.md "States").
// The sim already has the numbers (BodyMod: panic and endurance 0..100, body heat; Health and
// Wounds for pain); a startle would be an event (a zombie suddenly seen, a hit taken). Here they
// are sliders and a button. Each state is layers in the AnimationTree over whatever clip plays:
//   panic    stiff: shoulders up, arms held close and bent (less swing), quick shallow breaths,
//            the head glancing round; face: brows worried (inner ends up, drawn together), eyes wide, mouth tight
//   fatigue  sluggish (low endurance): slumped, head down, arms hanging slack, heavy slow heaves;
//            slower to speed up, slower top speed, slower to crouch; face: heavy lids, mouth slack
//   cold     hunched, arms in, shivering; face: tight mouth
//   pain     face only here (a limp is a clip's job): brows down, eyes narrowed, a grimace
//   startle  a flinch that comes and goes in about a second: shoulders and head jerk back, arms
//            thrown up; face: brows up, eyes wide, mouth open
// Postures are additive clips (Add2: their difference from the rest pose), arm holds replace the
// clip's arms through a filter (Blend2), breathing and shivering are additive loops with their
// own rate. Faces are blend shapes on the head mesh, mixed here, with blinking. A zombie skips it
// all: a fixed slack face, no states (cheap, as zombies should be).
//
//   --panic=0..1 --fatigue=0..1 --cold=0..1 --pain=0..1 --startle --zombie --view=face

using System;
using System.Collections.Generic;
using Godot;

namespace LoonieRoo.Game.Characters;

public partial class Lab
{
    double panic, fatigue, cold, pain;
    double startleAt = -10, blinkTimer = 2, blinkLeft;
    bool zombie, startleOnShot;
    MeshInstance3D? headMesh;
    readonly Dictionary<string, int> faceShapes = new();
    static readonly string[] FaceShapes = { "brows_up", "brows_frown", "brows_worry", "eyes_wide", "eyes_heavy", "mouth_open", "mouth_frown", "mouth_tight" };
    static readonly string[] ArmBones = { "UpperArm", "LowerArm", "Hand", "Thumb", "Index", "Middle", "Ring", "Little" };
    readonly List<HSlider> stateSliders = new();

    void SetupBody(Dictionary<string, string> args)
    {
        if (args.TryGetValue("panic", out var p)) panic = Num(p);
        if (args.TryGetValue("fatigue", out var f)) fatigue = Num(f);
        if (args.TryGetValue("cold", out var c)) cold = Num(c);
        if (args.TryGetValue("pain", out var q)) pain = Num(q);
        startleOnShot = args.ContainsKey("startle");  // flinch just before the screenshot
        zombie = args.ContainsKey("zombie");
        headMesh = actor.FindChild("head", true, false) as MeshInstance3D;
        if (headMesh != null)
            foreach (var s in FaceShapes)
            {
                int i = headMesh.FindBlendShapeByName(s);
                if (i >= 0) faceShapes[s] = i;
            }
    }

    void BuildBodyTab(VBoxContainer box)
    {
        box.AddChild(new Label { Text = "States (the sim's panic, endurance, body heat, pain)" });
        stateSliders.Add(Slider(box, "Panic", 0, 1, 0.01, panic, v => panic = v));
        stateSliders.Add(Slider(box, "Fatigue", 0, 1, 0.01, fatigue, v => fatigue = v));
        stateSliders.Add(Slider(box, "Cold", 0, 1, 0.01, cold, v => cold = v));
        stateSliders.Add(Slider(box, "Pain", 0, 1, 0.01, pain, v => pain = v));
        var row = new HBoxContainer();
        row.AddChild(Btn("Startle!", () => startleAt = Time.GetTicksMsec() / 1000.0));
        var zbox = new CheckBox { Text = "Zombie (no states)", ButtonPressed = zombie };
        zbox.Toggled += on => zombie = on;
        row.AddChild(zbox);
        row.AddChild(Btn("Face view", () => SetView("face")));
        box.AddChild(row);
    }

    // ------------------------------------------------------------------ the tree's layers

    /// <summary>Everything over the state machine: arm holds, stump layers, postures, breathing. Returns the last node.</summary>
    string BuildLayers(AnimationNodeBlendTree root, string skelPath)
    {
        string last = "sm";
        void Chain(string name, AnimationNode node, string clip, string? rate = null)
        {
            root.AddNode(name, node);
            root.AddNode(name + "Clip", new AnimationNodeAnimation { Animation = clip });
            string input = name + "Clip";
            if (rate != null)
            {
                root.AddNode(rate, new AnimationNodeTimeScale());
                root.ConnectNode(rate, 0, input);
                input = rate;
            }
            root.ConnectNode(name, 0, last);
            root.ConnectNode(name, 1, input);
            last = name;
        }
        AnimationNodeBlend2 Arms(params string[] sides)
        {
            var b = new AnimationNodeBlend2 { FilterEnabled = true };
            foreach (var side in sides)
                foreach (var bone in ArmBones)
                    foreach (var n in BonesStartingWith(side + bone)) b.SetFilterPath(skelPath + ":" + n, true);
            return b;
        }
        // Arm holds replace the clip's arms (later ones win over earlier ones).
        Chain("tuck", Arms("Left", "Right"), "arms_tucked");
        Chain("hang", Arms("Left", "Right"), "arms_hang");
        Chain("flinch", Arms("Left", "Right"), "arms_flinch");
        foreach (var side in Sides) Chain("stump" + side, Arms(side), "stump_hold_" + side.ToLowerInvariant());
        // Postures and loops add on top.
        Chain("addPanic", new AnimationNodeAdd2(), "posture_panic");
        Chain("addTired", new AnimationNodeAdd2(), "posture_tired");
        Chain("addCold", new AnimationNodeAdd2(), "posture_cold");
        Chain("addStartle", new AnimationNodeAdd2(), "posture_startle");
        Chain("addBreathShallow", new AnimationNodeAdd2(), "breath_shallow", "breathShallowRate");
        Chain("addBreathHeavy", new AnimationNodeAdd2(), "breath_heavy", "breathHeavyRate");
        Chain("addShiver", new AnimationNodeAdd2(), "shiver", "shiverRate");
        Chain("addScan", new AnimationNodeAdd2(), "scan", "scanRate");
        return last;
    }

    double Startle()
    {
        double t = Time.GetTicksMsec() / 1000.0 - startleAt;
        if (t < 0 || t > 1.4) return 0;
        if (t < 0.06) return t / 0.06;
        if (t < 0.25) return 1;
        return Math.Exp(-(t - 0.25) * 4.5);
    }

    // The states' effect on movement (the sim's job in the game: here so the lab can show it).
    double AccelScale() => zombie ? 1 : Math.Max(0.25, 1 + 0.4 * panic - 0.6 * fatigue);
    double SpeedCapScale() => zombie ? 1 : 1 - 0.45 * fatigue;
    double CrouchTimeScale() => zombie ? 1 : 1 + fatigue;

    void UpdateBody(double delta)
    {
        if (tree == null) return;
        if (startleOnShot && frame == shotFrames - 12) startleAt = Time.GetTicksMsec() / 1000.0;
        double p = zombie ? 0 : panic, f = zombie ? 0 : fatigue, c = zombie ? 0 : cold, q = zombie ? 0 : pain, e = zombie ? 0 : Startle();
        void Set(string node, string param, double v) => tree.Set($"parameters/{node}/{param}", v);
        double tuck = Math.Max(0.65 * p, 0.8 * c);
        Set("tuck", "blend_amount", tuck);
        Set("hang", "blend_amount", 0.6 * f * (1 - tuck));
        Set("flinch", "blend_amount", e);
        Set("addPanic", "add_amount", p);
        Set("addTired", "add_amount", f);
        Set("addCold", "add_amount", c);
        Set("addStartle", "add_amount", e);
        Set("addBreathShallow", "add_amount", Math.Max(p, 0.5 * c));
        Set("breathShallowRate", "scale", 1.4 + 0.8 * p);
        Set("addBreathHeavy", "add_amount", f);
        Set("breathHeavyRate", "scale", 0.55 + 0.35 * f);
        Set("addShiver", "add_amount", c);
        Set("shiverRate", "scale", 6.0);
        Set("addScan", "add_amount", 0.9 * p);
        Set("scanRate", "scale", 1 + 0.6 * p);
        UpdateFace(delta, p, f, c, q, e);
    }

    void UpdateFace(double delta, double p, double f, double c, double q, double e)
    {
        if (headMesh == null) return;
        // Blinks: often when panicked, slow and long when tired, none for the dead.
        double blink = 0;
        if (!zombie)
        {
            blinkTimer -= delta;
            if (blinkTimer <= 0)
            {
                blinkLeft = 0.12 + 0.2 * f;
                blinkTimer = blinkLeft + (p > 0.5 ? 0.8 + 1.2 * GD.Randf() : 2.5 + 3 * GD.Randf() + 2 * f);
            }
            if (blinkLeft > 0) { blinkLeft -= delta; blink = 1; }
        }
        var w = new Dictionary<string, double>
        {
            ["brows_up"] = Math.Max(e, 0.4 * p),
            ["brows_frown"] = q,
            ["brows_worry"] = Math.Max(0.9 * p, 0.4 * c),
            ["eyes_wide"] = Math.Max(e, 0.8 * p) * (1 - blink),
            ["eyes_heavy"] = Math.Max(Math.Max(0.6 * f, 0.35 * q), blink),
            ["mouth_open"] = Math.Max(0.8 * e, Math.Max(0.3 * p, 0.35 * f * (1 - q))),
            ["mouth_frown"] = Math.Min(1, 0.6 * q + 0.2 * c),
            ["mouth_tight"] = Math.Max(0.7 * q, Math.Max(0.3 * p, 0.5 * c)),
        };
        if (zombie) w = new() { ["brows_up"] = 0, ["brows_frown"] = 0.4, ["brows_worry"] = 0, ["eyes_wide"] = 0, ["eyes_heavy"] = 0.35, ["mouth_open"] = 0.65, ["mouth_frown"] = 0.3, ["mouth_tight"] = 0 };
        foreach (var (name, idx) in faceShapes) headMesh.SetBlendShapeValue(idx, (float)Math.Clamp(w[name], 0, 1));
    }

    string BodyReadout()
    {
        if (zombie) return "body     zombie (no states)";
        var parts = new List<string>();
        if (panic > 0.01) parts.Add($"panic {panic:0.00}");
        if (fatigue > 0.01) parts.Add($"fatigue {fatigue:0.00}");
        if (cold > 0.01) parts.Add($"cold {cold:0.00}");
        if (pain > 0.01) parts.Add($"pain {pain:0.00}");
        if (Startle() > 0.01) parts.Add("startled");
        return parts.Count == 0 ? "" : "body     " + string.Join(", ", parts);
    }
}
