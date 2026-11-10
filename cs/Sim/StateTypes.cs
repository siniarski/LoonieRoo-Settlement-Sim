// The sim state's types, ported from the TypeScript's (retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Field for field as the saves have them (plain JSON: StateJson.cs, Savefile.cs).
#nullable enable
namespace LoonieRoo.Sim;

public partial interface Body
{
    Point pos { get; set; }
    Movement? move { get; set; }
    Point at { get; set; }
    Point was { get; set; }
}

public partial class Alert
{
    public string level { get; set; } = "";
    public string text { get; set; } = "";
    /// <summary>Where to look, if anywhere.</summary>
    public Point? at { get; set; }
}

public partial class Animal : Body, IHasId, IHasAt, IHasPos, IBody
{
    public string id { get; set; } = "";
    public string kind { get; set; } = "";
    public Point pos { get; set; } = new();
    /// <summary>Where exactly, in motion.ts SUB units (pos is the tile under it); and where at the tick's start.</summary>
    public Point at { get; set; } = new();
    public Point was { get; set; } = new();
    public Movement? move { get; set; }
    /// <summary>Marked by the Hunt order (or for attacking someone).</summary>
    public bool hunted { get; set; }
    /// <summary>Deer leave after a while; the rest leave when nobody of yours is near (WILD_RADIUS).</summary>
    public double leavesTick { get; set; }
    /// <summary>Rabbits, wolves, bears: health left (deer go down to a lucky blow: HUNT_CHANCE).</summary>
    public double? health { get; set; }
    /// <summary>Wolves and bears: how hungry, 0 just fed.</summary>
    public double? hunger { get; set; }
    /// <summary>Wolves: the pack (its leader's id).</summary>
    public string? packId { get; set; }
    /// <summary>Who it's after this tick: an animal's id or a survivor's (shown in the debug view).</summary>
    public string? targetId { get; set; }
    /// <summary>A survivor it's out to get: who hurt it (or its pack), or who a starving wolf has picked.</summary>
    public string? angryAt { get; set; }
    /// <summary>After a warning bite it keeps away from people until this tick.</summary>
    public double? backOffUntil { get; set; }
    /// <summary>No bite before this tick.</summary>
    public double? nextBite { get; set; }
    /// <summary>When it last bit one of yours (menace: guards go for it a while after).</summary>
    public double? bitTick { get; set; }
    /// <summary>Where it's walking to (SUB units: it's a free mover, Walkers.cs), or nothing (standing).</summary>
    public Point? goal { get; set; }
    /// <summary>Tile waypoints, when the straight way to its goal is blocked (A*).</summary>
    public List<Point> path { get; set; } = new();
    /// <summary>No new A* plan before this tick.</summary>
    public double nextPlan { get; set; }
    /// <summary>Grazing (or wandering): it stands until this tick before the next walk.</summary>
    public double restUntil { get; set; }
}

public partial class Blueprint : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Blueprint v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public string target { get; set; } = "";
    public bool needsMaterial { get; set; }
    public bool delivered { get; set; }
    /// <summary>Food for a settlement's stores (a number); goods for you (items).</summary>
    public double workDone { get; set; }
    public double workRequired { get; set; }
    /// <summary>An edge wall or door (grid.ts): on this tile's north or west side, not on the tile.</summary>
    public string? side { get; set; }
}

/// <summary>What a tick's zombie updates share: the plans left, and who's near a point.</summary>
public partial class Budget
{
    public double plans { get; set; }
    public Func<Point, List<Survivor>> near { get; set; } = null!;
}

public partial class BugReport
{
    public string format { get; set; } = "";
    public double schemaVersion { get; set; }
    public string note { get; set; } = "";
    public SimState initial { get; set; } = new();
    public List<LoggedCommand> commands { get; set; } = new();
    public double tick { get; set; }
}

public partial class BuildingsPlan
{
    public int w { get; set; }
    public int h { get; set; }
    public List<double> cells { get; set; } = new();
    public List<string> ground { get; set; } = new();
    public List<PlanFurniture> furniture { get; set; } = new();
    /// <summary>Food for a settlement's stores (a number); goods for you (items).</summary>
    public string kind { get; set; } = "";
    public double sleepers { get; set; }
    public List<PlanEdge> edges { get; set; } = new();
}

public partial class Caravan : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Caravan v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    /// <summary>Who sent it (a settlement id).</summary>
    public string @from { get; set; } = "";
    /// <summary>Where it's going: a settlement id, or 'you'.</summary>
    public string to { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    /// <summary>The end of the road.</summary>
    public Point dest { get; set; } = new();
    public double people { get; set; }
    /// <summary>Food for a settlement's stores (a number); goods for you (items).</summary>
    public double food { get; set; }
    public List<string> goods { get; set; } = new();
    public double nextStepTick { get; set; }
}

public partial class Character
{
    public string name { get; set; } = "";
    /// <summary>Index into STORY_JOBS.</summary>
    public double job { get; set; }
    public List<string> traits { get; set; } = new();
}

