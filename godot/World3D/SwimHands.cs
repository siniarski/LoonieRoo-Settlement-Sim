// Your arms while you swim (2026-11-12): out of your depth the weapon is put away (Held.cs) and nothing showed
// that you were swimming. Two hands in front of the eye make a breast stroke: together and forward, out and back,
// in under the chin; slowly treading water, in time with your paddling when you move, and one strong pull for a
// stroke (Space: Swim.cs). On the world's time, so it holds when the game is held. Reads state only.

using System;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class SwimHands
{
    readonly Node3D root = new() { Visible = false };
    readonly MeshInstance3D[] hands = { new(), new() };
    double phase;

    /// <summary>Whether the arms are drawn, and where the right hand is (camera space): the self-test.</summary>
    public bool Shown => root.Visible;
    public Vector3 RightAt => hands[1].Position;

    public SwimHands(Camera3D camera, ItemModels models)
    {
        camera.AddChild(root);
        foreach (var h in hands)
        {
            h.Mesh = models.MeshFor("hand");
            h.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            root.AddChild(h);
        }
    }

    /// <summary>The right hand through one stroke (camera space: -Z ahead, +X right, +Y up); the left mirrors it.</summary>
    static Vector3 At(double t)
    {
        // Three legs: reach (in front of the chin to arm's length), pull (out and back), recover (in under the chin).
        var chin = new Vector3(0.08f, -0.17f, -0.34f);
        var reach = new Vector3(0.06f, -0.10f, -0.66f);
        var wide = new Vector3(0.40f, -0.18f, -0.40f);
        static float S(double x) => (float)(x * x * (3 - 2 * x));
        if (t < 0.35) return chin.Lerp(reach, S(t / 0.35));
        if (t < 0.75) return reach.Lerp(wide, S((t - 0.35) / 0.4));
        return wide.Lerp(chin, S((t - 0.75) / 0.25));
    }

    public void Update(SimState state, Survivor you, double delta, bool hidden)
    {
        var c = state.control;
        root.Visible = !hidden && c != null && Swim.swimming(state, you) && you.sleep?.asleep != true;
        if (!root.Visible) { phase = 0; return; }
        bool stroke = c!.busy?.what == "stroke";
        bool moving = c.dx != 0 || c.dy != 0;
        // A stroke every second and a bit paddling, a slow scull treading water, a quick pull for Space's stroke.
        phase = (phase + delta * (stroke ? 1.6 : moving ? 0.8 : 0.35)) % 1;
        var p = At(phase);
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1 : 1;
            var at = new Vector3(p.X * side, p.Y, p.Z);
            // The hand model's sleeve runs back along +Z: the forearm comes from under the eye, fingers ahead; turned
            // outward through the pull.
            float turn = phase is > 0.35 and < 0.75 ? 0.9f : 0.2f;
            hands[i].Transform = new Transform3D(Basis.FromEuler(new Vector3(0.3f, side * -turn, 0)), at);
        }
    }
}
