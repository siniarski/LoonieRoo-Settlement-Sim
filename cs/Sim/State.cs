// The sim state's hand-written parts (state.ts): the version, a new state, and the RNG it holds.
// Its fields are generated (State.g.cs) from the TypeScript types.
namespace LoonieRoo.Sim;

public partial class SimState : IRngHolder
{
    /// <summary>The RNG state lives in sim state as a plain number (rng.ts).</summary>
    double IRngHolder.rng
    {
        get => rng;
        set => rng = value;
    }
}

public static class States
{
    /// <summary>Bump when the shape of SimState changes (the TypeScript's SCHEMA_VERSION; they move together).</summary>
    public const int SCHEMA_VERSION = 71;

    /// <summary>Deep copy via JSON (cloneState). Doubles as a check that state is JSON-safe.</summary>
    public static SimState CloneState(SimState state) => StateJson.Clone(state);
}
