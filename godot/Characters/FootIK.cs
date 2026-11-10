// Foot IK (docs/characters.md, "IK at runtime"): feet on uneven ground (a kerb, stairs, a hill)
// on top of whatever clip plays. Pure presentation: the sim never sees it. Three skeleton
// modifiers, in order, under the character's Skeleton3D:
//   FootProbe    reads each foot's animated pose, casts a ray down under it, and sets an IK
//                target that keeps the foot's animated height above the ground it's over (so a
//                swing still lifts); drops the hips by however far the lower foot must reach.
//                A second ray under the toe tip: where the ground there stands above the plane under
//                the ankle (a step, not a slope: the tilt handles slopes), the toes are raised about the
//                ankle (up to 35 deg, the heel staying down) and only a small rest (a lip, 4 cm at most)
//                lifts the foot: lifting it whole onto the step left the heel 15-20 cm in the air.
//                Foot locking (Lock): a foot that touches down (the clip's ankle within 1 cm of its
//                floor: the mannequin's scripted walk swings its foot 1-3 cm up just before contact) stays where it landed until the clip lifts it, then eases back to the clip
//                over ~0.1 s; it lets go early if the leg would stretch more than 25 cm. So a planted
//                foot doesn't slide when the ground speed and the stride disagree (between gaits,
//                turning, starting and stopping).
//   TwoBoneIK3D  (Godot's) bends thigh and shin to put the ankle on the target, knee to the pole
//                (ahead of the knee, the way the character faces).
//   FootAlign    turns each foot from its animated angle by the tilt of the ground under it
//                (capped), so soles lie on slopes.
// The hips and foot tilt are smoothed so stepping onto a kerb doesn't pop; the feet follow the
// ground at once (smoothing them let a planted foot sink into a slope: the IK test).
// The character's root sits at the ground height under its centre (the caller's job).

using System;
using Godot;

namespace LoonieRoo.Game.Characters;

public sealed class FootIK
{
    public const float AnkleHeight = 0.09f;  // the rig's ankle above the sole (rig.py: LeftFoot head)
    public const float ToeHeight = 0.02f, ToeLength = 0.07f;  // the toes bone above the sole, and its length (rig.py)
    public const float MaxToePitch = 35, MaxToeLift = 0.04f;  // toes raised at most this far; the foot lifted only for a lip
    public bool ToeRay = true, Lock = true;
    /// <summary>The ground under a point (height, normal), in place of the physics rays: the world's ground is the
    /// sim's heights (World3D/Terrain.cs), with no collision shapes.</summary>
    public Func<Vector3, (float y, Vector3 normal)>? GroundAt;
    public (float y, Vector3 normal) GroundUnder(Node3D from, Vector3 at) => GroundAt != null ? GroundAt(at) : Ground(from, at);
    public const float PlantHeight = 0.01f, MaxLockStretch = 0.25f, ReleaseTime = 0.08f;
    /// <summary>A held foot the body has moved this far off (m) steps to where the clip has it: over StepTime (s),
    /// lifted StepHeight (m) at the middle (2026-11-10: a stop, a turn on the spot, a cross-fade slid it before).</summary>
    public const float StepStretch = 0.12f, StepTime = 0.2f, StepHeight = 0.07f;
    /// <summary>A clip's ankle this far under its floor (m) is held there: a cross-fade can carry a foot through the ground.</summary>
    public const float SinkLimit = 0.02f;
    /// <summary>Steps are for a body all but standing (m/s) whose caller says it stands (Steps: People.cs, while the
    /// clip is a standing one or the shuffle); walking, a held foot lets go as it always did.</summary>
    public bool Steps;
    public const float StepBelow = 0.6f;
    const float MaxTilt = 40;                // degrees a foot will tilt to meet the ground

    readonly Node3D character;
    readonly Skeleton3D skel;
    readonly FootProbe probe;
    readonly TwoBoneIK3D ik;
    readonly FootAlign align;

