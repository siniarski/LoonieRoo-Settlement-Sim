// The self-test's steps for what you do with a car on foot (2026-11-12; CarTrickSteps is called last from
// SelfTest.cs, so the steps before keep their tick counts): J held pushes a parked car (VehiclePush.cs). A sedan on
// open ground near you is the fixture, taken away again; `SELFTEST_ONLY=cars` runs only these.

using System;
using Task = System.Threading.Tasks.Task;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task CarTrickSteps()
    {
        var you = ControlMod.controlled(state)!;
        var (pos, at) = (you.pos, you.at);
        double yaw0 = view.yaw, pitch0 = view.pitch;
        bool paused = view.paused;
        if (OpenGround(you.pos, 15, 11) is not { } o) { Check("Open ground near you for the car fixtures", false); return; }
        view.paused = false;
        int cx = o.x + 5, cy = o.y + 5;

        // Push: behind the boot, W held into it, then J held as well: it rolls east, about a metre a second.
        var car = Staging.car(state, cx, cy, 0);
        car.lights = false;
        var mid = car.at;
        await WalkUpTo(mid.x / (double)Motion.SUB - 4.5, mid.y / (double)Motion.SUB + 0.1, mid, -0.3, true);
        SendKey(Godot.Key.J, true);
        // Still walking up to it (slow ground, deep snow): on until it first moves, then 30 ticks of pushing.
        int start = car.at.x, tw = state.tick;
        for (int i = 0; i < 4000 && car.at.x == start && state.tick < tw + 200; i++)
        {
            int t = state.tick;
            await Frames(1);
            if (state.tick != t) view.yaw = Math.Atan2(car.at.y - you.at.y, car.at.x - you.at.x);
        }
        int x0 = car.at.x, t0 = state.tick;
        double e0 = you.endurance;
        for (int i = 0; i < 2000 && state.tick < t0 + 30; i++)
        {
            int t = state.tick;
            await Frames(1);
            if (state.tick != t) view.yaw = Math.Atan2(car.at.y - you.at.y, car.at.x - you.at.x);
        }
        SendKey(Godot.Key.J, false);
        SendKey(Godot.Key.W, false);
        await Frames(3);
        int rolled = car.at.x - x0;
        Check("J held, walking into a parked car's boot, pushes it along", rolled > 20 * Vehicles.PUSH_STEP && rolled <= 32 * Vehicles.PUSH_STEP && you.endurance <= e0,
            $"{state.tick - tw - (state.tick - t0)} ticks to the first roll, then rolled {rolled} SUB in {state.tick - t0} ticks, endurance {e0:0} -> {you.endurance:0}, {Vehicles.gap(car, you.at):0} SUB from the body");
        Vehicles.remove(state, car);

        Staging.place(you, pos.x, pos.y);
        you.at = at;
        you.was = at;
        view.yaw = yaw0;
        view.pitch = pitch0;
        view.paused = paused;
        await Frames(3);
    }
}
