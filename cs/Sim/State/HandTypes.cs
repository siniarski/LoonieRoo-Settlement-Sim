// Types the TypeScript wrote inline in a union (a value or a reason string, say), which the translator can't
// name: written here by hand.
namespace LoonieRoo.Sim;

/// <summary>What working a tile would do (Handiwork.cs plan): repair it, or build a wall, barricade or reinforcement.</summary>
public sealed class WorkPlan
{
    public string what { get; set; } = "";
    public string? target { get; set; }
}

/// <summary>A piece of handiwork started (Handiwork.cs startWork): what, on which tile (and what), and how long.</summary>
public sealed class WorkStart
{
    public string what { get; set; } = "";
    public string targetId { get; set; } = "";
    public double duration { get; set; }
}

/// <summary>A town's centre and name (Mapnotes.cs townNames).</summary>
public sealed class TownName
{
    public int x { get; set; }
    public int y { get; set; }
    public string name { get; set; } = "";
}
