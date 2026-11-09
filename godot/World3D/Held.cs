// What you hold, in first person (2026-11-09): the weapon you wield in your right hand, the tool you carry
// in your left (lowered, at the edge of the view), or what you carry in both hands, drawn with the item
// models (Items.cs: the same variant as on the ground) and a gloved hand and sleeve round each grip (the
// "hand" model; the left hand is the same model mirrored). A rifle and an axe take both hands: the left on
// the fore-end or up the handle (Support), following the item through a swing or a kick; the tool is put away
// meanwhile. Reads state only (rule 5): the weapon, the tool and what you carry; a swing from
// control.swungTick (a melee blow over SWING_TICKS: wound up, struck, carried through, back; a gun kicks back);
// running from control.running. Hidden in a car, asleep, looking from --eye, or in a timed action (eating,
// searching...).
//
// Motion, all render-side: a walking bob (the distance walked, a dip each step), a slow breath standing
// still, a lag behind the look as you turn (the hands trail the camera a little and catch up), the weapon
// lowered and turned in while you run, and a raise from below whenever what's in a hand changes.
//
// The whole of it is drawn at SCALE of its size, that much nearer the eye: on screen it looks the same
// (a perspective projection doesn't change under a scale about the eye), but nothing reaches out past the
// body's 0.3 m circle, so it never pokes into a wall you stand against.
//
// Debug (after `--`): --held=weapon_v1|axe|gun|... draws that model in your right hand whatever you hold,
// --heldtool=hammer|medkit|... in your left;
// --swingat=P holds a swing (or a gun's kick) at P (0..1) for a screenshot; --raiseat=P holds the raise.

