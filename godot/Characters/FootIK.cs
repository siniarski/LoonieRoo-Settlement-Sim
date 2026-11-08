// Foot IK (docs/characters.md, "IK at runtime"): feet on uneven ground (a kerb, stairs, a hill)
// on top of whatever clip plays. Pure presentation: the sim never sees it. Three skeleton
// modifiers, in order, under the character's Skeleton3D:
//   FootProbe    reads each foot's animated pose, casts a ray down under it, and sets an IK
//                target that keeps the foot's animated height above the ground it's over (so a
//                swing still lifts); drops the hips by however far the lower foot must reach.
//   TwoBoneIK3D  (Godot's) bends thigh and shin to put the ankle on the target, knee to the pole
//                (ahead of the knee, the way the character faces).
//   FootAlign    turns each foot from its animated angle by the tilt of the ground under it
//                (capped), so soles lie on slopes.
// The hips and foot tilt are smoothed so stepping onto a kerb doesn't pop; the feet follow the
// ground at once (smoothing them let a planted foot sink into a slope: the IK test).
// The character's root sits at the ground height under its centre (the caller's job).

using System;
using Godot;

namespace LoonieRoo.Game.Characters;

public sealed class FootIK
{
    public const float AnkleHeight = 0.09f;  // the rig's ankle above the sole (rig.py: LeftFoot head)
    const float MaxTilt = 40;                // degrees a foot will tilt to meet the ground

    readonly Node3D character;
    readonly Skeleton3D skel;
    readonly FootProbe probe;
    readonly TwoBoneIK3D ik;
    readonly FootAlign align;

    public FootIK(Node3D character, Skeleton3D skel, Node world)
    {
        this.character = character;
        this.skel = skel;
        var feet = new Foot[2];
        ik = new TwoBoneIK3D { Name = "LegIK" };
        ik.SetSettingCount(2);
        for (int i = 0; i < 2; i++)
        {
            string s = i == 0 ? "Left" : "Right";
            var target = new Node3D { Name = $"FootTarget{s}" };
            var pole = new Node3D { Name = $"KneePole{s}" };
            world.AddChild(target);
            world.AddChild(pole);
            feet[i] = new Foot(skel.FindBone(s + "UpperLeg"), skel.FindBone(s + "LowerLeg"), skel.FindBone(s + "Foot"), target, pole);
            ik.SetRootBoneName(i, s + "UpperLeg");
            ik.SetMiddleBoneName(i, s + "LowerLeg");
            ik.SetEndBoneName(i, s + "Foot");
        }
        probe = new FootProbe { Name = "FootProbe", Character = character, Feet = feet };
        align = new FootAlign { Name = "FootAlign", Feet = feet, MaxTilt = MaxTilt };
        skel.AddChild(probe);
        skel.AddChild(ik);
        skel.AddChild(align);
        for (int i = 0; i < 2; i++)  // node paths once everything is in the tree
        {
            ik.SetTargetNode(i, ik.GetPathTo(feet[i].Target));
            ik.SetPoleNode(i, ik.GetPathTo(feet[i].Pole));
        }
    }

    public bool Enabled
    {
        get => probe.Active;
        set { probe.Active = ik.Active = align.Active = value; if (!value) probe.Reset(); }
    }

    /// <summary>Ground under a point (world): height and normal, or the point's own height and up when nothing's there.</summary>
    public static (float y, Vector3 normal) Ground(Node3D from, Vector3 at)
    {
        var space = from.GetWorld3D().DirectSpaceState;
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 1.2f, at + Vector3.Down * 2.5f));
        if (hit.Count == 0) return (at.Y, Vector3.Up);
        return (((Vector3)hit["position"]).Y, ((Vector3)hit["normal"]).Normalized());
    }

    public Foot[] Feet => probe.Feet;
}

