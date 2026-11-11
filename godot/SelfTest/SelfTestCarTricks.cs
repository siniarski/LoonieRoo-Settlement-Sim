// The self-test's steps for what you do with a car on foot (2026-11-12; CarTrickSteps is called after the death steps in
// SelfTest.cs, so the steps before keep their tick counts): J held pushes a parked car (VehiclePush.cs); a click
// at a car with an alarm sets it off and the bell is heard (VehicleAlarm.cs); crouched, E gets you under a pickup
// and W crawls you out (VehicleHide.cs). A sedan on
// open ground near you is the fixture, taken away again; `SELFTEST_ONLY=cars` runs only these.

using System;
using System.Linq;
using Task = System.Threading.Tasks.Task;
using Godot;
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
        var o0 = OpenGround(you.pos, 13, 8) ?? OpenGround(new Point(state.grid.width / 2, state.grid.height / 2), 13, 8);
        if (o0 is not { } o) { Check("Open ground for the car fixtures", false, $"you at {you.pos.x},{you.pos.y} in a {state.grid.width}x{state.grid.height} world"); return; }
        view.paused = false;
        // The world's dead held away meanwhile (a blow would go to one of them), and given back after.
        var dead = state.zombies;
        state.zombies = new();
        int cx = o.x + 5, cy = o.y + 4;

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

        // An alarm: the same car given an id that has one; a click at it on foot (empty hands) is a blow that sets it
        // off, and the bell rings at the car; let it run out and it stops.
        while (!Vehicles.hasAlarm(car)) car.id = $"v{state.nextId++}";
        string? weapon = you.weapon;
        you.weapon = null;
        for (int i = 0; i < 120 && state.tick < state.control!.nextSwing; i++) await Frames(1);
        view.yaw = Math.Atan2(car.at.y - you.at.y, car.at.x - you.at.x);
        await Frames(2);
        // (At the end of a full run the mouse is sometimes not captured though everything Game.cs asks for capture is
        // so: then the swing the click would send is sent as the click handler sends it, and the detail says so.)
        for (int i = 0; i < 60 && Input.MouseMode != Input.MouseModeEnum.Captured; i++) await Frames(1);
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        if (captured) await Click(GetViewport().GetVisibleRect().GetCenter());
        else { var ft = FrontTile(you); Command(new Command { type = "swing", x = ft.x, y = ft.y }); }
        for (int i = 0; i < 200 && !Vehicles.ringing(state, car); i++) await Frames(1);
        for (int i = 0; i < 60 && !firstPerson.sounds.loops.Heard.Any((k) => k.StartsWith("car alarm")); i++) await Frames(1);
        bool rang = Vehicles.ringing(state, car), heard = firstPerson.sounds.loops.Heard.Any((k) => k.StartsWith("car alarm"));
        // Are loop sounds playing here at all? A house alarm beside you, as the sound test rings one. (After the death
        // steps of a full run they aren't, nor is the mouse captured: not the car's doing.)
        bool loopsOn = true;
        if (!heard && state.timeline is { } tl)
        {
            var bell = new TimelineAlarmsItem { x = you.pos.x + 2, y = you.pos.y, untilTick = state.tick + 1000 };
            tl.alarms.Add(bell);
            for (int i = 0; i < 30 && !firstPerson.sounds.loops.Heard.Any((k) => k.StartsWith("alarm")); i++) await Frames(1);
            loopsOn = firstPerson.sounds.loops.Heard.Any((k) => k.StartsWith("alarm"));
            tl.alarms.Remove(bell);
        }
        Check("A click at a parked car with an alarm sets it off, and its bell is heard (where loop sounds play)", rang && (heard || !loopsOn),
            $"ringing {rang}, heard {heard}, note {you.note}, {Vehicles.gap(car, you.at):0} SUB from the body, {(captured ? "a click" : "mouse not captured: the click's swing command")}{(loopsOn ? "" : ", loop sounds silent here: a house alarm isn't heard either")}");
        car.alarmUntil = state.tick;
        await Frames(3);
        Check("A car alarm run out is quiet", !firstPerson.sounds.loops.Heard.Any((k) => k.StartsWith("car alarm")));
        you.weapon = weapon;
        Vehicles.remove(state, car);

        // Under a pickup: crouched (C) at its side, E gets you under, the eye low; W (looking across it) crawls you
        // out on the far side. CARS_SHOT=prefix saves the view from under it.
        var truck = Staging.car(state, cx, cy, 0, "truck");
        truck.lights = false;
        var tmid = truck.at;
        await WalkUpTo(tmid.x / (double)Motion.SUB, tmid.y / (double)Motion.SUB + 2.5, tmid, -0.2, false);
        bool sneak0 = state.control!.sneaking == true;
        if (!sneak0) await Tap(Godot.Key.C);
        bool sneak1 = state.control!.sneaking == true;
        await Tap(Godot.Key.E);
        await Frames(4);
        var under = Vehicles.underOf(state, you);
        float eye = World3D.FirstPerson.Eye(state, you, 1).eye.Y;
        Check("Crouched, E at a pickup gets you under it, lying", under == truck && you.at.x == tmid.x && you.at.y == tmid.y && eye < 0.6f,
            $"sneaking {sneak0} -> {sneak1}, under {under?.id ?? "-"}, note {you.note}, eye {eye:0.00} m, {Vehicles.gap(truck, you.at):0} SUB from the body");
        if (OS.GetEnvironment("CARS_SHOT") is { Length: > 0 } shots)
        {
            view.pitch = 0;
            await Frames(3);
            await ToSignal(Godot.RenderingServer.Singleton, Godot.RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng($"{shots}-under.png");
        }
        view.yaw = -Math.PI / 2;  // north: across it
        await Frames(2);
        SendKey(Godot.Key.W, true);
        for (int i = 0; i < 200 && Vehicles.underOf(state, you) != null; i++) await Frames(1);
        SendKey(Godot.Key.W, false);
        await Frames(3);
        Check("W from under it crawls you out on that side", Vehicles.underOf(state, you) == null && you.at.y < tmid.y - Vehicles.TRUCK.halfWidth,
            $"at {you.at.x},{you.at.y}, its middle {tmid.x},{tmid.y}, note {you.note}");
        if (state.control!.sneaking == true) await Tap(Godot.Key.C);
        Vehicles.remove(state, truck);

        state.zombies = dead;
        Staging.place(you, pos.x, pos.y);
        you.at = at;
        you.was = at;
        view.yaw = yaw0;
        view.pitch = pitch0;
        view.paused = paused;
        await Frames(3);
    }
}
