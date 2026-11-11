// Ported from src/sim/runs.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Scavenging runs: neighbours' people out in the world, looking for food.
//
// At dawn a settlement that's short of food (or now and then one that isn't) with RUN_MIN_HOME
// people to spare may send RUN_PARTY of them to a ruin RUN_MIN..RUN_MAX tiles off (by hash). The
// party is a marker, like a caravan (Caravans.cs): out a tile every RUN_STEP ticks, searching
// for RUN_SEARCH ticks, and back. Home again, what they found goes into the stores (RUN_FIND a
// head) and they're counted back in.
//   the dead   each hour there's a chance they're caught (more at the ruin than on the road):
//              one is lost. Lose them all and they lie where they fell, bodies and what they'd
//              found, marked on your map ("dead scavengers").
//   meeting    if one of yours comes within RUN_MEET tiles, they stop to talk: their settlement
//              thinks a little better of you, and they tell you where they're going (a place on
//              your map).
// Rolls are hashes of the run's id and the tick, not the sim RNG.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Runs
{
    static Runs()
    {
        Time.onClock(() => RUN_SEARCH = ((double)(2) * (Time.TICKS_PER_HOUR)));
    }

    public const int RUN_PARTY = 3;

    public const int RUN_MIN_HOME = 5;

    public const int RUN_MIN = 25;

    public const int RUN_MAX = 60;

    public const int RUN_STEP = 6;

    public static double RUN_SEARCH = ((double)(2) * (Time.TICKS_PER_HOUR));

    public const int RUN_FIND = 2;

    /// <summary>Chance a dawn that a settlement short of food sends a run (a well-fed one, a quarter of that).</summary>
    public const double RUN_CHANCE = 0.4;

    /// <summary>Chance an hour of being caught: on the road, and at the ruin.</summary>
    public const double ROAD_DANGER = 0.02;

    public const double RUIN_DANGER = 0.08;

    public const int RUN_MEET = 3;

    public const string DEAD_LABEL = "dead scavengers";

    public static List<ScavRun> runsOf(SimState state)
    {
        return state.runs ?? [];
    }

    /// <summary>Every tick: setting out at dawn, moving, searching, trouble, meeting yours, coming home.</summary>
    public static void updateRuns(SimState state)
    {
        if ((state.tick > 0) && ((state.tick % Time.DAY_LENGTH) == 0))
        {
            setOut(state);
        }
        var list = runsOf(state);
        if (list.length == 0)
        {
            return;
        }
        foreach (var r in Js.Iter((list).ToList()))
        {
            if (((state.tick % Time.TICKS_PER_HOUR) == 0) && caught(state, r))
            {
                continue;
            }
            if (!Js.T(r.met))
            {
                meet(state, r);
            }
            if (r.phase == "search")
            {
                if (state.tick >= r.untilTick)
                {
                    r.food += ((double)(r.people) * (RUN_FIND));
                    r.phase = "back";
                }
                continue;
            }
            if (state.tick < r.nextStepTick)
            {
                continue;
            }
            r.nextStepTick = state.tick + RUN_STEP;
            var goal = (r.phase == "out") ? r.ruin : r.home;
            r.x += JsMath.sign(goal.x - r.x);
            r.y += JsMath.sign(goal.y - r.y);
            if ((r.x != goal.x) || (r.y != goal.y))
            {
                continue;
            }
            if (r.phase == "out")
            {
                r.phase = "search";
                r.untilTick = state.tick + RUN_SEARCH;
            }
            else
            {
                home(state, r);
            }
        }
    }

    public static void setOut(SimState state)
    {
        var day = Time.dayOf(state.tick);
        var __d0 = state.grid;
        var width = __d0.width;
        var height = __d0.height;
        foreach (var n in Js.Iter(state.settlements))
        {
            if ((Colonies.isFallen(n) || (n.people < RUN_MIN_HOME)) || runsOf(state).some((r) => r.from == n.id))
            {
                continue;
            }
            double roll(double k)
            {
                return Hash.hashRoll(Hash.idNumber(n.id), day, 0x5ca, k);
            }
            var chance = (Colonies.hungerOf(n) == "fed") ? ((double)(RUN_CHANCE) / (4)) : RUN_CHANCE;
            if (roll(0) >= chance)
            {
                continue;
            }
            var angle = ((double)(((double)(roll(1)) * (JsMath.PI))) * (2));
            var dist = RUN_MIN + JsMath.floor(((double)(roll(2)) * ((RUN_MAX - RUN_MIN))));
            Point ruin = new Point { x = Js.I(JsMath.max(1, JsMath.min(width - 2, JsMath.round(n.x + ((double)(JsMath.cos(angle)) * (dist)))))), y = Js.I(JsMath.max(1, JsMath.min(height - 2, JsMath.round(n.y + ((double)(JsMath.sin(angle)) * (dist)))))) };
            var people = JsMath.min(RUN_PARTY, (n.people - RUN_MIN_HOME) + RUN_PARTY);
            n.people -= people;
            state.runs ??= [];
            state.runs.push(new() { id = $"sr{Js.Str(day)}x{Js.Str(Hash.idNumber(n.id))}", from = n.id, x = Js.I(n.x), y = Js.I(n.y), home = new Point { x = Js.I(n.x), y = Js.I(n.y) }, ruin = ruin, people = people, phase = "out", untilTick = 0, nextStepTick = state.tick + RUN_STEP, food = 0, met = false });
            if (!Settlements.isHostile(n))
            {
                Events.logEvent(state, $"{Js.Str(people)} from {Js.Str(n.name)} went out scavenging towards ({Js.Str(ruin.x)}, {Js.Str(ruin.y)})");
            }
        }
    }

    /// <summary>The dead catch up with them: one lost; all of them, and they lie where they fell. Returns true if the run is over.</summary>
    public static bool caught(SimState state, ScavRun r)
    {
        var danger = ((double)(((double)(((double)(((r.phase == "search") ? RUIN_DANGER : ROAD_DANGER)) * ((1 + ((double)(Time.dayOf(state.tick)) / (20)))))) * (state.settings.zombies))) / (100));
        if (Hash.hashRoll(Hash.idNumber(r.id), state.tick, 3562.0) >= danger)
        {
            return false;
        }
        r.people -= 1;
        var n = Settlements.getSettlement(state, r.from);
        var heard = Js.T(n) && !Settlements.isHostile(n);
        var spot = Grids.inBounds(state.grid, r.x, r.y) ? Space.nearestTile(state.grid, r, (x, y) => Grids.isWalkable(state.grid, x, y), 60) : null;
        if (Js.T(spot))
        {
            Corpses.leaveCorpse(state, spot, $"a scavenger from {Js.Str(n?.name ?? "somewhere")}");
        }
        if (r.people > 0)
        {
            if (Js.T(heard))
            {
                Events.logEvent(state, $"One of {Js.Str(n.name)}'s scavengers was killed near ({Js.Str(r.x)}, {Js.Str(r.y)})");
            }
            return false;
        }
        if (Js.T(spot))
        {
            for (var k = 0; k < JsMath.min(6, r.food); k++)
            {
                Items.spawnItem(state, "food", spot.x, spot.y);
            }
        }
        var at = spot ?? new Point { x = Js.I(r.x), y = Js.I(r.y) };
        state.pointsOfInterest.push(new() { id = $"p{Js.Str(state.nextId++)}", x = Js.I(at.x), y = Js.I(at.y), label = DEAD_LABEL });
        if (Js.T(heard))
        {
            Events.logEvent(state, $"{Js.Str(n.name)}'s scavengers never came home: the dead got them near ({Js.Str(at.x)}, {Js.Str(at.y)})");
        }
        Js.NN(state.runs).splice(Js.NN(state.runs).indexOf(r), 1);
        return true;
    }

    /// <summary>One of yours crosses their path: a few words, and they say where they're going.</summary>
    public static void meet(SimState state, ScavRun r)
    {
        var you = state.survivors.find((s) => Space.manhattan(s.pos, r) <= RUN_MEET);
        if (!Js.T(you))
        {
            return;
        }
        r.met = true;
        var n = Settlements.getSettlement(state, r.from);
        if (!Js.T(n) || Settlements.isHostile(n))
        {
            return;
        }
        Settlements.adjustReputation(state, n, 3);
        if (!state.pointsOfInterest.some((p) => Space.manhattan(p, r.ruin) <= 5))
        {
            state.pointsOfInterest.push(new() { id = $"p{Js.Str(state.nextId++)}", x = Js.I(r.ruin.x), y = Js.I(r.ruin.y), label = $"{Js.Str(n.name)}'s scavenging spot" });
        }
        Events.logEvent(state, $"{Js.Str(you.name)} met {Js.Str(r.people)} scavengers from {Js.Str(n.name)}, {Js.Str((r.phase == "back") ? Js.Str("on their way home") : Js.Str($"on their way to a ruin at ({Js.Str(r.ruin.x)}, {Js.Str(r.ruin.y)})"))}");
    }

    public static void home(SimState state, ScavRun r)
    {
        Js.NN(state.runs).splice(Js.NN(state.runs).indexOf(r), 1);
        var n = Settlements.getSettlement(state, r.from);
        if (!Js.T(n))
        {
            return;
        }
        n.people += r.people;
        n.food += r.food;
        if (!Settlements.isHostile(n))
        {
            Events.logEvent(state, $"{Js.Str(n.name)}'s scavengers came home with {Js.Str(r.food)} food");
        }
    }
}