public partial class Command
{
    /// <summary>A point, where it has one (TypeScript narrows to the members that do).</summary>
    public static implicit operator Point(Command v) => v is null ? null! : new(v.x!.Value, v.y!.Value);
    public string type { get; set; } = "";
    public int? x { get; set; }
    public int? y { get; set; }
    public string? side { get; set; }
    /// <summary>No bite before this tick.</summary>
    public string? kind { get; set; }
    public double? count { get; set; }
    public string? bedId { get; set; }
    public string? survivorId { get; set; }
    public string? structure { get; set; }
    public string? crop { get; set; }
    public int? x0 { get; set; }
    public int? y0 { get; set; }
    public int? x1 { get; set; }
    public int? y1 { get; set; }
    /// <summary>Only these kinds (all if missing).</summary>
    public List<string>? kinds { get; set; }
    public string? purpose { get; set; }
    public string? jobType { get; set; }
    public double? priority { get; set; }
    public string? wandererId { get; set; }
    public List<string>? survivorIds { get; set; }
    public string? poiId { get; set; }
    public bool? outpost { get; set; }
    public string? expeditionId { get; set; }
    public string? category { get; set; }
    public bool? accept { get; set; }
    public string? prisonerId { get; set; }
    public string? shift { get; set; }
    public string? requestId { get; set; }
    public string? policy { get; set; }
    public bool? on { get; set; }
    public string? settlementId { get; set; }
    public string? traderId { get; set; }
    public double? offer { get; set; }
    public double? rest { get; set; }
    public double? satiety { get; set; }
    public double? health { get; set; }
    public double? amount { get; set; }
    public string? text { get; set; }
    public string? id { get; set; }
    public double? dx { get; set; }
    public double? dy { get; set; }
    public bool? run { get; set; }
    public string? itemId { get; set; }
    public string? containerId { get; set; }
    public string? order { get; set; }
    public string? choice { get; set; }
    public string? who { get; set; }
}

public partial class CommandResult
{
    public bool ok { get; set; }
    public bool? changed { get; set; }
    public string? reason { get; set; }
}

public partial class Control
{
    public string survivorId { get; set; } = "";
    public double dx { get; set; }
    public double dy { get; set; }
    /// <summary>With a side: an edge wall or door on that tile's north or west side (grid.ts).</summary>
    public Point facing { get; set; } = new();
    public Point? aim { get; set; }
    public double nextSwing { get; set; }
    public double? swungTick { get; set; }
    public bool? running { get; set; }
    public bool? sneaking { get; set; }
    public ControlBusy? busy { get; set; }
}

public partial class ControlBusy
{
    public string what { get; set; } = "";
    public string targetId { get; set; } = "";
    public double progress { get; set; }
    public double duration { get; set; }
}

public partial class Dog : Body, IHasId, IHasAt, IHasPos, IBody
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public Point pos { get; set; } = new();
    /// <summary>Where exactly, in motion.ts SUB units (pos is the tile under it); and where at the tick's start.</summary>
    public Point at { get; set; } = new();
    public Point was { get; set; } = new();
    public Movement? move { get; set; }
    public List<Point> path { get; set; } = new();
    public double health { get; set; }
    public double cooldown { get; set; }
    public double nextThinkTick { get; set; }
    public double barkedTick { get; set; }
    public bool waiting { get; set; }
    /// <summary>Following someone: where they were (SUB units: a free mover, Walkers.cs), until it's caught up.</summary>
    public Point? goal { get; set; }
}

public partial class DormantZombie : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(DormantZombie v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
}

public partial class Drink
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Drink v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public int x { get; set; }
    public int y { get; set; }
    public string source { get; set; } = "";
    public Point @from { get; set; } = new();
    public double progress { get; set; }
}

public partial class Endgame
{
    public double cure { get; set; }
    public double? evacuation { get; set; }
    public string? won { get; set; }
    public bool helicopter { get; set; }
    public double escaped { get; set; }
}

public partial class Errand : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Errand v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public string settlementId { get; set; } = "";
    public string kind { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public List<string> zombieIds { get; set; } = new();
    public string who { get; set; } = "";
    public double until { get; set; }
}

public partial class Expedition : IHasId
{
    public string id { get; set; } = "";
    public List<Survivor> members { get; set; } = new();
    public ExpeditionTarget target { get; set; } = new();
    public Point home { get; set; } = new();
    public double returnTick { get; set; }
    public bool truck { get; set; }
    public bool outpost { get; set; }
    public string? truckId { get; set; }
    public double? leftTick { get; set; }
}

public partial class ExpeditionTarget
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(ExpeditionTarget v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public int x { get; set; }
    public int y { get; set; }
    public string label { get; set; } = "";
}

public partial class ExploreArea : IHasId
{
    public string id { get; set; } = "";
    public int x0 { get; set; }
    public int y0 { get; set; }
    public int x1 { get; set; }
    public int y1 { get; set; }
}

