// Pose intent (docs/characters.md section 1): what a mover's body is doing, read from the sim for the
// renderer to turn into an animation state. A pure read: nothing here is saved or changes state,
// and a loaded game rebuilds every pose from it.
//
//   stance      stand | sneak | sit (in a car) | lie (asleep) | crawl (a crawler) | dead
//   locomotion  speed (m/s: a tile is a metre, TICKS_PER_SECOND ticks a second), heading (radians,
//               of the motion, screen x right / y down), facing (the aim or the way you face, for
//               the controlled survivor)
//   action      what (control's busy action: getIn, openBoot, forage, build, eat...), progress 0..1
//               (the renderer plays its clip at this normalised time), anchor (where it happens:
//               driverDoor, boot, hood) and the target's id; approach (normal for now)
//   carry       right hand (the weapon's kind), left hand (a tool), both (something carried), back
//               (a pack), cart
//   condition   0..1 weights: limp left/right (a wounded or lost leg), arm left/right (a wounded or
//               lost arm), exhaustion, panic, cold, pain, infection, blood (bleeding), wet, startle
//               (1 at the startle, gone after STARTLE_TICKS)
//   lostLimbs   "armLeft:elbow" and the like (wounds.ts); crawlers have lost both legs at the knee
// A wound has no side in the sim: its side is a hash of the survivor's id (stable, no RNG draw).
#nullable disable warnings
namespace LoonieRoo.Sim;

public sealed class PoseIntent
{
    public string stance = "stand";
    public double speed, heading;
    public Point? facing;
    public string? action, target, anchor, approach;
    public double progress;
    public string? carryRight, carryLeft, carryBoth, carryBack;
    public bool cart;
    public double limpLeft, limpRight, armLeft, armRight, exhaustion, panic, cold, pain, infection, blood, wet, startle;
    public List<string> lostLimbs = new();
}

public static class PoseIntents
{
    /// <summary>The sim's rate at 1x (CLAUDE.md rule 6): for speeds in metres a second.</summary>
    public const double TICKS_PER_SECOND = 10;

    /// <summary>A startle shows for this long (a second at 1x).</summary>
    public const int STARTLE_TICKS = 10;

    public static PoseIntent ofSurvivor(SimState state, Survivor s)
    {
        var p = new PoseIntent();
        Locomotion(p, s.at, s.was);
        var c = state.control;
        bool controlled = c != null && c.survivorId == s.id;
        if (s.health <= 0) p.stance = "dead";
        else if (Js.T(Vehicles.vehicleOf(state, s))) p.stance = "sit";
        else if (Js.T(s.sleep) || (controlled && c.busy?.what == "sleep")) p.stance = "lie";
        else if (controlled && c.sneaking == true) p.stance = "sneak";
        if (controlled)
        {
            p.facing = c.aim ?? c.facing;
            if (Js.T(c.busy))
            {
                var b = c.busy;
                p.action = b.what;
                p.target = b.targetId;
                p.approach = "normal";
                p.progress = (b.what == "sleep" || b.duration <= 0) ? 0 : JsMath.min(1, b.progress / b.duration);
                if (Vehicles.isCarAction(b.what)) p.anchor = Vehicles.anchorOf(b.what);
            }
        }
        if (Js.T(s.weapon)) p.carryRight = Items.getItem(state, s.weapon)?.kind;
        if (Js.T(s.tool)) p.carryLeft = Items.getItem(state, s.tool)?.kind;
        if (Js.T(s.carrying)) p.carryBoth = Items.getItem(state, s.carrying)?.kind ?? s.carrying;
        if (s.pack.length > 0) p.carryBack = "pack";
        p.cart = Js.T(s.cart);

        p.lostLimbs = s.lostLimbs.slice();
        // A wound's side: a hash of the survivor, the same every time.
        var woundSide = (Hash.hashRoll(Hash.idNumber(s.id), 0, 0x51df) < 0.5) ? "Left" : "Right";
        foreach (var side in new[] { "Left", "Right" })
        {
            double leg = (s.wound?.limb == "leg" && woundSide == side) || s.lostLimbs.some((l) => l.StartsWith("leg" + side)) ? 1 : 0;
            double arm = (s.wound?.limb == "arm" && woundSide == side) || s.lostLimbs.some((l) => l.StartsWith("arm" + side)) ? 1 : 0;
            if (side == "Left") { p.limpLeft = leg; p.armLeft = arm; } else { p.limpRight = leg; p.armRight = arm; }
        }
        p.exhaustion = Clamp(1 - s.endurance / BodyMod.BODY_MAX);
        p.panic = Clamp(s.panic / BodyMod.BODY_MAX);
        p.cold = Clamp((BodyMod.COLD + 20 - s.bodyHeat) / (BodyMod.COLD + 20));
        p.pain = Clamp(1 - s.health / Health.HEALTH_MAX);
        p.infection = s.infection == null ? 0 : Clamp(s.infection.Value / Infection.INFECTION_TURN);
        p.blood = s.bleeding ? 1 : 0;
        p.wet = Clamp(s.wetness / BodyMod.BODY_MAX);
        p.startle = s.startledAt == null ? 0 : Clamp(1 - (state.tick - s.startledAt.Value) / STARTLE_TICKS);
        return p;
    }

    public static PoseIntent ofZombie(SimState state, Zombie z)
    {
        var p = new PoseIntent();
        Locomotion(p, z.at, z.was);
        if (z.variant == "crawler")
        {
            p.stance = "crawl";
            p.lostLimbs = ["legLeft:knee", "legRight:knee"];
        }
        return p;
    }

    static void Locomotion(PoseIntent p, Point at, Point was)
    {
        double dx = at.x - was.x, dy = at.y - was.y;
        double perTick = JsMath.sqrt(dx * dx + dy * dy) / Motion.SUB;  // tiles (metres) a tick
        p.speed = perTick * TICKS_PER_SECOND;
        p.heading = (dx == 0 && dy == 0) ? 0 : Math.Atan2(dy, dx);
    }

    static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
