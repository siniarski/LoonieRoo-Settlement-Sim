// Your body through a leap (Leaps.cs: Space jumps, vaults or hops; 2026-11-09, the playtest: "fix body not
// jumping"): the body under the camera leaves the ground with the eye, on the same arc (Leaps.lift at the
// frame's blend), and is posed from the library's jump clips, which are made in place (the hips stay at standing
// height and the legs tuck under them), so lifting the model by the arc puts the feet on it:
//   jump   ual_jump_start from just after its crouch to a light tuck, then the in-air loop (one leg reaching down)
//   hop    the same, tucked all the way (the start clip's deepest), over the water
//   vault  the full tuck too, facing the way over, and a hand on the sill or the barricade (HandIK, the lab's): it
//          reaches down for the edge's top as you go up, the chest leaning over it (LeapLean) so the arm gets there,
//          and pushes off over the top (higher up the arm can't reach down to it); the hand your body has free (the
//          left, unless Held shows something in it; with both full, none)
//   car    over a parked car or onto one (CarLeaps.cs: over, climb), the same hand on the car's top where your line
//          first meets its body (CarEdge), the chest leant over it; onto one it stays there till you are up
//   land   the landing clip from part-way down its knee bend, for a moment, then back to walking or standing
//          through the usual cross-fade; nothing sinks: the lift is 0 on the landing and the clip's feet are at
//          the standing ankle height there
// The clips aren't played at their own pace (a leap is 0.4 to 0.6 s, the start clip alone 1.3 s): each frame
// the playhead is put where the leap's progress says, so it holds when the game is paused and keeps up at 4x. The shadow copy does the same. Render-side only.

