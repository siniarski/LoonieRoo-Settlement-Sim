// Survivors, the roster (part of Survivors; the rest, how they walk, is Movement/Survivors.cs): making one,
// the names handed out, finding one by id or by tile.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Survivors
{
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
}
