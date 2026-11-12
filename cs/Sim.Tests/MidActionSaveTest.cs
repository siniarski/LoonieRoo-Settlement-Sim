// A save taken in the middle of each timed action you can be in (Control.busy: eating, treating, sleeping, foraging,
// searching, a leap) reads back and plays on the same as the game it was taken from. The stroke has its own
// (SwimTest); the car's roof and the leaps onto it theirs (CarLeapsTest).
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class MidActionSaveTest
{
    static readonly List<string> ROOM = ["..........", "..........", "....s.....", "..........", ".........."];

    static (SimState state, Survivor s, Control c) start()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = ROOM });
        state.weather = "clear";
        state.tick = Js.I(10 * Time.TICKS_PER_HOUR + 1);
        var s = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        return (state, s, state.control);
    }

    /// <summary>Starts the action named; returns the busy kind it should leave you in.</summary>
    static string begin(SimState state, Survivor s, Control c, string what)
    {
        switch (what)
        {
            case "eat":
                {
                    s.satiety = Food.HUNGRY - 1;
                    var food = Items.spawnItem(state, "food", s.pos.x, s.pos.y);
                    ControlMod.take(state, s, c, food);
                    return "eat";
                }
            case "treat":
                {
                    s.health = Health.HEALTH_MAX / 2;
                    var bandage = Items.spawnItem(state, "bandage", s.pos.x, s.pos.y);
                    c.busy = new ControlBusy { what = "treat", targetId = bandage.id, progress = 0, duration = Health.TREAT_TICKS };
                    return "treat";
                }
            case "sleep":
                s.rest = Rest.TIRED - 1;
                ControlMod.lieDown(s, c, "floor");
                return "sleep";
            case "forage":
                c.busy = new ControlBusy { what = "forage", targetId = "here", progress = 0, duration = Handiwork.FORAGE_TICKS };
                return "forage";
            case "search":
                {
                    var crate = FurnitureMod.addFurniture(state, "crate", s.pos.x + 1, s.pos.y);
                    c.busy = new ControlBusy { what = "search", targetId = crate.id, progress = 0, duration = Jobs.SCAVENGE_WORK };
                    return "search";
                }
            case "jump":
                Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
                Commands.applyCommand(state, new Command { type = "jump" });
                return "jump";
        }
        throw new ArgumentException(what);
    }

    [Theory(DisplayName = "a save mid-action > reads back and plays on the same")]
    [InlineData("eat")]
    [InlineData("treat")]
    [InlineData("sleep")]
    [InlineData("forage")]
    [InlineData("search")]
    [InlineData("jump")]
    public void Plays_on_the_same(string what)
    {
        var (state, s, c) = start();
        var kind = begin(state, s, c, what);
        Tick.tick(state);
        Tick.tick(state);
        expect(c.busy?.what, $"{what}: busy after two ticks").toBe(kind);
        var loaded = Savefile.loadFile(Savefile.toSaveFile(state));
        expect(loaded.ok, what).toBe(true);
        var copy = loaded.state;
        expect(Savefile.toSaveFile(copy), $"{what}: read back").toBe(Savefile.toSaveFile(state));
        for (var i = 0; i < 300; i++)
        {
            Tick.tick(state);
            Tick.tick(copy);
        }
        expect(Savefile.toSaveFile(copy), $"{what}: played on").toBe(Savefile.toSaveFile(state));
    }
}
