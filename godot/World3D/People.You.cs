// Your own body under the camera (part of People: People.cs): the same model as everyone's, posed from your pose
// intent, its legs the way you walk and its chest the way you look, a little behind the eye so looking down is over
// the chest; its head hidden in first person (OwnBody.cs cuts it), shown from outside (--eye). A second copy casts
// your shadow. Standing on a car, its feet are on the car's top. Render-side only.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LoonieRoo.Game.Characters;
using LoonieRoo.Sim;
using static LoonieRoo.Game.IdHash;
using static LoonieRoo.Game.Nodes;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.World3D;

public sealed partial class People
{
    /// <summary>How far behind the eye the body stands (m), so looking down the camera is over the chest, not in the neck.</summary>
    const float OWN_BACK = 0.22f;
    /// <summary>Most the chest turns from the legs (radians), and how far you look round before the legs follow, standing.</summary>
    const float OWN_TWIST = 1.2f, OWN_TURN_AT = 0.9f;
    Model? own, ownShadow;
    OwnBody? ownMod;
    string ownLook = "";
    float ownYaw = float.NaN;
    double ownHeading;

    /// <summary>How far your body is let down into the water, swimming (m, 0 or less: FirstPerson's sink, the eye's).</summary>
    public float YourSink;

    /// <summary>Your own body: shown standing or sneaking, posed like everyone's, its legs the way you walk and its
    /// chest the way you look (camYaw, FirstPerson's: the look along (cos, sin) on the grid); its head hidden unless
    /// you look from outside (fromOutside: --eye).</summary>
    public void UpdateYou(SimState state, Survivor you, double alpha, double camYaw, double delta, bool show, bool fromOutside, bool rightHeld = false, bool leftHeld = false)
    {
        var pose = PoseIntents.ofSurvivor(state, you);
        roofUnder = Leaps.carOf(state, state.control);
        show &= pose.stance is "stand" or "sneak";
        // Swimming, the body is seen only from outside (first person has the stroke's hands: SwimHands.cs).
        bool swim = Swim.swimming(state, you);
        show &= !swim || fromOutside;
        var hair = Looks.LivingOutfit(you.id);
        string look = LivingLook(you.id) + Lost(pose);
        if (show && look + "#" + hair != ownLook)
        {
            own?.root.QueueFree();
            ownShadow?.root.QueueFree();
            ForgetHands(ownLeap);  // a vault's hand IK was on the old skeletons
            ForgetHands(shadowLeap);
            own = Make(look);
            ownShadow = Make(look);
            own.own = ownShadow.own = true;
            Dress(own, hair);
            Dress(ownShadow, hair);
            foreach (var mi in FindAll<MeshInstance3D>(own.root)) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            foreach (var mi in FindAll<MeshInstance3D>(ownShadow.root)) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
            ownMod = new OwnBody();
            Find<Skeleton3D>(own.root)!.AddChild(ownMod);
            own.phase = ownShadow.phase = 0;
            ownLook = look + "#" + hair;
            ownYaw = float.NaN;
        }
        if (own == null || ownShadow == null || ownMod == null) return;
        own.root.Visible = ownShadow.root.Visible = show;
        if (!show) { own.watch.Reset(); return; }

        // The legs: the way you walk (backing off: facing you, the walk run backwards); standing, they follow
        // the look once it's turned far enough. The chest makes up the rest, up to OWN_TWIST.
        float lookYaw = (float)Math.Atan2(Math.Cos(camYaw), Math.Sin(camYaw));
        var w = new Wanted(you.id, look, hair, you, pose, 0, false, "");
        Hold(own, w, fromOutside && !swim);  // in first person your hands are Held.cs's; swimming, it is put away
        // A leap: the jump clips, and lifted with the eye (People.Leap.cs).
        double lift = LeapPre(own, ownLeap, state, you, alpha, delta, !leftHeld, !rightHeld);
        LeapPre(ownShadow, shadowLeap, state, you, alpha, delta, !leftHeld, !rightHeld);
        SwimPre(own, swim, pose, delta);
        SwimPre(ownShadow, swim, pose, delta);
        Pose(own, w, alpha, delta, 0.5, 720);
        Pose(ownShadow, w, alpha, delta, 0.5, 720);
        bool moving = own.speed > 0.3;
        bool back = false;
        float want;
        // The way you walk: the last heading you moved along (a tick that didn't move you says 0, which isn't a way).
        if (pose.speed > 0.15) ownHeading = pose.heading;
        if (moving)
        {
            float walk = (float)Math.Atan2(Math.Cos(ownHeading), Math.Sin(ownHeading));
            back = Math.Abs(Mathf.AngleDifference(lookYaw, walk)) > 1.75f;
            float legs = back ? walk + Mathf.Pi : walk;
            // The legs turn at most OWN_TWIST from the look (the chest makes up the rest): strafing walks them half
            // sideways rather than crossing the chest over.
            want = lookYaw + Math.Clamp(Mathf.AngleDifference(lookYaw, legs), -OWN_TWIST, OWN_TWIST);
        }
        else want = float.IsNaN(ownYaw) || Math.Abs(Mathf.AngleDifference(ownYaw, lookYaw)) > OWN_TURN_AT ? lookYaw : ownYaw;
        if (LeapYaw() is float over) { want = over; back = false; }
        if (swim && moving) { want = (float)Math.Atan2(Math.Cos(ownHeading), Math.Sin(ownHeading)); back = false; }
        float before = ownYaw;
        ownYaw = float.IsNaN(ownYaw) ? want : Mathf.LerpAngle(ownYaw, want, (float)Math.Min(1, delta * (LeapYaw() != null ? 16 : 8)));
        if (!float.IsNaN(before) && delta > 0)
            foreach (var m in new[] { own, ownShadow }) Turning(m, Mathf.AngleDifference(before, ownYaw) / delta, delta);
        float twist = Math.Clamp(Mathf.AngleDifference(ownYaw, lookYaw), -OWN_TWIST, OWN_TWIST);
        ownMod.Twist = swim ? 0 : twist;
        ownMod.HideHead = !fromOutside;
        ownMod.HideRightArm = rightHeld && !fromOutside;
        ownMod.HideLeftArm = leftHeld && !fromOutside;
        var behind = new Vector3((float)-Math.Cos(camYaw), 0, (float)-Math.Sin(camYaw)) * (fromOutside ? 0 : OWN_BACK);
        foreach (var m in new[] { own, ownShadow })
        {
            m.yaw = ownYaw;
            m.root.Rotation = new Vector3(0, ownYaw, 0);
            m.root.Position += behind + Vector3.Up * YourSink;  // swimming, in the water to the chin (FirstPerson)
            SwimPost(m);
            if (back && m.forced == null) m.player.SpeedScale = -Math.Abs(m.player.SpeedScale);
            LeapPost(m, lift, m == own);
            m.player.Advance(delta);
        }
        Footfall(own!, you.id, true, false, delta);
    }

