// The helicopter in first person (2026-11-11): the timeline's helicopter (TimelineMod: it comes over on a rolled
// day, hangs over you, then flies off dragging the dead after it) was only a shadow on the Colony tab and a noise.
// Here it is a model overhead (art/scripts/build_world.py: heli, and heli_rotor spun here).
//
// The sim moves it a tile every couple of ticks; the model follows that tile smoothly, noses the way it travels and
// leans into it, and bobs a little when it hangs still. All of that is the view's (nothing is written to the sim).

using System;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class Aircraft
{
    /// <summary>Metres over the ground it flies at.</summary>
    const float ALTITUDE = 26;
    /// <summary>How fast the model catches up with the sim's tile (per second), and the rotor's turns a second.</summary>
    const float FOLLOW = 3, ROTOR_TURNS = 4.5f;
    /// <summary>Where the rotor sits on the body (m over its skids).</summary>
    const float ROTOR_UP = 2.78f;

    readonly Node3D root = new() { Visible = false };
    readonly MeshInstance3D rotor;
    Vector3 at;
    float heading;
    double time;

    /// <summary>Whether it is overhead now (the self-test).</summary>
    public bool Shown => root.Visible;

    public Aircraft(Node3D parent)
    {
        var models = new ModelSet(parent, "res://assets/world/world.glb");
        root.AddChild(new MeshInstance3D { Mesh = models.MeshOf("heli") });
        rotor = new MeshInstance3D { Mesh = models.MeshOf("heli_rotor"), Position = new Vector3(0, ROTOR_UP, 0) };
        root.AddChild(rotor);
        parent.AddChild(root);
    }

    public void Update(SimState state, double delta)
    {
        var heli = state.timeline?.heli;
        if (heli == null) { root.Visible = false; return; }
        time += delta;
        var to = new Vector3(heli.x + 0.5f, 0, heli.y + 0.5f);
        if (!root.Visible) at = to; // just arrived: no easing in from wherever it last was
        var step = (to - at) * (float)Math.Min(1, delta * FOLLOW);
        at += step;
        float speed = delta > 0 ? step.Length() / (float)delta : 0;
        if (speed > 0.5f) heading = Mathf.LerpAngle(heading, Mathf.Atan2(step.Z, step.X), (float)Math.Min(1, delta * 2));
        float bob = Mathf.Sin((float)time * 1.3f) * 0.35f;
        root.Visible = true;
        // The model's nose is its +X: turned to the heading, and tipped nose-down as it moves.
        var basis = new Basis(Vector3.Up, -heading) * new Basis(Vector3.Back, -Math.Min(0.22f, speed * 0.03f));
        root.Transform = new Transform3D(basis, new Vector3(at.X, Terrain.Height(at.X, at.Z) + ALTITUDE + bob, at.Z));
        rotor.Rotation = new Vector3(0, (float)(time * ROTOR_TURNS * Math.Tau % Math.Tau), 0);
    }
}
