// Everything that moves near you in first person that isn't a character model (People.cs): animals,
// dogs, cars, things lying on the ground, and people past the models' budget or range, as
// placeholders (capsules and boxes) from a pool of nodes reused every frame.

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.World3D;

public sealed class Bodies
{
    readonly Node3D parent;
    readonly Dictionary<string, (Mesh mesh, Material material)> kinds = new();
    readonly Dictionary<string, List<MeshInstance3D>> pools = new();
    readonly Dictionary<string, int> used = new();

    public Bodies(Node3D parent)
    {
        this.parent = parent;
        var person = new CapsuleMesh { Radius = 0.25f, Height = 1.75f };
        Kind("survivor", person, new Color(0.62f, 0.56f, 0.46f));
        Kind("resident", person, new Color(0.48f, 0.5f, 0.44f));
        Kind("zombie", person, new Color(0.36f, 0.42f, 0.3f));
        Kind("raider", person, new Color(0.45f, 0.2f, 0.17f));
        Kind("animal", new BoxMesh { Size = new Vector3(0.35f, 0.7f, 1.1f) }, new Color(0.4f, 0.32f, 0.22f));
        Kind("dog", new BoxMesh { Size = new Vector3(0.25f, 0.5f, 0.75f) }, new Color(0.3f, 0.24f, 0.18f));
        // The sim's car is a 0.43 m circle: drawn a little bigger than that, not the lab's 4.2 m car.
        Kind("car", new BoxMesh { Size = new Vector3(1.0f, 1.3f, 1.9f) }, new Color(0.3f, 0.34f, 0.38f));
        Kind("item", new BoxMesh { Size = new Vector3(0.22f, 0.14f, 0.22f) }, new Color(0.6f, 0.6f, 0.55f));
    }

    void Kind(string name, Mesh mesh, Color colour)
    {
        kinds[name] = (mesh, new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.9f });
        pools[name] = new();
    }

    /// <summary>The next free node of a kind this frame.</summary>
    MeshInstance3D Next(string kind)
    {
        int n = used.GetValueOrDefault(kind);
        used[kind] = n + 1;
        var pool = pools[kind];
        if (n < pool.Count) { pool[n].Visible = true; return pool[n]; }
        var (mesh, material) = kinds[kind];
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
        parent.AddChild(node);
        pool.Add(node);
        return node;
    }

    public void Update(SimState state, double alpha, Vector3 eye, string youId, HashSet<string> modelled)
    {
        used.Clear();
        float range2 = 48 * 48;
        bool Near(double x, double z) => (x - eye.X) * (x - eye.X) + (z - eye.Z) * (z - eye.Z) <= range2;

        // People stand on the ground (a capsule's centre at half its height); asleep, they lie down.
        void Person(string kind, Body b, bool lying)
        {
            if (modelled.Contains(b is Survivor s ? s.id : b is Zombie z ? z.id : b is Resident r ? r.id : "")) return;
            var p = worldPos(b, alpha);
            if (!Near(p.x, p.y)) return;
            var n = Next(kind);
            n.Position = new Vector3((float)p.x, lying ? 0.3f : 0.875f, (float)p.y);
            n.Rotation = lying ? new Vector3(Mathf.Pi / 2, 0, 0) : Vector3.Zero;
        }
        foreach (var s in state.survivors)
            if (s.id != youId && Vehicles.vehicleOf(state, s) == null) Person("survivor", s, s.sleep?.asleep == true);
        foreach (var r in state.residents) Person("resident", r, false);
        foreach (var z in state.zombies) Person(z.kind == "raider" ? "raider" : "zombie", z, false);
        foreach (var a in state.animals)
        {
            var p = worldPos(a, alpha);
            if (!Near(p.x, p.y)) continue;
            Next("animal").Position = new Vector3((float)p.x, 0.35f, (float)p.y);
        }
        foreach (var d in state.dogs)
        {
            var p = worldPos(d, alpha);
            if (!Near(p.x, p.y)) continue;
            Next("dog").Position = new Vector3((float)p.x, 0.25f, (float)p.y);
        }
        foreach (var v in Vehicles.vehiclesOf(state))
        {
            var p = worldPos(v.at, v.was, alpha);
            if (!Near(p.x, p.y)) continue;
            var dir = Js.Get(Vehicles.HEADING_DIRS, v.heading)!;
            var n = Next("car");
            n.Position = new Vector3((float)p.x, 0.65f, (float)p.y);
            n.Basis = Basis.LookingAt(new Vector3((float)dir[0], 0, (float)dir[1]), Vector3.Up);
        }
        foreach (var i in state.items)
        {
            if (i.pos == null || i.carriedBy != null || i.inside != null) continue;
            float x = i.pos.x + 0.5f, z = i.pos.y + 0.5f;
            if (!Near(x, z)) continue;
            // Several on one tile spread out a little, by id.
            float j = (i.id.GetHashCode() & 0xff) / 255f - 0.5f;
            Next("item").Position = new Vector3(x + j * 0.5f, 0.07f, z - j * 0.4f);
        }
        // Hide what wasn't used this frame.
        foreach (var (kind, pool) in pools)
            for (int k = used.GetValueOrDefault(kind); k < pool.Count; k++) pool[k].Visible = false;
    }
}
