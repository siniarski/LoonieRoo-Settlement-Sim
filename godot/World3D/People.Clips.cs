// Choosing a body's clip each frame (part of People: People.cs): from its stance, speed and turning (the living's
// gaits and stops, LivingClip; the dead's, Gaits.Dead), what it is busy with, a load in both arms, a leg lost, and
// the one-shots over them (a blow taken, an attack); then Play cross-fades to it. Render-side only.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LoonieRoo.Game.Characters;
using LoonieRoo.Sim;
using static LoonieRoo.Game.IdHash;
using static LoonieRoo.Game.Nodes;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.World3D;

public sealed partial class People
{
    void Clip(Model m, Wanted w, string stance, bool dead, double delta)
    {
        if (OneShot(m, w, stance, dead, delta)) return;
        if (!dead && stance is "stand" or "sneak" && w.action == "")
        {
            if (BusyClip(w.pose.action, w.pose) is string busy && w.pose.speed < Gaits.STILL)
            {
                m.speed += (w.pose.speed - m.speed) * Math.Min(1, delta * 25);
                Play(m, busy, 1, 0.3);
                return;
            }
            var pick = LivingClip(ref m.speed, ref m.since, w.pose.speed, delta, stance, w.pose.running, Uncarried(m.clip), m.turn, OneLeg(w.pose));
            if (w.pose.carryBoth != null && Carried(pick.clip) is var held && held != pick.clip)
            {
                // The carry clips have their walks' strides: the same rate fits.
                Play(m, held, pick.rate, pick.blend);
                return;
            }
            Play(m, pick.clip, pick.rate, pick.blend);
            return;
        }
        // Speed eased, so a tick without a step doesn't flick the walk to idle and back.
        m.speed += (w.pose.speed - m.speed) * Math.Min(1, delta * 8);
        if (dead && m.rise != null)
        {
            // Woken where it lay: up off the ground (the sim has it on its feet already; it turns to go once up).
            m.riseAt += delta;
            if (m.riseAt < RISE_FOR) { Play(m, m.rise, 1, 0.12); return; }
            m.rise = null;
        }
        if (dead && stance is "stand" && w.action == "")
        {
            var z = w.body as Zombie;
            var g = Gaits.Dead(z?.variant ?? "walker", Hash01(w.id, 23), m.speed);
            Play(m, g.clip, g.rate, 0.3);
            return;
        }
        (string clip, float rate) = stance switch
        {
            "lie" when dead => (DormantFront(w.id) ? "more/dead_lie_front" : "more/dead_lie_back", 1f),
            "lie" or "dead" => ("lie_idle", 1f),
            "crawl" => (Gaits.Crawl(m.speed).clip, Gaits.Crawl(m.speed).rate),
            "sit" => ("ual/ual_driving", 1f),
            _ when w.action == "attack" => ("ual/ual_punch_cross", 1.2f),
            _ when w.action == "hit" => ("ual/ual_hit_chest", 1f),
            _ when m.speed < 0.2 => ("more/idle", dead ? 0.6f : 1f),
            _ when stance == "sneak" => ("ual/ual_crouch_fwd", Rate(m.speed, 1.0)),
            _ when m.speed < 2.2 || dead => ("ual/ual_walk", Rate(m.speed, dead ? 1.0 : 1.4)),
            _ when m.speed < 3.8 => ("ual/ual_jog_fwd", Rate(m.speed, 3.0)),
            _ => ("ual/ual_sprint", Rate(m.speed, 5.0)),
        };
        Play(m, clip, rate, 0.25);
    }

    /// <summary>A leg lost (the sim cuts legs at the knee): 'l' or 'r', else ' '. Both lost, the body crawls (the sim's crawler).</summary>
    static char OneLeg(PoseIntent pose)
    {
        bool l = false, r = false;
        foreach (var x in pose.lostLimbs) { l |= x.StartsWith("legLeft"); r |= x.StartsWith("legRight"); }
        return l == r ? ' ' : l ? 'l' : 'r';
    }

    /// <summary>The living's busy actions as clips (the controlled survivor's: PoseIntent.action), looping while they last.</summary>
    static string? BusyClip(string? action, PoseIntent pose) => action switch
    {
        "eat" => pose.lostLimbs.Any((l) => l.StartsWith("armRight")) ? "more/eat_left" : "more/eat", "drink" => "more/drink", "treat" => "more/treat", "search" => "more/search",
        "forage" => "more/forage", "cook" => "more/cook",
        // Others at their jobs (PoseIntents: one standing at its job has its type as the action).
        "build" => "more/work_hammer", "farm" or "clean" or "firefight" => "more/work_dig", "doctor" => "more/treat",
        "scavenge" or "craft" => "more/search", _ => null,
    };

    static string Uncarried(string clip) => clip switch
    {
        "more/carry_walk" => "more/walk", "more/carry_brisk" => "more/walk_brisk", "more/carry_idle" => "more/idle", _ => clip,
    };

    /// <summary>Carrying something in both arms (PoseIntent.carryBoth): the gait's own clip with the arms under the load.</summary>
    static string Carried(string clip) => clip switch
    {
        "more/walk" or "more/walk_slow" => "more/carry_walk", "more/walk_brisk" => "more/carry_brisk", "more/idle" => "more/carry_idle", _ => clip,
    };

