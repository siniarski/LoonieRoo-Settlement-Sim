// The self-test's second round of sound steps (2026-11-09; SoundTest in SelfTest.cs calls them): the
// sounds are made off the main thread; a wall, a shut door and an open one between you and a sound;
// your steps when you walk and the swing when you click; a gun firing, glass breaking, an alarm
// ringing, rain. Each puts back what it changed in state. Nothing here is heard: the checks are on
// what was played, from where, and through which bus.

using System;
using System.Linq;
using Task = System.Threading.Tasks.Task;
using Godot;
using LoonieRoo.Game.Audio;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task SoundSteps()
    {
        var sounds = firstPerson.sounds;
        sounds.bank.Wait();
        Check("The sounds are made off the main thread", sounds.bank.Ready && sounds.bank.MakeMs < 3000,
            $"{Bank.KINDS.Length} kinds, {Bank.KINDS.Sum((k) => k.count)} sounds in {sounds.bank.MakeMs:0} ms on a worker, {sounds.bank.InstallMs:0.0} ms to hand over");

        // Muffling, on a little grid of its own: a wall, a shut door, the same door open, glass.
        var g = Grids.createGrid(6, 1);
        Grids.setEdge(g, 3, 0, "w", Grids.EDGE_WALL);
        float wall = Muffle.Between(g, 0.5, 0.5, 5.5, 0.5);
        Grids.setEdge(g, 3, 0, "w", Grids.EDGE_DOOR);
        float shut = Muffle.Between(g, 0.5, 0.5, 5.5, 0.5);
        g.edgeOpen.Add(Grids.edgeIndex(g, 3, 0, "w"));
        float open = Muffle.Between(g, 0.5, 0.5, 5.5, 0.5);
        Grids.setEdge(g, 1, 0, "w", Grids.EDGE_WINDOW);
        float glass = Muffle.Between(g, 5.5, 0.5, 0.5, 0.5);
        Check("Walls and shut doors muffle, open ones don't", wall == Muffle.WALL && shut == Muffle.DOOR && open == 0 && glass == Muffle.GLASS
            && Muffle.BusFor(wall) == Muffle.MUFFLED && Muffle.BusFor(0) == "Master", $"wall {wall}, shut door {shut}, open {open}, glass {glass}");

        view.paused = true;
        var you = ControlMod.controlled(state)!;
        var me = new P(you.at.x / (double)Motion.SUB, you.at.y / (double)Motion.SUB);
        // A straight run of floor from you: three tiles one way.
        var dir = new[] { (1, 0), (0, 1), (-1, 0), (0, -1) }.FirstOrDefault((d) =>
            Enumerable.Range(1, 3).All((k) => Grids.getTile(state.grid, you.pos.x + d.Item1 * k, you.pos.y + d.Item2 * k) == Grids.FLOOR));
        if (dir == default) Check("Floor round you for the sound steps", false);
        else
        {
            // Two walls between you and a sound three tiles off.
            int e1 = Grids.edgeBetween(state.grid, you.pos.x + dir.Item1, you.pos.y + dir.Item2, you.pos.x + dir.Item1 * 2, you.pos.y + dir.Item2 * 2);
            int e2 = Grids.edgeBetween(state.grid, you.pos.x + dir.Item1 * 2, you.pos.y + dir.Item2 * 2, you.pos.x + dir.Item1 * 3, you.pos.y + dir.Item2 * 3);
            int was1 = state.grid.edges[e1], was2 = state.grid.edges[e2];
            state.grid.edges[e1] = Grids.EDGE_WALL;
            state.grid.edges[e2] = Grids.EDGE_WALL;
            var far = new P(you.pos.x + dir.Item1 * 3 + 0.5, you.pos.y + dir.Item2 * 3 + 0.5);
            sounds.Play(state, "glass", null, far, 1.2f, me, 1);
            float behind = sounds.LastMuffle;
            state.grid.edges[e1] = was1;
            state.grid.edges[e2] = was2;
            sounds.Play(state, "glass", null, far, 1.2f, me, 2);
            Check("A sound behind two walls comes through low and quiet", behind >= 2 && Muffle.BusFor(behind) == Muffle.WALLED && sounds.LastMuffle == 0,
                $"{behind} walls' worth -> {Muffle.DbFor(behind):0} dB on {Muffle.BusFor(behind)}; without them {sounds.LastMuffle}");

            // Glass: a window three tiles off, then smashed.
            int ew = Grids.edgeBetween(state.grid, you.pos.x + dir.Item1 * 2, you.pos.y + dir.Item2 * 2, you.pos.x + dir.Item1 * 3, you.pos.y + dir.Item2 * 3);
            int wasW = state.grid.edges[ew];
            state.grid.edges[ew] = Grids.EDGE_WINDOW;
            state.walkabilityVersion++;
            await Frames(3);
            int glass0 = sounds.Count("glass");
            Breach.smashGlass(state, ew);
            await Frames(3);
            Check("A window smashed near you is heard", sounds.Count("glass") > glass0 && sounds.LastKind == "glass", $"glass {sounds.Count("glass") - glass0}");
            state.grid.edges[ew] = wasW;
            state.walkabilityVersion++;
        }

        // A gun losing a round's wear in your hands: a shot.
        var gun = Items.spawnItem(state, "gun", you.pos.x, you.pos.y);
        var weapon = you.weapon;
        you.weapon = gun.id;
        await Frames(3);
        int shots0 = sounds.Count("gunshot");
        gun.wear -= 1;
        await Frames(3);
        Check("Your gun firing is heard", sounds.Count("gunshot") > shots0);
        you.weapon = weapon;
        state.items.Remove(gun);

        // An alarm ringing beside you, then stopped.
        if (state.timeline is { } tl)
        {
            var alarm = new TimelineAlarmsItem { x = you.pos.x + 2, y = you.pos.y, untilTick = state.tick + 1000 };
            tl.alarms.Add(alarm);
            await Frames(3);
            bool ringing = sounds.loops.Heard.Any((k) => k.StartsWith("alarm"));
            tl.alarms.Remove(alarm);
            await Frames(3);
            Check("An alarm rings while it's on, and stops", ringing && !sounds.loops.Heard.Any((k) => k.StartsWith("alarm")));
        }
        else Check("An alarm rings while it's on (no timeline in this world)", false);

        // Rain: the weather turns and the rain comes in; back as it was.
        var weather = state.weather;
        var (_, dryRain, _) = Ambience.Targets("clear", false, false, false);
        state.weather = "rain";
        await Frames(3);
        float rain = sounds.ambience.Target("rain");
        var (_, roofRain, _) = Ambience.Targets("rain", false, false, true);
        var (stormWind, _, _) = Ambience.Targets("storm", false, false, false);
        var (calmWind, _, insects) = Ambience.Targets("clear", true, false, false);
        var (_, _, winterInsects) = Ambience.Targets("clear", true, true, false);
        state.weather = weather;
        Check("Rain falls when it rains (softer under a roof); wind and night follow the weather and the hour",
            rain > -60 && dryRain <= -60 && roofRain < rain && stormWind > calmWind && insects > -60 && winterInsects <= -60,
            $"rain {rain} dB, under a roof {roofRain}; wind {calmWind} -> {stormWind} in a storm; insects {insects}, in winter {winterInsects}");

        // Your steps and your swing: walk a second, then swing at the air.
        view.paused = false;
        int steps0 = sounds.yours.Steps, swings0 = sounds.yours.Swings;
        var at0 = you.at;
        SendKey(Godot.Key.W, true);
        await Frames(60);
        SendKey(Godot.Key.W, false);
        await Frames(3);
        you = ControlMod.controlled(state)!;
        double moved = Math.Sqrt(Math.Pow(you.at.x - at0.x, 2) + Math.Pow(you.at.y - at0.y, 2)) / Motion.SUB;
        Check("Walking, your steps are heard", sounds.yours.Steps > steps0, $"{sounds.yours.Steps - steps0} steps on {sounds.yours.LastSurface} over {moved:0.0} m");
        state.control!.nextSwing = 0;
        await Click(GetViewport().GetVisibleRect().Size / 2);
        await Frames(4);
        Check("A swing is heard", sounds.yours.Swings > swings0, $"swungTick {state.control.swungTick}");
        view.paused = true;
    }
}
