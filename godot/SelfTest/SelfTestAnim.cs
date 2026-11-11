// The self-test's last steps (after the new games: earlier steps count ticks from each other), for how bodies are
// drawn in hand, in water and over cars (2026-11-12, log/2026-11-12-10-animation.md).

using System;
using System.Linq;
using LoonieRoo.Game.World3D;
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
    }
}
