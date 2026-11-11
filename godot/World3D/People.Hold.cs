// What a body holds, drawn in its hand (2026-11-12; part of People): others were drawn empty-handed whatever the
// sim said they held. The item's model (Items.cs) rides the right hand's bone: the weapon in hand (the pose's
// carryRight), else the load carried in both arms (carryBoth). The living only; your own body too when it is seen
// from outside (first person draws your hands itself: Held.cs). Reads the pose only.

using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed partial class People
{
    /// <summary>The item models (set by FirstPerson once both exist); none, and nobody holds anything.</summary>
    public ItemModels? Items;

    /// <summary>The item in the hand's own frame: its handle (the models' +X) along the closed fingers, a little out
    /// of the palm. Found by eye against the walk and the idle (experiments/play-fixes/hold/).</summary>
    static readonly Transform3D GRIP = new(Basis.FromEuler(new Vector3(0, 0, Mathf.Pi / 2)), new Vector3(0, 0.09f, 0.03f));
    /// <summary>A load in both arms sits in front of the chest, further from the hand.</summary>
    static readonly Transform3D LOAD = new(Basis.Identity, new Vector3(-0.15f, 0.12f, 0.18f));

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
                m.holdMesh.Transform = w.pose.carryRight != null ? GRIP : LOAD;
            }
        }
        if (m.holdMesh is { Visible: true }) Holding++;
    }
}
