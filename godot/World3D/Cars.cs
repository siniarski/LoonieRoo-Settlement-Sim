// Cars you can drive, in first person (2026-11-11): the sedan and the pickup the furniture file already had for
// parked wrecks (art/scripts/build_furniture.py: car, car_v1, truck), where Bodies.cs drew a grey box.
//
// The size is the model's (4.2 m and 5 m long), and the sim's body is the same size (cs/Sim/Movement/VehicleBody.cs),
// so what the car hits is what you see. The car you are sitting in isn't drawn here: Cockpit.cs draws it from the
// inside. Reads state only.

using System;
using Godot;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.World3D;

public sealed class Cars
{
    const float RANGE = 96;

    readonly ModelSet models;

    /// <summary>Cars drawn (the self-test).</summary>
    public int Count => models.Count;

    public Cars(Node3D parent) => models = new ModelSet(parent, "res://assets/furniture/furniture.glb");

    /// <summary>The model a vehicle is drawn with: the pickup for a truck, else one of the sedans by its id.</summary>
    public static string ModelOf(Vehicle v) => v.kind == "truck" ? "truck" : IdHash.Hash01(v.id, 11) < 0.5 ? "car" : "car_v1";

    /// <summary>`mine`: the car not to draw (the one you sit in: Cockpit.cs draws that), or null.</summary>
    public void Update(SimState state, double alpha, Vector3 eye, Vehicle? mine)
    {
        models.Begin();
        foreach (var v in Vehicles.vehiclesOf(state))
        {
            if (v == mine) continue;
            var p = worldPos(v.at, v.was, alpha);
            if (Math.Abs(p.x - eye.X) > RANGE || Math.Abs(p.y - eye.Z) > RANGE) continue;
            var dir = Js.Get(Vehicles.HEADING_DIRS, v.heading)!;
            models.Add(ModelOf(v), ModelSet.Facing((float)p.x, Terrain.Height((float)p.x, (float)p.y), (float)p.y, dir[0], dir[1]));
        }
        models.End(ModelSet.Around(eye, RANGE + 16));
    }
}
