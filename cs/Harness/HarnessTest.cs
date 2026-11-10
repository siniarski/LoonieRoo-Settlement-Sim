// Tests of the balance players themselves (2026-11-10, balance check). Not in the check, like the rest of the
// harness: dotnet test cs/Harness -c Release --filter "DisplayName~harness >"
#nullable disable warnings
using LoonieRoo.Sim;
namespace LoonieRoo.Harness;

public class HarnessTest
{
    [Fact(DisplayName = "harness > newEvents gives every event once, though the state keeps only the last 50")]
    public void NewEventsAcrossTheCap()
    {
        var state = States.createState(new NewStateOptions { seed = 1, width = 8, height = 8 });
        GameEvent last = null;
        for (var i = 0; i < 30; i++)
        {
            Events.logEvent(state, $"a{i}");
        }
        Assert.Equal(30, Probe.newEvents(state, ref last).Count);
        for (var i = 0; i < 40; i++)
        {
            Events.logEvent(state, $"b{i}");
        }
        // 70 logged, 50 kept: the 40 new ones all come through, none of the first 30 again.
        var fresh = Probe.newEvents(state, ref last).Select((e) => e.message).ToList();
        Assert.Equal(Enumerable.Range(0, 40).Select((i) => $"b{i}"), fresh);
        Assert.Empty(Probe.newEvents(state, ref last));
    }

    [Fact(DisplayName = "harness > the solo bot drinks from a pond when there's no well, collector or tap")]
    public void SoloBotDrinksFromAPond()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s...........%%", "............%%", "..............", ".............."] });
        state.harsh = true;
        var s = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        s.hydration = Water.THIRSTY - 300;
        s.satiety = Food.SATIETY_MAX;
        var home = new Point { x = 0, y = 0 };
        var bot = new Bot();
        var best = s.hydration;
        for (var t = 0; t < 400; t++)
        {
            SoloHarness.decide(state, s, bot, home, home);
            Tick.tick(state);
            best = Math.Max(best, s.hydration);
        }
        Assert.True(best > Water.HYDRATION_MAX - 400, $"never drank: hydration at most {best}, at {s.pos.x},{s.pos.y}, goal {bot.goal}");
    }
}
