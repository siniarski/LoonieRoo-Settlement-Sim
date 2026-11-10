// Building sites in first person (2026-11-11): what is planned and what is going up (state.blueprints), which only
// the Colony tab drew until now (art/scripts/build_world.py: world.glb).
//
//   a tile (furniture, a floor, a wall tile)   pegged out with stakes and tape; a floor shows its joists once work starts
//   an edge (a wall, a door, a barricade)      pegged out along the border; once work starts, a stud frame (a door's
//                                              has the doorway) rising with the work done
//   the material delivered                     a stack of planks on the tile
//
// Gathered again when you move a way, or when the blueprints or the work done on them change (a cheap sum over the
// list each frame), not rebuilt every frame (rule 14). Reads state only.

using System;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class Sites
{
    const float RANGE = 64;
    /// <summary>A frame's height when the work has only begun, as a share of the full frame.</summary>
    const float FRAME_START = 0.3f;

    readonly ModelSet models;
    (int, int, int, double) key = (int.MinValue, 0, 0, 0);

    /// <summary>Pieces drawn (the self-test).</summary>
    public int Count => models.Count;

    public Sites(Node3D parent) => models = new ModelSet(parent, "res://assets/world/world.glb");

    public void Update(SimState state, Vector3 eye)
    {
        double work = 0;
        foreach (var bp in state.blueprints) work += bp.workDone + (bp.delivered ? 0.5 : 0);
        var now = ((int)Math.Floor(eye.X / 8), (int)Math.Floor(eye.Z / 8), state.blueprints.Count, work);
        if (now == key) return;
        key = now;
        models.Begin();
        foreach (var bp in state.blueprints)
        {
            float cx = bp.x + 0.5f, cz = bp.y + 0.5f;
            if (Math.Abs(cx - eye.X) > RANGE || Math.Abs(cz - eye.Z) > RANGE) continue;
            float ground = Terrain.Height(cx, cz) + 0.01f;
            float done = bp.workRequired > 0 ? (float)Math.Clamp(bp.workDone / bp.workRequired, 0, 1) : 0;
            if (bp.side != null)
            {
                // The north border runs along X at the tile's top; the west border along Z at its left.
                bool north = bp.side == "n";
                var at = north ? new Vector3(cx, ground, bp.y) : new Vector3(bp.x, ground, cz);
                var turn = north ? Basis.Identity : new Basis(Vector3.Up, Mathf.Pi / 2);
                if (done <= 0) models.Add("site_edge", new Transform3D(turn, at));
                else models.Add(bp.target == "door" ? "frame_door" : "frame", new Transform3D(turn.Scaled(new Vector3(1, FRAME_START + (1 - FRAME_START) * done, 1)), at));
            }
            else if (bp.target == "floor" && done > 0) models.Add("joists", new Transform3D(Basis.Identity, new Vector3(cx, ground, cz)));
            else models.Add("site", new Transform3D(Basis.Identity, new Vector3(cx, ground, cz)));
            if (bp.delivered && done < 1) models.Add("planks", new Transform3D(new Basis(Vector3.Up, 0.5f), new Vector3(cx + 0.12f, ground, cz + 0.1f)));
        }
        models.End(ModelSet.Around(eye, RANGE + 16));
    }
}
