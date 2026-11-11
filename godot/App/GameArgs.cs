// The command line (Game is partial: App/Game.cs lists the parts). Everything after `--` on Godot's command line,
// as --name or --name=value, to skip the menu and to set up a run for a picture, a text trace or the self-test.
//
// The flags that set a scene by hand (a clock, weather, a fire, a gun in your hand) are not commands, so they are
// not in the log and a bug report doesn't replay them. The game doesn't write state for them either: each is one
// call to Sim.Staging (cs/Sim/Debug/Staging.cs).
//
// Which world:
//   --seed=N --size=N --start=solo|group   a new world (a solo start, 128 tiles, a random seed by default)
//   --load=path.json                       a save file instead; --arena=one|crowd a debug arena (the dead round you)
//   --ticks=N                              run N ticks before showing it; --play then takes control of the first survivor
// What comes out:
//   --shot=path.png --frames=N             save a screenshot after N frames and quit (GameShots.cs)
//   --shot-at=5,10 --shot-scale=F|--shot-width=W   one picture per listed tick (path-5.png, path-10.png), smaller (a fraction, or W pixels wide)
//   --trace[=K] --trace-near=R --textmap=R --textmap-at=T,..   the same run as text (GameText.cs); works under --headless
//   --selftest                             the game driven by input events and checked (SelfTest/)
// The view:
//   --zoom=Z --paused --debug --map --colony --overview --build=category --menu[=page]   the view to start with
//   --look=DEG --pitch=DEG                 first person: which way you look (0 east, 90 south; pitch up)
//   --eye=X,Y,H                            first person: look from tile point (X, Y), H m over the ground, not your eyes (a debug view)
//   --flat                                 first person: draw the ground flat, as before the heightmap (to compare)
//   --view=M --detail=N                    the view distance (m, 0 off) and the detail radius (chunks) for this run, over the
//                                          Options page's (not saved); a --shot or --selftest ignores the player's options
//   --lights=N --light-shadows=K           the renderer's light budget and its shadowed share (World3D/Lights.cs); --lights=-1 none at all
//   --lightbench[=0,8,16,32]               frame times with each budget, printed, then quit
//   --hold=w,d,shift,space                 keys held down from the start (for pictures of walking and jumping)
// The scene (Sim.Staging; in this order):
//   --day=N --weather=W --snow=CM          start on day N at 06:00 (17: second day of winter; 17.33: 14:00; grown as of then), with that day's
//                                          weather (clear rain storm heatwave snow blizzard) and CM cm of snow on open ground
//   --at=X,Y                               put the survivor you control on tile X, Y (after --ticks)
//   --hour=H                               the clock jumped to the next H o'clock (no ticks run: a night without a day before it)
//   --mains=off                            the mains gone (no porches, street lamps or mains-lit rooms)
//   --switch=X,Y[;X,Y]                     flip the light switch of the room at each tile
//   --give=flashlight[:on]                 a flashlight in your bag (switched on)
//   --car=X,Y,H[,truck]                    a car (or a pickup) at tile X, Y, heading H (0-31, 0 east, 8 south), headlights on, a full tank
//   --drive[=FUEL,HEALTH,KMH]              you behind the wheel of the nearest car (after --car), its tank and its health at those
//                                          percentages and going that fast, for pictures of the dashboard
//   --oncar                                you standing on the middle of the nearest car (after --car), for pictures from a roof
//   --fire=X,Y --stove=X,Y --heater=X,Y    a fire burning there; a stove someone is cooking at; a lit heater (and a blizzard)
//   --clock=TICK --burn=dx,dy,dx,dy..      the clock set; tiles alight at those offsets from you (a line of grass fire: --burn=4,-3,4,-2,4,-1,4,0)
//   --lineup[=variants|weapons|furniture]  one of every item kind on the ground in front of you (or every model of the kinds with several,
//                                          the weapons, or every furniture kind and look)
//   --stage=farm,sites,visitors,heli       east of you: a farm at every stage; building sites; a wanderer, a trader, a prisoner, a caravan
//                                          and a scavenging party; the helicopter overhead (pictures of what only the Colony tab used to draw)
//   --arm=KIND[:ROUNDS]                    that weapon in your hand (a gun with ROUNDS in it, else 60% of its magazine, and a box of its
//                                          ammunition in your bag), for pictures of the HUD's readout
//   --animals=N | --animals=wolf:3,deer    animals spawned round you (spawnAnimal commands): N of each kind in turn, or kinds with counts
//                                          (deer, rabbit, wolf, bear, dog); --animal-range=M how far out the nearest stand (3.5 m)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Godot;
using LoonieRoo.Game.Render;
using LoonieRoo.Game.Ui;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Game;

