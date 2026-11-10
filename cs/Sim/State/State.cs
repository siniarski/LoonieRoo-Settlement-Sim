// The sim state's hand-written part: the RNG it holds, and what is put right when a state is read. Its fields are
// in StateTypes.cs; createState is States.cs.
namespace LoonieRoo.Sim;

public partial class SimState : IRngHolder, System.Text.Json.Serialization.IJsonOnDeserialized
{
    /// <summary>A state just read (a save, a copy through JSON): what isn't saved because the rest says it is laid in
    /// again. The tiles under parked cars (VehicleParking.cs).</summary>
    void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized()
    {
        if (grid != null) Vehicles.remask(this);
    }

    /// <summary>The RNG state lives in sim state as a plain number (rng.ts).</summary>
    double IRngHolder.rng
    {
        get => rng;
        set => rng = value;
    }
}
