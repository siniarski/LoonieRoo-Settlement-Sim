// The clear bugs the quality pass listed (log/2026-11-11-13-clear-bugs.md), each held by a test: a command runs
// at its own world's day length; a refused command changes nothing; an unknown command is refused; "talk" picks the
// nearest by the distance its range is in; someone lost far from home takes all they carried; an outpost's list
// isn't changed while it is walked; a settlement keeps its person when there is nowhere to send them.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class RefusalsAndLossesTest : IDisposable
{
    public void Dispose() => Time.setDayScale(1);

    static (SimState state, Survivor you) alone()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s...", "...."] });
        var you = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
        return (state, you);
    }

    [Fact(DisplayName = "commands > a command before the first tick runs at its own world's day length, not the last world's")]
    public void Day_length_before_the_first_tick()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.."] });
        state.settings.dayScale = 2;
        Time.setDayScale(1); // what the process was left at by the world before
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = state.survivors[0].id });
        expect(Time.dayScale()).toBe(2);
        expect(Time.DAY_LENGTH).toBe(2 * Time.BASE_TICKS_PER_HOUR * Time.HOURS_PER_DAY);
    }

    [Fact(DisplayName = "commands > a refused work or use leaves the state exactly as it was: no turn, no note")]
    public void A_refusal_changes_nothing()
    {
        var (state, you) = alone();
        string before = Js.Json_stringify(state);
        // East of you is bare floor: nothing to repair or build, nothing to use; and east is a turn from where you face.
        var work = Commands.applyCommand(state, new Command { type = "work", x = 1, y = 0 });
        expect(work.ok).toBe(false);
        expect(work.reason != null).toBe(true);
        expect(Js.Json_stringify(state)).toBe(before);
        var use = Commands.applyCommand(state, new Command { type = "use", x = 1, y = 0 });
        expect(use.ok).toBe(false);
        expect(Js.Json_stringify(state)).toBe(before);
    }

    [Fact(DisplayName = "commands > a command of no known type is refused, not a crash")]
    public void Unknown_command()
    {
        var (state, you) = alone();
        var r = Commands.applyCommand(state, new Command { type = "noSuchCommand" });
        expect(r.ok).toBe(false);
        expect(r.reason).toContain("unknown command");
    }

    [Fact(DisplayName = "talk > with nobody named you talk to the nearest in range, a diagonal counting as one")]
    public void Talk_to_the_nearest_in_range()
    {
        var f = Ported.TalkTest.field(); // you at 40,40
        var state = f.state;
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = f.you.id });
        // Three tiles straight ahead (out of range, three steps) and two away on the diagonal (in range, four steps).
        f.them.pos = new Point { x = 43, y = 40 };
        var near = Survivors.addSurvivor(state, 42, 42);
        var r = Commands.applyCommand(state, new Command { type = "talk" });
        expect(r.ok, r.reason ?? "").toBe(true);
        expect(near.story.nextTalk).toBeGreaterThan(state.tick);
    }

    [Fact(DisplayName = "expeditions > someone who doesn't come back takes all they wore and carried, and their bed is free")]
    public void The_lost_take_everything()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.b.", "/f.."] });
        var s = state.survivors[0];
        var weapon = state.items.find((i) => i.kind == "weapon");
        var food = state.items.find((i) => i.kind == "food");
        var bed = state.furniture.find((x) => x.kind == "bed");
        weapon.pos = null;
        s.weapon = weapon.id;
        expect(Inventory.stow(state, s, food)).toBeNull();
        expect(s.pack.includes(food.id)).toBe(true);
        bed.owner = s.id;
        Expeditions.lose(state, s);
        expect(state.items.includes(weapon)).toBe(false);
        expect(state.items.includes(food)).toBe(false);
        expect(state.items.some((i) => i.carriedBy == s.id)).toBe(false);
        expect(s.pack.length).toBe(0);
        expect(bed.owner).toBeNull();
    }

    /// <summary>Two one-person outposts, and a dawn on which the first is in danger and the second is not.</summary>
    static (SimState state, Expedition first, Expedition second, int dawn) twoOutposts()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 8, map = ["ss.$$)" + ".".repeat(44)] });
        state.pointsOfInterest.push(new() { id = "poi1", x = 49, y = 0, label = "ruin" });
        state.pointsOfInterest.push(new() { id = "poi2", x = 48, y = 0, label = "farm" });
        expect(Commands.applyCommand(state, new Command { type = "sendExpedition", survivorIds = [state.survivors[0].id], poiId = "poi1", outpost = true }).ok).toBe(true);
        expect(Commands.applyCommand(state, new Command { type = "sendExpedition", survivorIds = [state.survivors[0].id], poiId = "poi2", outpost = true }).ok).toBe(true);
        var first = state.expeditions[0];
        var second = state.expeditions[1];
        for (int day = 1; day < 400; day++)
        {
            int t = day * Time.DAY_LENGTH;
            if (Hash.hashRoll(Hash.idNumber(first.id), t, 0xd4) < Outposts.OUTPOST_DANGER && Hash.hashRoll(Hash.idNumber(second.id), t, 0xd4) >= Outposts.OUTPOST_DANGER)
                return (state, first, second, t);
        }
        throw new Exception("no such dawn in 400 days");
    }

    [Fact(DisplayName = "outposts > when an outpost's last member is killed, the next outpost still has its dawn")]
    public void An_outpost_lost_doesnt_skip_the_next()
    {
        // The same dawn twice: the first outpost's member hurt (it stays), and killed (it is taken off the list).
        double finds(bool killed)
        {
            var (state, first, second, dawn) = twoOutposts();
            if (killed) first.members[0].health = 100;
            state.tick = dawn;
            double before = state.items.length;
            Outposts.updateOutposts(state);
            expect(state.expeditions.includes(first)).toBe(!killed);
            expect(state.expeditions.includes(second)).toBe(true);
            return state.items.length - before;
        }
        double hurt = finds(false);
        expect(hurt).toBeGreaterThanOrEqual(2 * Outposts.OUTPOST_FINDS); // one from each
        expect(finds(true)).toBe(hurt);
    }

    [Fact(DisplayName = "settlements > with nowhere for a newcomer to turn up, a friendly settlement keeps its person")]
    public void A_settlement_keeps_its_person()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.."] });
        foreach (var id in new[] { "n1", "n2" })
            state.settlements.push(new() { id = id, name = id, x = 0, y = 0, reputation = Settlements.FRIENDLY, people = 8, food = 32, defence = 1, ties = new() { }, unfolded = true, lastTalk = -1, lastRaid = -1 });
        state.survivors = []; // nobody to arrive near: no spot
        int t = 0;
        while (Time.tickOfDay(t) != 12 * Time.TICKS_PER_HOUR || Time.dayOf(t) % Settlements.FRIENDLY_SEND_DAYS != 0) t++;
        state.tick = t;
        Settlements.updateSettlements(state);
        expect(state.settlements[0].people).toBe(8);
        expect(state.settlements[1].people).toBe(8);
    }
}