    public FootIK(Node3D character, Skeleton3D skel, Node world)
    {
        this.character = character;
        this.skel = skel;
        var feet = new Foot[2];
        ik = new TwoBoneIK3D { Name = "LegIK" };
        ik.SetSettingCount(2);
        for (int i = 0; i < 2; i++)
        {
            string s = i == 0 ? "Left" : "Right";
            var target = new Node3D { Name = $"FootTarget{s}" };
            var pole = new Node3D { Name = $"KneePole{s}" };
            world.AddChild(target);
            world.AddChild(pole);
            feet[i] = new Foot(skel.FindBone(s + "UpperLeg"), skel.FindBone(s + "LowerLeg"), skel.FindBone(s + "Foot"), target, pole) { Toes = skel.FindBone(s + "Toes") };
            ik.SetRootBoneName(i, s + "UpperLeg");
            ik.SetMiddleBoneName(i, s + "LowerLeg");
            ik.SetEndBoneName(i, s + "Foot");
        }
        probe = new FootProbe { Name = "FootProbe", Character = character, Feet = feet, Ik = this };
        align = new FootAlign { Name = "FootAlign", Feet = feet, MaxTilt = MaxTilt };
        skel.AddChild(probe);
        skel.AddChild(ik);
        skel.AddChild(align);
        for (int i = 0; i < 2; i++)  // node paths once everything is in the tree
        {
            ik.SetTargetNode(i, ik.GetPathTo(feet[i].Target));
            ik.SetPoleNode(i, ik.GetPathTo(feet[i].Pole));
        }
    }

    public bool Enabled
    {
        get => probe.Active;
        set { probe.Active = ik.Active = align.Active = value; if (!value) probe.Reset(); }
    }

    /// <summary>Ground under a point (world): height and normal, or the point's own height and up when nothing's there.</summary>
    public static (float y, Vector3 normal) Ground(Node3D from, Vector3 at)
    {
        var space = from.GetWorld3D().DirectSpaceState;
        // Layer 1 only: the ground. A ragdoll's bodies (layer 2) follow the clip even when not simulating.
        // From 2 m up: from 1.2 m a point at street level under the 1.26 m stair landing started inside it.
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 2.0f, at + Vector3.Down * 2.5f, 1));
        if (hit.Count == 0) return (at.Y, Vector3.Up);
        return (((Vector3)hit["position"]).Y, ((Vector3)hit["normal"]).Normalized());
    }

    public Foot[] Feet => probe.Feet;
}

public sealed class Foot
{
    public readonly int Thigh, Shin, Bone;
    public readonly Node3D Target, Pole;
    public Basis AnimatedBasis = Basis.Identity;  // the foot's world basis from the clip, this frame
    public Vector3 Normal = Vector3.Up;          // smoothed ground normal under it
    public float Lift;                            // smoothed vertical correction to its animated height
    public float GroundY;
    public Vector3 GroundNormal = Vector3.Up;    // this frame's, unsmoothed (for the IK test)
    public Vector3 FinalAnkle;                    // world, after the IK: what's drawn (Skeleton3D restores
                                                  // its poses after the modifiers, so read this, not the bone)
    public int Toes = -1;
    public Vector3 FinalToe;                      // the toe tip, world, as drawn
    public float ToeLift;                         // this frame's lift from the toe ray (a lip: at most MaxToeLift)
    public float ToePitch;                        // this frame's toes-up turn about the ankle (radians)
    public Vector3 ToeAxis = Vector3.Right;       // ...about this axis (world)
    public bool Planted;                          // locked where it touched down
    public float AnimHeight;                      // the clip's ankle over its floor this frame (contact: under PlantHeight)
    public Vector3 LockPos, ReleaseOff;           // where (world), and the offset easing out after lift-off
    public float Step;                            // a step under way: 1 -> 0 over StepTime
    public Vector3 StepFrom;                      // ...from this offset to the clip's place
    public Foot(int thigh, int shin, int bone, Node3D target, Node3D pole) { Thigh = thigh; Shin = shin; Bone = bone; Target = target; Pole = pole; }
}

public partial class FootProbe : SkeletonModifier3D
{
    public Node3D Character = null!;
    public Foot[] Feet = Array.Empty<Foot>();
    public FootIK Ik = null!;
    float hips;  // smoothed hip drop (world metres, <= 0)
    Vector3 lastRoot;
    bool hasRoot;
    int hipsBone = -1;

    public void Reset() { hips = 0; hasRoot = false; foreach (var f in Feet) { f.Lift = 0; f.Normal = Vector3.Up; f.Planted = false; f.ReleaseOff = Vector3.Zero; f.Step = 0; } }