public partial class Game
{
    static Dictionary<string, string> ParseArgs()
    {
        var args = new Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            args[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        return args;
    }

    static int[] Ints(string s, char sep = ',') => Array.ConvertAll(s.Split(sep, StringSplitOptions.RemoveEmptyEntries), (v) => int.Parse(v, CultureInfo.InvariantCulture));
    static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>The start the command line asks for, in the order the flags are documented above. The order matters:
    /// the ticks run before you are placed, and the scene is set after them.</summary>
    void StartFromArgs(Dictionary<string, string> args)
    {
        bool direct = WorldFromArgs(args);
        SkyArgs(args);
        if (args.TryGetValue("zoom", out var zoom)) view.camera.zoom = double.Parse(zoom);
        ParseTextOptions(args);
        ParseShotOptions(args);
        if (args.TryGetValue("look", out var lk)) lookArg = double.Parse(lk) * Math.PI / 180;
        if (args.TryGetValue("hold", out var hk)) holdKeys = hk.ToLowerInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries);
        // With --trace or --textmap the ticks run tick by tick (GameText.cs), so they come after the placing and spawning below:
        // the run then starts from the position and the animals you asked for.
        int ticksArg = args.TryGetValue("ticks", out var t) ? int.Parse(t) : 0;
        if (!TextRun && ticksArg > 0) Tick.step(state, ticksArg);
        SceneArgs(args);
        // --play: a group start, playing its first survivor (to look at the others in first person).
        if (args.ContainsKey("play") && state.control == null && state.survivors.Count > 0) Command(new Command { type = "takeControl", survivorId = state.survivors[0].id });
        if (args.TryGetValue("animals", out var an)) SpawnAnimals(an, args.TryGetValue("animal-range", out var ar) ? Num(ar) : 3.5);
        if (TextRun || shotAt != null) InitYaw();
        if (TextRun)
        {
            TextRunTicks(ticksArg);
            // Text only: nothing to draw, so don't open the game's frames at all.
            if (shotPath == null && !selftest) { TextFinish(); GetTree().Quit(); return; }
        }
        ViewArgs(args);
        if (direct)
        {
            view.inGame = true;
            view.paused = args.ContainsKey("paused");
            if (state.control != null && state.settings.start == "solo") view.Toast(SOLO_TOAST);
            if (selftest) _ = SelfTest();
        }
        else
        {
            // On starting: the menu, over a world to look at.
            view.inGame = false;
            view.menu = "main";  // the menu holds the world still (view.halted)
        }
    }

    /// <summary>Which world: an arena, a save file, or a new one from --seed, --size and --start. Returns whether the
    /// command line chose (straight into the game) or nothing did (the menu, over a world to look at).</summary>
    bool WorldFromArgs(Dictionary<string, string> args)
    {
        selftest = args.ContainsKey("selftest");
        if (selftest)
        {
            Saves.DIR = "user://selftest-saves";
            if (!args.ContainsKey("seed")) args["seed"] = "42";
        }
        bool direct = args.ContainsKey("seed") || args.ContainsKey("start") || args.ContainsKey("size") || args.ContainsKey("load") || shotPath != null;
        double seed = args.TryGetValue("seed", out var s) ? double.Parse(s) : RandomSeed();
        double size = args.TryGetValue("size", out var z) ? double.Parse(z) : Worldgen.WORLD_SIZE;
        var settings = CopySettings(Difficulty.NORMAL);
        settings.start = args.GetValueOrDefault("start", "solo");
        if (args.TryGetValue("arena", out var arena)) { Replace(new LoadResult { ok = true, state = LoonieRoo.Sim.Arena.createArena(seed, arena == "" ? "crowd" : arena), kind = "save" }, "a debug arena"); direct = true; }
        else if (args.TryGetValue("load", out var load)) Replace(Savefile.loadFile(System.IO.File.ReadAllText(load)), "the file");
        else Replace(new LoadResult { ok = true, state = Worldgen.createWorld(seed, size, settings), kind = "save" }, $"a new {size}×{size} world");
        return direct;
    }