    /// <summary>One-shots over the stance's clip, started by what the sim says: one of the dead struck (its stagger: from the
    /// side the blow came from), winding up a bite (the lunge, its whole second), one of the living startled (a flinch,
    /// standing or walking slowly). Returns whether one plays this frame.</summary>
    bool OneShot(Model m, Wanted w, string stance, bool dead, double delta)
    {
        if (w.body is Zombie z && stance == "stand")
        {
            if (w.action == "hit" && z.staggerUntil != m.lastStagger)
            {
                m.lastStagger = z.staggerUntil;
                m.lastSide = BlowFrom(m, w.pose);
                Shot(m, dead ? $"more/dead_hit_{m.lastSide}" : "ual/ual_hit_chest", dead ? 0.6 : 0.33);
            }
            bool attacking = w.action == "attack";
            if (attacking && !m.attacking && dead) Shot(m, "more/dead_lunge", 1.0);
            m.attacking = attacking;
        }
        else if (!dead && stance is "stand" or "sneak")
        {
            bool startled = w.pose.startle > 0.5;
            if (startled && !m.startled && w.pose.speed < 1.2) Shot(m, "more/flinch", 1.0);
            m.startled = startled;
        }
        if (m.shot == null) return false;
        m.shotAt += delta;
        if (m.shotAt >= m.shotFor) { m.shot = null; return false; }
        m.speed += (w.pose.speed - m.speed) * Math.Min(1, delta * 8);
        m.player.SpeedScale = 1;
        return true;
    }

    /// <summary>Starts a one-shot from its beginning (again, if it was playing).</summary>
    static void Shot(Model m, string clip, double length)
    {
        if (!m.player.HasAnimation(clip)) return;
        m.shot = clip;
        m.shotAt = 0;
        m.shotFor = length;
        m.player.Play(clip, m.clip == "" ? 0 : 0.08);
        m.player.Seek(0, true);
        m.player.SpeedScale = 1;
        m.clip = clip;
    }

    /// <summary>The side a blow came from, by the way the sim shoves the body (away from the blow) against the way it
    /// faces: shoved back, it was struck from the front; not shoved (against a wall), the front.</summary>
    static string BlowFrom(Model m, PoseIntent pose)
    {
        if (pose.speed < 0.3) return "front";
        float moved = (float)Math.Atan2(Math.Cos(pose.heading), Math.Sin(pose.heading));
        float rel = Mathf.AngleDifference(m.yaw, moved);  // the shove against the facing (+: to its left)
        if (Math.Abs(rel) > 2.36f) return "front";
        if (Math.Abs(rel) < 0.79f) return "back";
        return rel > 0 ? "right" : "left";
    }

    /// <summary>Plays clip at rate on model m, cross-fading over `blend` s from what it played (a new model starts at its own phase).</summary>
    static void Play(Model m, string clip, float rate, double blend)
    {
        if (!m.player.HasAnimation(clip)) clip = "more/idle";
        if (clip != m.clip)
        {
            bool first = m.clip == "";
            m.player.Play(clip, first ? 0 : blend);
            if (first) m.player.Seek(m.phase * m.player.CurrentAnimationLength, true);
            m.clip = clip;
        }
        m.player.SpeedScale = rate;
    }

    /// <summary>A clip, its rate and the cross-fade into it (s).</summary>
    public readonly record struct ClipPick(string clip, float rate, double blend);

    /// <summary>The living on their feet at sim speed `v` (m/s): `eased` follows it quickly (in about 0.04 s: the sim's
    /// speed changes at once, and a rate behind it slides the feet), the gait from Gaits.cs at a rate
    /// that keeps the planted foot still; standing, the idle, or a shuffle on the spot while `turning` (rad/s).</summary>
    public static ClipPick LivingClip(ref double eased, ref double since, double v, double dt, string stance, bool running, string current, double turning, char oneLeg = ' ')
    {
        eased += (v - eased) * Math.Min(1, dt * 25);
        since += dt;
        ClipPick pick;
        if (oneLeg != ' ')
        {
            // A leg lost at the knee: standing on the other, or hopping on it (the sim moves them at a hobble's pace).
            var hop = Gaits.InBandsPick($"more/hop_{oneLeg}", eased);
            pick = v < Gaits.STILL ? new($"more/stand_{oneLeg}", 1, 0.25) : new(hop.clip, hop.rate, hop.blend);
        }
        else if (v < Gaits.STILL)
        {
            // Stopped (the sim stops at once): to standing, the stride held as it was while it cross-fades (its feet
            // don't run on; the near tier's foot IK steps them in), rather than slowing through the slower gaits.
            string idle = stance == "sneak" ? "more/crouch_idle" : "more/idle";
            pick = Math.Abs(turning) > TURN_SHUFFLE ? new("more/shuffle", (float)Math.Clamp(Math.Abs(turning) / 1.2, 0.8, 2.5), 0.15)
                : new(idle, current == idle && since < STOP_BLEND ? STOP_RATE : 1, STOP_BLEND);
            if (current != idle && pick.clip == idle && current != "more/shuffle") pick = pick with { rate = STOP_RATE };
        }
        else
        {
            var p = Gaits.Living(eased, stance, running, current);
            pick = new(p.clip, p.rate, 0.2);
        }
        if (pick.clip != current) since = 0;
        return pick;
    }

    /// <summary>Stopping: the cross-fade to standing (s), and the rate meanwhile (the stride all but held).</summary>
    const double STOP_BLEND = 0.15;
    const float STOP_RATE = 0.12f;

    /// <summary>Turning faster than this on the spot (rad/s), the feet step round (the shuffle clip).</summary>
    public const double TURN_SHUFFLE = 0.8;

    /// <summary>A clip's playback rate for a speed, against the speed it was made at.</summary>
    static float Rate(double speed, double clipSpeed) => (float)Math.Clamp(speed / clipSpeed, 0.5, 1.6);
}
