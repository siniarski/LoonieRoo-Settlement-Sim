// Hand IK (docs/characters.md, "IK at runtime"): hands onto grips (a weapon's, a door handle), on
// top of whatever clip plays. Presentation only. Per character, after the foot IK:
//   ShoulderReach     a grip beyond the straight arm brings the shoulder (clavicle) forward, up to 25 deg
//   ArmIK.Left/Right  Godot's TwoBoneIK3D (upper arm, forearm, hand): the wrist to where the grip
//                     puts it; the elbow to a pole down, out and back from the shoulder.
//   HandAlign         turns each hand so its palm and fingers match the grip's, and records where
//                     the wrist ended up (Skeleton3D restores its poses after the modifiers).
// A grip is a Node3D: its origin where the palm's centre goes, its X axis the palm's normal (from
// the palm into the thing held), its Y axis the way the fingers point. Each hand has a weight
// (0 = the clip's arm, 1 = on the grip), so a reach is a weight going 0 -> 1.
// The rig's hand is palm down in its rest pose (a real rig's T-pose); that's how the align finds
// the palm.

using System;
using Godot;

namespace LoonieRoo.Game.Characters;

public sealed class HandIK
{
    public const float PalmFromWrist = 0.08f, PalmDepth = 0.025f;  // the palm's centre from the wrist joint

    readonly Node3D character;
    readonly TwoBoneIK3D[] arm = new TwoBoneIK3D[2];
    readonly Node3D[] target = new Node3D[2], pole = new Node3D[2];
    readonly HandAlign align;
    readonly ShoulderReach reach;
    public readonly Node3D?[] Grip = new Node3D?[2];  // 0 left, 1 right
    public Vector3 Target(int i) => target[i].GlobalPosition;
    public readonly float[] Weight = new float[2];
    public bool Enabled = true;

    public HandIK(Node3D character, Skeleton3D skel, Node world)
    {
        this.character = character;
        align = new HandAlign { Name = "HandAlign", Ik = this };
        reach = new ShoulderReach { Name = "ShoulderReach", Ik = this };
        skel.AddChild(reach);  // before the arms' IK
        for (int i = 0; i < 2; i++)
        {
            string s = i == 0 ? "Left" : "Right";
            target[i] = new Node3D { Name = $"WristTarget{s}" };
            pole[i] = new Node3D { Name = $"ElbowPole{s}" };
            world.AddChild(target[i]);
            world.AddChild(pole[i]);
            arm[i] = new TwoBoneIK3D { Name = $"ArmIK{s}", Active = false };
            arm[i].SetSettingCount(1);
            arm[i].SetRootBoneName(0, s + "UpperArm");
            arm[i].SetMiddleBoneName(0, s + "LowerArm");
            arm[i].SetEndBoneName(0, s + "Hand");
            skel.AddChild(arm[i]);
            arm[i].SetTargetNode(0, arm[i].GetPathTo(target[i]));
            arm[i].SetPoleNode(0, arm[i].GetPathTo(pole[i]));
            align.Hands[i] = skel.FindBone(s + "Hand");
        }
        skel.AddChild(align);
    }

    /// <summary>Where the wrist must be for the palm to sit on a grip.</summary>
    public static Vector3 WristFor(Node3D grip)
    {
        var b = grip.GlobalBasis.Orthonormalized();
        return grip.GlobalPosition - b.Y * PalmFromWrist - b.X * PalmDepth;
    }

    /// <summary>Switch the arms' IK on or off by the weights, and place the targets; call each frame.</summary>
    public void Update()
    {
        for (int i = 0; i < 2; i++)
        {
            bool on = Enabled && Grip[i] != null && Weight[i] > 0.001f;
            arm[i].Active = on;
            if (on) arm[i].Influence = Weight[i];
        }
        align.Active = reach.Active = Enabled;
        PlaceTargets();
    }

    /// <summary>The wrist targets from the grips as they are now. Called again from inside the
    /// skeleton's update (AimPose), once the weapon is placed on this frame's body.</summary>
    public void PlaceTargets()
    {
        var xf = character.GlobalTransform;
        for (int i = 0; i < 2; i++)
        {
            if (Grip[i] == null) continue;
            target[i].GlobalPosition = WristFor(Grip[i]!);
            float side = i == 0 ? 1 : -1;  // the character's left is +x
            pole[i].GlobalPosition = xf * new Vector3(side * 0.6f, 0.7f, -0.35f);
        }
    }

    /// <summary>How far each wrist is from its target, in metres (after the IK); -1 for a free hand.</summary>
    public float Error(int i) => Enabled && Grip[i] != null && Weight[i] > 0.999f ? align.FinalWrist[i].DistanceTo(WristFor(Grip[i]!)) : -1;
}