    /// <summary>--day, --weather, --snow: the clock and the sky, before any tick runs.</summary>
    void SkyArgs(Dictionary<string, string> args)
    {
        if (args.TryGetValue("day", out var day)) Staging.day(state, Num(day));
        if (args.TryGetValue("weather", out var weather)) Staging.weather(state, weather);
        if (args.TryGetValue("snow", out var snow)) Staging.snow(state, Num(snow));
    }

    /// <summary>The scene set by hand after the ticks have run: where you stand, the lights, fires, what lies about
    /// and what you hold. Each flag is one call to Sim.Staging.</summary>
    void SceneArgs(Dictionary<string, string> args)
    {
        if (args.TryGetValue("at", out var at) && ControlMod.controlled(state) is { } me)
        {
            var p = Ints(at);
            Staging.place(me, p[0], p[1]);
        }
        if (args.TryGetValue("hour", out var hour)) Staging.hour(state, int.Parse(hour, CultureInfo.InvariantCulture));
        if (args.TryGetValue("mains", out var mains) && mains == "off") Staging.mainsOff(state);
        var you = ControlMod.controlled(state);
        if (args.TryGetValue("switch", out var sw))
            foreach (var tile in sw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = Ints(tile);
                if (!Staging.flipSwitch(state, p[0], p[1])) GD.Print($"--switch: no room at {tile}");
            }
        if (args.TryGetValue("give", out var give) && you != null && give.StartsWith("flashlight")) Staging.giveFlashlight(state, you, give.EndsWith(":on"));
        if (args.TryGetValue("car", out var car))
        {
            // X,Y[,H[,truck]]
            var f = car.Split(',');
            Staging.car(state, int.Parse(f[0]), int.Parse(f[1]), f.Length > 2 ? int.Parse(f[2]) : 0, f.Length > 3 ? f[3] : "car");
        }
        if (args.TryGetValue("drive", out var drive) && you != null)
        {
            var n = Array.ConvertAll(drive.Split(',', StringSplitOptions.RemoveEmptyEntries), Num);
            if (Staging.seat(state, you, n.Length > 0 ? n[0] : null, n.Length > 1 ? n[1] : null, n.Length > 2 ? n[2] : null) == null) GD.Print("--drive: no car near you (put one there with --car=X,Y,H)");
        }
        if (args.ContainsKey("oncar") && you != null && Staging.onRoof(state, you) == null) GD.Print("--oncar: no car near you to stand on (put one there with --car=X,Y,H)");
        if (args.TryGetValue("fire", out var fire))
        {
            var p = Ints(fire);
            Staging.fire(state, p[0], p[1]);
        }
        if (args.TryGetValue("stove", out var stove) && you != null)
        {
            var p = Ints(stove);
            Staging.stove(state, you, p[0], p[1]);
        }
        if (args.TryGetValue("heater", out var heater))
        {
            var p = Ints(heater);
            Staging.heater(state, p[0], p[1]);
        }
        if (args.TryGetValue("clock", out var clock)) Staging.clock(state, int.Parse(clock));
        if (args.TryGetValue("burn", out var burn) && you != null) Staging.burn(state, you, Ints(burn));
        if (args.TryGetValue("lineup", out var lineup) && you != null)
        {
            if (lineup == "furniture") Staging.furnitureLineup(state, you);
            else World3D.ItemModels.ShowAs(Staging.itemLineup(state, you, lineup));
        }
        if (args.TryGetValue("stage", out var stage) && you != null)
            foreach (var scene in stage.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (scene == "farm") Staging.farm(state, you);
                else if (scene == "sites") Staging.sites(state, you);
                else if (scene == "visitors") Staging.visitors(state, you);
                else if (scene == "heli") Staging.helicopter(state, you.pos.x + 14, you.pos.y);
                else if (scene == "climb") Staging.climb(state, you);
                else GD.Print($"--stage: no scene called {scene} (farm, sites, visitors, heli, climb)");
            }
        if (args.TryGetValue("arm", out var arm) && you != null)
        {
            var parts = arm.Split(':');
            Staging.arm(state, you, parts[0], parts.Length > 1 ? Num(parts[1]) : null);
        }
    }

