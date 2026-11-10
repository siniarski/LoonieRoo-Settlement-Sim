// Ported from src/sim/survivors.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Survivors: tile-to-tile movement along cached A* paths.
//
// A survivor's `path` is a cache stamped with the walkabilityVersion it was
// computed at. Paths are re-checked at every tile boundary, so a wall placed
// mid-route causes a repath at the next step, never a walk through a wall.
// If no path exists the survivor drops the goal and goes idle with a note
// saying why (never stuck).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Survivors
{

    /// <summary>Ticks to cross one tile (4 ticks = 0.4s at 1x).</summary>
    public const int TICKS_PER_TILE = 4;

    /// <summary>Ground indices (GROUNDS) that are quicker to walk on.</summary>
    public static readonly /*TODO array type*/List<object> ROAD_GROUNDS = [Grids.GROUNDS.indexOf("road"), Grids.GROUNDS.indexOf("bridge"), Grids.GROUNDS.indexOf("paving")];

    /// <summary>Chance that stepping onto an indoor floor tile leaves dirt on it. Uses the sim RNG.</summary>
    public static readonly double DIRT_CHANCE = ((double)(1) / (30));

    public static readonly List<string> NAMES = ["Ash", "Bea", "Cal", "Dee", "Eli", "Fen", "Gus", "Hal", "Ivy", "Jo", "Kit", "Lou", "Max", "Nell", "Oz", "Pip", "Quin", "Rae", "Sam", "Tess", "Uma", "Vic", "Wren", "Xan", "Yan", "Zed"];

    /// <summary>Hands out names in order (its own counter, so ids of other entities don't shift names).</summary>
    public static string nextSurvivorName(SimState state)
    {
        return Js.NN(Js.Get(NAMES, state.namesGiven++ % NAMES.length));
    }

    public static Survivor addSurvivor(SimState state, int x, int y, string? name = default)
    {
        var n = state.nextId++;
        Survivor survivor = new Survivor { id = $"s{Js.Str(n)}", name = name ?? nextSurvivorName(state), pos = new Point { x = Js.I(x), y = Js.I(y) }, move = null, goal = null, path = [], pathVersion = -1, note = null, priorities = new(JobTypes.DEFAULT_PRIORITIES) {  }, jobId = null, carrying = null, nextSeekTick = 0, rest = Rest.initialRest(state.survivors.length), sleep = null, satiety = Food.initialSatiety(state.survivors.length), meal = null, nextMealTick = 0, health = Health.HEALTH_MAX, lastHurtBy = null, hurtTick = -1, treatment = null, nextTreatTick = 0, weapon = null, arming = null, nextArmTick = 0, thoughts = [], leisure = null, breakdown = null, xp = new() {  }, traits = [], infection = null, opinions = new() {  }, body = null, feet = null, dressing = null, nextDressTick = 0, sick = null, hydration = Water.HYDRATION_MAX, drink = null, nextDrinkTick = 0, reading = null, age = Family.startingAge($"s{Js.Str(n)}"), childUntil = null, cart = null, cartLoad = [], pack = [], tool = null, shift = "day", kills = 0, watchHours = 0, wound = null, bleeding = false, bleedUntil = 0, bitten = null, order = null, story = StoryMod.makeStory($"s{Js.Str(n)}"), resolve = Resolve.startingResolve($"s{Js.Str(n)}"), social = 60, comfort = 50, wish = null, endurance = 100, panic = 0, bodyHeat = 70, wetness = 0, at = Motion.placedAt(new Point { x = Js.I(x), y = Js.I(y) }).at, was = Motion.placedAt(new Point { x = Js.I(x), y = Js.I(y) }).was };
        state.survivors.push(survivor);
        return survivor;
    }
    public static Survivor addSurvivor(SimState state, double x, double y, string? name = default) => addSurvivor(state, Js.I(x), Js.I(y), name);

    public static Survivor? getSurvivor(SimState state, string id)
    {
        return Lookup.findById(state.survivors, id);
    }

    /// <summary>The survivor standing on, or stepping onto, a tile.</summary>
    public static Survivor? survivorAt(SimState state, int x, int y)
    {
        return state.survivors.find((s) => ((s.pos.x == x) && (s.pos.y == y)) || (((s.move != null) && (s.move.to.x == x)) && (s.move.to.y == y)));
    }
    public static Survivor? survivorAt(SimState state, double x, double y) => survivorAt(state, Js.I(x), Js.I(y));

    public static void setGoal(Survivor survivor, Point goal)
    {
        survivor.goal = new Point { x = Js.I(goal.x), y = Js.I(goal.y) };
        survivor.path = [];
        survivor.pathVersion = -1;
        survivor.note = $"ordered to ({Js.Str(goal.x)}, {Js.Str(goal.y)})";
    }

    /// <summary>Follow a path that was already computed (by job planning) at the current walkabilityVersion.</summary>
    public static void walk(Survivor s, List<Point> path, double version)
    {
        var last = path.at(-1);
        if (!Js.T(last))
        {
            return;
        }
        s.goal = new Point { x = Js.I(last.x), y = Js.I(last.y) };
        s.path = path.slice();
        s.pathVersion = version;
    }

    /// <summary>Advances movement by one tick. Afterwards the survivor is mid-step, walking, or standing still.</summary>
    public static void updateMovement(SimState state, Survivor s)
    {
        if (Js.T(s.move) && !advanceStep(state, s))
        {
            return;
        }
        var goal = s.goal;
        if (!Js.T(goal))
        {
            return;
        }
        if ((s.pos.x == goal.x) && (s.pos.y == goal.y))
        {
            stop(s, Js.T(s.jobId) ? s.note : $"arrived at ({Js.Str(goal.x)}, {Js.Str(goal.y)})");
            return;
        }
        if ((s.pathVersion != state.walkabilityVersion) || (s.path.length == 0))
        {
            var wasRepath = s.pathVersion != -1;
            var path = Pathfinding.findPath(state.grid, s.pos, goal);
            if (!Js.T(path) || (path.length == 0))
            {
                stop(s, $"no path to ({Js.Str(goal.x)}, {Js.Str(goal.y)})");
                return;
            }
            s.path = path;
            s.pathVersion = state.walkabilityVersion;
            s.note = wasRepath ? $"walls changed, repathed to ({Js.Str(goal.x)}, {Js.Str(goal.y)})" : $"walking to ({Js.Str(goal.x)}, {Js.Str(goal.y)}), {Js.Str(path.length)} tiles";
        }
        if (s.path.length >= 2)
        {
            s.path = Pathfinding.straightenAhead(state.grid, s.pos, s.path);
        }
        var next = Js.NN(s.path.shift());
        var diagonal = (next.x != s.pos.x) && (next.y != s.pos.y);
        Doors.passThrough(state, next.x, next.y, true, s.pos);
        var pace = stepPace(state, s, next);
        s.move = new Movement { from = Js.Clone(s.pos), to = next, progress = 0, duration = Js.T(diagonal) ? JsMath.round(((double)(((double)(pace) * (1414))) / (1000))) : pace };
    }

    /// <summary>Advances a step in progress by a tick. Returns true once it has landed (or there was none).</summary>
    public static bool advanceStep(SimState state, Survivor s)
    {
        if (!Js.T(s.move))
        {
            return true;
        }
        s.move.progress += 1;
        if (s.move.progress < s.move.duration)
        {
            return false;
        }
        s.pos = s.move.to;
        s.move = null;
        enterTile(state, s);
        return true;
    }

    /// <summary>Stepping onto a tile: maybe dirt underfoot, maybe a trap.</summary>
    public static void enterTile(SimState state, Survivor s)
    {
        trackDirt(state, s.pos);
        Clothing.trapStep(state, s);
    }

    /// <summary>Ticks to step onto a tile. Roads (and bridges) are a tick quicker per tile; so are quick survivors. Never under 2.</summary>
    public static double stepPace(SimState state, Survivor s, Point next)
    {
        var road = ROAD_GROUNDS.includes(Js.NN(Grids.groundAt(state.grid, ((double)(next.y) * (state.grid.width)) + next.x)));
        return JsMath.max(2, (((((Health.isHurt(s.health) ? Health.HURT_STEP_TICKS : TICKS_PER_TILE) - (Skills.hasTrait(s, "quick") ? 1 : 0)) - (Js.T(road) ? 1 : 0)) + (Wounds.legSlowed(s) ? 1 : 0)) - (Orders.hurries(state, s) ? 1 : 0)) + (Moodlets.boosted(s, "slowWalk") ? 1 : 0));
    }

    public static void trackDirt(SimState state, Point p)
    {
        if (Grids.getTile(state.grid, p.x, p.y) != "floor")
        {
            return;
        }
        if ((Rng.random(state) < DIRT_CHANCE) && Rooms.isIndoors(state, p.x, p.y))
        {
            Grids.setDirty(state.grid, p.x, p.y, true);
        }
    }

    public static void stop(Survivor s, string? note)
    {
        s.goal = null;
        s.path = [];
        s.pathVersion = -1;
        s.note = note;
    }
}