    public override void _ProcessModificationWithDelta(double delta)
    {
        var skel = GetSkeleton();
        if (skel == null || Feet.Length == 0) return;
        if (hipsBone < 0) hipsBone = skel.FindBone("Hips");
        var xf = skel.GlobalTransform;
        float rootY = Character.GlobalPosition.Y;
        float k = 1 - (float)Math.Exp(-14 * delta);  // smoothing per frame
        float lowest = 0;
        var rootNow = Character.GlobalPosition;
        float rootSpeed = delta > 0 && hasRoot ? new Vector2(rootNow.X - lastRoot.X, rootNow.Z - lastRoot.Z).Length() / (float)delta : 0;
        lastRoot = rootNow;
        hasRoot = true;
        var forward = Character.GlobalBasis.Z.Normalized();
        foreach (var f in Feet)
        {
            var world = xf * skel.GetBoneGlobalPose(f.Bone);
            f.AnimatedBasis = world.Basis.Orthonormalized();
            f.AnimHeight = world.Origin.Y - rootY - FootIK.AnkleHeight;  // the clip's ankle over its own floor
            // Where the foot goes across the ground first (the clip's place, or held where it landed,
            // or easing out from there), then the rays there: on stairs the clip's foot and a locked
            // one stand over different steps.
            var at = world.Origin;
            float stepUp = 0;
            if (Ik.Lock)
            {
                bool down = f.AnimHeight < FootIK.PlantHeight;
                var flat = new Vector3(at.X - f.LockPos.X, 0, at.Z - f.LockPos.Z);
                if (f.Step > 0)
                {
                    // A step: the clip left the foot down but the body moved off it (a stop, a turn on the spot, a
                    // cross-fade): it's lifted and put down at the clip's place over STEP_TIME, on an arc.
                    f.Step = Math.Max(0, f.Step - (float)(delta / FootIK.StepTime));
                    float u = 1 - f.Step, e = u * u * (3 - 2 * u);
                    at += f.StepFrom * (1 - e);
                    stepUp = FootIK.StepHeight * 4 * u * (1 - u) * Math.Min(1, f.StepFrom.Length() / 0.15f);
                    if (f.Step == 0 && down) { f.Planted = true; f.LockPos = world.Origin; }
                }
                else if (down && !f.Planted) { f.Planted = true; f.LockPos = at; f.ReleaseOff = Vector3.Zero; }
                else if (f.Planted && (!down || flat.Length() > FootIK.MaxLockStretch) && (rootSpeed >= FootIK.StepBelow || !Ik.Steps))
                {
                    f.Planted = false;
                    f.ReleaseOff = -flat;  // lifted by the clip: from where it was held, easing to the clip's place
                }
                else if (f.Planted && !down)
                {
                    f.Planted = false;
                    f.ReleaseOff = -flat;
                }
                else if (f.Planted && Ik.Steps && flat.Length() > FootIK.StepStretch)
                {
                    f.Planted = false;
                    f.Step = 1;
                    f.StepFrom = -flat;
                    at += f.StepFrom;
                }
                if (f.Planted) at = new Vector3(f.LockPos.X, at.Y, f.LockPos.Z);
                else if (f.Step == 0 && f.ReleaseOff.LengthSquared() > 1e-8f)
                {
                    at += f.ReleaseOff;
                    f.ReleaseOff *= (float)Math.Exp(-delta / FootIK.ReleaseTime);
                }
            }
            else { f.Planted = false; f.Step = 0; }
            var shift = new Vector3(at.X - world.Origin.X, 0, at.Z - world.Origin.Z);
            var (gy, n) = Ik.GroundUnder(Character, at);
            f.GroundY = gy;
            f.GroundNormal = n;
            // Keep the clip's height above its own floor (the root's plane), over the real ground;
            // on a slope the ankle sits higher above the point under it (the sole is tilted).
            float slope = AnkleHeight(n) - FootIK.AnkleHeight;
            float want = gy - rootY + slope + stepUp;
            // Never under its own floor: two clips cross-fading can carry a foot through the ground (a stride
            // fading into standing sank a foot 15-20 cm).
            if (f.AnimHeight < -FootIK.SinkLimit) want -= f.AnimHeight + FootIK.SinkLimit;
            // Feet follow the ground at once (a planted foot mustn't lag into a slope); the hips and
            // the tilt are smoothed instead, which is what hides a step up the kerb.
            f.ToeLift = 0;
            f.ToePitch = 0;
            if (Ik.ToeRay && f.Toes >= 0)
            {
                // The ground under the toe tip against the plane under the ankle: higher means a step.
                var toes = xf * skel.GetBoneGlobalPose(f.Toes);
                var tip = toes.Origin + toes.Basis.Y.Normalized() * FootIK.ToeLength + shift;
                var (ty, tn) = Ik.GroundUnder(Character, tip);
                var d = tip - at;
                float plane = gy - (n.X * d.X + n.Z * d.Z) / Math.Max(n.Y, 0.3f);
                float step = ty - plane;
                if (step > 0.01f && tn.Y > 0.9f)
                {
                    // How far the toe tip would be under the step with the ankle where it's going;
                    // raise the toes about the ankle (heel down) up to MaxToePitch, lift only a small rest.
                    float need = ty + FootIK.ToeHeight - (tip.Y + want);
                    if (need > 0)
                    {
                        var flatD = new Vector3(d.X, 0, d.Z);
                        float reach = Math.Max(flatD.Length(), 0.08f);
                        float sn = Math.Min(need / reach, Mathf.Sin(Mathf.DegToRad(FootIK.MaxToePitch)));
                        f.ToePitch = Mathf.Asin(sn);
                        f.ToeAxis = flatD.Normalized().Cross(Vector3.Up).Normalized();  // turning the toes about this lifts them
                        float rest = need - reach * sn;
                        if (rest > 0 && rest <= FootIK.MaxToeLift) { f.ToeLift = rest; want += rest; }
                    }
                }
            }
            f.Lift = want;
            f.Normal = (delta > 0 ? f.Normal.Lerp(n, k) : n).Normalized();
            lowest = Math.Min(lowest, f.Lift);
            var target = at + Vector3.Up * f.Lift;
            f.Target.GlobalPosition = target;
            var knee = xf * skel.GetBoneGlobalPose(f.Shin).Origin;
            f.Pole.GlobalPosition = knee + forward * 1.0f + Vector3.Up * f.Lift;
        }
        // Drop the hips so the lower leg can reach (the higher one just bends more).
        hips = delta > 0 ? Mathf.Lerp(hips, lowest, k) : lowest;
        var local = xf.Basis.Inverse() * new Vector3(0, hips, 0);
        skel.SetBonePosePosition(hipsBone, skel.GetBonePosePosition(hipsBone) + local);
    }

