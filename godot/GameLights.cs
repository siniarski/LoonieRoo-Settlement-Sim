// The lights' keys and debug options (Game is partial: the rest of the game is Game.cs).
//
// Keys (first person, playing a survivor): G your flashlight (in a car: its headlights), X the light switch of the room
// you're in (or of the room past the door in front of you). Each is a command (Sim.Lights), so it's in the log.
// Debug options, for pictures and the light bench (not commands, so not in the log, like --at):
//   --mains=off             the mains gone (no porches, street lamps or mains-lit rooms)
//   --hour=H                the clock jumped to the next H o'clock (no ticks run: a night without a day before it)
//   --switch=X,Y[;X,Y]      flip the light switch of the room at each tile
//   --give=flashlight[:on]  a flashlight in your bag (switched on)
//   --car=X,Y,H             a car at tile X, Y, heading H (0-31, 0 east, 8 south), headlights on, a full tank
//   --fire=X,Y --stove=X,Y --heater=X,Y   a fire burning there; a stove someone is cooking at; a lit heater (a blizzard)
//   --lights=N --light-shadows=K   the renderer's light budget and its shadowed share (WorldLights); --lights=-1 none at all
//   --lightbench[=0,8,16,32]  frame times with each budget (World3D/Lights.cs), printed, then quit

using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using LoonieRoo.Sim;
using SimLights = LoonieRoo.Sim.Lights;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Game;

public partial class Game
{
    static int[] XY(string s) => Array.ConvertAll(s.Split(','), (v) => int.Parse(v, CultureInfo.InvariantCulture));

    void LightArgs(Dictionary<string, string> args)
    {
        if (args.TryGetValue("hour", out var h))
        {
            int hour = int.Parse(h, CultureInfo.InvariantCulture);
            int guard = 0;
            while ((int)Math.Floor(Time.hourOf(state.tick)) != hour && guard++ < Time.DAY_LENGTH) state.tick++;
        }
        if (args.TryGetValue("mains", out var mains) && state.timeline != null && mains == "off") state.timeline.powerOffDay = 0;
        if (args.TryGetValue("lights", out var n)) World3D.WorldLights.Budget = int.Parse(n, CultureInfo.InvariantCulture);
        if (args.TryGetValue("light-shadows", out var k)) World3D.WorldLights.Shadowed = int.Parse(k, CultureInfo.InvariantCulture);
        if (args.TryGetValue("lightbench", out var b)) World3D.WorldLights.Bench(b == "" ? "0,8,16,32" : b);
        var you = ControlMod.controlled(state);
        if (args.TryGetValue("switch", out var sw))
            foreach (var t in sw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = XY(t);
                var r = SimLights.roomAt(state, p[0], p[1]);
                if (r == null) { GD.Print($"--switch: no room at {t}"); continue; }
                state.lightSwitches ??= new JsRecord<bool>();
                state.lightSwitches[r.key] = !SimLights.switchedOn(state, r);
            }
        if (args.TryGetValue("give", out var give) && you != null && give.StartsWith("flashlight"))
        {
            var item = Items.spawnItem(state, SimLights.FLASHLIGHT, you.pos.x, you.pos.y);
            Inventory.stow(state, you, item);
            if (give.EndsWith(":on")) item.lit = true;
        }
        if (args.TryGetValue("car", out var car))
        {
            var p = XY(car);
            var v = Vehicles.addVehicle(state, p[0], p[1], p.Length > 2 ? p[2] : 0, Vehicles.FUEL_MAX);
            v.lights = true;
        }
        if (args.TryGetValue("fire", out var fire))
        {
            var p = XY(fire);
            if (!FireMod.ignite(state, p[0], p[1])) state.fires.Add(new Fire { x = p[0], y = p[1], left = 600 });
        }
        if (args.TryGetValue("stove", out var stove) && you != null)
        {
            var p = XY(stove);
            var f = FurnitureMod.addFurniture(state, "stove", p[0], p[1]);
            state.jobs.Add(new Job { id = $"j{state.nextId++}", type = "cook", x = p[0], y = p[1], furnitureId = f.id, reservedBy = you.id, progress = 1 });
        }
        if (args.TryGetValue("heater", out var heater))
        {
            var p = XY(heater);
            FurnitureMod.addFurniture(state, "heater", p[0], p[1]).stored = 3;
            state.weather = "blizzard";
        }
    }

    /// <summary>G: your flashlight, or in a car its headlights.</summary>
    void LightKey(Survivor you) =>
        Command(new Command { type = Vehicles.vehicleOf(state, you) != null ? "headlights" : "flashlight" });

    /// <summary>X: the switch of the room you're in, else of the room past the door in front of you.</summary>
    void SwitchKey(Survivor you)
    {
        var t = SimLights.roomAt(state, you.pos.x, you.pos.y) != null ? you.pos : FrontTile(you);
        Command(new Command { type = "lightSwitch", x = t.x, y = t.y });
    }
}