public partial class FarmPlot : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(FarmPlot v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    /// <summary>The end of the road.</summary>
    public string crop { get; set; } = "";
    public bool planted { get; set; }
    public double growth { get; set; }
}

public partial class Field
{
    public string key { get; set; } = "";
    public Int32Array dist { get; set; } = null!;
    public HordeBox box { get; set; } = new();
}

public partial class Fire
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Fire v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public int x { get; set; }
    public int y { get; set; }
    public double left { get; set; }
}

public partial class Furniture : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Furniture v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public string kind { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public string? occupant { get; set; }
    public string? owner { get; set; }
    /// <summary>Rabbits, wolves, bears: health left (deer go down to a lucky blow: HUNT_CHANCE).</summary>
    public double loot { get; set; }
    public bool scavenge { get; set; }
    public double stored { get; set; }
    public string? storedKind { get; set; }
    /// <summary>After a warning bite it keeps away from people until this tick.</summary>
    public string? look { get; set; }
    public string? table { get; set; }
}

public partial class GameEvent
{
    public double tick { get; set; }
    public string message { get; set; } = "";
}

public partial class Herd : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Herd v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public Point to { get; set; } = new();
    public double size { get; set; }
    public double nextStepTick { get; set; }
    public bool heard { get; set; }
}

public partial class Horde : IHasId
{
    public string id { get; set; } = "";
    public List<string> members { get; set; } = new();
    public string mode { get; set; } = "";
    /// <summary>Give a bed to a survivor (their only one), or take it back with null.</summary>
    public List<Point> goals { get; set; } = new();
    public List<string> goalIds { get; set; } = new();
    public Point? heading { get; set; }
    public HordeBox box { get; set; } = new();
    public double refreshTick { get; set; }
    public double? goalTick { get; set; }
    public bool night { get; set; }
}

public partial class HordeBox
{
    public int x0 { get; set; }
    public int y0 { get; set; }
    public int x1 { get; set; }
    public int y1 { get; set; }
}

public partial class HouseOptions
{
    /// <summary>Most width and depth the lot allows (outer, garage included).</summary>
    public double maxW { get; set; }
    public double maxH { get; set; }
    /// <summary>A garage is allowed (town houses; not farmhouses or cabins).</summary>
    public bool garage { get; set; }
}

public partial class IdMap
{
    public JsMap<string, double> ids { get; set; } = new();
    public double covered { get; set; }
}

public partial class Item : IHasId
{
    public string id { get; set; } = "";
    public string kind { get; set; } = "";
    public Point? pos { get; set; }
    public string? carriedBy { get; set; }
    public string? reservedBy { get; set; }
    public double wear { get; set; }
    public string? tag { get; set; }
    public string? inside { get; set; }
}

/// <summary>Haul jobs come in two kinds: - deliver: `blueprintId` set; any free wood is chosen at claim time (`itemId`). - store:   `blueprintId` null; `itemId` is the loose item the job exists for, and `dest` (a free tile in a complete storage room) is chosen at claim time.</summary>
public partial class Job : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Job v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public string type { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public string? blueprintId { get; set; }
    public string? itemId { get; set; }
    public string? furnitureId { get; set; }
    public string? zombieId { get; set; }
    public string? plotId { get; set; }
    public string? patientId { get; set; }
    public string? areaId { get; set; }
    public string? animalId { get; set; }
    public Point? dest { get; set; }
    public double? edge { get; set; }
    public double progress { get; set; }
    public string? reservedBy { get; set; }
}

public partial class JobChoicePlan
{
    public List<Point> path { get; set; } = new();
    public string? itemId { get; set; }
    public Point? dest { get; set; }
}

/// <summary>A world's natural features and its plan (towns, roads, lots): what a chunk is drawn from.</summary>
public partial class Layout : Nature
{
    /// <summary>The world's plan, by region (WorldRegions: docs/worldplan-regions.md).</summary>
    public WorldRegions regions { get; set; } = null!;
}

public partial class LoadResult
{
    public bool ok { get; set; }
    public SimState? state { get; set; }
    public string? kind { get; set; }
    public string? error { get; set; }
}

public partial class LoggedCommand
{
    public int tick { get; set; }
    public Command command { get; set; } = new();
}

public partial class Lot
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Lot v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public double id { get; set; }
    public int x { get; set; }
    public int y { get; set; }
    public int w { get; set; }
    public int h { get; set; }
    public string facing { get; set; } = "";
    public string zone { get; set; } = "";
    public double? town { get; set; }
}

/// <summary>What drawing a lot needs to know about the world.</summary>
public partial class LotContext
{
    /// <summary>Deer leave after a while; the rest leave when nobody of yours is near (WILD_RADIUS).</summary>
    public double seed { get; set; }
    public Func<double, double, double> lootAt { get; set; } = null!;
    public Func<double, double, double> distance { get; set; } = null!;
    public double calm { get; set; }
}

