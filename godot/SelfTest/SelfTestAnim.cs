// The self-test's last steps (after the new games: earlier steps count ticks from each other), for how bodies are
// drawn in hand, in water and over cars (2026-11-12, log/2026-11-12-10-animation.md).

using System;
using System.Linq;
using LoonieRoo.Game.World3D;
using LoonieRoo.Sim;
using Task = System.Threading.Tasks.Task;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task AnimSteps()
    {
        await Frames(1);
        // Grips (People.Hold.cs): each class's own grip point in the palm, the item pointing ahead of the fist (the
        // hand bone's +X, the thumb side) and down from it (towards the fingers, +Y), not straight along them.
        var kinds = new[] { "bat", "knife", "axe", "pistol", "rifle" };
        var grips = kinds.Select((k) =>
        {
            var g = People.GripFor(k);
            var palm = g * Held.GripOf(k);
            var along = g.Basis.X.Normalized();
            return (k, off: palm.DistanceTo(new Godot.Vector3(0, 0.085f, 0.035f)), along);
        }).ToList();
        Check("Others hold each class of weapon by its grip, pointing ahead of the fist and down",
            grips.All((x) => x.off < 0.002f && x.along.X > 0.5f && x.along.Y > 0.2f),
            string.Join(", ", grips.Select((x) => $"{x.k} off {x.off * 100:0.0} cm along ({x.along.X:0.00}, {x.along.Y:0.00})")));

        // Swimming (People.Swim.cs): deep water put round you (setup, put back after). In first person no body is
        // drawn (the stroke's hands are); watched from outside, it treads water at the surface, not on the bed.
        var you = ControlMod.controlled(state)!;
        var g = state.grid;
        var was = new System.Collections.Generic.List<(int x, int y, string tile)>();
        for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int x = you.pos.x + dx, y = you.pos.y + dy;
                if (Grids.getTile(g, x, y) is not string tile) continue;
                was.Add((x, y, tile));
                Grids.setTile(g, x, y, Grids.WATER);
            }
        await Frames(20);
        var ppl = firstPerson.People;
        bool inFirst = ppl.YourSwim == null && firstPerson.SwimHands.Shown;
        firstPerson.EyeAt = new Godot.Vector3(you.at.x / (float)Motion.SUB + 3, 2.5f, you.at.y / (float)Motion.SUB);
        await Frames(20);
        string? clip = ppl.YourSwim;
        string stance = PoseIntents.ofSurvivor(state, you).stance;
        bool car = Vehicles.vehicleOf(state, you) != null, swum = Swim.swimming(state, you);
        float over = ppl.YourOverWater;
        firstPerson.EyeAt = null;
        foreach (var (x, y, tile) in was) Grids.setTile(g, x, y, tile);
        await Frames(5);
        Check("Swimming, first person shows the stroke's hands and no body; from outside the body swims at the surface",
            inFirst && clip != null && over < -0.5f && over > -2f, $"first person: body {(inFirst ? "hidden" : "shown")}; outside: {clip ?? "no swim clip"}, root {over:0.00} m from the surface; stance {stance}, car {car}, swimming {swum}");
    }
}
