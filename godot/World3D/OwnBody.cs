// Your own body in first person (full-body awareness, 2026-11-09): the skeleton modifier on the model People
// draws for the survivor you control. After the animation it
//   - shrinks the neck and head to nothing (HideHead), so the camera at your eyes isn't inside it and looking down
//     shows your chest, legs and feet, not the inside of your face (the hair and eyes go with it);
//   - turns the spine (Twist, radians about the vertical, shared over Spine, Chest and UpperChest), so the
//     chest faces where you look while the legs face where you walk.
// A second copy of the model, its head on, casts the shadow (ShadowsOnly), so your shadow has a head.
// Render-side only: People.UpdateYou places and poses both copies from your pose intent and the camera.

using Godot;

namespace LoonieRoo.Game.World3D;

public partial class OwnBody : SkeletonModifier3D
{
    public bool HideHead = true;
    public float Twist;

    static readonly string[] SPINE = { "Spine", "Chest", "UpperChest" };

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton();
        if (sk == null) return;
        if (HideHead)
        {
            // The neck too: with only the head gone, the neck's skin stretched up to the eye and filled the view looking down.
            foreach (var name in new[] { "Neck", "Head" })
            {
                int b = sk.FindBone(name);
                if (b >= 0) sk.SetBonePoseScale(b, new Vector3(0.001f, 0.001f, 0.001f));
            }
        }
        if (Twist == 0) return;
        float each = Twist / SPINE.Length;
        foreach (var name in SPINE)
        {
            int b = sk.FindBone(name);
            if (b < 0) continue;
            // The twist about the model's vertical, expressed in the bone's own frame.
            var global = sk.GetBoneGlobalPose(b).Basis;
            var axis = (global.Inverse() * Vector3.Up).Normalized();
            sk.SetBonePoseRotation(b, sk.GetBonePoseRotation(b) * new Quaternion(axis, each));
        }
    }
}