using System;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class Held
{
    const float SCALE = 0.3f;
    /// <summary>How long a melee swing is drawn (ticks: 10 a second; the sim swings at most every 10), and a gun's kick.</summary>
    const double SWING_TICKS = 6, KICK_TICKS = 2.5;
    /// <summary>How long a raise takes (s): an item coming into a hand.</summary>
    const double RAISE_S = 0.35;
    /// <summary>Where the left shoulder is (camera space, before SCALE): a support hand's forearm reaches back towards it.</summary>
    static readonly Vector3 LEFT_SHOULDER = new(-0.22f, -0.42f, 0.05f);

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
        "axe" => new(-0.3f, 0.022f, 0),
        "hammer" => new(-0.09f, 0.018f, 0),
        "gun" => new(-0.1f, 0.03f, 0),
        "medkit" => new(0, 0.075f, 0.12f),
        _ => Vector3.Zero,
    };

    /// <summary>Where the left hand holds a two-handed item (its own space), or null: one hand.</summary>
    static Vector3? SupportOf(string kind) => kind switch
    {
        "gun" => new(0.2f, 0.028f, 0),
        "axe" => new(-0.16f, 0.022f, 0),
        _ => null,
    };

    // The right hand's holds for a club or hammer: at rest (up before you, on the right), drawn back over
    // the shoulder, through the blow (down and across to the left), and carried on past it.
    static readonly Pose REST = new(new(0.3f, -0.24f, -0.58f), new(0.4f, 0.8f, -0.4f), new(0.4f, -0.6f, 0.6f), new(-0.7f, 0, -0.7f));
    static readonly Pose WIND = new(new(0.4f, -0.1f, -0.44f), new(0.55f, 0.7f, 0.45f), new(0.4f, -0.7f, 0.4f), new(-0.6f, 0.4f, -0.6f));
    static readonly Pose STRIKE = new(new(0.02f, -0.28f, -0.62f), new(-0.7f, 0.05f, -0.7f), new(0.55f, -0.35f, 0.75f), new(-0.3f, -0.6f, -0.7f));
    static readonly Pose FOLLOW = new(new(-0.08f, -0.31f, -0.6f), new(-0.8f, -0.3f, -0.5f), new(0.6f, -0.2f, 0.75f), new(-0.2f, -0.8f, -0.3f));
    // An axe in both hands: across the body, the head up on the left, the right hand low near the handle's end.
    static readonly Pose AXE_REST = new(new(0.2f, -0.44f, -0.62f), new(-0.3f, 0.85f, -0.3f), new(0.5f, -0.5f, 0.6f), new(-0.3f, 0.1f, -0.9f));
    static readonly Pose AXE_WIND = new(new(0.3f, -0.24f, -0.66f), new(0.15f, 0.9f, 0.45f), new(0.5f, -0.7f, 0.4f), new(-0.2f, 0.6f, -0.6f));
    static readonly Pose AXE_STRIKE = new(new(0.06f, -0.3f, -0.58f), new(-0.2f, 0.12f, -1f), new(0.5f, -0.45f, 0.7f), new(0, -0.9f, -0.3f));
    static readonly Pose AXE_FOLLOW = new(new(0.02f, -0.35f, -0.62f), new(-0.35f, -0.45f, -0.8f), new(0.5f, -0.3f, 0.8f), new(0, -0.7f, 0.6f));
    // A rifle at the low ready in both hands: the muzzle ahead, a little left and down, the stock under your arm.
    static readonly Pose GUN = new(new(0.15f, -0.23f, -0.42f), new(-0.14f, -0.08f, -1f), new(0.35f, -0.75f, 0.45f), new(0, 1, 0));
    // The left hand's tool, lowered at the edge of the view; a case hangs from its handle at your side
    // (its top edge in view, the hand on the handle below the view's edge: not drawn, its forearm would stand up).
    static readonly Pose TOOL = new(new(-0.38f, -0.3f, -0.55f), new(0.1f, 0.5f, -0.85f), new(-0.3f, -0.7f, 0.6f), new(0.2f, 0, -1));
    static readonly Pose CASE = new(new(-0.4f, -0.42f, -0.5f), new(0.25f, 0, -1), new(0, 1, 0), Vector3.Zero);

    sealed class Slot
    {
        public readonly Node3D grip = new();
        public readonly MeshInstance3D item = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        public readonly MeshInstance3D hand = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        public string model = "";
        /// <summary>Seconds since what's in this hand last changed (for the raise).</summary>
        public double since = RAISE_S;
    }

    readonly ItemModels models;
    readonly Camera3D camera;
    readonly Node3D root = new() { Scale = Vector3.One * SCALE };
    readonly Slot right = new(), left = new(), both = new();
    /// <summary>The left hand on a two-handed item (a hand only, no item).</summary>
    readonly MeshInstance3D support = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
    readonly string? forceModel, forceTool;
    readonly double swingAt = -1, raiseAt = -1;
    double bob, breath, run;
    Vector3 lastFwd;
    Basis runTilt = Basis.Identity;
    Vector2 lag;

    /// <summary>Whether a hand (the glove round a grip) is drawn on the right, or on the left: your own body (OwnBody)
    /// hides its arm on that side, so there aren't two.</summary>
    public bool RightHand => root.Visible && right.grip.Visible && right.hand.Visible;
    public bool LeftHand => root.Visible && (support.Visible || left.grip.Visible && left.hand.Visible);

    /// <summary>What's drawn in your right hand, or "" (the self-test and the debug panel).</summary>
    public string Right => root.Visible && right.grip.Visible ? right.model : "";
    /// <summary>Whether the left hand is on a two-handed item (the self-test).</summary>
    public bool TwoHanded => root.Visible && support.Visible;
    /// <summary>Where the right hand is now (camera space, before SCALE: the self-test sees a swing move it).</summary>
    public Vector3 RightAt => right.grip.Position;

    public Held(Camera3D camera, ItemModels models)
    {
        this.models = models;
        this.camera = camera;
        camera.AddChild(root);
        foreach (var s in new[] { right, left, both })
        {
            root.AddChild(s.grip);
            s.grip.AddChild(s.item);
            s.grip.AddChild(s.hand);
            s.hand.Mesh = models.MeshFor("hand");
            s.grip.Visible = false;
        }
        root.AddChild(support);
        support.Mesh = models.MeshFor("hand");
        both.hand.Visible = false;
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            if (kv.Length < 2) continue;
            if (kv[0] == "held") forceModel = kv[1];
            if (kv[0] == "heldtool") forceTool = kv[1];
            if (kv[0] == "swingat") swingAt = double.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
            if (kv[0] == "raiseat") raiseAt = double.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public void Update(SimState state, Survivor you, double alpha, double delta, bool hidden)
    {
        var c = state.control;
        root.Visible = !hidden && you.sleep?.asleep != true && (c?.busy == null || Leaps.airborne(c)); // busy hands put it away; a jump keeps it in hand
        if (!root.Visible)
        {
            right.since = left.since = both.since = 0; // taken out again, it's raised
            return;
        }
        var weapon = Js.T(you.weapon) ? Sim.Items.getItem(state, you.weapon) : null;
        var tool = Js.T(you.tool) ? Sim.Items.getItem(state, you.tool) : null;
        var carried = Js.T(you.carrying) ? Sim.Items.getItem(state, you.carrying) : null;
        string rightModel = forceModel ?? (weapon != null ? models.ModelOf(weapon) : "");
        string rightKind = Kind(rightModel);
        bool twoHands = SupportOf(rightKind) != null && models.MeshFor(rightModel) != null;

        // Walking bobs the hands, a dip each step (a step every ~0.55 m); standing still they breathe.
        double moved = Math.Sqrt((double)(you.at.x - you.was.x) * (you.at.x - you.was.x) + (double)(you.at.y - you.was.y) * (you.at.y - you.was.y)) / Motion.SUB;
        double perTick = moved; // tiles this tick (walking 0.25, running ~0.44)
        bob += perTick * 10 * delta * Math.PI / 0.55;
        breath += delta * 1.7;
        float amp = (float)Math.Min(1, perTick * 4.5);
        float still = 1 - amp;
        // Running lowers the weapon and turns it in (eased both ways).
        bool running = c?.running == true && perTick > 0.3;
        run += ((running ? 1 : 0) - run) * Math.Min(1, delta * 8);
        float bobK = 1 + (float)run * 0.8f;
        var sway = new Vector3((float)Math.Sin(bob) * 0.014f * bobK, -(float)Math.Abs(Math.Cos(bob)) * 0.02f * bobK, 0) * amp
            + new Vector3(0, (float)Math.Sin(breath) * 0.004f, 0) * still;

        // The hands trail the look: the camera's turn this frame, eased back to rest.
        var fwd = -camera.GlobalBasis.Z;
        if (lastFwd != Vector3.Zero && delta > 0)
        {
            float dYaw = Mathf.Wrap(Mathf.Atan2(fwd.X, -fwd.Z) - Mathf.Atan2(lastFwd.X, -lastFwd.Z), -Mathf.Pi, Mathf.Pi);
            float dPitch = Mathf.Asin(Math.Clamp(fwd.Y, -1, 1)) - Mathf.Asin(Math.Clamp(lastFwd.Y, -1, 1));
            lag += new Vector2(dYaw, -dPitch) * 0.6f;
        }
        lastFwd = fwd;
        lag = lag.Lerp(Vector2.Zero, (float)Math.Min(1, delta * 10));
        lag = lag.Clamp(new Vector2(-0.12f, -0.1f), new Vector2(0.12f, 0.1f));
        // The whole set: trailing the look, lowered while running.
        float rk = (float)(run * run * (3 - 2 * run));
        var set = new Basis(Vector3.Up, lag.X) * new Basis(Vector3.Right, lag.Y)
            * new Basis(Vector3.Up, 0.12f * rk);
        root.Transform = new Transform3D(set.Scaled(Vector3.One * SCALE), new Vector3(-0.006f, -0.012f, 0.004f) * rk);
        // Running, each item tips forward and in round its grip (the hand stays in view); a rifle swings
        // its muzzle in and down across you instead (tipped forward, its stock would come up at your eye).
        runTilt = new Basis(Vector3.Up, 0.35f * rk) * new Basis(Vector3.Right, -0.75f * rk);
        var gunTilt = new Basis(Vector3.Up, 0.45f * rk) * new Basis(Vector3.Right, -0.12f * rk);

        // The blow: 0..1 since the last swing (or held for a screenshot).
        double age = swingAt >= 0 ? swingAt * (rightKind == "gun" ? KICK_TICKS : SWING_TICKS) : state.tick + alpha - (c?.swungTick ?? -99);
        bool swinging = rightKind != "gun" && age >= 0 && age < SWING_TICKS;
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
        else if (rightKind == "axe")
            r = swinging ? Swing(age / SWING_TICKS, rightKind, AXE_REST, AXE_WIND, AXE_STRIKE, AXE_FOLLOW) : AXE_REST.Frames(rightKind);
        else r = swinging ? Swing(age / SWING_TICKS, rightKind, REST, WIND, STRIKE, FOLLOW) : REST.Frames(rightKind);
        if (rightKind == "gun") r = (new Transform3D(gunTilt * r.hand.Basis, r.hand.Origin + new Vector3(0, -0.04f, 0) * rk), gunTilt * r.item);
        Show(right, rightModel, r, sway, delta, tilt: rightKind != "gun");
        PlaceSupport(twoHands ? rightKind : null);

        // The left hand: a tool, lowered (put away while both hands are on a rifle or an axe; ducked out of
        // the way during a swing).
        string toolModel = twoHands ? "" : forceTool ?? (tool != null ? models.ModelOf(tool) : "");
        var toolSway = sway;
        if (swinging) toolSway += new Vector3(-0.04f, -0.12f, 0.04f) * (float)Math.Sin(Math.PI * age / SWING_TICKS);
        Show(left, toolModel, (Kind(toolModel) == "medkit" ? CASE : TOOL).Frames(Kind(toolModel)), toolSway, delta);

        // Carried in both hands: the thing itself, low before you and tipped towards you; no hands drawn.
        string carriedModel = carried != null ? models.ModelOf(carried) : "";
        var tip = new Basis(new Vector3(1, 0, 0), 0.5f);
        Show(both, carriedModel, (new Transform3D(tip, new Vector3(0, -0.5f, -0.6f)), tip), sway, delta, grip: false);
    }

    void Show(Slot s, string model, (Transform3D hand, Basis item) frames, Vector3 sway, double delta, bool grip = true, bool tilt = true)
    {
        var mesh = model == "" ? null : models.MeshFor(model);
        s.grip.Visible = mesh != null;
        if (mesh == null) { s.model = ""; return; }
        if (s.model != model)
        {
            s.model = model;
            s.item.Mesh = mesh;
            s.since = 0;
        }
        s.since += delta;
        var (hand, item) = frames;
        s.hand.Visible = grip && Kind(model) != "medkit";
        // The raise: from below and tipped back, eased out (or held for a screenshot).
        double t = raiseAt >= 0 ? raiseAt : Math.Min(1, s.since / RAISE_S);
        float down = (float)Math.Pow(1 - t, 2.2);
        var lift = new Basis(Vector3.Right, -0.9f * down);
        s.grip.Transform = new Transform3D(lift * (tilt ? runTilt : Basis.Identity) * hand.Basis, lift * hand.Origin + sway + new Vector3(0.04f, -0.22f, 0.08f) * down);
        // The item turned as the hold says, its grip point on the hand's origin.
        var local = hand.Basis.Inverse() * item;
        s.item.Transform = new Transform3D(local, -(local * (grip ? GripOf(Kind(model)) : MidOf(mesh))));
    }

    /// <summary>The left hand on a two-handed item: at its support point, the item's way, the forearm back
    /// towards the left shoulder; the right hand's model mirrored.</summary>
    void PlaceSupport(string? kind)
    {
        var at = kind == null ? null : SupportOf(kind);
        support.Visible = at != null && right.grip.Visible;
        if (!support.Visible) return;
        var item = right.grip.Transform * right.item.Transform;
        var p = item * at!.Value;
        var h = (item.Basis * Vector3.Right).Normalized();
        var arm = LEFT_SHOULDER - p;
        // Mirrored: the frame built with the handle reversed, then its X flipped back, so the index finger
        // points along the item (towards the muzzle or the head) and the thumb wraps the other way.
        var x = -h;
        var z = (arm - x * arm.Dot(x)).Normalized();
        var b = new Basis(x, z.Cross(x), z) * Basis.FromScale(new Vector3(-1, 1, 1));
        support.Transform = new Transform3D(b, p);
    }

    /// <summary>A blow at t (0..1): wound up (eased, a beat held at the top), struck (accelerating),
    /// carried through past the strike (slowing), then back to rest.</summary>
    static (Transform3D, Basis) Swing(double t, string kind, Pose rest, Pose wind, Pose strike, Pose follow)
    {
        (Transform3D, Basis) Blend(Pose a, Pose b, double u)
        {
            float w = (float)u;
            var (ha, ia) = a.Frames(kind);
            var (hb, ib) = b.Frames(kind);
            var q = ha.Basis.GetRotationQuaternion().Slerp(hb.Basis.GetRotationQuaternion(), w);
            var qi = ia.GetRotationQuaternion().Slerp(ib.GetRotationQuaternion(), w);
            return (new Transform3D(new Basis(q), ha.Origin.Lerp(hb.Origin, w)), new Basis(qi));
        }
        static double Smooth(double u) => u * u * (3 - 2 * u);
        if (t < 0.2) return Blend(rest, wind, 1 - Math.Pow(1 - t / 0.2, 2.5));
        if (t < 0.26) return Blend(wind, wind, 0);
        if (t < 0.4) return Blend(wind, strike, Math.Pow((t - 0.26) / 0.14, 1.6));
        if (t < 0.52) return Blend(strike, follow, 1 - Math.Pow(1 - (t - 0.4) / 0.12, 2));
        return Blend(follow, rest, Smooth((t - 0.52) / 0.48));
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
