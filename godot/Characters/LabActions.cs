// The character lab, part nine: weapon actions over the IK holds (docs/characters.md "Where the clips
// come from" and section 5). Each is one action with progress 0..1, the way the sim would time it;
// the weapon's hold is keyed through it and the hands follow by IK, so they stay on their grips:
//   swing   (bat, 0.75 s)   wind up behind the right shoulder, fast through the contact point in
//                           front at chest height, follow through to the left; the spine twists
//                           right then left with it
//   stab    (knife, 0.45 s) draw back, thrust forward and up, recover
//   fire    (pistol, rifle, 0.2 s)  recoil: the gun kicks back and the muzzle rises, then settles
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
        if (action is not ("swing" or "stab")) return;
        double twist = action == "swing"
            ? Keys(actionP, (0, 0), (0.30, -25), (0.55, 5), (0.75, 35), (1, 0))
            : Keys(actionP, (0, 0), (0.25, -10), (0.50, 15), (1, 0));
        var up = (xf.Basis.Inverse() * (charBasis * Vector3.Up)).Normalized();
        foreach (var (bone, share) in new[] { ("Spine", 0.35f), ("Chest", 0.35f), ("UpperChest", 0.3f) })
            Turn(skel, bone, up, Mathf.DegToRad((float)twist) * share);  // + turns to the left
    }

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
                var contact = ch * HoldAt(new Vector3(-0.02f, 1.14f, 0.40f), new Vector3(0.15f, 0.05f, 1f), Vector3.Up);
                var follow = ch * HoldAt(new Vector3(0.24f, 1.22f, 0.20f), new Vector3(0.85f, 0.35f, -0.4f), Vector3.Up);
                if (p < 0.30) return Lerp(hold, windup, p / 0.30);
                if (p < 0.75) return Path(p, (0.30, windup), (0.55, contact), (0.75, follow));
                return Lerp(follow, hold, (p - 0.75) / 0.25);
            }
            case "stab":
            {
                var chamber = ch * HoldAt(new Vector3(-0.26f, 1.06f, 0.18f), new Vector3(0, 0.3f, 1), Vector3.Up);
                var thrust = ch * HoldAt(new Vector3(-0.10f, 1.28f, 0.60f), new Vector3(0.1f, 0.05f, 1), Vector3.Up);
                if (p < 0.25) return Lerp(hold, chamber, p / 0.25);
                if (p < 0.60) return Path(p, (0.25, chamber), (0.50, thrust), (0.60, thrust));
                return Lerp(thrust, hold, (p - 0.60) / 0.40);
            }
            case "fire":
            {
                // A sharp kick (rising over 12% of it), then a slower settle.
                double k = p < 0.12 ? p / 0.12 : 1 - Smooth((p - 0.12) / 0.88);
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

    /// <summary>The left hand's grip while an action needs it elsewhere (a reload), else null.</summary>
    Node3D? ActionLeftGrip() => action == "reload" ? reloadGrip : null;
}