    /// <summary>Every model's clip and playhead (s) this frame, yours as "you" (the self-test: they hold while the world is held).</summary>
    public List<(string id, string clip, double at)> Playheads()
    {
        var l = byBody.Select((kv) => (kv.Key, kv.Value.player.CurrentAnimation.ToString(), kv.Value.player.CurrentAnimationPosition)).ToList();
        if (own != null && own.root.Visible) l.Add(("you", own.player.CurrentAnimation, own.player.CurrentAnimationPosition));
        return l;
    }

    /// <summary>Where a clip carries the hands over its cycle, on your body's skeleton (the self-test: a walk swings them
    /// at the sides, a carry holds them in front): the most either hand rises over the hips, the furthest behind the
    /// hips either goes, and the least ahead of them either stays (m; NaN without your body).</summary>
    public (float up, float behind, float ahead) HandsIn(string clip)
    {
        if (own == null || !Bones(own) || !own.player.HasAnimation(clip)) return (float.NaN, float.NaN, float.NaN);
        var a = own.player.GetAnimation(clip);
        var fk = new Gaits.Fk(a, own.skel!, Gaits.ToScene(own.root, own.skel!));
        float up = float.MinValue, behind = float.MinValue, ahead = float.MaxValue;
        for (int i = 0; i <= 60; i++)
        {
            double t = a.Length * i / 60;
            var hips = fk.Head("Hips", t);
            foreach (var hand in new[] { "LeftHand", "RightHand" })
            {
                var h = fk.Head(hand, t) - hips;  // the model's front is +Z
                up = Math.Max(up, h.Y);
                behind = Math.Max(behind, -h.Z);
                ahead = Math.Min(ahead, h.Z);
            }
        }
        return (up, behind, ahead);
    }

    /// <summary>The car you stand on (CarLeaps.cs), set by UpdateYou: your feet's ground is its top where it is under them.</summary>
    Vehicle? roofUnder;

    /// <summary>The ground under a point for your own feet: the car's top where you stand on one, else the ground's.</summary>
    (float y, Vector3 normal) OwnGroundAt(Vector3 at)
    {
        var g = GroundAt(at);
        if (roofUnder == null) return g;
        var p = new Point { x = (int)Math.Round(at.X * Motion.SUB), y = (int)Math.Round(at.Z * Motion.SUB) };
        return Vehicles.gap(roofUnder, p) == 0 ? (g.y + (float)Vehicles.topAt(roofUnder, p), Vector3.Up) : g;
    }
}