public partial class LotLayout
{
    public List<Placement> buildings { get; set; } = new();
    public Func<double, double, string> yard { get; set; } = null!;
    public List<Point> trees { get; set; } = new();
    public List<LotLayoutDecorItem> decor { get; set; } = new();
}

public partial class LotLayoutDecorItem
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(LotLayoutDecorItem v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public int x { get; set; }
    public int y { get; set; }
    public string look { get; set; } = "";
}

public partial class MapNote : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(MapNote v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public string text { get; set; } = "";
}

public partial class Meal
{
    public string itemId { get; set; } = "";
    public double progress { get; set; }
    public Point? seat { get; set; }
}

public partial class Movement
{
    public Point @from { get; set; } = new();
    public Point to { get; set; } = new();
    public double progress { get; set; }
    public double duration { get; set; }
}

public partial class Nature
{
    public double seed { get; set; }
    /// <summary>Where it's going: a settlement id, or 'you'.</summary>
    public double size { get; set; }
    public string map { get; set; } = "";
    public Point start { get; set; } = new();
    public bool riverAlongX { get; set; }
    public double riverBase { get; set; }
}

public partial class NewStateOptions
{
    public double seed { get; set; }
    public int width { get; set; }
    public int height { get; set; }
}

public partial class Offer
{
    public string give { get; set; } = "";
    public double giveCount { get; set; }
    public string want { get; set; } = "";
    public double wantCount { get; set; }
}

public partial class Order
{
    /// <summary>A point, where it has one (TypeScript narrows to the members that do).</summary>
    public static implicit operator Point(Order v) => v is null ? null! : new(v.x!.Value, v.y!.Value);
    public string kind { get; set; } = "";
    public int? x { get; set; }
    public int? y { get; set; }
    public string? crateId { get; set; }
    public double? progress { get; set; }
}

/// <summary>A building on a lot: its plan, its footprint's top-left corner on the map, which way it faces.</summary>
public partial class Placement
{
    public BuildingsPlan plan { get; set; } = new();
    public int ox { get; set; }
    public int oy { get; set; }
    public string facing { get; set; } = "";
    public bool mirror { get; set; }
}

public partial class PlanEdge
{
    public int ax { get; set; }
    public int ay { get; set; }
    public int bx { get; set; }
    public int by { get; set; }
    public bool door { get; set; }
    /// <summary>Glass in an outer wall (grid.ts EDGE_WINDOW).</summary>
    public bool? window { get; set; }
}

public partial class PlanFurniture
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(PlanFurniture v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public int x { get; set; }
    public int y { get; set; }
    public string look { get; set; } = "";
    /// <summary>Containers: what's in them. Decor: null.</summary>
    public string? table { get; set; }
}

public partial class PlannerPlan
{
    public List<Command> commands { get; set; } = new();
    /// <summary>Human-readable explanation: what was planned, or why nothing could be.</summary>
    public List<string> notes { get; set; } = new();
}

/// <summary>A marker for something found while exploring (shown on the minimap).</summary>
public partial class PointOfInterest : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(PointOfInterest v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    /// <summary>The end of the road.</summary>
    public string id { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public string label { get; set; } = "";
}

public partial class Prisoner : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Prisoner v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    /// <summary>Where to look, if anywhere.</summary>
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public string? @from { get; set; }
    public double since { get; set; }
}

public partial class Profession
{
    /// <summary>Starting skill levels.</summary>
    public JsRecord<double> skills { get; set; } = new();
    public List<string> kit { get; set; } = new();
    /// <summary>Containers: what's in them. Decor: null.</summary>
    public double points { get; set; }
    public string about { get; set; } = "";
}

/// <summary>Reachability labels, per grid and mover. Chunked: each REACH_CHUNK square is flooded on its own into local pieces, and pieces that touch across a chunk border are joined (a union-find over every piece on the map). When walls, doors, locks or edges change, only the chunks they're in are flooded again (grid.ts reachChangesSince: a log of changed chunks), then the borders are joined afresh. A new map chunk generated, or a wall built, re-floods a chunk or two, not the whole map (the stress test, 2026-11-06: on a 1024 map each kind of mover re-flooded a million tiles per change, the 30-60 ms spikes). With no log to go on (the first time, a loaded grid, or more changes than the log keeps) it floods every chunk. Doors: for movers that need a door open to get through it (you, the dead, crawlers: DOOR_MOVERS) the flood is done with every door shut, and the doors open now join the pieces either side (another union-find over the pieces' roots). Doors swing open and shut all the time without bumping the version (held open for someone passing: doors.ts), so only that join is redone. The answer is always the same as one flood of the whole map with the doors as they are (tests/reach.test.ts checks, with walls and doors built, broken and locked between questions).</summary>
public partial class Reach
{
    public double version { get; set; }
    public double cursor { get; set; }
    public double cw { get; set; }
    public double ch { get; set; }
    public Int32Array local { get; set; } = null!;
    public Int32Array counts { get; set; } = null!;
    public Int32Array offsets { get; set; } = null!;
    public List<List<double>> east { get; set; } = new();
    public List<List<double>> south { get; set; } = new();
    public Int32Array baseRoot { get; set; } = null!;
    public double count { get; set; }
    public string doors { get; set; } = "";
    public Int32Array root { get; set; } = null!;
    public JsMap<double, double> doorIds { get; set; } = new();
}

