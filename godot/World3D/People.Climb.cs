// The dead over a parked car or a barricade (the sim's Crossings.cs, 2026-11-11; the climb itself 2026-11-12).
//   lift   the body is raised by Crossings.lift at the frame's blend (the pose's lift, set in Update): up onto the
//          bonnet, the roof or the pickup's bed in the first part of a leg (Crossings.RISE), across the top, down
//          in the last part of the leg that leaves it
//   climb  one leg at a time, by where it is in the leg (so it holds when paused and keeps up at 4x):
//            haul   onto the car (the first RISE of the leg that gets on): the hands go to the car's top at its near
//                   edge, either side of the way over (HandIK, as your vault's; the chest leant over them), and
//                   the legs tuck up under it as it rises (the jump-start clip's tuck, its playhead by the leg)
//            top    across: on its belly, the crawl clip (the pose's stance), each at its own place in the crawl's
//                   cycle (a hash of its id), so a crowd on one car is not in step
//            drop   off (the last RISE of the leg that gets off): upright again, the hands pushing off the far edge
//                   as it goes, the legs reaching down (the in-air clip, by the leg)
//          a crawler hauls itself over on its arms (the crawl throughout); under a pickup, the crawl on the ground
//   vault  a runner over a barricade: the vault clip your own body uses (People.Leap.cs), its playhead put where
//          the leg's progress says
// The hands only for the near (CLIMB_RANGE, CLIMB_MAX a frame). Render-side only.

using System;
using Godot;
using LoonieRoo.Game.Characters;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Nodes;
using static LoonieRoo.Game.IdHash;

namespace LoonieRoo.Game.World3D;

public sealed partial class People
{
    /// <summary>One of the dead's hands on a car: the arms' IK, the grips, the lean, and the edges of the leg it is on.</summary>
    sealed class ClimbHands
    {
        public HandIK ik = null!;
        public LeapLean lean = null!;
        public readonly Node3D[] grip = new Node3D[2];
        public string leg = "";
        public bool onEdge;
    }

    /// <summary>The hands on a car within this (m) of the eye, and at most this many a frame.</summary>
    const float CLIMB_RANGE = 14;
    const int CLIMB_MAX = 8;
    int climbHands;

    /// <summary>The haul's tuck (s into the jump-start clip: past its crouch, to its deepest) and the in-air clip's
    /// stretch used for the drop (s).</summary>
    const double HAUL_FROM = 0.07, HAUL_TO = 0.33, DROP_FOR = 0.44;
    /// <summary>The hands this far either side of the way over (m), and the chest's lean over them (radians).</summary>
    const float HAND_APART = 0.22f, HAUL_LEAN = 0.9f;

    /// <summary>Where one of the dead is in a leg over a car: hauling up (u: 0..1 of it), across the top, or dropping off.</summary>
    static (string phase, double u, double t) ClimbPhase(SimState state, Crossing c, double alpha)
    {
        double t = Math.Clamp((c.of - c.ticks - 1 + alpha) / c.of, 0, 1);
        var f = Motion.tileOf(c.from);
        var to = Motion.tileOf(c.to);
        bool onFrom = Vehicles.parkedOver(state, f.x, f.y) != null, onTo = Vehicles.parkedOver(state, to.x, to.y) != null;
        if (!onFrom && onTo && t < Crossings.RISE) return ("haul", t / Crossings.RISE, t);
        if (onFrom && !onTo && t > 1 - Crossings.RISE) return ("drop", (t - (1 - Crossings.RISE)) / Crossings.RISE, t);
        return ("top", 0, t);
    }

    /// <summary>The first point of a car's body on the line from a to b (SUB), as a world point on its top, or null.</summary>
    public static Vector3? CarEdge(SimState state, Point a, Point b)
    {
        for (int k = 0; k <= 48; k++)
        {
            var p = new Point { x = a.x + (b.x - a.x) * k / 48, y = a.y + (b.y - a.y) * k / 48 };
            var t = Motion.tileOf(p);
            var v = Vehicles.parkedOver(state, t.x, t.y);
            if (v == null || Vehicles.gap(v, p) > 0) continue;
            float x = p.x / (float)Motion.SUB, z = p.y / (float)Motion.SUB;
            return new Vector3(x, Terrain.Height(x, z) + (float)(Vehicles.partTopCm(v, Vehicles.alongOf(v, p)) / 100), z);
        }
        return null;
    }