    /// <summary>The view to start with, and the renderer's options for this run. Nothing here touches the sim.</summary>
    void ViewArgs(Dictionary<string, string> args)
    {
        if (args.TryGetValue("lights", out var n)) World3D.WorldLights.Budget = int.Parse(n, CultureInfo.InvariantCulture);
        if (args.TryGetValue("light-shadows", out var k)) World3D.WorldLights.Shadowed = int.Parse(k, CultureInfo.InvariantCulture);
        if (args.TryGetValue("lightbench", out var b)) World3D.WorldLights.Bench(b == "" ? "0,8,16,32" : b);
        view.showDebug = args.ContainsKey("debug");
        view.showMap = args.ContainsKey("map");
        view.showOverview = args.ContainsKey("overview");
        view.colonyMode = args.ContainsKey("colony");
        // The graphics options (Ui/Graphics.cs): the player's file, then the command line's for this run. A self-test or a shot sees the defaults.
        Graphics.Transient = selftest || shotPath != null;
        if (args.TryGetValue("view", out var vw)) Graphics.ViewOverride = (int)float.Parse(vw, CultureInfo.InvariantCulture);
        if (args.TryGetValue("detail", out var dt)) Graphics.DetailOverride = int.Parse(dt);
        Graphics.Init(GetViewport());
        if (args.TryGetValue("pitch", out var pt)) pitchArg = double.Parse(pt) * Math.PI / 180;
        if (args.TryGetValue("eye", out var ey))
        {
            var e = ey.Split(',');
            firstPerson.EyeAt = new Vector3((float)Num(e[0]), (float)Num(e[2]), (float)Num(e[1]));
        }
        if (args.ContainsKey("flat")) World3D.Terrain.ForceFlat = true;
        if (args.TryGetValue("build", out var cat)) Tools.chooseCategory(view, cat == "" ? "structure" : cat);
        if (args.TryGetValue("menu", out var m)) view.menu = m == "" ? "main" : m;
    }

    /// <summary>--animals: "N" (that many, the kinds in turn) or "wolf:3,deer" round you, from `near` m out, on open ground.</summary>
    void SpawnAnimals(string spec, double near)
    {
        string[] kinds = { "deer", "rabbit", "wolf", "bear", "dog" };
        var list = new List<string>();
        if (int.TryParse(spec, out int n)) for (int i = 0; i < n; i++) list.Add(kinds[i % kinds.Length]);
        else
            foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(':');
                for (int i = 0, c = kv.Length > 1 ? int.Parse(kv[1]) : 1; i < c; i++) list.Add(kv[0]);
            }
        var you = state.control != null ? state.survivors.Find((s) => s.id == state.control.survivorId) : null;
        var at = you?.pos ?? (state.survivors.Count > 0 ? state.survivors[0].pos : new Point { x = state.grid.width / 2, y = state.grid.height / 2 });
        for (int i = 0; i < list.Count; i++)
        {
            // Spread round a circle, every other one further out; the nearest open tile if that's not.
            double a = i * 2.39996, r = near + (i % 3) * 1.6;
            int x0 = at.x + (int)Math.Round(Math.Cos(a) * r), y0 = at.y + (int)Math.Round(Math.Sin(a) * r);
            for (int k = 0; k < 9 && !Command(new Command { type = "spawnAnimal", kind = list[i], x = x0 + k % 3 - 1, y = y0 + k / 3 - 1 }); k++) { }
        }
    }
}
