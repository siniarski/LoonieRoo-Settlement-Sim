// The character lab, part nine: weapon actions over the IK holds (docs/characters.md "Where the clips
// come from" and section 5). Each is one action with progress 0..1, the way the sim would time it;
// the weapon's hold is keyed through it and the hands follow by IK, so they stay on their grips:
//   swing   (bat, 0.75 s)   wind up behind the right shoulder (quick out, slowing into the top: the
//                           bat's weight cocked), a beat there, then accelerating round a high arc on the
//                           right and through the contact point in front at chest height at full speed,
//                           carried on past the follow-through by its weight, and back; the spine twists
//                           with it, the body leans into the blow and the head stays on the target
//   stab    (knife, 0.45 s) drawn back (and down a little), driven forward and up, fastest at full reach,
//                           a little past it, back
//   fire    (pistol, rifle, 0.2 s)  recoil: a sharp kick back and up, a damped settle with one small
//                           dip below the aim, the shoulders taking some of it
// Each segment has its own ease (2026-11-10): out of a key fast and into the next slowly (Out: a wind-up
// arriving at its top), into it fast (In: accelerating into contact), straight through (Linear), or smooth.
// Before, every key was a smoothstep stop: the bat came to rest at the contact point (2% of its peak speed).
//   reload  (pistol 1.8 s, rifle 2.4 s)  the gun tips in; the left hand goes to the magazine well,
//                           pulls the magazine, fetches a new one from the left hip, seats it and
//                           returns to its grip (a magazine prop rides in the gun or the hand)
// Keys are in the character's space (+z ahead, +x its left) or the weapon's; between them, smoothstep.
//
//   --action=swing|stab|fire|reload [--actionat=P]   start one (held at progress P)

using System;
using System.Collections.Generic;
using Godot;

namespace LoonieRoo.Game.Characters;

public partial class Lab
{
    string? action;          // null, or the action playing
    double actionP, actionDur = 1, actionHoldAt = -1;
    Node3D? reloadGrip;      // the left hand's target while reloading
    readonly Dictionary<string, (MeshInstance3D inGun, MeshInstance3D inHand)> mags = new();

    static double ActionDuration(string kind, string w) => kind switch
    {
        "swing" => 0.75, "stab" => 0.45, "fire" => 0.2, "reload" => w == "rifle" ? 2.4 : 1.8, _ => 1,
    };

    static bool ActionFits(string kind, string w) => kind switch
    {
        "swing" => w == "bat", "stab" => w == "knife", "fire" or "reload" => w is "pistol" or "rifle", _ => false,
    };

    void SetupActions(Dictionary<string, string> args)
    {
        reloadGrip = new Node3D { Name = "ReloadGrip" };
        AddChild(reloadGrip);
        foreach (var (w, well, size) in new[] { ("pistol", PistolWell, new Vector3(0.022f, 0.10f, 0.03f)), ("rifle", RifleWell, new Vector3(0.025f, 0.16f, 0.06f)) })
        {
            var inGun = Part(new BoxMesh { Size = size }, well + new Vector3(0, -size.Y / 2 + 0.02f, 0), new Color(0.12f, 0.12f, 0.13f));
            weaponNodes[w].AddChild(inGun);
            var inHand = Part(new BoxMesh { Size = size }, Vector3.Zero, new Color(0.12f, 0.12f, 0.13f));
            inHand.Visible = false;
            AddChild(inHand);
            mags[w] = (inGun, inHand);
        }
        if (args.TryGetValue("action", out var a)) CallDeferred(nameof(StartAction), a);
        if (args.ContainsKey("actioncurve")) CallDeferred(nameof(ActionCurves));
        if (args.TryGetValue("actionat", out var at)) actionHoldAt = Num(at);
    }

    // Magazine wells (where a magazine seats), in the weapon's space.
    static readonly Vector3 PistolWell = new(0, -0.08f, -0.02f), RifleWell = new(0, -0.07f, 0.40f);

