// What a body holds, drawn in its hand (2026-11-12; part of People): others were drawn empty-handed whatever the
// sim said they held. The item's model (Items.cs) rides the right hand's bone: the weapon in hand (the pose's
// carryRight), else the load carried in both arms (carryBoth). The living only; your own body too when it is seen
// from outside (first person draws your hands itself: Held.cs). Reads the pose only.
//
// The grip by class (2026-11-12, pictures: experiments/anim-next/grips.sh): the item's own grip point (Held.GripOf,
// the one first person uses) put in the palm, and the item turned by its class (Held.Family). The hand bone's frame
// on the pz rig: +Y along the fingers, +X to the thumb, +Z out of the palm. A club, a knife or an axe: the handle
// across the fist, out of the thumb side, the head ahead and a little down, an edge or a face to the front; a handgun:
// the barrel ahead and down (a low ready), its top to the thumb; a long gun held by its grip, the barrel ahead and
// level-ish; a load in both arms between the two hands, in front of the chest, placed each frame.

using Godot;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Nodes;

namespace LoonieRoo.Game.World3D;

public sealed partial class People
{
    /// <summary>The item models (set by FirstPerson once both exist); none, and nobody holds anything.</summary>
    public ItemModels? Items;

    /// <summary>The middle of the closed fist in the hand bone's frame (m): along the fingers, a little out of the palm.</summary>
    static readonly Vector3 PALM = new(0.0f, 0.085f, 0.035f);

    /// <summary>How far each class points its item down from straight ahead of the fist (towards the fingers, 0..1):
    /// a club or a blade hangs a little, a handgun at a low ready, a long gun nearly level (its muzzle off the ground).</summary>
    static float Droop(string family) => family switch { "handgun" => 1.3f, "rifle" => 0.6f, "axe" => 0.9f, "stab" => 1.0f, _ => 1.0f };

    /// <summary>The item's place in the right hand's bone frame (the lab too: AnimTest --held).</summary>
    public static Transform3D GripFor(string kind)
    {
        if (IsLoad(kind)) return Transform3D.Identity;
        string family = Held.Family(kind);
        // The item's +X (the handle, grip to head; a gun's barrel) ahead of the fist, drooping towards the fingers.
        var x = (Vector3.Right + Vector3.Up * Droop(family)).Normalized();
        // Its working side: an edge or a face along the fingers' curl (to the front of a swing), a gun's top away from
        // the fingers (up, held low).
        var face = family is "handgun" or "rifle" ? Vector3.Down : Vector3.Up;
        face = (face - x * face.Dot(x)).Normalized();
        var axis = Held.FaceOf(kind);
        Basis b = axis.Y != 0 ? new Basis(x, face, x.Cross(face))
            : axis.Z != 0 ? new Basis(x, (face * axis.Z).Cross(x), face * axis.Z)
            : new Basis(x, face, x.Cross(face));
        return new Transform3D(b, PALM - b * Held.GripOf(kind));
    }

    /// <summary>Whether a kind is a load in both arms rather than a thing in the hand.</summary>
    public static bool IsLoad(string kind) => Weapons.specOfKind(kind) == null && kind is not ("axe" or "hammer" or "crowbar" or "weapon" or "gun");

    /// <summary>A load's place in the right hand's frame this frame: between the two hands, a little ahead of them,
    /// squared to the body (the skeleton's own frame).</summary>
    public static Transform3D LoadFrame(Skeleton3D sk)
    {
        int r = sk.FindBone("RightHand"), l = sk.FindBone("LeftHand");
        if (r < 0 || l < 0) return Transform3D.Identity;
        var rp = sk.GetBoneGlobalPose(r);
        var mid = (rp.Origin + sk.GetBoneGlobalPose(l).Origin) / 2 + new Vector3(0, 0.02f, 0.04f);
        return rp.AffineInverse() * new Transform3D(Basis.Identity, mid);
    }

    /// <summary>How many bodies have something drawn in hand this frame (the self-test).</summary>
    public int Holding { get; private set; }

    void Hold(Model m, Wanted w, bool show = true)
    {
        string kind = !show || w.body is Zombie ? "" : w.pose.carryRight ?? w.pose.carryBoth ?? "";
        if (kind != m.heldKind)
        {
            m.heldKind = kind;
            var mesh = kind == "" ? null : Items?.MeshFor(kind);
            if (mesh != null && m.holdMesh == null)
            {
                m.skel ??= Find<Skeleton3D>(m.root);
                if (m.skel == null || m.skel.FindBone("RightHand") < 0) return;
                var at = new BoneAttachment3D { BoneName = "RightHand" };
                m.skel.AddChild(at);
                m.holdMesh = new MeshInstance3D();
                at.AddChild(m.holdMesh);
            }
            if (m.holdMesh != null)
            {
                m.holdMesh.Mesh = mesh;
                m.holdMesh.Visible = mesh != null;
                m.holdLoad = w.pose.carryRight == null && kind != "";
                m.holdMesh.Transform = m.holdLoad ? Transform3D.Identity : GripFor(kind);
            }
        }
        // A load follows both hands (the pose of the frame before: the clip plays after this).
        if (m.holdLoad && m.holdMesh is { Visible: true } && m.skel != null) m.holdMesh.Transform = LoadFrame(m.skel);
        if (m.holdMesh is { Visible: true }) Holding++;
    }
}
