// The self-test's light steps (SelfTest.cs calls them): G switches a flashlight in your bag on and off (the camera's
// beam with it), Z flips the light switch of the room you stand in (a room walled in round you for the test).

using System;
using Task = System.Threading.Tasks.Task;
using Godot;
using LoonieRoo.Sim;
using SimLights = LoonieRoo.Sim.Lights;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task LightSteps()
    {
        var you = ControlMod.controlled(state)!;
        var torch = Sim.Items.spawnItem(state, SimLights.FLASHLIGHT, you.pos.x, you.pos.y);
        Inventory.stow(state, you, torch);
        int logged = state.commandLog.Count;
        await Tap(Godot.Key.G);
        await Frames(2);
        Check("G switches your flashlight on: a command, and a beam from the camera", torch.lit == true && state.commandLog.Count > logged && firstPerson.Lights.TorchOn,
            $"lit {torch.lit}, log {logged} -> {state.commandLog.Count}, beam {firstPerson.Lights.TorchOn}");
        await Tap(Godot.Key.G);
        await Frames(2);
        Check("G again switches it off", torch.lit == null && !firstPerson.Lights.TorchOn);
        Sim.Items.removeItem(state, torch);
        you.pack.Remove(torch.id);
        // A room round you (walls on the four edges of your tile: a room of one tile, as a player walls in).
        var g = state.grid;
        var p = you.pos;
        var was = new[] { Grids.edgeKind(g, Grids.edgeIndex(g, p.x, p.y, "n")), Grids.edgeKind(g, Grids.edgeIndex(g, p.x, p.y, "w")), Grids.edgeKind(g, Grids.edgeIndex(g, p.x, p.y + 1, "n")), Grids.edgeKind(g, Grids.edgeIndex(g, p.x + 1, p.y, "w")) };
        bool inRoom = SimLights.roomAt(state, p.x, p.y) != null;
        if (!inRoom)
        {
            Grids.setEdge(g, p.x, p.y, "n", Grids.EDGE_WALL); Grids.setEdge(g, p.x, p.y, "w", Grids.EDGE_WALL);
            Grids.setEdge(g, p.x, p.y + 1, "n", Grids.EDGE_WALL); Grids.setEdge(g, p.x + 1, p.y, "w", Grids.EDGE_WALL);
            state.walkabilityVersion++;
        }
        var room = SimLights.roomAt(state, p.x, p.y);
        bool before = room != null && SimLights.switchedOn(state, room);
        await Tap(Godot.Key.Z);
        await Frames(2);
        room = SimLights.roomAt(state, p.x, p.y);
        Check("Z flips the light switch of the room you're in", room != null && SimLights.switchedOn(state, room) != before, $"room {room?.key ?? "none"}, on {before} -> {(room != null && SimLights.switchedOn(state, room))}");
        await Tap(Godot.Key.Z);
        await Frames(2);
        if (!inRoom)
        {
            Grids.setEdge(g, p.x, p.y, "n", was[0]); Grids.setEdge(g, p.x, p.y, "w", was[1]);
            Grids.setEdge(g, p.x, p.y + 1, "n", was[2]); Grids.setEdge(g, p.x + 1, p.y, "w", was[3]);
            state.walkabilityVersion++;
        }
        await Frames(2);
    }
}