/// <summary>Before the arms' IK: a grip beyond the straight arm brings the shoulder forward (the
/// clavicle turns towards it, up to MaxDeg), as people reach, so the arm gets there. Grips in reach
/// are left alone (the weapon holds don't change).</summary>
public partial class ShoulderReach : SkeletonModifier3D
{
    public HandIK Ik = null!;
    const float MaxDeg = 25, Slack = 0.995f;
    int[]? clav, upper, lower, hand;

    public override void _ProcessModificationWithDelta(double delta)
    {
        var skel = GetSkeleton();
        if (skel == null) return;
        if (clav == null)
        {
            clav = new int[2]; upper = new int[2]; lower = new int[2]; hand = new int[2];
            for (int i = 0; i < 2; i++)
            {
                string s = i == 0 ? "Left" : "Right";
                clav[i] = skel.FindBone(s + "Shoulder"); upper[i] = skel.FindBone(s + "UpperArm");
                lower[i] = skel.FindBone(s + "LowerArm"); hand[i] = skel.FindBone(s + "Hand");
            }
        }
        var inv = skel.GlobalTransform.AffineInverse();
        for (int i = 0; i < 2; i++)
        {
            if (Ik.Grip[i] == null || Ik.Weight[i] < 0.001f) continue;
            var t = inv * Ik.Target(i);
            var c = skel.GetBoneGlobalPose(clav[i]!).Origin;
            var s = skel.GetBoneGlobalPose(upper![i]).Origin;
            float arm = skel.GetBoneGlobalRest(lower![i]).Origin.DistanceTo(skel.GetBoneGlobalRest(upper[i]).Origin)
                      + skel.GetBoneGlobalRest(hand![i]).Origin.DistanceTo(skel.GetBoneGlobalRest(lower[i]).Origin);
            float over = s.DistanceTo(t) - arm * Slack;
            if (over <= 0) continue;
            // Turn the clavicle (about its root) so the shoulder joint swings towards the target.
            var from = (s - c).Normalized();
            var to = (t - c).Normalized();
            var axis = from.Cross(to);
            if (axis.LengthSquared() < 1e-8f) continue;
            float want = Math.Min(over / Math.Max(c.DistanceTo(s), 0.05f), Mathf.DegToRad(MaxDeg)) * Ik.Weight[i];
            var g = skel.GetBoneGlobalPose(clav[i]).Basis.Orthonormalized();
            var local = (g.Inverse() * axis.Normalized()).Normalized();
            skel.SetBonePoseRotation(clav[i], skel.GetBonePoseRotation(clav[i]) * new Quaternion(local, want));
        }
    }
}

/// <summary>A skeleton modifier that runs a callback: for work that needs this frame's pose mid-update.</summary>
public partial class CallbackModifier : SkeletonModifier3D
{
    public Action<Skeleton3D>? Run;
    public override void _ProcessModificationWithDelta(double delta)
    {
        if (GetSkeleton() is Skeleton3D skel) Run?.Invoke(skel);
    }
}

public partial class HandAlign : SkeletonModifier3D
{
    public HandIK Ik = null!;
    public readonly int[] Hands = new int[2];
    public readonly Vector3[] FinalWrist = new Vector3[2];

    public override void _ProcessModificationWithDelta(double delta)
    {
        var skel = GetSkeleton();
        if (skel == null) return;
        var xf = skel.GlobalTransform;
        var inv = xf.AffineInverse();
        for (int i = 0; i < 2; i++)
        {
            int bone = Hands[i];
            var grip = Ik.Grip[i];
            float w = Ik.Weight[i];
            var pose = skel.GetBoneGlobalPose(bone);
            if (grip != null && w > 0.001f)
            {
                // Rest frame: fingers along the bone, palm down. Target frame: the grip's.
                var rest = skel.GetBoneGlobalRest(bone).Basis.Orthonormalized();
                var fR = rest.Y.Normalized();
                var pR = (Vector3.Down - fR * Vector3.Down.Dot(fR)).Normalized();
                var gb = inv.Basis * grip.GlobalBasis.Orthonormalized();
                var fT = gb.Y.Normalized();
                var pT = (gb.X - fT * gb.X.Dot(fT)).Normalized();
                var from = new Basis(fR, pR, fR.Cross(pR));
                var to = new Basis(fT, pT, fT.Cross(pT));
                var want = (to * from.Inverse() * rest).Orthonormalized();
                var now = pose.Basis.Orthonormalized();
                var basis = new Basis(now.GetRotationQuaternion().Slerp(want.GetRotationQuaternion(), w));
                skel.SetBoneGlobalPose(bone, new Transform3D(basis, pose.Origin));
            }
            FinalWrist[i] = xf * pose.Origin;
        }
    }
}
