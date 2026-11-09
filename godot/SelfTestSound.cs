// The self-test's second round of sound steps (2026-11-09; SoundTest in SelfTest.cs calls them): the
// sounds are made off the main thread; a wall, a shut door and an open one between you and a sound;
// your steps from your own feet walking, running and backing off, a zombie's steps, each ground's step,
// and the swing when you click; a gun firing, glass breaking, an alarm
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

        // Your steps: from your own body's footfalls (Footfalls.cs), walking, running and backing off. A step is
        // on a foot that's down: the clip's ankle within 5 cm of its lowest, the drawn one no higher than the other.
        view.paused = false;
        var people = firstPerson.People;
        int steps0 = sounds.yours.Steps, swings0 = sounds.yours.Swings;
        static bool Down(World3D.People.YourStep s) => s.clipH <= 0.05f && s.drawnH <= s.otherH + 0.01f;
        static string Trace(System.Collections.Generic.List<World3D.People.YourStep> l) =>
            string.Join("; ", l.Select((s) => $"{(s.foot == 0 ? "L" : "R")} {s.clip[8..]}@{s.at:0.00} +{s.clipH * 100:0}cm {s.drawnH * 100:0}/{s.otherH * 100:0}"));
        static double Gap(System.Collections.Generic.List<World3D.People.YourStep> l) =>
            l.Count < 2 ? 0 : (l[^1].ms - l[0].ms) / (double)(l.Count - 1);
        async System.Threading.Tasks.Task<(System.Collections.Generic.List<World3D.People.YourStep>, double)> Walk(Godot.Key key, bool run, int frames)
        {
            you = ControlMod.controlled(state)!;
            var from = you.at;
            people.YourSteps.Clear();
            if (run) SendKey(Godot.Key.Shift, true);
            SendKey(key, true);
            await Frames(frames);
            SendKey(key, false);
            if (run) SendKey(Godot.Key.Shift, false);
            await Frames(20);
            you = ControlMod.controlled(state)!;
            return (people.YourSteps.ToList(), Math.Sqrt(Math.Pow(you.at.x - from.x, 2) + Math.Pow(you.at.y - from.y, 2)) / Motion.SUB);
        }
        var (walk, walkM) = await Walk(Godot.Key.W, false, 90);
        Check("Walking, your steps are heard as your feet come down", sounds.yours.Steps > steps0 && walk.Count >= 2 && walk.All(Down),
            $"{walk.Count} steps on {sounds.yours.LastSurface} over {walkM:0.0} m, every {Gap(walk):0} ms: {Trace(walk)}");
        var (run, runM) = await Walk(Godot.Key.W, true, 90);
        Check("Running, your steps come quicker, still on your feet", run.Count >= 2 && run.All(Down) && Gap(run) < Gap(walk) && sounds.yours.LastGait is "jog" or "sprint",
            $"{run.Count} steps over {runM:0.0} m, every {Gap(run):0} ms ({sounds.yours.LastGait}): {Trace(run)}");
        var (back, backM) = await Walk(Godot.Key.S, false, 60);
        Check("Backing off, your steps come from the clip played backwards", backM < 0.8 || (back.Count >= 1 && back.All(Down)),
            $"{back.Count} steps over {backM:0.0} m: {Trace(back)}");

        // Others' steps: a zombie coming at you from six tiles off is heard walking, from its feet.
        var zdir = state.control!.aim ?? state.control.facing;
        var zspot = Space.nearestTile(state.grid, new Point { x = you.pos.x + Math.Sign(zdir.x) * 6, y = you.pos.y + Math.Sign(zdir.y) * 6 }, (x, y) => Grids.getTile(state.grid, x, y) == Grids.FLOOR && Math.Abs(x - you.pos.x) + Math.Abs(y - you.pos.y) >= 4);
        if (zspot == null) Check("Floor for a zombie to walk at you", false);
        else
        {
            var walker = Zombies.addZombie(state, zspot.x, zspot.y, 100, "static");
            walker.targetId = you.id;
            int heard0 = sounds.Count("step"), its = 0;
            for (int i = 0; i < 120 && its < 2; i++)
            {
                await Frames(1);
                its += people.Steps.Count((f) => f.bodyId == walker.id);
            }
            Check("A zombie walking at you is heard stepping", its >= 1 && sounds.Count("step") > heard0, $"its footfalls {its}, others' steps heard {sounds.Count("step") - heard0}");
            Zombies.damageZombie(state, walker, 1000, you);
            await Frames(2);
        }

        // Each ground its own steps: the surface under a foot, on a little grid of its own (a row of every ground,
        // and water with a bank and a bridge beside it), and every surface's sound different from the others.
        var sg = Grids.createGrid(Grids.GROUNDS.Length, 3);
        for (int i = 0; i < Grids.GROUNDS.Length; i++) sg.ground[i] = i;
        sg.tiles[2 * sg.width + 5] = Grids.WATER;
        sg.ground[2 * sg.width + 6] = Array.IndexOf(Grids.GROUNDS, "bridge");
        var under = Enumerable.Range(0, Grids.GROUNDS.Length).Select((i) => Yours.Surface(sg, i + 0.5, 0.5)).ToList();
        string inWater = Yours.Surface(sg, 5.5, 2.5), bank = Yours.Surface(sg, 4.85, 2.5), dry = Yours.Surface(sg, 4.4, 2.5), bridge = Yours.Surface(sg, 6.1, 2.5);
        var feats = SynthSteps.SURFACES.Select((g) =>
        {
            var w = (AudioStreamWav)sounds.Kind("step_" + g)[0];
            var d = w.Data;
            int zc = 0;
            for (int j = 2; j + 1 < d.Length; j += 2) if (((short)(d[j] | (d[j + 1] << 8)) >= 0) != ((short)(d[j - 2] | (d[j - 1] << 8)) >= 0)) zc++;
            return (g, data: d, len: d.Length / 2 / (double)Synth.RATE, zcr: zc / (d.Length / 2 / (double)Synth.RATE));
        }).ToList();
        bool distinct = feats.All((a) => feats.All((b) => ReferenceEquals(a.data, b.data) || a.g == b.g || !a.data.SequenceEqual(b.data)));
        Check("Each ground has its own step, and shallow water too",
            under.SequenceEqual(Grids.GROUNDS.Select((g) => "step_" + g)) && inWater == "step_water" && bank == "step_water" && dry == "step_grass" && bridge == "step_bridge" && distinct,
            $"water {inWater}, bank {bank}, a step back {dry}, bridge {bridge}; " + string.Join(", ", feats.Select((f) => $"{f.g} {f.len:0.00}s {f.zcr / 1000:0.0}k zc/s")));

        state.control!.nextSwing = 0;
        await Click(GetViewport().GetVisibleRect().Size / 2);
        await Frames(4);
        Check("A swing is heard", sounds.yours.Swings > swings0, $"swungTick {state.control.swungTick}");
        view.paused = true;
    }
}
