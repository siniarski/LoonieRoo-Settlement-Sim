// The self-test's fire steps (2026-11-10, wildfire; FireSteps is called from SelfTest.cs after the held items): a few
// burning tiles beside you are drawn (flames, smoke, embers), and hundreds of them still draw within the fixed budgets and
// the same few nodes (World3D/Fires.cs). Each puts back what it changed in state.

using System;
using Task = System.Threading.Tasks.Task;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task FireSteps()
    {
        var you = ControlMod.controlled(state)!;
        int nodes = fires.NodeCount;
        for (int i = 0; i < 6; i++) FireMod.ignite(state, you.pos.x + 4 + i, you.pos.y + 3);
        await Frames(3);
        Check("Burning tiles beside you are drawn as flames, smoke and embers", fires.FlameCount >= 6 && fires.SmokeCount > 0 && fires.EmberCount > 0,
            $"{state.fires.Count} fires, flames {fires.FlameCount}, smoke {fires.SmokeCount}, embers {fires.EmberCount}");
        for (int y = -22; y <= 22; y++)
            for (int x = -22; x <= 22; x++)
                if (Math.Abs(x) + Math.Abs(y) > 6) FireMod.ignite(state, you.pos.x + x, you.pos.y + y);
        await Frames(30);
        Check("Hundreds of burning tiles still draw within the budgets, in the same nodes",
            state.fires.Count > 500 && fires.FlameCount <= World3D.Fires.FLAMES && fires.SmokeCount <= World3D.Fires.SMOKE_SOURCES && fires.EmberCount <= World3D.Fires.EMBER_SOURCES && fires.NodeCount == nodes,
            $"{state.fires.Count} fires, flames {fires.FlameCount}/{World3D.Fires.FLAMES}, smoke {fires.SmokeCount}/{World3D.Fires.SMOKE_SOURCES}, embers {fires.EmberCount}/{World3D.Fires.EMBER_SOURCES}, nodes {nodes} -> {fires.NodeCount}, Update {fires.UpdateMs:0.00} ms a frame");
        state.fires.Clear();
        state.burnt.Clear();
        await Frames(30); // the scorch is rebuilt at most four times a second
        Check("With the fires out and the ground cleared nothing is drawn", fires.FlameCount == 0 && fires.ScorchTiles == 0, $"flames {fires.FlameCount}, scorch tiles {fires.ScorchTiles}");
    }
}
