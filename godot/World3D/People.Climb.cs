// The dead over a parked car or a barricade (the sim's Crossings.cs, 2026-11-11): the least to show it.
//   lift   the body is raised by Crossings.lift at the frame's blend (the pose's lift, set in Update): up onto the
//          bonnet, the roof or the pickup's bed in the first part of a leg, across the top, down in the last part
//   climb  on its belly: the pose's stance is the crawl (PoseIntents), so the crawl clip plays, as for a crawler;
//          a crawler under a pickup is the same, on the ground
//   vault  a runner over a barricade: the vault clip your own body uses (People.Leap.cs), its playhead put where
//          the leg's progress says, so it holds when paused and keeps up at 4x
// No hand on the car and no clip of hauling up its side: the body slides up the flank lying down. Render-side only.

using System;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed partial class People
{
    /// <summary>Before Pose: a vaulting zombie's clip, forced to the leg's progress (cleared for everyone else).</summary>
    void CrossPre(Model m, Wanted w, double alpha)
    {
        m.forced = null;
        if (w.body is not Zombie z || z.crossing is not { } c || c.what != Crossings.VAULT || c.of <= 0) return;
        double t = Math.Clamp((c.of - c.ticks - 1 + alpha) / c.of, 0, 1);
        m.forced = "more/vault";
        m.forcedAt = VAULT_LENGTH * Smooth(t);
    }

    /// <summary>After Pose: one of the dead on a crossing is raised to what it is on.</summary>
    static void CrossPost(Model m, Wanted w)
    {
        if (w.body is Zombie && w.pose.lift != 0) m.root.Position += Vector3.Up * (float)w.pose.lift;
    }
}