public partial class Recipe
{
    public string station { get; set; } = "";
    public string input { get; set; } = "";
    /// <summary>Containers: what's in them. Decor: null.</summary>
    public double count { get; set; }
    public string label { get; set; } = "";
    /// <summary>Where it's going: a settlement id, or 'you'.</summary>
    public bool? workshopOnly { get; set; }
    public string? needs { get; set; }
}

public partial class Requirement
{
    public string label { get; set; } = "";
    public bool met { get; set; }
}

public partial class Resident : Body, IHasId, IHasAt, IHasPos, IBody
{
    public string id { get; set; } = "";
    public string settlementId { get; set; } = "";
    public string name { get; set; } = "";
    public Point pos { get; set; } = new();
    public Point at { get; set; } = new();
    public Point was { get; set; } = new();
    public Movement? move { get; set; }
    public List<Point> path { get; set; } = new();
    public double health { get; set; }
    public double cooldown { get; set; }
    public double nextThinkTick { get; set; }
}

public partial class RngHolder : IRngHolder
{
    public double rng { get; set; }
}

public partial class RoadNode : IHasAt
{
    public Point at { get; set; } = new();
    public double? town { get; set; }
}

/// <summary>A point on a road's centre line, in tiles: fractional where the curve is smoothed (corner cutting). A Point's shape.</summary>
public partial class RoadPoint
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(RoadPoint v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public int x { get; set; }
    public int y { get; set; }
}

public partial class Room
{
    public double index { get; set; }
    public List<double> tiles { get; set; } = new();
    public List<Point> doors { get; set; } = new();
    public double minX { get; set; }
    public double minY { get; set; }
    public double maxX { get; set; }
    /// <summary>Glass in an outer wall (grid.ts EDGE_WINDOW).</summary>
    public double maxY { get; set; }
}

public partial class RoomDesignation : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(RoomDesignation v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public string purpose { get; set; } = "";
    public List<string>? refuses { get; set; }
}

public partial class RoomMap
{
    public Int32Array labels { get; set; } = null!;
    public List<Room> rooms { get; set; } = new();
}

public partial class RoomStatus
{
    public string purpose { get; set; } = "";
    public bool complete { get; set; }
    public List<Requirement> requirements { get; set; } = new();
    public double beds { get; set; }
    public double tables { get; set; }
    public double size { get; set; }
}

public partial class SaveFile
{
    public string format { get; set; } = "";
    public double schemaVersion { get; set; }
    public SimState state { get; set; } = new();
}

public partial class ScavRun : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(ScavRun v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public string @from { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public Point home { get; set; } = new();
    public Point ruin { get; set; } = new();
    public double people { get; set; }
    public string phase { get; set; } = "";
    public double untilTick { get; set; }
    public double nextStepTick { get; set; }
    public double food { get; set; }
    public bool met { get; set; }
}

public partial class Scenario
{
    public string? name { get; set; }
    public double seed { get; set; }
    public List<string> map { get; set; } = new();
    public List<ScenarioRoomsItem>? rooms { get; set; }
    public List<ScenarioEdgesItem>? edges { get; set; }
    public List<LoggedCommand>? commands { get; set; }
}

public partial class ScenarioEdgesItem
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(ScenarioEdgesItem v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public int x { get; set; }
    public int y { get; set; }
    public string side { get; set; } = "";
    public string kind { get; set; } = "";
}

public partial class ScenarioRoomsItem
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(ScenarioRoomsItem v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public int x { get; set; }
    public int y { get; set; }
    public string purpose { get; set; } = "";
}

public partial class Settings
{
    public string start { get; set; } = "";
    public double zombies { get; set; }
    public double weather { get; set; }
    public double progression { get; set; }
    public bool winter { get; set; }
    public double dayScale { get; set; }
    /// <summary>The end of the road.</summary>
    public string map { get; set; } = "";
}

public partial class Settlement : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Settlement v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public double reputation { get; set; }
    public double people { get; set; }
    public double food { get; set; }
    public double defence { get; set; }
    public JsRecord<double> ties { get; set; } = new();
    public bool unfolded { get; set; }
    public double lastTalk { get; set; }
    public double lastRaid { get; set; }
}

