// Temperature, snow and tracks (Weather.cs, Snow.cs).
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class SnowTest
{
    static double dayStart(int day) => (double)(day - 1) * Time.DAY_LENGTH;

    /// <summary>An open field, you at (2, 3); a mild hand-made map unless `world` is set.</summary>
    static SimState field(int width = 40, int rows = 7, bool world = false)
    {
        Time.setDayScale(1);
        var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = Js.ArrayFrom(rows, (int y) => (y == 3 ? "..s" + ".".repeat(width - 3) : ".".repeat(width))) });
        Js.NN(Js.Get(state.survivors, 0)).priorities = new(JobTypes.NO_JOBS) { };
        if (world)
        {
            state.world = new WorldInfo { seed = 1, size = 512 };
        }
        return state;
    }

    /// <summary>The first tick of the day at this hour (06:00 is the dawn roll's tick, so +1).</summary>
    static int at(int day, double hour) => (int)(dayStart(day) + (((hour - Time.START_HOUR + 24) % 24) * Time.TICKS_PER_HOUR)) + 1;

    static void snowOf(SimState state, double cold, double mild, double hot)
    {
        state.snow = new SnowState { depth = [cold, mild, hot], fallen = [0, 0, 0], melted = [0, 0, 0] };
    }

    /// <summary>Only the snow's tick, for `hours` in-game hours (the rest of the sim stays out of it).</summary>
    static void drive(SimState state, double hours, Action? each = null)
    {
        var n = (int)(hours * Time.TICKS_PER_HOUR);
        for (var i = 0; i < n; i++)
        {
            Snow.tick(state);
            state.tick++;
            each?.Invoke();
        }
    }

    static double depth(SimState state, int region) => state.snow?.depth[region] ?? 0;

    static IEnumerable<string> weathersIn(string season) => season switch
    {
        "winter" => ["clear", "snow", "blizzard"],
        "summer" => ["clear", "rain", "storm", "heatwave"],
        _ => ["clear", "rain", "storm"],
    };

    // ---- temperature ----

    [Fact(DisplayName = "temperature > below freezing exactly when the sim calls the air cold: every region, season, weather, nuclear winter, every quarter hour")]
    public void agrees_with_the_cold_rule()
    {
        Time.setDayScale(1);
        var bad = 0;
        var first = "";
        for (var tick = 0; tick < 21 * Time.DAY_LENGTH; tick += Time.TICKS_PER_HOUR / 4)
        {
            var season = Seasons.seasonOf(tick);
            var night = Time.isNight(tick);
            foreach (var climate in Weather.CLIMATES)
                foreach (var weather in weathersIn(season))
                    foreach (var nuclear in new[] { false, true })
                    {
                        var want = Climate.coldFor(climate, season, night, nuclear) || (weather == "blizzard");
                        var t = Weather.temperatureOf(climate, season, tick, weather, nuclear);
                        if (Weather.isFreezing(t) != want && ++bad == 1)
                        {
                            first = $"tick {tick} ({Time.clockText(tick)}, {season}) {climate} {weather} nuclear {nuclear}: {t:0.00} but cold is {want}";
                        }
                    }
        }
        expect(bad, first).toBe(0);
    }

    [Fact(DisplayName = "temperature > scorching exactly when 30 degrees or more, on a day without a heatwave")]
    public void agrees_with_the_hot_rule()
    {
        Time.setDayScale(1);
        var bad = 0;
        var first = "";
        for (var tick = 0; tick < 21 * Time.DAY_LENGTH; tick += Time.TICKS_PER_HOUR / 4)
        {
            var season = Seasons.seasonOf(tick);
            foreach (var climate in Weather.CLIMATES)
                foreach (var weather in weathersIn(season).Where(w => w != "heatwave"))
                {
                    var want = Climate.scorchingFor(climate, season, Time.isNight(tick));
                    var t = Weather.temperatureOf(climate, season, tick, weather, false);
                    if ((t >= Weather.SCORCHING) != want && ++bad == 1)
                    {
                        first = $"tick {tick} ({Time.clockText(tick)}, {season}) {climate} {weather}: {t:0.00} but scorching is {want}";
                    }
                }
        }
        expect(bad, first).toBe(0);
    }

    [Fact(DisplayName = "temperature > the state's own rules: Seasons.isCold's region test and Climate.scorching, read at a tile, agree with it")]
    public void agrees_through_the_state()
    {
        var state = field(world: true);
        var tick = at(17, 23);
        state.tick = tick;
        state.weather = "clear";
        var cold = Climate.regionCold(state, 5, 5);
        expect(Weather.isFreezing(Weather.temperature(state, 5, 5))).toBe(cold);
        state.tick = at(17, 13);
        expect(Weather.isFreezing(Weather.temperature(state, 5, 5))).toBe(Climate.regionCold(state, 5, 5));
        state.tick = at(7, 13);
        for (var y = 0; y < 500; y += 25)
        {
            expect(Weather.temperature(state, 5, y) >= Weather.SCORCHING, $"y={y}").toBe(Climate.scorching(state, 5, y));
        }
    }

    [Fact(DisplayName = "temperature > a blizzard is cold everywhere, a heatwave 6 degrees warmer, cloud narrows the day's range and does not move its dusk and dawn")]
    public void events_and_cloud()
    {
        Time.setDayScale(1);
        var hot = at(17, 14.5);
        expect(Weather.temperatureOf("hot", "winter", hot, "blizzard", false)).toBeLessThan(0);
        var summer = at(7, 14.5);
        expect(Weather.temperatureOf("mild", "summer", summer, "heatwave", false)).toBeCloseTo(Weather.temperatureOf("mild", "summer", summer, "clear", false) + Weather.HEATWAVE, 1);
        var range = (string w) => Enumerable.Range(0, 48).Select(i => Weather.temperatureOf("mild", "summer", at(7, 6) + (i * Time.TICKS_PER_HOUR / 2) - 1, w, false)).ToList();
        var clear = range("clear");
        var rain = range("rain");
        expect(rain.Max() - rain.Min()).toBeLessThan((clear.Max() - clear.Min()) * 0.8);
        // Dusk is the same either way.
        var dusk = at(7, 21) - 1;
        expect(Weather.temperatureOf("mild", "summer", dusk, "rain", false)).toBeCloseTo(Weather.temperatureOf("mild", "summer", dusk, "clear", false), 6);
    }

    [Fact(DisplayName = "temperature > indoors is never colder than 8; the table")]
    public void indoors_and_table()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = ["..........", ".#######..", ".#.....#..", ".#..s...#.", ".#######..", ".........."] });
        state.tick = at(17, 3);
        state.weather = "clear";
        var inside = Js.NN(Js.Get(state.survivors, 0)).pos;
        if (Rooms.isIndoors(state, inside.x, inside.y))
        {
            expect(Weather.temperatureAt(state, inside.x, inside.y)).toBeGreaterThanOrEqual(Weather.INDOORS_MIN);
        }
        expect(Weather.temperatureAt(state, 0, 0)).toBe(Weather.temperature(state, 0, 0));
        expect(Weather.table([0, 3, 6, 9, 12, 15, 18, 21]).Split((char)10).Length).toBe(14); // a header, twelve rows, the end
    }

    // ---- depth ----

    [Fact(DisplayName = "snow > nothing falls without snow weather, and a snowless stretch leaves the state as it was (no snow field, no RNG drawn)")]
    public void snowless_stretch()
    {
        var state = field();
        state.tick = at(11, 8);
        state.weather = "rain";
        var json = StateJson.Write(state);
        var rng = state.rng;
        drive(state, 12);
        expect(state.snow == null).toBe(true);
        expect(state.rng).toBe(rng);
        state.tick = at(11, 8);
        expect(StateJson.Write(state)).toBe(json);
        expect(json.Contains("\"snow\"")).toBe(false);
    }

    [Fact(DisplayName = "snow > depth rises in snow, faster in a blizzard, by region: the cold north most, the hot south none, a blizzard reaches even the south; capped at MAX")]
    public void rises()
    {
        var state = field(world: true);
        state.tick = at(17, 6);
        state.weather = "snow";
        drive(state, 24);
        var cold = depth(state, 0);
        var mild = depth(state, 1);
        expect(cold).toBeGreaterThan(15);
        expect(mild).toBeGreaterThan(0);
        expect(mild).toBeLessThan(cold);
        expect(depth(state, 2)).toBe(0);
        // A blizzard drops more, even where it is hot.
        var storm = field(world: true);
        storm.tick = at(17, 6);
        storm.weather = "blizzard";
        drive(storm, 24);
        expect(depth(storm, 0)).toBeGreaterThan(cold * 1.5);
        expect(depth(storm, 2)).toBeGreaterThan(0);
        // And no more than MAX.
        drive(storm, 24 * 2);
        expect(depth(storm, 0)).toBeLessThanOrEqual(Snow.MAX_DEPTH);
        expect(depth(storm, 0)).toBeGreaterThan(Snow.MAX_DEPTH * 0.9);
    }

    [Fact(DisplayName = "snow > it melts above freezing, faster in rain, never below zero, and a game with none left drops its snow state")]
    public void melts()
    {
        double after(string weather, int day)
        {
            var state = field();
            state.tick = at(day, 9);
            state.weather = weather;
            snowOf(state, 0, 30, 0);
            drive(state, 5);
            return depth(state, 1);
        }
        var dry = after("clear", 17);
        expect(dry).toBeLessThan(30);
        expect(after("clear", 2)).toBeLessThan(dry); // a warmer day melts it quicker
        expect(after("rain", 2)).toBeLessThan(after("clear", 2)); // and rain on it quicker still
        // Frozen: no melt.
        var frozen = field();
        frozen.tick = at(17, 23);
        frozen.weather = "clear";
        snowOf(frozen, 0, 30, 0);
        drive(frozen, 3);
        expect(depth(frozen, 1)).toBe(30);
        // Gone, and never below zero on the way.
        var thaw = field();
        thaw.tick = at(2, 7);
        thaw.weather = "rain";
        snowOf(thaw, 0, 12, 0);
        var low = 99.0;
        drive(thaw, 30, () => low = Math.Min(low, depth(thaw, 1)));
        expect(low).toBeGreaterThanOrEqual(0);
        expect(thaw.snow == null).toBe(true);
    }

    [Fact(DisplayName = "snow > the same at any day length: a day at scale 2 gives the depth of a day at scale 1")]
    public void day_length()
    {
        var a = field(world: true);
        a.tick = at(17, 6);
        a.weather = "snow";
        drive(a, 24);
        var b = field(world: true);
        b.settings.dayScale = 2;
        Time.setDayScale(2);
        b.tick = at(17, 6);
        b.weather = "snow";
        drive(b, 24);
        Time.setDayScale(1);
        expect(depth(b, 0)).toBeCloseTo(depth(a, 0), 1);
    }

    [Fact(DisplayName = "snow > none indoors, half under a tree")]
    public void roofs_and_trees()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = ["...........", ".#######...", ".#.....#.Y.", ".#..s..#...", ".#######...", "..........."] });
        snowOf(state, 0, 20, 0);
        var inside = Js.NN(Js.Get(state.survivors, 0)).pos;
        expect(Rooms.isIndoors(state, inside.x, inside.y)).toBe(true);
        expect(Snow.depthAt(state, inside.x, inside.y)).toBe(0);
        expect(Snow.depthAt(state, 0, 0)).toBe(20);
        expect(Trunks.treeAt(state, 9, 2)).toBe(true);
        expect(Snow.depthAt(state, 9, 2)).toBe(10);
    }

    // ---- tracks ----

    [Fact(DisplayName = "tracks > a walked line is lower than the snow beside it, fills after new snowfall, and goes when the snow melts")]
    public void line_fills_and_melts()
    {
        var state = field();
        state.tick = at(17, 20);
        state.weather = "clear";
        snowOf(state, 0, 20, 0);
        for (var x = 5; x < 15; x++)
        {
            Snow.tread(state, x, 3, 0);
        }
        for (var x = 5; x < 15; x++)
        {
            expect(Snow.depthAt(state, x, 3)).toBeLessThan(Snow.depthAt(state, x, 2) * 0.8);
        }
        expect(Snow.depthAt(state, 4, 3)).toBe(20);
        // A cold night's blizzard: the beside deepens, the line fills in faster and, within a few hours, is no longer a line.
        state.weather = "blizzard";
        var before = Snow.depthAt(state, 8, 2);
        drive(state, 1);
        var gap1 = Snow.depthAt(state, 8, 2) - Snow.depthAt(state, 8, 3);
        drive(state, 5);
        var gap2 = Snow.depthAt(state, 8, 2) - Snow.depthAt(state, 8, 3);
        expect(Snow.depthAt(state, 8, 2)).toBeGreaterThan(before);
        expect(gap2).toBeLessThan(gap1);
        drive(state, 4);
        expect(Snow.depthAt(state, 8, 3)).toBe(Snow.depthAt(state, 8, 2));
        expect(state.snow.tracks.Count).toBe(0);
        // A new line, then a thaw: bare before the snow beside is, and nothing at all once it is gone.
        for (var x = 5; x < 15; x++)
        {
            Snow.tread(state, x, 3, 0);
        }
        state.weather = "clear";
        state.tick = at(18, 8);
        var low = Snow.depthAt(state, 8, 3);
        drive(state, 3);
        expect(Snow.depthAt(state, 8, 3)).toBeLessThan(low);
        drive(state, 40);
        expect(state.snow == null).toBe(true);
        expect(Snow.depthAt(state, 8, 3)).toBe(0);
    }

    [Fact(DisplayName = "tracks > treading again goes deeper, to a floor; a lane (6 treads) refills slower than a lone footprint")]
    public void lanes()
    {
        var state = field();
        snowOf(state, 0, 40, 0);
        var seen = new List<double>();
        for (var i = 0; i < 14; i++)
        {
            Snow.tread(state, 8, 3, 0);
            seen.Add(Snow.depthAt(state, 8, 3));
        }
        expect(seen[0]).toBeLessThan(40);
        for (var i = 1; i < seen.Count; i++)
        {
            expect(seen[i]).toBeLessThanOrEqual(seen[i - 1]);
        }
        expect(seen[^1]).toBe(Snow.FLOOR);
        Snow.tread(state, 12, 3, 0);
        var lane = Snow.find(state.snow, 8, 3);
        var lone = Snow.find(state.snow, 12, 3);
        state.snow.fallen[1] += 5;
        expect(Snow.trackDepth(state.snow, lane) - lane.d).toBeLessThan(Snow.trackDepth(state.snow, lone) - lone.d);
    }

    [Fact(DisplayName = "tracks > a car cuts a rut deeper than a foot, marked with its heading")]
    public void ruts()
    {
        var state = field();
        snowOf(state, 0, 20, 0);
        var car = Vehicles.addVehicle(state, 10, 3, 8);
        car.was = Motion.centre(new Point { x = 10, y = 3 });
        car.at = Motion.centre(new Point { x = 11, y = 3 });
        car.pos = new Point { x = 11, y = 3 };
        Snow.tick(state);
        var rut = Snow.find(state.snow, 11, 3);
        expect(rut != null).toBe(true);
        expect(rut.k).toBe(9);
        Snow.tread(state, 14, 3, 0);
        expect(Snow.depthAt(state, 11, 3)).toBeLessThan(Snow.depthAt(state, 14, 3));
    }

    [Fact(DisplayName = "tracks > anyone who steps into a tile leaves one: you, the dead; the far dead and cars' riders do not")]
    public void who_leaves_tracks()
    {
        var state = field(width: 200);
        snowOf(state, 0, 20, 0);
        var you = Js.NN(Js.Get(state.survivors, 0));
        var near = Zombies.addZombie(state, 20, 3, Zombies.ZOMBIE_HEALTH, "static");
        var far = Zombies.addZombie(state, 150, 3, Zombies.ZOMBIE_HEALTH, "static");
        foreach (Body e in new Body[] { you, near, far })
        {
            e.was = Js.Clone(e.at);
            e.at = new Point { x = e.at.x + Motion.SUB, y = e.at.y };
        }
        Snow.tick(state);
        expect(Snow.find(state.snow, 3, 3) != null).toBe(true);
        expect(Snow.find(state.snow, 21, 3) != null).toBe(true);
        expect(Snow.find(state.snow, 151, 3) == null).toBe(true);
        // A teleport is not a step.
        you.was = Js.Clone(you.at);
        you.at = new Point { x = you.at.x + (10 * Motion.SUB), y = you.at.y };
        Snow.tick(state);
        expect(Snow.find(state.snow, 14, 3) == null).toBe(true);
    }

    [Fact(DisplayName = "tracks > the record is capped, the oldest go first, the same on every run, however many movers walk")]
    public void cap()
    {
        string run()
        {
            var state = field(width: 100, rows: 100);
            snowOf(state, 0, 60, 0);
            var you = Js.NN(Js.Get(state.survivors, 0));
            Motion.place(you, new Point { x = 50, y = 50 });
            var movers = new List<Zombie>();
            for (var i = 0; i < 400; i++)
            {
                movers.Add(Zombies.addZombie(state, 10 + ((i % 5) * 16), 10 + (i / 5), Zombies.ZOMBIE_HEALTH, "static"));
            }
            var most = 0;
            for (var step = 0; step < 24; step++)
            {
                foreach (var z in movers)
                {
                    z.was = Js.Clone(z.at);
                    z.at = new Point { x = z.at.x + (Motion.SUB / 2) + 1, y = z.at.y };
                    z.pos = Motion.tileOf(z.at);
                }
                state.tick++;
                Snow.tick(state);
                most = Math.Max(most, state.snow.tracks.Count);
            }
            expect(most).toBeLessThanOrEqual(Snow.TRACK_CAP);
            expect(state.snow.tracks.Count).toBeGreaterThan(Snow.TRACK_CAP - Snow.TRIM - 1);
            // The oldest went: nothing from the first steps is left, the last steps are.
            expect(state.snow.tracks.Min(t => t.t)).toBeGreaterThan(3);
            expect(state.snow.tracks.Max(t => t.t)).toBeGreaterThanOrEqual(state.tick - 1);
            return StateJson.Write(state);
        }
        expect(run()).toBe(run());
    }

    [Fact(DisplayName = "tracks > no per-tick cost on a clear day: movers are not looked at when no snow lies")]
    public void nothing_without_snow()
    {
        var state = field();
        snowOf(state, 0, 0, 0);
        var you = Js.NN(Js.Get(state.survivors, 0));
        you.was = Js.Clone(you.at);
        you.at = new Point { x = you.at.x + Motion.SUB, y = you.at.y };
        Snow.tick(state);
        expect(state.snow?.tracks.Count ?? 0).toBe(0);
    }

    // ---- walking ----

    [Fact(DisplayName = "walking > deep untrodden snow slows you and the colony a little, a trodden tile does not, and with the effect off nothing changes")]
    public void slows()
    {
        var state = field();
        var s = Js.NN(Js.Get(state.survivors, 0));
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        var c = state.control;
        var dry = ControlMod.stepLength(state, s, c, false);
        var tile = new Point { x = 7, y = 3 };
        var drySteps = Survivors.stepPace(state, s, tile);
        snowOf(state, 0, 30, 0);
        var deep = ControlMod.stepLength(state, s, c, false);
        expect(deep).toBeLessThan(dry);
        expect(deep).toBeGreaterThan(dry * 0.6);
        expect(Survivors.stepPace(state, s, tile)).toBeGreaterThan(drySteps);
        // Trodden: not slowed.
        Snow.tread(state, tile.x, tile.y, 0);
        expect(Survivors.stepPace(state, s, tile)).toBe(drySteps);
        Snow.tread(state, s.pos.x, s.pos.y, 0);
        expect(ControlMod.stepLength(state, s, c, false)).toBe(dry);
        // Shallow snow does not slow.
        snowOf(state, 0, 4, 0);
        expect(ControlMod.stepLength(state, s, c, false)).toBe(dry);
        // Off.
        snowOf(state, 0, 30, 0);
        Snow.slowsWalking = false;
        try
        {
            expect(ControlMod.stepLength(state, s, c, false)).toBe(dry);
            expect(Survivors.stepPace(state, s, tile)).toBe(drySteps);
        }
        finally
        {
            Snow.slowsWalking = true;
        }
    }

    // ---- saves ----

    [Fact(DisplayName = "saves > a save mid-winter loads to the same depth and tracks, and plays on the same; a save from before the snow has none")]
    public void saves()
    {
        var state = field();
        state.tick = at(17, 8);
        state.weather = "snow";
        var s = Js.NN(Js.Get(state.survivors, 0));
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
        snowOf(state, 0, 25, 0);
        Tick.step(state, 300);
        expect(state.snow.tracks.Count).toBeGreaterThan(3);
        var loaded = Savefile.loadFile(Savefile.toSaveFile(state));
        expect(loaded.ok).toBe(true);
        expect(Js.Json_stringify(loaded.state.snow)).toBe(Js.Json_stringify(state.snow));
        expect(Snow.depthAt(loaded.state, 5, 3)).toBe(Snow.depthAt(state, 5, 3));
        Tick.step(state, 200);
        Tick.step(loaded.state, 200);
        expect(Js.Json_stringify(loaded.state.snow)).toBe(Js.Json_stringify(state.snow));
        // The older save: no field at all.
        state.snow = null;
        var older = Savefile.loadFile(Savefile.toSaveFile(state));
        expect(older.ok).toBe(true);
        expect(older.state.snow == null).toBe(true);
    }

    [Fact(DisplayName = "saves > you walk a line through snow in the full sim: your trail is lower than the snow beside it, and the walk is slower than on bare ground")]
    public void walk_a_line()
    {
        double walked(bool snow)
        {
            var state = field();
            state.tick = at(17, 8);
            state.weather = "clear";
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
            Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
            if (snow)
            {
                snowOf(state, 0, 30, 0);
            }
            Tick.step(state, 100);
            if (snow)
            {
                expect(Snow.depthAt(state, 5, 3)).toBeLessThan(Snow.depthAt(state, 5, 2));
            }
            return s.at.x;
        }
        expect(walked(true)).toBeLessThan(walked(false));
    }
}
