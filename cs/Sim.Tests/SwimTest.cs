// Swim (Swim.cs): you walk into water, wade the shallows and swim the deep; Space is a stroke out of your depth.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class SwimTest
{
    /// <summary>A river five tiles wide (x 3 to 7): its first and last tiles shallow, the middle three deep.</summary>
    static readonly List<string> RIVER = ["...%%%%%...", "...%%%%%...", "...%%%%%...", ".s.%%%%%...", "...%%%%%...", "...%%%%%...", "...%%%%%..."];

    static (SimState state, Survivor s) start(List<string> map = null, int x = 1, int y = 3)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map ?? RIVER });
        state.weather = "clear";
        var s = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        put(s, x, y);
        s.endurance = BodyMod.BODY_MAX;
        return (state, s);
    }

    static void put(Survivor s, int x, int y)
    {
        s.pos = new Point { x = x, y = y };
        s.at = Motion.centre(s.pos);
        s.was = s.at;
    }

    static void ticks(SimState state, int n)
    {
        for (int i = 0; i < n; i++) Tick.tick(state);
    }

    [Fact(DisplayName = "swim > depth is the distance from the bank: a tile out is shallow, further is deep")]
    public void Depth()
    {
        var (state, _) = start();
        var row = Enumerable.Range(0, 11).Select(x => Swim.depth(state.grid, x, 3)).ToList();
        expect(string.Join("", row)).toBe("00012221000");
        // The map's edge is not a bank: a lake against it is deep there.
        var (lake, _) = start(["%%%%", "%%%%", "%%%%", ".s.."], 1, 3);
        expect(Swim.depth(lake.grid, 1, 0)).toBe(Swim.DEEP);
        expect(Swim.depth(lake.grid, 1, 2)).toBe(Swim.SHALLOW);
    }

    [Fact(DisplayName = "swim > you walk into the river: wading slower than walking, swimming slower still, out the far side")]
    public void Cross()
    {
        var (state, s) = start();
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
        double land = ControlMod.stepLength(state, s, state.control, false);
        var seen = new List<int>();
        var pace = new Dictionary<int, double>();
        for (int t = 0; t < 400 && s.pos.x < 9; t++)
        {
            Tick.tick(state);
            var d = Swim.depth(state.grid, s.pos);
            if (seen.Count == 0 || seen[^1] != d) seen.Add(d);
            if (!pace.ContainsKey(d)) pace[d] = ControlMod.stepLength(state, s, state.control, false);
        }
        expect(string.Join("", seen)).toBe("01210");
        expect(s.pos.x).toBe(9);   // out on the far bank
        expect(pace[Swim.SHALLOW]).toBe(JsMath.round(land * Swim.WADE_PCT / 100));
        expect(pace[Swim.DEEP]).toBeLessThan(pace[Swim.SHALLOW]);
        expect(s.endurance).toBeLessThan(BodyMod.BODY_MAX);   // the deep water cost you
    }

    [Fact(DisplayName = "swim > deep water soaks you through at once, wading part way; the water chills you and nobody dries in it")]
    public void Soak()
    {
        var (state, s) = start();
        s.wetness = 0;
        s.bodyHeat = 70;
        put(s, 3, 3);
        ticks(state, 40);
        expect(s.wetness).toBe(Swim.WADE_WET);
        expect(s.wetness).toBeGreaterThan(BodyMod.SOAKED);
        double wading = s.bodyHeat;
        expect(wading).toBeLessThan(70);
        put(s, 5, 3);
        Tick.tick(state);
        expect(s.wetness).toBe(BodyMod.BODY_MAX);
        s.bodyHeat = 70;
        ticks(state, 40);
        expect(70 - s.bodyHeat).toBeGreaterThan(70 - wading);   // faster in deep water
        expect(s.wetness).toBe(BodyMod.BODY_MAX);
        // Out on the bank you dry slowly, as after rain.
        put(s, 1, 3);
        ticks(state, 50);
        expect(s.wetness).toBeLessThan(BodyMod.BODY_MAX);
        expect(s.wetness).toBeGreaterThan(BodyMod.DRENCHED);
    }

    [Fact(DisplayName = "swim > the water chills you as fast a day at a longer day: by slow ticks, not plain ones")]
    public void SoakFollowsTheClock()
    {
        var (state, s) = start();
        try
        {
            Time.setDayScale(2);
            foreach (var (x, every) in new[] { (5, Swim.DEEP_CHILL_EVERY), (3, Swim.WADE_CHILL_EVERY) })
            {
                put(s, x, 3);
                s.bodyHeat = 70;
                for (var slow = 0; slow < 8; slow++)
                {
                    state.tick = slow * 2;  // the slow ticks at scale 2
                    Swim.soak(state, s);
                }
                expect(70 - s.bodyHeat).toBe(8 / every);
            }
        }
        finally
        {
            Time.setDayScale(1);
        }
    }

    [Fact(DisplayName = "swim > paddling costs endurance as running does; winded, you struggle and the water does you harm")]
    public void Struggle()
    {
        var (state, s) = start();
        put(s, 5, 3);
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 1 });
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 0 });
        ticks(state, 20);   // treading water: a little
        expect(s.endurance).toBeGreaterThanOrEqual(BodyMod.BODY_MAX - 20 / Swim.TREAD_EVERY - 1);
        s.endurance = BodyMod.BODY_MAX;
        s.health = Health.HEALTH_MAX;
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = -1, run = true });
        put(s, 5, 6);
        ticks(state, 10);
        expect(s.endurance).toBe(BodyMod.BODY_MAX - 10 * Swim.SWIM_COST);   // no running out of your depth: paddling's cost only
        expect(s.health).toBe(Health.HEALTH_MAX);
        // Winded: slower, and the water does harm, until you're out of your depth no longer.
        s.endurance = BodyMod.WINDED - 1;
        put(s, 5, 3);
        expect(Swim.pacePct(state, s)).toBe(Swim.STRUGGLE_PCT);
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 0 });
        ticks(state, 50);
        expect(s.health).toBeLessThan(Health.HEALTH_MAX - 3 * Swim.DROWN_DAMAGE);   // five blows in five seconds, less what healed
        expect(s.lastHurtBy).toBe(Swim.DROWNING);
        expect(Commands.applyCommand(state, new Command { type = "jump" }).ok).toBe(false);   // too winded for a stroke
        // In the shallows the harm stops and your breath comes back.
        put(s, 3, 3);
        double hp = s.health;
        ticks(state, 80);
        expect(s.health).toBeGreaterThanOrEqual(hp);   // no more harm (it heals as anywhere)
        expect(s.endurance).toBeGreaterThan(BodyMod.WINDED - 1);
    }

    [Fact(DisplayName = "swim > Space out of your depth is a stroke: no lift, further and cheaper than paddling")]
    public void Stroke()
    {
        var (state, s) = start();
        put(s, 4, 0);
        Commands.applyCommand(state, new Command { type = "aim", x = 0, y = 1 });
        expect(Commands.applyCommand(state, new Command { type = "jump" }).ok).toBe(true);
        expect(state.control.busy.what).toBe("stroke");
        double y0 = s.at.y;
        Tick.tick(state);
        expect(PoseIntents.ofSurvivor(state, s).lift).toBe(0);
        ticks(state, Swim.STROKE_TICKS);
        expect(state.control.busy).toBe(null);
        expect(s.at.y - y0).toBe(Swim.STROKE_LENGTH);
        double strokeCost = BodyMod.BODY_MAX - s.endurance;
        // Paddling the same time: less far, more spent.
        var (st2, s2) = start();
        put(s2, 4, 0);
        Commands.applyCommand(st2, new Command { type = "steer", dx = 0, dy = 1 });
        ticks(st2, Swim.STROKE_TICKS + 1);
        expect(s2.at.y - y0).toBeLessThan(Swim.STROKE_LENGTH);
        expect(BodyMod.BODY_MAX - s2.endurance).toBeGreaterThan(strokeCost);
        // On land and wading, Space is still a jump.
        var (st3, s3) = start();
        put(s3, 3, 3);
        Commands.applyCommand(st3, new Command { type = "jump" });
        expect(st3.control.busy.what).toBe("jump");
    }

    [Fact(DisplayName = "swim > swinging, shooting, using and working are refused out of your depth, not wading")]
    public void Refusals()
    {
        var (state, s) = start();
        put(s, 5, 3);
        state.control.nextSwing = 0;
        foreach (var type in new[] { "swing", "use", "work" })
            expect(Commands.applyCommand(state, new Command { type = type, x = 6, y = 3 }).reason, type).toBe("swimming");
        expect(Commands.applyCommand(state, new Command { type = "fire", x = 1, y = 0 }).reason).toBe("swimming");
        expect(Commands.applyCommand(state, new Command { type = "reload" }).reason).toBe("swimming");
        put(s, 3, 3);
        expect(Commands.applyCommand(state, new Command { type = "swing", x = 4, y = 3 }).reason).not.toBe("swimming");
    }

    [Fact(DisplayName = "swim > only you go in: colonists, the dead and animals keep out of the water")]
    public void OnlyYou()
    {
        foreach (var m in new[] { Pathfinding.SURVIVOR, Pathfinding.FREE, Pathfinding.ZOMBIE, Pathfinding.BREACHER, Pathfinding.RAIDER, Pathfinding.CRAWLER, Pathfinding.ANIMAL, Pathfinding.VEHICLE })
        {
            var (st, _) = start();
            expect(Pathfinding.isPassable(st.grid, 5, 3, m), m).toBe(false);
        }
        // The dead come to the bank for you and stop there.
        var map = RIVER.map((r, y) => y == 1 ? "z" + r.Substring(1) : r);
        var (state, s) = start(map);
        put(s, 5, 3);
        for (int t = 0; t < 300; t++)
        {
            Tick.tick(state);
            s.endurance = BodyMod.BODY_MAX;
            foreach (var z in state.zombies) expect(Grids.getTile(state.grid, z.pos.x, z.pos.y), $"tick {t}").not.toBe(Grids.WATER);
        }
    }

    [Fact(DisplayName = "swim > the map's edge holds in the water too")]
    public void Edge()
    {
        var (state, s) = start(["%%%%", "%%%%", "%%%%", ".s.."], 1, 1);
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = -1 });
        for (int t = 0; t < 60; t++)
        {
            Tick.tick(state);
            s.endurance = BodyMod.BODY_MAX;
        }
        expect(s.pos.y).toBe(0);
        expect(s.at.y).toBeGreaterThanOrEqual(Motion.BODY_RADIUS);
    }

    [Fact(DisplayName = "swim > letting go in the water puts you ashore: colonists don't swim")]
    public void Ashore()
    {
        var (state, s) = start();
        put(s, 5, 3);
        expect(Commands.applyCommand(state, new Command { type = "takeControl", survivorId = null }).ok).toBe(true);
        expect(Grids.getTile(state.grid, s.pos.x, s.pos.y)).toBe(Grids.FLOOR);
        expect(JsMath.abs(s.pos.x - 5)).toBeLessThanOrEqual(3);
    }

    [Fact(DisplayName = "swim > a save taken mid-stroke reads back and plays on the same")]
    public void Save()
    {
        var (state, s) = start();
        put(s, 4, 1);
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 1 });
        ticks(state, 5);
        Commands.applyCommand(state, new Command { type = "jump" });
        ticks(state, 3);
        expect(state.control.busy.what).toBe("stroke");
        var loaded = Savefile.loadFile(Savefile.toSaveFile(state));
        expect(loaded.ok).toBe(true);
        var copy = loaded.state;
        for (int i = 0; i < 60; i++)
        {
            Tick.tick(state);
            Tick.tick(copy);
        }
        var a = copy.survivors[0];
        expect($"{a.at.x},{a.at.y},{a.endurance},{a.wetness}").toBe($"{s.at.x},{s.at.y},{s.endurance},{s.wetness}");
        expect(Savefile.toSaveFile(copy)).toBe(Savefile.toSaveFile(state));
    }
}
