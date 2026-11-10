// One of the dead's head turned to what it's after (2026-11-10, the animation pass): PoseIntent.lookAt (its target's
// position), turned to by the neck (40%) and the head (60%), at most MAX from where the clip has the face, by Weight
// (the caller eases it in and out). A skeleton modifier after the clip, put only on the near dead with a target
// (People.cs: the near LOD, within LOOK_RANGE), so the mid and far tiers never pay for it.

using Godot;

namespace LoonieRoo.Game.Characters;

public partial class HeadLook : SkeletonModifier3D
{
    /// <summary>The point looked at (world) and how much (0..1).</summary>
    public Vector3 Target;
    public float Weight;
    /// <summary>The most the head turns from the clip's (radians).</summary>
    const float MAX = 1.2f;
    int neck = -1, head = -1;

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton();
        if (sk == null || Weight <= 0.001f) return;
        if (head < 0) { neck = sk.FindBone("Neck"); head = sk.FindBone("Head"); }
        if (head < 0 || neck < 0) return;
        var xf = sk.GlobalTransform;
        var hp = sk.GetBoneGlobalPose(head);
        // The face's way now (the rest faces +z), and the way to the target, in the skeleton's space.
        var face = (hp.Basis.Orthonormalized() * sk.GetBoneGlobalRest(head).Basis.Orthonormalized().Inverse() * Vector3.Back).Normalized();
        var to = (xf.Basis.Inverse() * (Target - xf * hp.Origin)).Normalized();
        float angle = face.AngleTo(to);
        if (angle < 0.01f) return;
        var axis = face.Cross(to).Normalized();
        float turn = Mathf.Min(angle, MAX) * Weight;
        foreach (var (bone, share) in new[] { (neck, 0.4f), (head, 0.6f) })
        {
            var g = sk.GetBoneGlobalPose(bone);
            sk.SetBoneGlobalPose(bone, new Transform3D(new Basis(axis, turn * share) * g.Basis, g.Origin));
        }
    }
}
