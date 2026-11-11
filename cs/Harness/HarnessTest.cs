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

    [Fact(DisplayName = "harness > the solo bot's path goes round furniture and trees, which stop you on foot")]
    public void SoloPathAroundFurniture()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.cY.", "....."] });
        var path = SoloHarness.youPath(state, new Point { x = 0, y = 0 }, [new Point { x = 4, y = 0 }]);
        Assert.NotNull(path);
        Assert.DoesNotContain(path, (p) => p.y == 0 && (p.x == 2 || p.x == 3));
        Assert.Equal((4, 0), (path[^1].x, path[^1].y));
        Assert.Equal(6, path.Count);
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
