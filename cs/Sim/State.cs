// The sim state's hand-written part: the RNG it holds. Its fields are generated (State.g.cs) from the
// TypeScript types; createState and cloneState are translated from state.ts (Generated/States.g.cs).
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