public partial class SimState
{
    public int schemaVersion { get; set; }
    public int tick { get; set; }
    public double rng { get; set; }
    public Grid grid { get; set; } = new();
    public int walkabilityVersion { get; set; }
    public List<Survivor> survivors { get; set; } = new();
    public List<Zombie> zombies { get; set; } = new();
    public List<Wanderer> wanderers { get; set; } = new();
    public JsRecord<double> craftTargets { get; set; } = new();
    public double? zombiesTonight { get; set; }
    public List<ExploreArea> exploreAreas { get; set; } = new();
    public List<DormantZombie> dormant { get; set; } = new();
    /// <summary>Wolves and bears: how hungry, 0 just fed.</summary>
    public List<Herd> herds { get; set; } = new();
    public List<PointOfInterest> pointsOfInterest { get; set; } = new();
    public WorldInfo? world { get; set; }
    public Timeline? timeline { get; set; }
    public List<Vehicle>? vehicles { get; set; }
    public List<MapNote>? mapNotes { get; set; }
    public List<Caravan>? caravans { get; set; }
    public List<Errand>? errands { get; set; }
    public List<Horde>? hordes { get; set; }
    public List<ScavRun>? runs { get; set; }
    public double? wildId { get; set; }
    public string weather { get; set; } = "";
    public List<Expedition> expeditions { get; set; } = new();
    public List<Trader> traders { get; set; } = new();
    public List<Fire> fires { get; set; } = new();
    public bool harsh { get; set; }
    public Settings settings { get; set; } = new();
    public List<string> knowledge { get; set; } = new();
    public List<Dog> dogs { get; set; } = new();
    public List<Settlement> settlements { get; set; } = new();
    public List<Resident> residents { get; set; } = new();
    public List<Animal> animals { get; set; } = new();
    public double lastRelief { get; set; }
    public List<Prisoner> prisoners { get; set; } = new();
    public List<SupplyRequest> requests { get; set; } = new();
    public Endgame endgame { get; set; } = new();
    public List<string> policies { get; set; } = new();
    public string? leaderId { get; set; }
    public Control? control { get; set; }
    public Succession? succession { get; set; }
    public List<Task> tasks { get; set; } = new();
    /// <summary>A prisoner: talk them round to join, or let them go home (prisoners.ts).</summary>
    public JsRecord<double> foraged { get; set; } = new();
    public List<Item> items { get; set; } = new();
    public List<Furniture> furniture { get; set; } = new();
    public List<FarmPlot> farm { get; set; } = new();
    public List<Blueprint> blueprints { get; set; } = new();
    public List<RoomDesignation> roomDesignations { get; set; } = new();
    public List<Job> jobs { get; set; } = new();
    public int nextId { get; set; }
    public int namesGiven { get; set; }
    public List<LoggedCommand> commandLog { get; set; } = new();
    public List<GameEvent> events { get; set; } = new();
    /// <summary>Snow on the ground (Snow.cs): null until the first flake falls, so a game without snow saves and loads as before.</summary>
    public SnowState? snow { get; set; }
}

/// <summary>Snow on the ground, a few numbers and a capped list of trodden tiles (Snow.cs). Never per tile.</summary>
public partial class SnowState
{
    /// <summary>Depth in cm on open ground, one per climate region: [cold, mild, hot].</summary>
    public List<double> depth { get; set; } = new();
    /// <summary>Snow that ever fell (cm), per region: a track's refill is what fell since it was trodden.</summary>
    public List<double> fallen { get; set; } = new();
    /// <summary>Snow that ever melted (cm), per region.</summary>
    public List<double> melted { get; set; } = new();
    /// <summary>Tiles someone walked or drove through: at most Snow.TRACK_CAP, the oldest dropped.</summary>
    public List<SnowTrack> tracks { get; set; } = new();
}

/// <summary>A trodden tile: depth d (cm) when last trodden, with the region's `fallen` (f) and `melted` (m) then.</summary>
public partial class SnowTrack
{
    public int x { get; set; }
    public int y { get; set; }
    /// <summary>Region: 0 cold, 1 mild, 2 hot.</summary>
    public int r { get; set; }
    public double d { get; set; }
    public double f { get; set; }
    public double m { get; set; }
    /// <summary>The tick it was last trodden.</summary>
    public int t { get; set; }
    /// <summary>How many times it has been trodden.</summary>
    public int n { get; set; }
    /// <summary>0 on foot; 1 + a car's heading for ruts.</summary>
    public int k { get; set; }
}

public partial class Sleep
{
    public string? bedId { get; set; }
    public bool asleep { get; set; }
}

/// <summary>Complete rooms that loose items get hauled to.</summary>
public partial class StoreRooms
{
    public JsSet<double> storage { get; set; } = new();
    public JsSet<double> armory { get; set; } = new();
    public JsSet<double> graveyard { get; set; } = new();
    /// <summary>Storage rooms that take each category (the stockpile filters).</summary>
    public JsRecord<JsSet<double>> storageFor { get; set; } = new();
}

