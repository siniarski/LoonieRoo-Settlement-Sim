// What you hold, in first person (2026-11-09): the weapon you wield in your right hand, the tool you carry
// in your left (lowered, at the edge of the view), or what you carry in both hands, drawn with the item
// models (Items.cs: the same variant as on the ground) and a gloved hand round each grip (the "hand" model,
// a stand-in until the first-person arms rig of docs/characters.md). Reads state only (rule 5): the weapon,
// the tool and what you carry (the slots PoseIntents reads for carry); a swing from control.swungTick (a melee blow over
// SWING_TICKS; a gun kicks back); a little bob as you walk. Hidden in a car, asleep, looking from --eye, or in a
// timed action (eating, searching...).
//
// The whole of it is drawn at SCALE of its size, that much nearer the eye: on screen it looks the same
// (a perspective projection doesn't change under a scale about the eye), but nothing reaches out past the
// body's 0.3 m circle, so it never pokes into a wall you stand against.
//
// Debug (after `--`): --held=weapon_v1|axe|gun|... draws that model in your right hand whatever you hold,
// --heldtool=hammer|medkit|... in your left;
// --swingat=P holds a swing (or a gun's kick) at P (0..1) for a screenshot.

using System;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class Held
{
    const float SCALE = 0.3f;
    /// <summary>How long a melee swing is drawn (ticks: 10 a second), and a gun's kick.</summary>
    const double SWING_TICKS = 4, KICK_TICKS = 2.5;

    /// <summary>A hold: where the grip is in the camera's space (before SCALE: m, x right, y up, -z ahead),
    /// which way the handle runs from the grip (the model's +X), which way the forearm goes (the hand's +Z),
    /// and which way the item's working side faces (an axe's blade, a hammer's face, a rifle's top: FaceOf).</summary>
    readonly record struct Pose(Vector3 at, Vector3 handle, Vector3 arm, Vector3 face)
    {
        /// <summary>The hand's frame, and the item's turn (in the camera's space).</summary>
        public (Transform3D hand, Basis item) Frames(string kind)
        {
            var x = handle.Normalized();
            var z = (arm - x * arm.Dot(x)).Normalized();
            var hand = new Basis(x, z.Cross(x), z);
            var axis = FaceOf(kind);
            if (axis == Vector3.Zero) return (new Transform3D(hand, at), hand);
            var f = (face - x * face.Dot(x)).Normalized();
            // Face along the item's +Y: Z = X x Y; along its +-Z: Y = Z x X.
            Basis item = axis.Y != 0 ? new Basis(x, f, x.Cross(f)) : new Basis(x, (f * axis.Z).Cross(x), f * axis.Z);
            return (new Transform3D(hand, at), item);
        }
    }

    /// <summary>Which of a model's own axes is its working side (Zero: it turns with the hand).</summary>
    static Vector3 FaceOf(string kind) => kind switch
    {
        "axe" => new(0, 0, 1),
        "hammer" => new(0, 0, -1),
        "gun" => new(0, 1, 0),
        _ => Vector3.Zero,
    };

    /// <summary>Where each model is gripped (its own space, m: x along it, y up) for a hand.</summary>
    static Vector3 GripOf(string kind) => kind switch
    {
        "weapon" => new(-0.29f, 0.035f, 0),
        "axe" => new(-0.27f, 0.022f, 0),
        "hammer" => new(-0.09f, 0.018f, 0),
        "gun" => new(-0.1f, 0.03f, 0),
        "medkit" => new(0, 0.075f, 0.12f),
        _ => Vector3.Zero,
    };

    // The right hand's holds for a club, axe or hammer: at rest (up before you, on the right), drawn back
    // over the shoulder, and through the blow (down and across to the left).
    static readonly Pose REST = new(new(0.3f, -0.24f, -0.58f), new(0.4f, 0.8f, -0.4f), new(0.4f, -0.6f, 0.6f), new(-0.7f, 0, -0.7f));
    static readonly Pose WIND = new(new(0.38f, -0.12f, -0.48f), new(0.55f, 0.7f, 0.35f), new(0.4f, -0.7f, 0.4f), new(-0.6f, 0.4f, -0.6f));
    static readonly Pose STRIKE = new(new(0.02f, -0.28f, -0.62f), new(-0.7f, 0.05f, -0.7f), new(0.55f, -0.35f, 0.75f), new(-0.3f, -0.6f, -0.7f));
    // A rifle at the low ready: its muzzle ahead and a little left and down, the stock under your arm.
    static readonly Pose GUN = new(new(0.2f, -0.24f, -0.42f), new(-0.1f, -0.06f, -1f), new(0.35f, -0.75f, 0.45f), new(0, 1, 0));
    // The left hand's tool, lowered at the edge of the view; a case hangs from its handle at your side
    // (its top in view, the hand on the handle below the view's edge: not drawn, its forearm would stand up).
    static readonly Pose TOOL = new(new(-0.38f, -0.3f, -0.55f), new(0.1f, 0.5f, -0.85f), new(-0.3f, -0.7f, 0.6f), new(0.2f, 0, -1));
    static readonly Pose CASE = new(new(-0.42f, -0.27f, -0.55f), new(0.15f, 0, -1), new(0, 1, 0), Vector3.Zero);

    sealed class Slot
    {
        public readonly Node3D grip = new();
        public readonly MeshInstance3D item = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        public readonly MeshInstance3D hand = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        public string model = "";
    }

    readonly ItemModels models;
    readonly Node3D root = new() { Scale = Vector3.One * SCALE };
    readonly Slot right = new(), left = new(), both = new();
    readonly string? forceModel, forceTool;
    readonly double swingAt = -1;
    double bob;

    /// <summary>Whether a hand (the glove round a grip) is drawn on the right, or on the left: your own body (OwnBody)
    /// hides its arm on that side, so there aren't two.</summary>
    public bool RightHand => root.Visible && right.grip.Visible && right.hand.Visible;
    public bool LeftHand => root.Visible && left.grip.Visible && left.hand.Visible;

    /// <summary>What's drawn in your right hand, or "" (the self-test and the debug panel).</summary>
    public string Right => root.Visible && right.grip.Visible ? right.model : "";

    public Held(Camera3D camera, ItemModels models)
    {
        this.models = models;
        camera.AddChild(root);
        foreach (var s in new[] { right, left, both })
        {
            root.AddChild(s.grip);
            s.grip.AddChild(s.item);
            s.grip.AddChild(s.hand);
            s.hand.Mesh = models.MeshFor("hand");
            s.grip.Visible = false;
        }
        both.hand.Visible = false;
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            if (kv.Length < 2) continue;
            if (kv[0] == "held") forceModel = kv[1];
            if (kv[0] == "heldtool") forceTool = kv[1];
            if (kv[0] == "swingat") swingAt = double.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public void Update(SimState state, Survivor you, double alpha, double delta, bool hidden)
    {
        var c = state.control;
        root.Visible = !hidden && you.sleep?.asleep != true && c?.busy == null;
        if (!root.Visible) return;
        var weapon = Js.T(you.weapon) ? Sim.Items.getItem(state, you.weapon) : null;
        var tool = Js.T(you.tool) ? Sim.Items.getItem(state, you.tool) : null;
        var carried = Js.T(you.carrying) ? Sim.Items.getItem(state, you.carrying) : null;
        string rightModel = forceModel ?? (weapon != null ? models.ModelOf(weapon) : "");
        string rightKind = Kind(rightModel);

        // Walking bobs the hands a little; standing still lets them settle.
        double moved = Math.Sqrt((double)(you.at.x - you.was.x) * (you.at.x - you.was.x) + (double)(you.at.y - you.was.y) * (you.at.y - you.was.y)) / Motion.SUB;
        bob += moved * 5.5 * delta * 10;
        float amp = (float)Math.Min(1, moved * 8);
        var sway = new Vector3((float)Math.Sin(bob) * 0.012f, -(float)Math.Abs(Math.Cos(bob)) * 0.014f, 0) * amp;

        // The blow: 0..1 since the last swing (or held for a screenshot).
        double age = swingAt >= 0 ? swingAt * (rightKind == "gun" ? KICK_TICKS : SWING_TICKS) : state.tick + alpha - (c?.swungTick ?? -99);
        (Transform3D hand, Basis item) r;
        if (rightKind == "gun")
        {
            r = GUN.Frames(rightKind);
            if (age >= 0 && age < KICK_TICKS)
            {
                float k = (float)Math.Sin(Math.PI * Math.Pow(age / KICK_TICKS, 0.6));
                var up = new Basis(new Vector3(1, 0, 0), 0.18f * k);
                r = (new Transform3D(up * r.hand.Basis, r.hand.Origin + new Vector3(0, 0.02f, 0.07f) * k), up * r.item);
            }
        }
        else r = age >= 0 && age < SWING_TICKS ? Swing(age / SWING_TICKS, rightKind) : REST.Frames(rightKind);
        Show(right, rightModel, r, sway);

        // The left hand: a tool, lowered (with nothing in the right, the same).
        string toolModel = forceTool ?? (tool != null ? models.ModelOf(tool) : "");
        Show(left, toolModel, (Kind(toolModel) == "medkit" ? CASE : TOOL).Frames(Kind(toolModel)), sway);

        // Carried in both hands: the thing itself, low before you and tipped towards you; no hands drawn.
        string carriedModel = carried != null ? models.ModelOf(carried) : "";
        var tip = new Basis(new Vector3(1, 0, 0), 0.5f);
        Show(both, carriedModel, (new Transform3D(tip, new Vector3(0, -0.5f, -0.6f)), tip), sway, grip: false);
    }

    void Show(Slot s, string model, (Transform3D hand, Basis item) frames, Vector3 sway, bool grip = true)
    {
        var mesh = model == "" ? null : models.MeshFor(model);
        s.grip.Visible = mesh != null;
        if (mesh == null) return;
        if (s.model != model)
        {
            s.model = model;
            s.item.Mesh = mesh;
        }
        var (hand, item) = frames;
        s.hand.Visible = grip && Kind(model) != "medkit";
        s.grip.Transform = new Transform3D(hand.Basis, hand.Origin + sway);
        // The item turned as the hold says, its grip point on the hand's origin.
        var local = hand.Basis.Inverse() * item;
        s.item.Transform = new Transform3D(local, -(local * (grip ? GripOf(Kind(model)) : MidOf(mesh))));
    }

    /// <summary>A blow at t (0..1): drawn back, then down and across, then back to rest.</summary>
    static (Transform3D, Basis) Swing(double t, string kind)
    {
        (Transform3D, Basis) Blend(Pose a, Pose b, double t)
        {
            float u = (float)(t * t * (3 - 2 * t));
            var (ha, ia) = a.Frames(kind);
            var (hb, ib) = b.Frames(kind);
            var q = ha.Basis.GetRotationQuaternion().Slerp(hb.Basis.GetRotationQuaternion(), u);
            var qi = ia.GetRotationQuaternion().Slerp(ib.GetRotationQuaternion(), u);
            return (new Transform3D(new Basis(q), ha.Origin.Lerp(hb.Origin, u)), new Basis(qi));
        }
        if (t < 0.3) return Blend(REST, WIND, t / 0.3);
        if (t < 0.55) return Blend(WIND, STRIKE, (t - 0.3) / 0.25);
        return Blend(STRIKE, REST, (t - 0.55) / 0.45);
    }

    static string Kind(string model)
    {
        int v = model.LastIndexOf("_v", StringComparison.Ordinal);
        return v > 0 ? model[..v] : model;
    }

    static Vector3 MidOf(Mesh mesh)
    {
        var b = mesh.GetAabb();
        return b.Position + b.Size * 0.5f;
    }
}