    static float AnkleHeight(Vector3 n) => FootIK.AnkleHeight / Math.Max(0.5f, n.Y);
}

public partial class FootAlign : SkeletonModifier3D
{
    public Foot[] Feet = Array.Empty<Foot>();
    public float MaxTilt = 40;

    public override void _ProcessModificationWithDelta(double delta)
    {
        var skel = GetSkeleton();
        if (skel == null) return;
        var xf = skel.GlobalTransform;
        var inv = xf.AffineInverse();
        foreach (var f in Feet)
        {
            var n = f.Normal;
            f.FinalAnkle = xf * skel.GetBoneGlobalPose(f.Bone).Origin;
            float angle = Vector3.Up.AngleTo(n);
            // Always: the leg IK carries the foot round with the shin as it bends the knee (toes pointing
            // into a kerb or a step); the foot goes back to the clip's angle, tilted to the ground.
            angle = Math.Min(angle, Mathf.DegToRad(MaxTilt));
            var axis = Vector3.Up.Cross(n).Normalized();
            var tilted = angle < 0.001f ? f.AnimatedBasis : new Basis(axis, angle) * f.AnimatedBasis;
            if (f.ToePitch > 0) tilted = new Basis(f.ToeAxis, f.ToePitch) * tilted;  // toes up off a step
            var current = skel.GetBoneGlobalPose(f.Bone);
            var basisSkel = inv.Basis * tilted;
            skel.SetBoneGlobalPose(f.Bone, new Transform3D(basisSkel.Orthonormalized(), current.Origin));
            RecordToe(skel, xf, f);
        }
    }

    static void RecordToe(Skeleton3D skel, Transform3D xf, Foot f)
    {
        if (f.Toes < 0) return;
        var toes = xf * skel.GetBoneGlobalPose(f.Toes);
        f.FinalToe = toes.Origin + toes.Basis.Y.Normalized() * FootIK.ToeLength;
    }
}