    void StartAction(string kind)
    {
        if (!ActionFits(kind, weapon)) return;
        action = kind;
        actionP = actionHoldAt >= 0 ? actionHoldAt : 0;
        actionDur = ActionDuration(kind, weapon);
    }

    void BuildActionRow(VBoxContainer box)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Action", CustomMinimumSize = new Vector2(60, 0) });
        foreach (var k in new[] { "swing", "stab", "fire", "reload" })
            row.AddChild(Btn(k, () => StartAction(k)));
        box.AddChild(row);
        box.AddChild(new Label { Text = "swing: bat; stab: knife; fire and reload: guns", Modulate = new Color(1, 1, 1, 0.6f) });
    }

    void UpdateAction(double delta)
    {
        if (action == null) return;
        if (!ActionFits(action, weapon)) { action = null; return; }
        if (actionHoldAt >= 0) actionP = actionHoldAt;
        else
        {
            actionP += delta / actionDur;
            if (actionP >= 1) { action = null; actionP = 0; }
        }
    }

    string ActionReadout() => action == null ? "" : $"action   {action} {actionP:0.00} ({actionDur:0.00} s)";

    static Transform3D HoldAt(Vector3 at, Vector3 dir, Vector3 up)
    {
        dir = dir.Normalized();
        var y = (up - dir * up.Dot(dir)).Normalized();
        return new Transform3D(new Basis(y.Cross(dir), y, dir), at);
    }

    static Transform3D Lerp(Transform3D a, Transform3D b, double t) => a.InterpolateWith(b, (float)Smooth(t));

    enum Ease { Smooth, In, Out, Linear }
    static double Eased(Ease e, double u) => e switch { Ease.In => u * u, Ease.Out => 1 - (1 - u) * (1 - u), Ease.Linear => u, _ => Smooth(u) };

    /// <summary>A keyed path of transforms over progress, each segment with the ease it arrives at its key by.</summary>
    static Transform3D PathE(double p, params (double p, Transform3D t, Ease e)[] keys)
    {
        if (p <= keys[0].p) return keys[0].t;
        for (int i = 1; i < keys.Length; i++)
            if (p <= keys[i].p)
            {
                double u = (p - keys[i - 1].p) / Math.Max(keys[i].p - keys[i - 1].p, 1e-6);
                return keys[i - 1].t.InterpolateWith(keys[i].t, (float)Eased(keys[i].e, u));
            }
        return keys[^1].t;
    }

    /// <summary>Numbers over progress, each segment with the ease it arrives at its key by.</summary>
    static double KeysE(double p, params (double p, double v, Ease e)[] keys)
    {
        if (p <= keys[0].p) return keys[0].v;
        for (int i = 1; i < keys.Length; i++)
            if (p <= keys[i].p)
            {
                double u = (p - keys[i - 1].p) / Math.Max(keys[i].p - keys[i - 1].p, 1e-6);
                return keys[i - 1].v + (keys[i].v - keys[i - 1].v) * Eased(keys[i].e, u);
            }
        return keys[^1].v;
    }

    /// <summary>A keyed path of transforms over progress (each segment eased).</summary>
    static Transform3D Path(double p, params (double p, Transform3D t)[] keys)
    {
        if (p <= keys[0].p) return keys[0].t;
        for (int i = 1; i < keys.Length; i++)
            if (p <= keys[i].p) return Lerp(keys[i - 1].t, keys[i].t, (p - keys[i - 1].p) / Math.Max(keys[i].p - keys[i - 1].p, 1e-6));
        return keys[^1].t;
    }

    /// <summary>Inside AimPose, before the hold: the body's part (a twist of the spine for swings and stabs).</summary>
    void ActionBody(Skeleton3D skel, Transform3D xf, Basis charBasis)
    {
        if (action is not ("swing" or "stab" or "fire")) return;
        double p = actionP;
        // The twist (+ to the left), the lean (+ forward: into the blow, back winding up; a gun's kick back).
        double twist = action switch
        {
            "swing" => KeysE(p, (0, 0, Ease.Smooth), (0.26, -26, Ease.Out), (0.36, -32, Ease.Smooth), (0.50, -10, Ease.In),
                (SWING_CONTACT, 6, Ease.Linear), (0.68, 30, Ease.Out), (0.76, 38, Ease.Out), (1, 0, Ease.Smooth)),
            "stab" => KeysE(p, (0, 0, Ease.Smooth), (0.22, -12, Ease.Out), (0.30, -14, Ease.Smooth), (STAB_CONTACT, 10, Ease.In),
                (0.48, 16, Ease.Out), (0.56, 18, Ease.Smooth), (1, 0, Ease.Smooth)),
            _ => 0,
        };
        double lean = action switch
        {
            "swing" => KeysE(p, (0, 0, Ease.Smooth), (0.36, -5, Ease.Out), (SWING_CONTACT, 9, Ease.In), (0.76, 6, Ease.Out), (1, 0, Ease.Smooth)),
            "stab" => KeysE(p, (0, 0, Ease.Smooth), (0.30, -4, Ease.Out), (STAB_CONTACT, 8, Ease.In), (1, 0, Ease.Smooth)),
            _ => -3.5 * Kick(p),
        };
        var up = (xf.Basis.Inverse() * (charBasis * Vector3.Up)).Normalized();
        var left = (xf.Basis.Inverse() * (charBasis * Vector3.Right)).Normalized();
        foreach (var (bone, share) in new[] { ("Spine", 0.35f), ("Chest", 0.35f), ("UpperChest", 0.3f) })
        {
            Turn(skel, bone, up, Mathf.DegToRad((float)twist) * share);  // + turns to the left
            Turn(skel, bone, left, Mathf.DegToRad((float)lean) * share);  // + about the left leans forward
        }
        // The head stays on what's struck: it turns back most of the twist.
        Turn(skel, "Head", up, -Mathf.DegToRad((float)twist) * 0.6f);
    }

    /// <summary>A gun's kick over the shot's progress: back and up over 8% of it, then a damped settle with one small dip.</summary>
    static double Kick(double p) => p < 0.08 ? 1 - (1 - p / 0.08) * (1 - p / 0.08) : Math.Exp(-5 * (p - 0.08) / 0.92) * Math.Cos(Math.PI * 1.4 * (p - 0.08) / 0.92);

    /// <summary>Inside AimPose: the action's hold, from the one the weapon would have.</summary>
    Transform3D ActionHold(Transform3D hold)
    {
        if (action == null) { ShowMags(); return hold; }
        var ch = actor.GlobalTransform;  // keys in the character's space
        double p = actionP;
        switch (action)
        {
            case "swing":
            {
                var windup = ch * HoldAt(new Vector3(-0.24f, 1.34f, 0.0f), new Vector3(-0.45f, 0.7f, -0.55f), Vector3.Back);
                var cocked = ch * HoldAt(new Vector3(-0.27f, 1.38f, -0.03f), new Vector3(-0.5f, 0.75f, -0.45f), Vector3.Back);
                var arc = ch * HoldAt(new Vector3(-0.18f, 1.24f, 0.30f), new Vector3(-0.30f, 0.30f, 0.90f), Vector3.Up);
                var contact = ch * HoldAt(new Vector3(-0.02f, 1.14f, 0.40f), new Vector3(0.15f, 0.05f, 1f), Vector3.Up);
                var follow = ch * HoldAt(new Vector3(0.24f, 1.22f, 0.20f), new Vector3(0.85f, 0.35f, -0.4f), Vector3.Up);
                var over = ch * HoldAt(new Vector3(0.28f, 1.18f, 0.10f), new Vector3(0.75f, 0.25f, -0.6f), Vector3.Up);
                return PathE(p, (0, hold, Ease.Smooth), (0.26, windup, Ease.Out), (0.36, cocked, Ease.Smooth), (0.50, arc, Ease.In),
                    (SWING_CONTACT, contact, Ease.Linear), (0.68, follow, Ease.Out), (0.76, over, Ease.Out), (1, hold, Ease.Smooth));
            }
            case "stab":
            {
                var chamber = ch * HoldAt(new Vector3(-0.26f, 1.06f, 0.18f), new Vector3(0, 0.3f, 1), Vector3.Up);
                var drawn = ch * HoldAt(new Vector3(-0.27f, 1.02f, 0.14f), new Vector3(0, 0.35f, 1), Vector3.Up);
                var thrust = ch * HoldAt(new Vector3(-0.10f, 1.28f, 0.60f), new Vector3(0.1f, 0.05f, 1), Vector3.Up);
                var past = ch * HoldAt(new Vector3(-0.09f, 1.30f, 0.63f), new Vector3(0.1f, 0.03f, 1), Vector3.Up);
                // Driven in fastest where the blade would meet a body (70% of the reach), slowing into full reach.
                var meet = drawn.InterpolateWith(thrust, 0.7f);
                return PathE(p, (0, hold, Ease.Smooth), (0.22, chamber, Ease.Out), (0.30, drawn, Ease.Smooth), (STAB_CONTACT, meet, Ease.In),
                    (0.48, thrust, Ease.Out), (0.54, past, Ease.Smooth), (0.64, thrust, Ease.Smooth), (1, hold, Ease.Smooth));
            }
            case "fire":
            {
                double k = Kick(p);
                bool rifle = weapon == "rifle";
                float back = (rifle ? 0.04f : 0.03f) * (float)k, rise = Mathf.DegToRad(rifle ? 5 : 11) * (float)k;
                var dir = hold.Basis.Z.Normalized();
                var axis = dir.Cross(Vector3.Up).Normalized();  // turning dir about dir x up by + lifts it
                var pivot = hold * new Vector3(0, -0.03f, rifle ? 0.30f : 0);  // about the firing hand
                var turned = new Transform3D(new Basis(axis, rise), Vector3.Zero);
                var t = new Transform3D(Basis.Identity, pivot) * turned * new Transform3D(Basis.Identity, -pivot) * hold;
                return new Transform3D(t.Basis, t.Origin - dir * back);
            }
            case "reload":
            {
                // The gun tips in (rolled 30 deg, nose up 12, a little towards the middle).
                double w = Keys(p, (0, 0), (0.12, 1), (0.85, 1), (1, 0));
                bool rifle = weapon == "rifle";
                var dir = hold.Basis.Z.Normalized();
                var pivot = hold * new Vector3(0, -0.03f, rifle ? 0.30f : 0);
                var tip = new Basis(dir, Mathf.DegToRad(-30) * (float)w) * new Basis(dir.Cross(Vector3.Up).Normalized(), Mathf.DegToRad(12) * (float)w);
                var t = new Transform3D(Basis.Identity, pivot) * new Transform3D(tip, Vector3.Zero) * new Transform3D(Basis.Identity, -pivot) * hold;
                t.Origin += ch.Basis * new Vector3(0.05f, 0, 0) * (float)w;
                PlaceReloadHand(t, p);
                return t;
            }
        }
        return hold;
    }

    /// <summary>The left hand's path while reloading, from the gun as it is this frame.</summary>
    void PlaceReloadHand(Transform3D gun, double p)
    {
        bool rifle = weapon == "rifle";
        var w = weaponNodes[weapon];
        var gripLeft = gun * w.GetNode<Node3D>("grip_left").Transform;
        var well = rifle ? RifleWell : PistolWell;
        // Palm up under the well, fingers ahead; the mag out 12 cm below; the pouch at the left hip.
        var atWell = gun * HoldGrip(well + new Vector3(0, -0.04f, 0), Vector3.Up, Vector3.Back);
        var below = new Transform3D(atWell.Basis, atWell.Origin + Vector3.Down * 0.12f);
        var pouch = actor.GlobalTransform * HoldGrip(new Vector3(0.17f, 0.96f, 0.10f), Vector3.Left, Vector3.Down);
        reloadGrip!.GlobalTransform = Path(p, (0, gripLeft), (0.12, atWell), (0.22, below), (0.42, pouch), (0.52, pouch),
            (0.70, below), (0.80, atWell), (1.0, gripLeft));
        // The magazine: in the gun until it's pulled; in the hand from the pouch until it's seated.
        var (inGun, inHand) = mags[weapon];
        inGun.Visible = p < 0.15 || p > 0.80;
        inHand.Visible = p > 0.44 && p <= 0.80;
        if (inHand.Visible)
            inHand.GlobalTransform = new Transform3D(gun.Basis, reloadGrip.GlobalPosition + reloadGrip.GlobalBasis.X * 0.03f + reloadGrip.GlobalBasis.X * (rifle ? 0.08f : 0.05f));
    }

    void ShowMags()
    {
        foreach (var (inGun, inHand) in mags.Values) { inGun.Visible = true; inHand.Visible = false; }
    }

    /// <summary>A grip's transform (palm normal, fingers) at a point, as GripAt builds them.</summary>
    static Transform3D HoldGrip(Vector3 at, Vector3 palm, Vector3 fingers)
    {
        var f = fingers.Normalized();
        var pn = (palm - f * palm.Dot(f)).Normalized();
        return new Transform3D(new Basis(pn, f, pn.Cross(f)), at);
    }

    /// <summary>The swing's and stab's contact (the bat through the point in front, the knife at full reach), as progress.</summary>
    static double ContactAt(string kind) => kind == "swing" ? SWING_CONTACT : STAB_CONTACT;

    /// <summary>--actioncurve: the held weapon's path over each action (its hold's origin, sampled at 400 steps of
    /// progress over the action's own time): the peak speed and where it falls, the speed at contact against it,
    /// and how long the strike takes from the top of the wind-up to contact. A keyed hold eased to a stop at every
    /// key halts the weapon at the very moment it lands; a blow with weight is fastest there.</summary>
    void ActionCurves()
    {
        var lines = new System.Text.StringBuilder();
        foreach (var kind in new[] { "swing", "stab" })
        {
            action = kind;
            actionDur = ActionDuration(kind, kind == "swing" ? "bat" : "knife");
            var rest = actor.GlobalTransform * HoldAt(new Vector3(-0.10f, 1.10f, 0.30f), Vector3.Back, Vector3.Up);
            int n = 400;
            var at = new Vector3[n + 1];
            for (int i = 0; i <= n; i++) { actionP = (double)i / n; at[i] = ActionHold(rest).Origin; }
            double peak = 0, peakP = 0, contact = ContactAt(kind), atContact = 0, top = 0, topV = double.MaxValue;
            for (int i = 1; i <= n; i++)
            {
                double p = (i - 0.5) / n, v = (at[i] - at[i - 1]).Length() / (actionDur / n);
                if (v > peak) { peak = v; peakP = p; }
                if (Math.Abs(p - contact) < 0.5 / n + 1e-9) atContact = v;
                if (p > 0.15 && p < contact && v < topV) { topV = v; top = p; }
            }
            string line = $"actioncurve: {kind} {actionDur:0.00} s: peak {peak:0.0} m/s at {peakP:0.00}, at contact ({contact:0.00}) {atContact:0.0} m/s ({100 * atContact / Math.Max(peak, 1e-6):0}% of the peak), the top of the wind-up at {top:0.00} ({topV:0.0} m/s), {(contact - top) * actionDur * 1000:0} ms from there to contact";
            GD.Print(line);
            lines.AppendLine(line);
        }
        action = null;
        GetTree().Quit();
    }

    const double SWING_CONTACT = 0.56, STAB_CONTACT = 0.41;

    /// <summary>The left hand's grip while an action needs it elsewhere (a reload), else null.</summary>
    Node3D? ActionLeftGrip() => action == "reload" ? reloadGrip : null;
}