public partial class Story
{
    /// <summary>Index into STORY_JOBS.</summary>
    public double job { get; set; }
    public double pride { get; set; }
    public double regret { get; set; }
    public StoryLost lost { get; set; } = new();
    public string? pet { get; set; }
    public double told { get; set; }
    public double nextTalk { get; set; }
    public List<string> asked { get; set; } = new();
    public double town { get; set; }
    public List<string> knew { get; set; } = new();
    public bool confessed { get; set; }
}

public partial class StoryLost
{
    public string name { get; set; } = "";
    public string relation { get; set; } = "";
    public bool dead { get; set; }
}

/// <summary>An axis-aligned street: the tiles from (x0, y0) to (x1, y1) and the row or column beside them (two wide).</summary>
public partial class Street
{
    public int x0 { get; set; }
    public int y0 { get; set; }
    public int x1 { get; set; }
    public int y1 { get; set; }
    public bool eastWest { get; set; }
    public bool main { get; set; }
    public double town { get; set; }
}

public partial class Succession
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Succession v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string name { get; set; } = "";
    public string cause { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
}

public partial class SupplyRequest : IHasId
{
    public string id { get; set; } = "";
    public string settlementId { get; set; } = "";
    public string kind { get; set; } = "";
    public double count { get; set; }
    public double until { get; set; }
}

public partial class Survivor : Body, IHasId, IHasAt, IHasPos, IBody
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public Point pos { get; set; } = new();
    public Point at { get; set; } = new();
    public Point was { get; set; } = new();
    public Movement? move { get; set; }
    public Point? goal { get; set; }
    public List<Point> path { get; set; } = new();
    public double pathVersion { get; set; }
    public string? note { get; set; }
    public JsRecord<double> priorities { get; set; } = new();
    public string? jobId { get; set; }
    public string? carrying { get; set; }
    public double nextSeekTick { get; set; }
    public double rest { get; set; }
    public Sleep? sleep { get; set; }
    /// <summary>No bite before this tick.</summary>
    public double satiety { get; set; }
    public Meal? meal { get; set; }
    public double nextMealTick { get; set; }
    public SurvivorForage? forage { get; set; }
    public double? nextForageTick { get; set; }
    public double health { get; set; }
    public string? lastHurtBy { get; set; }
    public double hurtTick { get; set; }
    public SurvivorTreatment? treatment { get; set; }
    public double nextTreatTick { get; set; }
    public string? weapon { get; set; }
    public string? arming { get; set; }
    public double nextArmTick { get; set; }
    public List<Thought> thoughts { get; set; } = new();
    public SurvivorLeisure? leisure { get; set; }
    public SurvivorBreakdown? breakdown { get; set; }
    public JsRecord<double> xp { get; set; } = new();
    public List<string> traits { get; set; } = new();
    public double? infection { get; set; }
    public JsRecord<double> opinions { get; set; } = new();
    public string? body { get; set; }
    /// <summary>Send survivors off the map to a point of interest (expeditions.ts).</summary>
    public string? feet { get; set; }
    public string? dressing { get; set; }
    public double nextDressTick { get; set; }
    public double? sick { get; set; }
    public double hydration { get; set; }
    public Drink? drink { get; set; }
    public double nextDrinkTick { get; set; }
    public SurvivorReading? reading { get; set; }
    public double age { get; set; }
    public double? childUntil { get; set; }
    public string? cart { get; set; }
    public List<string> cartLoad { get; set; } = new();
    public List<string> pack { get; set; } = new();
    public string? tool { get; set; }
    public string shift { get; set; } = "";
    public double kills { get; set; }
    public double watchHours { get; set; }
    public Wound? wound { get; set; }
    public bool bleeding { get; set; }
    public double bleedUntil { get; set; }
    public string? bitten { get; set; }
    public Order? order { get; set; }
    public Story story { get; set; } = new();
    public double resolve { get; set; }
    public double social { get; set; }
    public double comfort { get; set; }
    public Wish? wish { get; set; }
    public double endurance { get; set; }
    public double panic { get; set; }
    public double bodyHeat { get; set; }
    public double wetness { get; set; }
    /// <summary>Limbs gone for good, with side and level: "armLeft:elbow", "legRight:knee" (wounds.ts amputate).</summary>
    public List<string> lostLimbs { get; set; } = new();
    /// <summary>The tick of the last startle (a hit taken, the dead noticed out of calm), for the renderer's flinch.</summary>
    public double? startledAt { get; set; }
}

public partial class SurvivorBreakdown
{
    public double until { get; set; }
}

public partial class SurvivorForage
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(SurvivorForage v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public int x { get; set; }
    public int y { get; set; }
    public double progress { get; set; }
}

public partial class SurvivorLeisure
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(SurvivorLeisure v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public int x { get; set; }
    public int y { get; set; }
    public double progress { get; set; }
    public string at { get; set; } = "";
}

public partial class SurvivorReading
{
    /// <summary>A prisoner: talk them round to join, or let them go home (prisoners.ts).</summary>
    public string itemId { get; set; } = "";
    public double progress { get; set; }
}

