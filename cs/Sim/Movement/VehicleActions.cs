// Car actions and anchors (docs/characters.md section 5): what you do at a car takes time, and the
// renderer plays it from the sim's progress (a clip at normalised time = progress).
//
//   getIn      at the driver's door: GET_IN_TICKS, then you're in (Vehicles.cs useVehicle)
//   getOut     from the seat: GET_OUT_TICKS (the car brakes meanwhile), then you're out beside it
//   boot       open or shut it from behind the car: BOOT_TICKS
//   hood       open or shut the bonnet from in front: HOOD_TICKS
// They're direct control's busy actions (ControlMod.cs: what, targetId = the car, progress, duration),
// counted in ticks, not slow ticks: seconds of handling, not hours of work, whatever the day length.
// Walking (or steering, for getOut: no) cancels one; letting go of control drops it.
// The boot and the bonnet are state (bootOpen, hoodOpen) for the renderer; what's in the boot
// stays reachable either way (Inventory.cs stash and take, as before).
//
// Anchors: where a body stands to do each (SUB, the car's frame turned by its heading), and the way
// it faces (a heading index): the driver's door on the car's left beside the seat, the boot behind,
// the bonnet in front, each just clear of the car's body (VehicleBody.cs). The 3D renderer maps the
// same names onto its model.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Vehicles
{
    public const int GET_IN_TICKS = 25;
    public const int GET_OUT_TICKS = 20;
    public const int BOOT_TICKS = 8;
    public const int HOOD_TICKS = 10;

    public static readonly List<string> CAR_ACTIONS = ["getIn", "getOut", "openBoot", "closeBoot", "openHood", "closeHood"];

    public static bool isCarAction(string what) => CAR_ACTIONS.includes(what);

    /// <summary>The anchor a car action happens at.</summary>
    public static string anchorOf(string what) => what is "getIn" or "getOut" ? "driverDoor" : what.EndsWith("Boot") ? "boot" : "hood";

    /// <summary>Where a body stands for an anchor (SUB) and the heading index it faces (towards the car).</summary>
    public static (Point at, double facing) anchorAt(Vehicle v, string anchor)
    {
        var d = Js.Get(HEADING_DIRS, Js.I(v.heading));
        double dx = Js.Get(d, 0), dy = Js.Get(d, 1);
        var shape = shapeOf(v);
        double clear = Motion.BODY_RADIUS + 16;
        // The car's left, on screen (y down): heading 0 is east, its left is north.
        double lx = dy, ly = -dx;
        // How far along the car (ahead of its centre) and how far out to its left the body stands.
        double along, left, facing;
        if (anchor == "boot") { along = -(shape.halfLength + clear); left = 0; facing = v.heading; }
        else if (anchor == "hood") { along = shape.halfLength + clear; left = 0; facing = (v.heading + 16) % HEADINGS; }
        else { along = shape.seat; left = shape.halfWidth + clear; facing = (v.heading + 8) % HEADINGS; }
        var at = new Point { x = Js.I(v.at.x + JsMath.round((dx * along + lx * left) / 256)), y = Js.I(v.at.y + JsMath.round((dy * along + ly * left) / 256)) };
        return (at, facing);
    }

    /// <summary>Begin a car action for the controlled survivor. Returns what happened, or why not.</summary>
    public static string startCarAction(SimState state, Survivor s, Vehicle v, string what)
    {
        var c = Js.NN(state.control);
        double duration = what switch
        {
            "getIn" => GET_IN_TICKS,
            "getOut" => GET_OUT_TICKS,
            "openBoot" or "closeBoot" => BOOT_TICKS,
            _ => HOOD_TICKS,
        };
        c.busy = new ControlBusy { what = what, targetId = v.id, progress = 0, duration = duration };
        s.move = null;
        return what switch
        {
            "getIn" => "opening the door",
            "getOut" => "getting out",
            "openBoot" => "opening the boot",
            "closeBoot" => "shutting the boot",
            "openHood" => "lifting the bonnet",
            _ => "shutting the bonnet",
        };
    }

    /// <summary>A tick of a car action; at the end, its effect. Returns a note when it ends, else null.</summary>
    public static string? carActionTick(SimState state, Survivor s, Control c)
    {
        var busy = Js.NN(c.busy);
        busy.progress += 1;
        if (busy.progress < busy.duration)
        {
            return null;
        }
        c.busy = null;
        var v = vehiclesOf(state).find((x) => x.id == busy.targetId);
        if (!Js.T(v))
        {
            return "the car is gone";
        }
        switch (busy.what)
        {
            case "getIn":
                if (!within(v, s.at, ENTER_REACH)) return "the car moved off";
                return enterVehicle(state, s, v);
            case "getOut":
                return leaveVehicle(state, s);
            case "openBoot": v.bootOpen = true; return "the boot is open";
            case "closeBoot": v.bootOpen = false; return "the boot is shut";
            case "openHood": v.hoodOpen = true; return "the bonnet is up";
            default: v.hoodOpen = false; return "the bonnet is down";
        }
    }

    /// <summary>The boot or the bonnet of the car beside you: open it, or shut it. Returns (what happened, why not).</summary>
    public static (string? done, string? why) toggleLid(SimState state, Survivor s, string lid)
    {
        if (Js.T(vehicleOf(state, s)))
        {
            return (null, "get out first");
        }
        var v = vehicleNear(state, s.at);
        if (!Js.T(v))
        {
            return (null, "no car beside you");
        }
        if (Js.T(v.driverId))
        {
            return (null, "someone is in it");
        }
        bool open = lid == "boot" ? v.bootOpen : v.hoodOpen;
        var what = (open ? "close" : "open") + (lid == "boot" ? "Boot" : "Hood");
        return (startCarAction(state, s, v, what), null);
    }
}