using System;
using Godot;
using LoonieRoo.Game.Characters;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed partial class People
{
    /// <summary>A leap's clip, by its share t (0..1) of the time in the air: (clip, seconds into it).</summary>
    static (string clip, double at) LeapClip(string what, double t)
    {
        // A vault: its own clip (derive_clips.py: a tuck, the legs swung out to the left over the edge, down and through).
        // Over a car (CarLeaps.cs) is a vault too: the legs swung out over the bonnet, the boot or the roof.
        if (what == "vault" || what == "over") return ("more/vault", VAULT_LENGTH * Smooth(t));
        // The start clip from 0.07 s (past its one crouched frame) to a light tuck (0.22 s) for a jump, the deepest (0.33 s) else.
        // (more/: the library's jump clips with the arms held in, 2026-11-10: they flung them wide.)
        double tuck = what == "jump" ? 0.22 : 0.33;
        if (t < TUCKED) return ("more/jump_start", 0.07 + (tuck - 0.07) * Smooth(t / TUCKED));
        return ("more/jump_air", (t - TUCKED) * 0.8);
    }

    /// <summary>The vault clip's length (s).</summary>
    const double VAULT_LENGTH = 0.8;

    /// <summary>The share of the leap spent tucking up; the rest is the in-air clip, the legs reaching down for the landing.</summary>
    const double TUCKED = 0.45;
    /// <summary>The landing: into the land clip this far (s: part-way up from its deepest), at this rate, for this long (s; less if you're moving on).</summary>
    const double LAND_AT = 0.55, LAND_RATE = 1.3, LAND_FOR = 0.3, LAND_MOVING = 0.15;
    /// <summary>The cross-fade into a leap's clips (s): quick, a leap is short.</summary>
    const double LEAP_BLEND = 0.08;

    sealed class Leap
    {
        public ControlBusy? busy;
        public string what = "";
        public double land;
        public HandIK? hands;
        public Node3D grip = null!;
        public Vector3 dir;
        public LeapLean? lean;
        /// <summary>Over or onto a car: the hand stays on its top longer climbing onto it.</summary>
        public bool car;
    }
    readonly Leap ownLeap = new(), shadowLeap = new();

    /// <summary>How high your body is lifted this frame (m: its feet's ground over the ground), and the drawn feet's
    /// lowest ankle over the ground there (m): the self-test checks they follow the eye.</summary>
    public float YourLift { get; private set; }
    public float YourFeetUp { get; private set; }

    static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

    /// <summary>Before Pose: the leap clip for model m (into m.forced), if leaping or just landed; returns the lift (m).</summary>
    double LeapPre(Model m, Leap l, SimState state, Survivor you, double alpha, double delta, bool leftFree, bool rightFree)
    {
        var b = state.control?.busy;
        bool leaping = b != null && Leaps.isLeap(b.what) && b.what != "stroke";  // a stroke (Swim.cs) is no leap to see
        if (!leaping && l.busy != null) l.land = Math.Min(LAND_FOR, l.land <= 0 ? LAND_FOR : l.land);  // just landed
        if (leaping && !ReferenceEquals(b, l.busy)) { l.what = b!.what; l.land = 0; StartHands(m, l, state, you, b, leftFree, rightFree); }
        l.busy = leaping ? b : null;
        m.forced = null;
        double lift = Leaps.height(state, you, alpha);  // a leap's arc, or the top of the car you stand on (CarLeaps.cs)
        if (leaping)
        {
            double t = b!.duration < 2 ? 1 : Math.Clamp((b.progress - 1 + alpha) / (b.duration - 1), 0, 1);
            var (clip, at) = LeapClip(l.what, t);
            m.forced = clip;
            m.forcedAt = at;
            Hands(l, t);
            if (l == ownLeap) Shot(t);
        }
        else
        {
            Hands(l, 1);
            if (l.land > 0)
            {
                if (m.speed > 0.3) l.land = Math.Min(l.land, LAND_MOVING);
                l.land -= delta;
                m.forced = "more/jump_land";
                m.forcedAt = -1;  // played on from LAND_AT at LAND_RATE
            }
        }
        return lift;
    }

    int leapShots, leaps;
    double lastT = 1;

    /// <summary>LEAP_SHOT=prefix: pictures of your first two leaps at a quarter, half and three quarters of the way (with
    /// --eye to watch yourself), to see the body up with the eye and the hand on a sill.</summary>
    void Shot(double t)
    {
        if (OS.GetEnvironment("LEAP_SHOT") is not { Length: > 0 } shot) return;
        if (t < lastT) leaps++;
        foreach (double at in new[] { 0.25, 0.5, 0.75 })
            if (leaps <= 2 && lastT < at && t >= at)
                parent.GetViewport().GetTexture().GetImage().SavePng($"{shot}-{leaps}-{(int)(at * 100)}.png");
        lastT = t;
    }

    /// <summary>The playhead for a forced clip: put where the leap's progress says, every frame (a seek, so the rate
    /// stays 1: the cross-fade runs at the player's rate, and a rate of 0 would freeze it); the landing plays on.</summary>
    void ForcedPlay(Model m)
    {
        string clip = m.forced!;
        if (clip != m.clip)
        {
            m.player.Play(clip, m.clip == "" ? 0 : LEAP_BLEND);
            m.player.Seek(m.forcedAt >= 0 ? m.forcedAt : LAND_AT, true);
            m.clip = clip;
        }
        else if (m.forcedAt >= 0) m.player.Seek(m.forcedAt, true);
        m.player.SpeedScale = m.forcedAt >= 0 ? 1 : (float)LAND_RATE;
    }

    /// <summary>The eye dips as you land (the knees taking it): DIP m at most, over DIP_FOR s, deeper the longer the leap
    /// was in the air; added to the camera after FirstPerson has put it at your eyes this frame (People.UpdateYou runs
    /// after that), so the sim's eye and the self-test's EyeUp are untouched.</summary>
    const float DIP = 0.07f;
    const double DIP_FOR = 0.28;
    double dipLeft, dipDepth, airborne;
    /// <summary>How far the eye is dipped this frame (m), for the self-test and LEAP_SHOT.</summary>
    public float YourDip { get; private set; }

    void EyeDip()
    {
        double delta = parent.GetProcessDeltaTime();
        if (ownLeap.busy != null) airborne += delta;
        else if (airborne > 0)
        {
            dipDepth = DIP * Math.Clamp(airborne / 0.6, 0.4, 1.0);
            dipLeft = DIP_FOR;
            airborne = 0;
        }
        YourDip = 0;
        if (dipLeft <= 0) return;
        dipLeft -= delta;
        double u = 1 - Math.Max(0, dipLeft) / DIP_FOR;
        // Down quickly, back up slower: the peak a third of the way.
        double shape = u < 0.33 ? Math.Sin(u / 0.33 * Math.PI / 2) : Math.Cos((u - 0.33) / 0.67 * Math.PI / 2);
        YourDip = (float)(dipDepth * shape);
        if (parent.GetViewport().GetCamera3D() is Camera3D cam && cam.GetParent() == parent) cam.Position += Vector3.Down * YourDip;
    }

    /// <summary>A vault's hand: on the edge's top under the line you go over, the hand your body has free.</summary>
    void StartHands(Model m, Leap l, SimState state, Survivor you, ControlBusy b, bool leftFree, bool rightFree)
    {
        if (l.hands != null) { l.hands.Grip[0] = l.hands.Grip[1] = null; l.hands.Weight[0] = l.hands.Weight[1] = 0; }
        l.dir = Vector3.Zero;
        if (b.what == "jump" || b.targetId == null) return;
        var xy = b.targetId.Split(',');
        double lx = double.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture) / Motion.SUB;
        double ly = double.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture) / Motion.SUB;
        double fx = lx - you.at.x / (double)Motion.SUB, fy = ly - you.at.y / (double)Motion.SUB;
        // The way over: the nearest of the four sides (Leaps.side's rule).
        var d = Math.Abs(fx) >= Math.Abs(fy) ? new Vector3(Math.Sign(fx), 0, 0) : new Vector3(0, 0, Math.Sign(fy));
        if (d == Vector3.Zero) return;
        l.dir = d;
        l.car = b.what is "over" or "climb";
        if (!(b.what == "vault" || l.car) || !(leftFree || rightFree)) return;
        // The edge's line: CLEAR short of the landing; the hand a little to its side of your line over it.
        double clear = (double)Leaps.CLEAR / Motion.SUB;
        float ex = (float)(lx - d.X * clear), ez = (float)(ly - d.Z * clear);
        int hand = leftFree ? 0 : 1;
        var side = Vector3.Up.Cross(d) * (hand == 0 ? 0.18f : -0.18f);  // the character's left is up x forward
        // Over or onto a car: the hand on its top where your line first meets its body (CarEdge, as the dead's).
        Vector3? carTop = l.car ? CarEdge(state, you.at, new Point { x = (int)Math.Round(lx * Motion.SUB), y = (int)Math.Round(ly * Motion.SUB) }) : null;
        if (l.car && carTop == null) return;
        int ax = (int)Math.Floor(ex - d.X * 0.5), ay = (int)Math.Floor(ez - d.Z * 0.5);
        int kind = Grids.edgeKind(state.grid, Grids.edgeBetween(state.grid, ax, ay, ax + (int)d.X, ay + (int)d.Z));
        float top = kind == Grids.EDGE_BARRICADE ? BARRICADE_TOP : SILL_TOP;
        if (l.hands == null)
        {
            var skel = Find<Skeleton3D>(m.root)!;
            skel.AddChild(l.lean = new LeapLean { Name = "VaultLean" });  // before the arm's IK
            l.hands = new HandIK(m.root, skel, parent);
            l.grip = new Node3D { Name = "VaultGrip" };
            parent.AddChild(l.grip);
        }
        var at = new Vector3(ex, 0, ez) + side;
        // The edge's floor as WorldMesh puts it: the higher of its two corners.
        int ox = Math.Max(ax, ax + (int)d.X), oy = Math.Max(ay, ay + (int)d.Z);
        float floor = d.X != 0 ? Math.Max(Terrain.Corner(ox, oy), Terrain.Corner(ox, oy + 1)) : Math.Max(Terrain.Corner(ox, oy), Terrain.Corner(ox + 1, oy));
        at.Y = floor + top;
        if (carTop is Vector3 ct) at = ct + side + Vector3.Up * 0.02f;
        l.grip.GlobalPosition = at;
        // The palm down onto the top (the grip's X into the thing held), the fingers the way you go (its Y).
        l.grip.GlobalBasis = new Basis(Vector3.Down, d, Vector3.Down.Cross(d));
        l.hands.Grip[hand] = l.grip;
    }

    /// <summary>A leap's hands forgotten with the model they were on (its skeleton freed: your body made again).</summary>
    static void ForgetHands(Leap l)
    {
        l.grip?.QueueFree();
        l.hands = null;
        l.lean = null;
        l.grip = null!;
    }

    /// <summary>A vault or a hop faces the way over (the legs; the chest still turns to the look): the model's yaw, or null.</summary>
    float? LeapYaw() => ownLeap.busy != null && ownLeap.dir != Vector3.Zero ? (float)Math.Atan2(ownLeap.dir.X, ownLeap.dir.Z) : null;

    /// <summary>The edge tops a hand goes on (m over the ground): a window's sill, a barricade's top plank (WorldMesh's).</summary>
    const float SILL_TOP = 0.9f, BARRICADE_TOP = 1.27f;
    /// <summary>How far the chest leans forward over the edge (radians, over the spine) with the hand on it.</summary>
    const float VAULT_LEAN = 1.0f;

    /// <summary>The vault hand's weight over the leap: reaching till HAND_ON, on the edge till HAND_OFF (pushing off as
    /// you go over the top: past it the arm can't reach down to the sill), off 0.25 later.</summary>
    const double HAND_ON = 0.2, HAND_OFF = 0.45, CLIMB_HAND_OFF = 0.7;
    void Hands(Leap l, double t)
    {
        if (l.hands == null) return;
        for (int i = 0; i < 2; i++)
        {
            double off = l.car && l.what == "climb" ? CLIMB_HAND_OFF : HAND_OFF;
            double w = l.hands.Grip[i] == null ? 0 : t < HAND_ON ? Smooth(t / HAND_ON) : t < off ? 1 : 1 - Smooth((t - off) / 0.25);
            l.hands.Weight[i] = (float)w;
        }
        // Leaning over the edge as the hand goes on it: the shoulder down and forward, so the arm reaches.
        if (l.lean != null) l.lean.Angle = (float)(VAULT_LEAN * Math.Max(l.hands.Weight[0], l.hands.Weight[1]));
        l.hands.Update();
    }

    /// <summary>After Pose: lift the model, and note (for yours) how high it and its feet are.</summary>
    void LeapPost(Model m, double lift, bool yours)
    {
        if (lift != 0) m.root.Position += Vector3.Up * (float)lift;
        if (!yours) return;
        EyeDip();
        float ground = Terrain.Height(m.root.Position.X, m.root.Position.Z);
        YourLift = m.root.Position.Y - ground;
        if (!Bones(m) || m.feet[0] < 0 || m.feet[1] < 0) return;
        // The ankles as last drawn (the skeleton poses after this frame's code), over the ground under the body.
        var xf = m.skel.GlobalTransform;
        YourFeetUp = Math.Min((xf * m.skel.GetBoneGlobalPose(m.feet[0]).Origin).Y, (xf * m.skel.GetBoneGlobalPose(m.feet[1]).Origin).Y) - ground;
    }
}

/// <summary>Leans the chest forward (radians, shared over Spine, Chest and UpperChest, about the model's side axis): a vault's
/// lean over the sill, before the hand's IK.</summary>
public partial class LeapLean : SkeletonModifier3D
{
    public float Angle;
    static readonly string[] SPINE = { "Spine", "Chest", "UpperChest" };

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton();
        if (sk == null || Angle == 0) return;
        float each = Angle / SPINE.Length;
        foreach (var name in SPINE)
        {
            int b = sk.FindBone(name);
            if (b < 0) continue;
            // The model's side (+x, its left) in the bone's own frame: forward is a turn about it.
            var axis = (sk.GetBoneGlobalPose(b).Basis.Inverse() * Vector3.Right).Normalized();
            sk.SetBonePoseRotation(b, sk.GetBonePoseRotation(b) * new Quaternion(axis, each));
        }
    }
}
