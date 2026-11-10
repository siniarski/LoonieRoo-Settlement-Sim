// The self-test's steps for the driver's seat (2026-11-11; CarSteps is called from SelfTest.cs after the world's):
// in a car you sit in its cabin (World3D/Cockpit.cs) and the instruments read the sim's car: speed, fuel, the
// warning lamps, the gear; the view turns with the car; out of it the cabin is gone. The car and what is done to it
// are fixtures set straight on the state with the world held still, and taken away again.

using System;
using Task = System.Threading.Tasks.Task;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task CarSteps()
    {
        var you = ControlMod.controlled(state)!;
        var cockpit = firstPerson.Cockpit;
        var (pos, at) = (you.pos, you.at);
        double yaw = view.yaw, pitch = view.pitch;
        bool paused = view.paused;
        Check("On foot there is no cabin round you", !cockpit.Shown);

        // The world held still meanwhile: the car is wherever you stood (among trees, as likely as not), and what is
        // checked is what the dashboard shows of it, not how it drives (cs/Sim.Tests has that).
        view.paused = true;
        var car = Staging.car(state, you.pos.x + 2, you.pos.y, 0);
        Vehicles.enterVehicle(state, you, car);
        car.fuel = Vehicles.FUEL_MAX * 0.6;
        await Frames(3);
        var g = cockpit.Gauges;
        Check("Behind the wheel you sit in the car's cabin, looking the way it points", cockpit.Shown && Vehicles.vehicleOf(state, you) == car
            && Math.Abs(Godot.Mathf.AngleDifference((float)view.yaw, (float)World3D.Cockpit.YawOf(car))) < 0.05,
            $"cabin {(cockpit.Shown ? "shown" : "hidden")}, looking {view.yaw:0.00}, the car points {World3D.Cockpit.YawOf(car):0.00}");
        Check("The fuel gauge reads the tank, and the lamps are out on a sound car with fuel", Math.Abs(g.Fuel - 0.6) < 0.01 && !g.LowFuel && !g.Engine && !g.Oil && !g.Battery && g.Lights,
            $"fuel {g.Fuel:0.00}, lamps: fuel {g.LowFuel}, engine {g.Engine}, oil {g.Oil}, battery {g.Battery}, lights {g.Lights}");

        // Rolling: the speedometer is the car's speed as km/h, the selector is in D, the engine is turning.
        car.speed = 96;
        await Frames(8);
        double kmh = Math.Abs(car.speed) * Tick.TICKS_PER_SECOND / Motion.SUB * 3.6;
        Check("The speedometer reads the car's speed in km/h, in D with the engine turning", car.speed > 0 && Math.Abs(g.Kmh - kmh) < 1.2 && g.Gear == 'D' && g.Rpm > 500 && !g.Brake,
            $"the car does {car.speed:0} a tick ({kmh:0.0} km/h), the dial says {g.Kmh:0.0}, gear {g.Gear}, {g.Rpm:0} r/min");

        // Nearly dry and badly knocked about: the lamps say so.
        car.speed = 0;
        car.fuel = Vehicles.FUEL_MAX * 0.05;
        car.health = Vehicles.VEHICLE_HEALTH * 0.3;
        await Frames(2);
        Check("Low on fuel and damaged, the FUEL and ENGINE lamps light, and standing still it is in P with the brake on", g.LowFuel && g.Engine && g.Gear == 'P' && g.Brake && !g.Oil,
            $"fuel lamp {g.LowFuel}, engine lamp {g.Engine}, gear {g.Gear}, brake {g.Brake}");
        car.fuel = 0;
        await Frames(2);
        Check("Out of fuel the engine stops: OIL and BATT light", g.Oil && g.Battery && g.Fuel == 0, $"oil {g.Oil}, battery {g.Battery}, fuel {g.Fuel:0.00}");

        // The car turned under you: the view goes with it.
        double before = view.yaw;
        car.heading = 6;
        await Frames(40);
        double turned = Godot.Mathf.AngleDifference((float)before, (float)view.yaw);
        Check("The view turns with the car", Math.Abs(turned - 6.0 / Vehicles.HEADINGS * Math.Tau) < 0.08, $"the car turned {6.0 / Vehicles.HEADINGS * Math.Tau:0.00} rad, the view {turned:0.00}");

        // Out again, and the car gone.
        Vehicles.leaveVehicle(state, you);
        Vehicles.remove(state, car);
        you.pos = pos;
        you.at = at;
        you.was = at;
        view.yaw = yaw;
        view.pitch = pitch;
        view.paused = paused;
        await Frames(3);
        Check("Out of the car the cabin is gone", !cockpit.Shown && Vehicles.vehicleOf(state, you) == null);
    }
}
