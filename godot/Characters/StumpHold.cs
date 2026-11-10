// An arm lost at the elbow (the sim's only cut for arms: Wounds.amputate), held (2026-11-10, the animation pass): the
// upper arm's stump hangs close to the body, a little forward and out, carried by the chest as it leans and twists,
// whatever the clip does with that arm (a walk swings it, an action reaches with a hand that isn't there). A
// skeleton modifier after the clip, put only on models with a lost arm (People.cs), so no one else pays for it.
// The forearm and hand below the cut are hidden by StumpCut.cs; the other arm does what the clip says.

using Godot;

namespace LoonieRoo.Game.Characters;

public partial class StumpHold : SkeletonModifier3D
{
    public bool Left, Right;
    /// <summary>The stump's line in the character's frame at rest (+x its left, +y up, +z its front), per side.</summary>
    static readonly Vector3 HANG = new(0.30f, -0.93f, 0.20f);
    int chest = -1;
    readonly int[] upper = { -1, -1 };

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton();
        if (sk == null) return;
        if (chest < 0) { chest = sk.FindBone("UpperChest"); upper[0] = sk.FindBone("LeftUpperArm"); upper[1] = sk.FindBone("RightUpperArm"); }
        if (chest < 0) return;
        // The chest's turn from its rest (in the skeleton's space): the stump goes where the chest takes it.
        var turn = sk.GetBoneGlobalPose(chest).Basis.Orthonormalized() * sk.GetBoneGlobalRest(chest).Basis.Orthonormalized().Inverse();
        for (int s = 0; s < 2; s++)
        {
            if (!(s == 0 ? Left : Right) || upper[s] < 0) continue;
            var want = (turn * new Vector3(s == 0 ? HANG.X : -HANG.X, HANG.Y, HANG.Z)).Normalized();
            var g = sk.GetBoneGlobalPose(upper[s]);
            var along = g.Basis.Y.Normalized();
            if (along.Dot(want) > 0.9999f) continue;
            var swing = new Basis(new Quaternion(along, want));
            sk.SetBoneGlobalPose(upper[s], new Transform3D(swing * g.Basis, g.Origin));
        }
    }
}
