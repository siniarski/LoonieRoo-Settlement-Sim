// Shapes the ported tests return from their set-up helpers (the TypeScript wrote them inline: { state, s, bed }), each
// used by one test file. They were in the sim's State folder, beside the state's own types, until 2026-11-11.
#nullable disable warnings
namespace LoonieRoo.Sim;

public partial class WalkToResult
{
    public double ticks { get; set; }
    public List<string> tiles { get; set; }
}

public partial class HungryResult
{
    public SimState state { get; set; }
    public Survivor s { get; set; }
}

public partial class TestBefore
{
    public string pos { get; set; }
    public bool moving { get; set; }
    public string needs { get; set; }
    public string? jobId { get; set; }
}

public partial class TestOnJob
{
    public string id { get; set; }
    public double since { get; set; }
}

public partial class WorldResult
{
    public SimState state { get; set; }
    public Survivor s { get; set; }
    public Settlement a { get; set; }
    public Settlement b { get; set; }
}

public partial class SetupResult
{
    public Grid grid { get; set; }
    public Point start { get; set; }
    public Point goal { get; set; }
    public List<string> rows { get; set; }
}

public partial class TownResult
{
    public SimState state { get; set; }
    public Survivor you { get; set; }
    public Settlement n { get; set; }
}

public partial class WithTruckResult
{
    public SimState state { get; set; }
    public Vehicle v { get; set; }
}

public partial class PlayedResult
{
    public SimState initial { get; set; }
    public SimState state { get; set; }
}

public partial class RemoveWallResult
{
    public double ticks { get; set; }
    public Survivor s { get; set; }
}

public partial class OnlyResult
{
    public SimState state { get; set; }
    public Func<Survivor> survivor { get; set; }
    public string id { get; set; }
}

public partial class FieldResult
{
    public SimState state { get; set; }
    public Survivor you { get; set; }
    public Survivor them { get; set; }
}

public partial class FieldResultShape37
{
    public SimState state { get; set; }
    public Survivor s { get; set; }
    public Vehicle car { get; set; }
}

public partial class RngHolder : IRngHolder
{
    public double rng { get; set; }
}
