// The self-test's steps for standing on a car (2026-11-11; RoofSteps is called from SelfTest.cs after the car's):
// on a parked car's roof the eye is the roof's height over your standing eye and your body stands on it
// (Leaps.height, CarLeaps.cs); Space takes you down off it, or jumps where you stand where there is no room below,
// and either way the eye ends where you end. The car is a fixture put next to you, and taken away again.

using System;
using Task = System.Threading.Tasks.Task;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task RoofSteps()
    {
        var you = ControlMod.controlled(state)!;
        var (pos, at) = (you.pos, you.at);
        bool paused = view.paused;
        view.paused = true;
        float ground = World3D.FirstPerson.Eye(state, you, 1).eye.Y;
        var car = Staging.car(state, you.pos.x + 3, you.pos.y, 0);
        bool on = Leaps.standOn(state, you, car);
        await Frames(4);
        double top = Leaps.topAt(car, you.at);
        float eye = World3D.FirstPerson.Eye(state, you, 1).eye.Y;
        float groundThere = World3D.Terrain.Height(you.at.x / (float)Motion.SUB, you.at.y / (float)Motion.SUB);
        var ppl = firstPerson.People;
        Check("On a car's roof the eye is the roof's height over your eye, and your body stands on it",
            on && Math.Abs(eye - groundThere - World3D.FirstPerson.EYE - top) < 0.02f && Math.Abs(ppl.YourLift - top) < 0.03f,
            $"on the car {on}, roof {top:0.00} m, eye {eye - groundThere:0.00} m over the ground, body up {ppl.YourLift:0.00} m");

        // Space: down off it on your way, or (no room there) a jump on the roof; the eye ends where you do.
        view.paused = false;
        await Tap(Godot.Key.Space);
        string leap = state.control!.busy?.what ?? "none";
        for (int i = 0; i < 120 && Leaps.airborne(state.control!); i++) await Frames(1);
        view.paused = true;
        await Frames(3);
        float after = World3D.FirstPerson.Eye(state, you, 1).eye.Y - World3D.Terrain.Height(you.at.x / (float)Motion.SUB, you.at.y / (float)Motion.SUB);
        bool off = state.control!.onCar == null;
        Check("Space on the roof jumps down (or, no room below, jumps on it), and the eye ends where you do",
            (leap == "down" && off && Math.Abs(after - World3D.FirstPerson.EYE) < 0.05f) || (leap == "jump" && !off && Math.Abs(after - World3D.FirstPerson.EYE - Leaps.topAt(car, you.at)) < 0.05f),
            $"{leap}, {(off ? "on the ground" : "still on the car")}, eye {after:0.00} m over the ground");

        state.control!.onCar = null;
        Vehicles.remove(state, car);
        you.pos = pos;
        you.at = at;
        you.was = at;
        view.paused = paused;
        await Frames(3);
        Check("Off the car and the car gone, the eye is back at your standing height", Math.Abs(World3D.FirstPerson.Eye(state, you, 1).eye.Y - ground) < 0.05f);
    }
}
