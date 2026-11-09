// Ported from src/sim/state.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Sim state types and constructors. Everything here must stay plain
// JSON-compatible data (architecture rule 7).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class States
{

    /// <summary>Bump when the shape of SimState changes. Hot reload and (later) save loading discard state with a different version instead of crashing on it.</summary>
    public const int SCHEMA_VERSION = 75;

    public static SimState createState(NewStateOptions __o0)
    {
        var seed = __o0.seed;
        var width = __o0.width;
        var height = __o0.height;
        return new SimState { schemaVersion = SCHEMA_VERSION, tick = 0, rng = Rng.seedRng(seed), grid = Grids.createGrid(width, height), walkabilityVersion = 0, survivors = [], zombies = [], wanderers = [], craftTargets = new() { ["weapon"] = 0, ["bandage"] = 0, ["medicine"] = 0, ["ammo"] = 0, ["gun"] = 0, ["coat"] = 0, ["armour"] = 0, ["boots"] = 0, ["cart"] = 0, ["axe"] = 0, ["hammer"] = 0, ["medkit"] = 0 }, zombiesTonight = null, exploreAreas = [], dormant = [], herds = [], pointsOfInterest = [], world = null, timeline = null, vehicles = [], mapNotes = [], caravans = [], errands = [], hordes = [], runs = [], weather = "clear", expeditions = [], traders = [], fires = [], harsh = false, settings = Js.Clone(Difficulty.NORMAL), knowledge = [], dogs = [], settlements = [], residents = [], animals = [], lastRelief = -1000000000.0, prisoners = [], requests = [], endgame = EndgameMod.newEndgame(), policies = [], leaderId = null, control = null, succession = null, tasks = [], foraged = new() {  }, items = [], furniture = [], farm = [], blueprints = [], roomDesignations = [], jobs = [], nextId = 1, namesGiven = 0, commandLog = [], events = [] };
    }

    /// <summary>Deep copy via JSON. Doubles as a check that state is JSON-safe.</summary>
    public static SimState cloneState(SimState state)
    {
        return Js.DeepClone(state);
    }
}