public partial class SurvivorTreatment
{
    public string itemId { get; set; } = "";
    public double progress { get; set; }
}

public partial class Task : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Task v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public string giverId { get; set; } = "";
    public string kind { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public string? targetId { get; set; }
}

public partial class Thought
{
    public string kind { get; set; } = "";
    public double until { get; set; }
}

public partial class Timeline
{
    public double waterOffDay { get; set; }
    public double powerOffDay { get; set; }
    public double helicopterDay { get; set; }
    public TimelineHeli? heli { get; set; }
    public List<TimelineAlarmsItem> alarms { get; set; } = new();
}

public partial class TimelineAlarmsItem
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(TimelineAlarmsItem v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public int x { get; set; }
    public int y { get; set; }
    public double untilTick { get; set; }
}

public partial class TimelineHeli
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(TimelineHeli v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public int x { get; set; }
    public int y { get; set; }
    public double leaveTick { get; set; }
    public Point exit { get; set; } = new();
}

public partial class Town
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Town v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public int x { get; set; }
    public int y { get; set; }
    public double radius { get; set; }
    public string kind { get; set; } = "";
    public bool eastWest { get; set; }
    /// <summary>A stable number for the town in its world (its region's index x 1000 + its place there): names are by it.</summary>
    public int index { get; set; }
}

public partial class Trader : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Trader v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public double leavesTick { get; set; }
    public List<Offer> offers { get; set; } = new();
    public string? @from { get; set; }
    /// <summary>An edge wall or door (grid.ts): on this tile's north or west side, not on the tile.</summary>
    public bool traded { get; set; }
}

public partial class VariantInfo
{
    public double healthPct { get; set; }
    public double ticksPerTile { get; set; }
    public double damage { get; set; }
    public double battering { get; set; }
    public double @from { get; set; }
    public double chance { get; set; }
}

public partial class Vehicle : IHasId, IHasAt, IHasPos, IBody
{
    public string id { get; set; } = "";
    public Point at { get; set; } = new();
    public Point was { get; set; } = new();
    public Point pos { get; set; } = new();
    public double heading { get; set; }
    public double speed { get; set; }
    public double fuel { get; set; }
    public double health { get; set; }
    public string? driverId { get; set; }
    public string? kind { get; set; }
    public bool bootOpen { get; set; }
    public bool hoodOpen { get; set; }
}

public partial class Wanderer : IHasId
{
    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>
    public static implicit operator Point(Wanderer v) => v is null ? null! : new(v.x, v.y); // (nothing stays nothing)
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public double leavesTick { get; set; }
    public List<string> traits { get; set; } = new();
}

public partial class Wish
{
    public string kind { get; set; } = "";
    public string? targetId { get; set; }
    public double since { get; set; }
    public double until { get; set; }
    public bool promised { get; set; }
}

/// <summary>What a generated world needs to make more of itself (null on hand-made maps).</summary>
public partial class WorldInfo
{
    public double seed { get; set; }
    public double size { get; set; }
    public double chunks { get; set; }
    public List<double> generated { get; set; } = new();
}

public partial class WorldPlan
{
    public Nature nature { get; set; } = new();
    public List<Town> towns { get; set; } = new();
    public List<Street> streets { get; set; } = new();
    /// <summary>Containers: what's in them. Decor: null.</summary>
    public List<List<PointD>> roads { get; set; } = new();
    public Uint8Array roadMask { get; set; } = null!;
    public List<Lot> lots { get; set; } = new();
    public Int32Array lotMask { get; set; } = null!;
    public List<List<double>> lotsByChunk { get; set; } = new();
    /// <summary>The window this plan covers (the whole map for a world of one region): the masks are its size, row by
    /// row from its corner, and lotsByChunk its chunks from (chunkX0, chunkY0), chunksW to a row.</summary>
    public Win window { get; set; }
    public int chunkX0 { get; set; }
    public int chunkY0 { get; set; }
    public int chunksW { get; set; }
}

public partial class Wound
{
    public string limb { get; set; } = "";
    public double until { get; set; }
}

public partial class Zombie : Body, IHasId, IHasAt, IHasPos, IBody
{
    public string id { get; set; } = "";
    public Point pos { get; set; } = new();
    public Point at { get; set; } = new();
    public Point was { get; set; } = new();
    public Movement? move { get; set; }
    public List<Point> path { get; set; } = new();
    public string? targetId { get; set; }
    public double health { get; set; }
    public double maxHealth { get; set; }
    public double cooldown { get; set; }
    public double nextThinkTick { get; set; }
    public string kind { get; set; } = "";
    public Point? heading { get; set; }
    public string variant { get; set; } = "";
    public bool screamed { get; set; }
    public double stuckUntil { get; set; }
    public double windup { get; set; }
    public double staggerUntil { get; set; }
    public double hurtTick { get; set; }
    public string? hordeId { get; set; }
}
