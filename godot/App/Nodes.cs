// Finding nodes of a type in a scene (an imported model's skeleton, its meshes, its animation player): depth first,
// a node before its children, so Find is the first FindAll would give. Used by the renderers and the labs
// (`using static LoonieRoo.Game.Nodes;`).

using System.Collections.Generic;
using Godot;

namespace LoonieRoo.Game;

public static class Nodes
{
    /// <summary>The first node of type T at or under `n`, or null.</summary>
    public static T? Find<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
            if (Find<T>(c) is T found) return found;
        return null;
    }

    /// <summary>Every node of type T at or under `n`.</summary>
    public static IEnumerable<T> FindAll<T>(Node n) where T : Node
    {
        if (n is T t) yield return t;
        foreach (var c in n.GetChildren())
            foreach (var x in FindAll<T>(c)) yield return x;
    }
}
