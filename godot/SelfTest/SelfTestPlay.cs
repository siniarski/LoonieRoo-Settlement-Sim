// The self-test's steps for cars and screens not driven elsewhere, and one long unattended play (2026-11-12, the self-test gaps
// pass; PlaySteps is called last from SelfTest.cs, after PlaceSteps; `SELFTEST_ONLY=play` runs only these). Cars: crawl out from
// under a pickup on each side, the dead stopped at the body of the car you sit in. Screens: F never gives up your character. Then a
// new solo world played with held keys for two game hours at 4x (LongPlaySteps).

using System;
using System.Collections.Generic;
using System.Linq;
using Task = System.Threading.Tasks.Task;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task PlaySteps()
    {
        await CarSideSteps();
        await ScreenSteps();
        await LongPlaySteps();
    }

    /// <summary>Under a pickup, out on the south side; the dead stopped at the body of a car you sit in.</summary>
    async Task CarSideSteps()
    {
        var you = ControlMod.controlled(state)!;
        var (pos, at) = (you.pos, you.at);
        double yaw0 = view.yaw, pitch0 = view.pitch;
        bool paused = view.paused;
        if (OpenGround(you.pos, 13, 8) is not { } o) { Check("Open ground for the car-side fixtures", false); return; }
        view.paused = false;
        var dead = state.zombies;
        state.zombies = new();
        int cx = o.x + 5, cy = o.y + 4;

        var truck = Staging.car(state, cx, cy, 0, "truck");
        truck.lights = false;
        var tmid = truck.at;
        await WalkUpTo(tmid.x / (double)Motion.SUB, tmid.y / (double)Motion.SUB - 2.5, tmid, -0.2, false);
        if (state.control!.sneaking != true) await Tap(Godot.Key.C);
        await Tap(Godot.Key.E);
        await Frames(4);
        bool under = Vehicles.underOf(state, you) == truck;
        view.yaw = Math.PI / 2;  // south: across it the other way
        await Frames(2);
        SendKey(Godot.Key.W, true);
        for (int i = 0; i < 200 && Vehicles.underOf(state, you) != null; i++) await Frames(1);
        SendKey(Godot.Key.W, false);
        await Frames(3);
        Check("Under a pickup, W looking south crawls you out on the south side", under && Vehicles.underOf(state, you) == null && you.at.y > tmid.y + Vehicles.TRUCK.halfWidth,
            $"under {under}, at {you.at.x},{you.at.y}, its middle {tmid.x},{tmid.y}, note {you.note}");
        if (state.control!.sneaking == true) await Tap(Godot.Key.C);
        Vehicles.remove(state, truck);

        // Sitting in a sedan with the dead after you: they come to it and stop at its body, none inside it.
        var car = Staging.car(state, cx, cy, 0);
        PutYou(you, cx + 0.5, cy + 0.5, cx + 3, cy + 0.5);
        var seated = Staging.seat(state, you, 1, 1, 0);
        await Frames(3);
        var zs = new[] { (-3, 0), (3, 2) }.Select((d) => Zombies.addZombie(state, cx + d.Item1, cy + d.Item2, 100, "static")).ToList();
        foreach (var z in zs) z.targetId = you.id;
        double nearest = double.MaxValue, hp0 = you.health;
        bool inside = false;
        for (int t0 = state.tick, i = 0; state.tick < t0 + 80 && i < 6000; i++)
        {
            await Frames(1);
            foreach (var z in zs)
            {
                double gap = Vehicles.gap(car, z.at);
                nearest = Math.Min(nearest, gap);
                inside |= gap < 0;
            }
        }
        Check("The dead come to the car you sit in and stop at its body, none inside it", seated != null && !inside && nearest < 2 * Motion.SUB,
            $"seated {seated != null}, nearest {nearest:0} SUB from the body, one inside {inside}, your health {hp0:0} -> {you.health:0}, alive {state.survivors.Contains(you) && ControlMod.controlled(state) == you}");
        foreach (var z in zs) Zombies.removeZombie(state, z);
        state.zombies = new();
        await Tap(Godot.Key.E);
        for (int i = 0; i < 400 && Vehicles.vehicleOf(state, you) != null; i++) await Frames(1);
        Vehicles.remove(state, car);

        state.zombies = dead;
        Staging.place(you, pos.x, pos.y);
        you.at = at;
        you.was = at;
        view.yaw = yaw0;
        view.pitch = pitch0;
        view.paused = paused;
        await Frames(3);
    }

    /// <summary>F gives up nothing: in first person, and in the Colony tab with nobody selected, you are still the one you were.</summary>
    async Task ScreenSteps()
    {
        if (ControlMod.controlled(state) is not { } you) { Check("Still playing someone after the car steps", false, $"control {state.control?.survivorId ?? "none"}, succession {state.succession != null}"); return; }
        string id = you.id;
        await Tap(Godot.Key.F);
        await Frames(2);
        Check("F in first person does not give up your character", ControlMod.controlled(state)?.id == id && view.firstPerson, $"playing {ControlMod.controlled(state)?.id ?? "nobody"}, first person {view.firstPerson}");
        await Tap(Godot.Key.M);
        await Frames(2);
        view.selectedId = null;
        await Tap(Godot.Key.F);
        await Frames(2);
        Check("F on the map with nobody selected does not give up your character either", ControlMod.controlled(state)?.id == id, $"playing {ControlMod.controlled(state)?.id ?? "nobody"}");
        await Tap(Godot.Key.M);
        await Frames(2);
        Check("M puts you back in first person", view.firstPerson && !view.colonyMode);
    }

    /// <summary>The nearest road tile and the way along it (a grid direction), or null.</summary>
    (Point at, Point along)? RoadNear(Point from)
    {
        var g = state.grid;
        int road = Array.IndexOf(Grids.GROUNDS, "road");
        for (int r = 0; r < 80; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = from.x + dx, y = from.y + dy;
                    if (!Grids.inBounds(g, x, y) || Grids.groundAt(g, x, y) != road || !Pathfinding.isPassable(g, x, y, Pathfinding.FREE)) continue;
                    foreach (var (ax, ay) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        if (Grids.inBounds(g, x + 2 * ax, y + 2 * ay) && Grids.groundAt(g, x + ax, y + ay) == road && Grids.groundAt(g, x + 2 * ax, y + 2 * ay) == road)
                            return (new Point(x, y), new Point(ax, ay));
                }
        return null;
    }

    /// <summary>A new solo world; the keys held to run along a road and then across country, turning every few seconds, for two game
    /// hours at 4x: you are never in a wall or a car, the tick never stalls, and you get somewhere.</summary>
    async Task LongPlaySteps()
    {
        var settings = CopySettings(Difficulty.NORMAL);
        settings.start = "solo";
        NewGame(512, settings, 7, new Character { name = "Wren", job = 0, traits = new() });
        for (int f = 0; f < 3000 && (making != null || view.menu != null); f++) await Frames(1);
        await Frames(5);
        var you = ControlMod.controlled(state)!;
        view.paused = false;
        view.speed = 4;
        var start = you.pos;
        string road = "no road near";
        if (RoadNear(you.pos) is { } r)
        {
            PutYou(you, r.at.x + 0.5, r.at.y + 0.5, r.at.x + 0.5 + r.along.x, r.at.y + 0.5 + r.along.y);
            road = $"on a road at {r.at.x},{r.at.y}";
        }
        await Frames(3);
        var g = state.grid;
        var rng = new Random(11);   // the test's own turns: nothing of the sim's
        int tick0 = state.tick, hour = LoonieRoo.Sim.Time.TICKS_PER_HOUR, goal = tick0 + 2 * hour;
        int inWall = 0, inCar = 0, stalls = 0, frames = 0, lastTick = state.tick, quiet = 0, turnAt = tick0, legs = 0;
        double maxGap = 0, travelled = 0;
        var prev = you.at;
        SendKey(Godot.Key.W, true);
        SendKey(Godot.Key.Shift, true);
        while (state.tick < goal && frames < 20000)
        {
            await Frames(1);
            frames++;
            you = ControlMod.controlled(state)!;
            if (state.tick == lastTick) { quiet++; if (quiet > 120) { stalls++; quiet = 0; } }
            else { maxGap = Math.Max(maxGap, quiet); quiet = 0; lastTick = state.tick; }
            // A road for the first leg, then a turn every 3 to 6 game seconds across country.
            if (state.tick >= turnAt + 30 + rng.Next(30)) { view.yaw += (rng.NextDouble() - 0.5) * (legs++ < 6 ? 0.3 : 1.6); turnAt = state.tick; }
            if (Grids.getTile(g, you.pos.x, you.pos.y) == Grids.WALL) inWall++;
            if (Vehicles.vehicleOf(state, you) == null && Vehicles.vehiclesOf(state).Any((v) => Vehicles.gap(v, you.at) < 0 && Vehicles.underOf(state, you) != v)) inCar++;
            travelled += Math.Sqrt(Math.Pow(you.at.x - prev.x, 2) + Math.Pow(you.at.y - prev.y, 2)) / Motion.SUB;
            prev = you.at;
        }
        SendKey(Godot.Key.W, false);
        SendKey(Godot.Key.Shift, false);
        view.speed = 1;
        Check("Two game hours at 4x played unattended: the tick never stalls", state.tick >= goal && stalls == 0 && !view.halted, $"tick {tick0} -> {state.tick} of {goal} in {frames} frames, stalls {stalls}, longest quiet {maxGap} frames");
        Check("In them you are never inside a wall or a car", inWall == 0 && inCar == 0, $"in a wall {inWall} frames, in a car {inCar}");
        Check("And you got somewhere: held keys walked and ran their way across", travelled > 60, $"{road}, walked {travelled:0} m, now {you.pos.x},{you.pos.y} from {start.x},{start.y}, alive {you.health > 0}");
    }
}
