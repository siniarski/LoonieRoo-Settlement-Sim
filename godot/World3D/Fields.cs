// Farm plots in first person (2026-11-11): a tilled bed where nothing is planted, and the crop on its bed by stage
// (sprouting, growing, ripe) for potatoes, wheat and herbs (art/scripts/build_world.py: world.glb). Until now only
// the Colony tab drew them.
//
// Which plots: those within RANGE of you, gathered again when you move a way, when a plot is added or removed, or
// every couple of seconds (a crop's stage only changes over hours), not every frame (rule 14). Reads state only.

using System;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class Fields
{
    const float RANGE = 64;
    /// <summary>A crop is "growing" from this share of its time, "sprouting" before it.</summary>
    const double GROWING_FROM = 0.4;

    readonly ModelSet models;
    (int, int, int, int) key = (int.MinValue, 0, 0, 0);

    /// <summary>Plots drawn (the self-test).</summary>
    public int Count => models.Count;

    public Fields(Node3D parent) => models = new ModelSet(parent, "res://assets/world/world.glb");

    /// <summary>The model a plot is drawn with: the bare bed, or its crop at its stage.</summary>
    public static string ModelOf(FarmPlot plot)
    {
        if (!plot.planted) return "soil";
        int stage = Farming.isRipe(plot) ? 3 : plot.growth / Farming.growTicks(plot) >= GROWING_FROM ? 2 : 1;
        return $"{plot.crop}_{stage}";
    }

    public void Update(SimState state, Vector3 eye)
    {
        var now = ((int)Math.Floor(eye.X / 8), (int)Math.Floor(eye.Z / 8), state.farm.Count, state.tick / 20);
        if (now == key) return;
        key = now;
        models.Begin();
        foreach (var plot in state.farm)
        {
            float x = plot.x + 0.5f, z = plot.y + 0.5f;
            if (Math.Abs(x - eye.X) > RANGE || Math.Abs(z - eye.Z) > RANGE) continue;
            string model = ModelOf(plot);
            // A crop with no model of its own still shows its bed.
            models.Add(models.Has(model) ? model : "soil", new Transform3D(Basis.Identity, new Vector3(x, Terrain.Height(x, z) + 0.01f, z)));
        }
        models.End(ModelSet.Around(eye, RANGE + 16));
    }
}