    /// <summary>Before Pose: a crossing zombie's clip, forced to the leg's progress (cleared for everyone else), and
    /// its hands on the car.</summary>
    void CrossPre(Model m, Wanted w, SimState state, double alpha)
    {
        m.forced = null;
        string phase = "";
        double u = 0;
        if (w.body is Zombie z && z.crossing is { } c && c.of > 0)
        {
            if (c.what == Crossings.VAULT)
            {
                double t = Math.Clamp((c.of - c.ticks - 1 + alpha) / c.of, 0, 1);
                m.forced = "more/vault";
                m.forcedAt = VAULT_LENGTH * Smooth(t);
            }
            else if (c.what == Crossings.CLIMB && z.variant != "crawler")
            {
                (phase, u, _) = ClimbPhase(state, c, alpha);
                if (phase == "haul") { m.forced = "more/jump_start"; m.forcedAt = HAUL_FROM + (HAUL_TO - HAUL_FROM) * Smooth(u); }
                else if (phase == "drop") { m.forced = "more/jump_air"; m.forcedAt = DROP_FOR * u; }
                bool near = w.dist2 < CLIMB_RANGE * CLIMB_RANGE && climbHands < CLIMB_MAX;
                if (phase != "top" && near) { climbHands++; CarHands(m, state, c, phase, u); }
                else ClimbOff(m);
            }
        }
        if (phase == "") ClimbOff(m);
        // Onto the top: the crawl from the body's own place in its cycle, not in step with the rest on the car.
        if (phase == "top" && m.climbPhase != "top") m.climbSeek = true;
        m.climbPhase = phase;
    }

    /// <summary>The hands on the near edge while hauling up, on the far edge as it pushes off it dropping down.</summary>
    void CarHands(Model m, SimState state, Crossing c, string phase, double u)
    {
        if (m.climb == null)
        {
            m.skel ??= Find<Skeleton3D>(m.root);
            if (m.skel == null) return;
            var h = new ClimbHands();
            m.skel.AddChild(h.lean = new LeapLean { Name = "ClimbLean" });  // before the arms' IK
            h.ik = new HandIK(m.root, m.skel, parent);
            for (int i = 0; i < 2; i++) parent.AddChild(h.grip[i] = new Node3D { Name = "ClimbGrip" });
            m.climb = h;
        }
        var ch = m.climb;
        string leg = $"{c.from.x},{c.from.y},{c.to.x},{c.to.y}";
        if (leg != ch.leg)
        {
            ch.leg = leg;
            // Hauling: the edge met going from the ground onto the car; dropping: the far edge, met coming back from the ground.
            var edge = phase == "haul" ? CarEdge(state, c.from, c.to) : CarEdge(state, c.to, c.from);
            ch.onEdge = edge != null;
            if (edge is Vector3 e)
            {
                var d = new Vector3(c.to.x - c.from.x, 0, c.to.y - c.from.y).Normalized();
                var side = Vector3.Up.Cross(d) * HAND_APART;  // the body's left is up x forward
                for (int i = 0; i < 2; i++)
                {
                    ch.grip[i].GlobalPosition = e + (i == 0 ? side : -side) + Vector3.Up * 0.02f;
                    ch.grip[i].GlobalBasis = new Basis(Vector3.Down, d, Vector3.Down.Cross(d));  // the palm down on the top
                    ch.ik.Grip[i] = ch.grip[i];
                }
            }
        }
        // Hauling: reaching for the edge over the first third, on it to the top; dropping: off it over the first half.
        double w = !ch.onEdge ? 0 : phase == "haul" ? Smooth(u / 0.35) : 1 - Smooth(u / 0.5);
        ch.ik.Weight[0] = ch.ik.Weight[1] = (float)w;
        ch.lean.Angle = (float)(HAUL_LEAN * w);
        ch.ik.Enabled = true;
        ch.ik.Update();
    }

    static void ClimbOff(Model m)
    {
        if (m.climb == null || (!m.climb.ik.Enabled && m.climb.leg == "")) return;
        m.climb.ik.Weight[0] = m.climb.ik.Weight[1] = 0;
        m.climb.lean.Angle = 0;
        m.climb.ik.Enabled = false;
        m.climb.ik.Update();
        m.climb.leg = "";
    }

    /// <summary>After Pose: one of the dead on a crossing is raised to what it is on, and onto the top its crawl starts
    /// at its own place in the cycle.</summary>
    void CrossPost(Model m, Wanted w)
    {
        if (w.body is Zombie && w.pose.lift != 0) m.root.Position += Vector3.Up * (float)w.pose.lift;
        if (m.climbSeek && m.forced == null && m.clip.Contains("crawl"))
        {
            m.climbSeek = false;
            m.player.Seek(Hash01(w.id, 31) * m.player.CurrentAnimationLength, true);
        }
    }

    /// <summary>How many of the dead have their hands on a car this frame (the self-test).</summary>
    public int ClimbHandsOn
    {
        get
        {
            int n = 0;
            foreach (var m in byBody.Values) if (m.climb is { ik.Enabled: true } c && c.ik.Weight[0] > 0.5f) n++;
            return n;
        }
    }

    /// <summary>The dead on a crossing this frame: id, phase and clip (the self-test, and --stage=climb's trace).</summary>
    public System.Collections.Generic.List<(string id, string phase, string clip, double at)> Climbers()
    {
        var l = new System.Collections.Generic.List<(string, string, string, double)>();
        foreach (var (id, m) in byBody) if (m.climbPhase != "") l.Add((id, m.climbPhase, m.player.CurrentAnimation, m.player.CurrentAnimationPosition));
        return l;
    }
}