public sealed class Foot
{
    public readonly int Thigh, Shin, Bone;
    public readonly Node3D Target, Pole;
    public Basis AnimatedBasis = Basis.Identity;  // the foot's world basis from the clip, this frame
    public Vector3 Normal = Vector3.Up;          // smoothed ground normal under it
    public float Lift;                            // smoothed vertical correction to its animated height
    public float GroundY;
    public Vector3 GroundNormal = Vector3.Up;    // this frame's, unsmoothed (for the IK test)
    public Vector3 FinalAnkle;                    // world, after the IK: what's drawn (Skeleton3D restores
                                                  // its poses after the modifiers, so read this, not the bone)
    public Foot(int thigh, int shin, int bone, Node3D target, Node3D pole) { Thigh = thigh; Shin = shin; Bone = bone; Target = target; Pole = pole; }
}

public partial class FootProbe : SkeletonModifier3D
{
    public Node3D Character = null!;
    public Foot[] Feet = Array.Empty<Foot>();
    float hips;  // smoothed hip drop (world metres, <= 0)
    int hipsBone = -1;

    public void Reset() { hips = 0; foreach (var f in Feet) { f.Lift = 0; f.Normal = Vector3.Up; } }

    public override void _ProcessModificationWithDelta(double delta)
    {
        var skel = GetSkeleton();
        if (skel == null || Feet.Length == 0) return;
        if (hipsBone < 0) hipsBone = skel.FindBone("Hips");
        var xf = skel.GlobalTransform;
        float rootY = Character.GlobalPosition.Y;
        float k = 1 - (float)Math.Exp(-14 * delta);  // smoothing per frame
        float lowest = 0;
        var forward = Character.GlobalBasis.Z.Normalized();
        foreach (var f in Feet)
        {
            var world = xf * skel.GetBoneGlobalPose(f.Bone);
            f.AnimatedBasis = world.Basis.Orthonormalized();
            var (gy, n) = FootIK.Ground(Character, world.Origin);
            f.GroundY = gy;
            f.GroundNormal = n;
            // Keep the clip's height above its own floor (the root's plane), over the real ground;
            // on a slope the ankle sits higher above the point under it (the sole is tilted).
            float slope = AnkleHeight(n) - FootIK.AnkleHeight;
            float want = gy - rootY + slope;
            // Feet follow the ground at once (a planted foot mustn't lag into a slope); the hips and
            // the tilt are smoothed instead, which is what hides a step up the kerb.
            f.Lift = want;
            f.Normal = (delta > 0 ? f.Normal.Lerp(n, k) : n).Normalized();
            lowest = Math.Min(lowest, f.Lift);
            f.Target.GlobalPosition = world.Origin + Vector3.Up * f.Lift;
            var knee = xf * skel.GetBoneGlobalPose(f.Shin).Origin;
            f.Pole.GlobalPosition = knee + forward * 1.0f + Vector3.Up * f.Lift;
        }
        // Drop the hips so the lower leg can reach (the higher one just bends more).
        hips = delta > 0 ? Mathf.Lerp(hips, lowest, k) : lowest;
        var local = xf.Basis.Inverse() * new Vector3(0, hips, 0);
        skel.SetBonePosePosition(hipsBone, skel.GetBonePosePosition(hipsBone) + local);
    }

    static float AnkleHeight(Vector3 n) => FootIK.AnkleHeight / Math.Max(0.5f, n.Y);
}

public partial class FootAlign : SkeletonModifier3D
{
    public Foot[] Feet = Array.Empty<Foot>();
    public float MaxTilt = 40;

    public override void _ProcessModificationWithDelta(double delta)
    {
        var skel = GetSkeleton();
        if (skel == null) return;
        var xf = skel.GlobalTransform;
        var inv = xf.AffineInverse();
        foreach (var f in Feet)
        {
            var n = f.Normal;
            f.FinalAnkle = xf * skel.GetBoneGlobalPose(f.Bone).Origin;
            float angle = Vector3.Up.AngleTo(n);
            if (angle < 0.001f) continue;
            angle = Math.Min(angle, Mathf.DegToRad(MaxTilt));
            var axis = Vector3.Up.Cross(n).Normalized();
            var tilted = new Basis(axis, angle) * f.AnimatedBasis;
            var current = skel.GetBoneGlobalPose(f.Bone);
            var basisSkel = inv.Basis * tilted;
            skel.SetBoneGlobalPose(f.Bone, new Transform3D(basisSkel.Orthonormalized(), current.Origin));
        }
    }
}
