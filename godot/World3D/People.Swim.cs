// Your body swimming, seen from outside (2026-11-12; part of People): out of your depth (Swim.cs) it played the
// walk standing on the river bed. Now it lies in the water and strokes (the library's swim_fwd: a breast stroke,
// flat), or treads water upright when still (swim_idle), facing the way you swim, at the water's surface: the
// back just under it swimming, the chin at it treading. In first person the body is not drawn while you swim
// (People.UpdateYou): the stroke's hands are SwimHands.cs. Wading (shallow water) is the walk on the bed, the water
// round the legs. Render-side only.

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Game.Characters;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed partial class People
{
    const string SWIM = "ual/ual_swim_fwd", TREAD = "ual/ual_swim_idle";
    /// <summary>The swim clip's speed (m/s) at rate 1, by eye (a stroke a second and a bit).</summary>
    const double SWIM_CLIP_SPEED = 1.0;
    /// <summary>Swimming, the hips this far under the surface (m); treading, the neck this far.</summary>
    const float SWIM_HIPS = 0.12f, TREAD_NECK = 0.06f;

    /// <summary>How high each swim clip carries the bone that is put at the water (m over the model's root, its mean).</summary>
    static readonly Dictionary<string, float> swimHeight = new();

    /// <summary>Before Pose: the swim clip for model m while swimming (none else).</summary>
    void SwimPre(Model m, bool swimming, PoseIntent pose, double delta)
    {
        if (!swimming) { m.swim = null; return; }
        m.speed += (pose.speed - m.speed) * Math.Min(1, delta * 6);
        bool stroking = m.speed > 0.3;
        m.swim = stroking ? SWIM : TREAD;
        m.swimRate = stroking ? (float)Math.Clamp(m.speed / SWIM_CLIP_SPEED, 0.7, 1.6) : 1f;
    }

    /// <summary>After Pose: a swimming body at the water, by its clip's height.</summary>
    void SwimPost(Model m)
    {
        if (m.swim == null) return;
        var p = m.root.Position;
        float water = Terrain.Water(p.X, p.Z);
        float y = m.swim == SWIM ? water - SWIM_HIPS - SwimHeight(m, SWIM, "Hips") : water - TREAD_NECK - SwimHeight(m, TREAD, "Neck");
        m.root.Position = p with { Y = y };
    }

    float SwimHeight(Model m, string clip, string bone)
    {
        if (swimHeight.TryGetValue(clip, out var h)) return h;
        h = bone == "Hips" ? 0.9f : 1.45f;
        if (Bones(m) && m.player.HasAnimation(clip))
        {
            var a = m.player.GetAnimation(clip);
            var fk = new Gaits.Fk(a, m.skel!, Gaits.ToScene(m.root, m.skel!));
            float sum = 0;
            for (int i = 0; i < 10; i++) sum += fk.Head(bone, a.Length * i / 10).Y;
            h = sum / 10;
        }
        swimHeight[clip] = h;
        return h;
    }

    /// <summary>Your body's swim clip this frame, or null (the self-test).</summary>
    public string? YourSwim => own != null && own.root.Visible ? own.swim : null;
    /// <summary>Your body's root over the water's surface (m; the self-test).</summary>
    public float YourOverWater => own == null ? float.NaN : own.root.Position.Y - Terrain.Water(own.root.Position.X, own.root.Position.Z);
}
