// The people the sim keeps as a tile and little else, in first person (2026-11-11): wanderers waiting to be taken
// in, traders with their goods, prisoners, caravans on the road and neighbours' scavenging parties. Only the Colony
// tab drew them; walking up to a trader, you saw nobody.
//
// The sim has no body for any of them (a wanderer is x, y and a name; a caravan is a tile, a head count and a step
// every few ticks), so the view gives each a stand-in: a body People.cs can draw like a resident, placed here.
//   wanderer, prisoner   stands on its tile, facing a way picked by its id
//   trader               stands behind its goods laid out on a blanket (the stall model)
//   caravan              its handcart (the wagon model) and up to MAX_PARTY people walking behind it
//   scavenging run       up to MAX_PARTY people in a loose file
// A party follows its tile smoothly (the sim steps it a tile at a time) and walks the way it moves. All of that is
// the view's: nothing here is written to the sim, and the stand-ins are never in the state (rule 5).

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Game.Characters;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class Others
{
    /// <summary>One person to draw: an id of its own, a body where it stands, and how it moves.</summary>
    public readonly record struct StandIn(string id, Resident body, PoseIntent pose);

    const float RANGE = 60;
    /// <summary>People drawn for one caravan or run (the sim's count can be more).</summary>
    const int MAX_PARTY = 4;
    /// <summary>A party catches up with its tile at this rate (per second), up to a brisk walk (m/s); further than
    /// SNAP m off (it was out of range, or just arrived) it is put there.</summary>
    const float FOLLOW = 1.6f, PACE = 2.2f, SNAP = 6;

    sealed class Party
    {
        public Vector2 at;
        public float heading, speed;
        public bool seen;
    }

    readonly ModelSet props;
    readonly Dictionary<string, Party> parties = new();
    readonly Dictionary<string, Resident> bodies = new();
    readonly HashSet<string> live = new();

    /// <summary>Everyone to draw this frame (People.cs models the nearest, Bodies.cs the rest).</summary>
    public readonly List<StandIn> Shown = new();

    /// <summary>Handcarts and stalls drawn (the self-test).</summary>
    public int Props => props.Count;

    public Others(Node3D parent) => props = new ModelSet(parent, "res://assets/world/world.glb");

    public void Update(SimState state, Vector3 eye, double delta)
    {
        Shown.Clear();
        live.Clear();
        props.Begin();
        bool Near(double x, double y) => Math.Abs(x + 0.5 - eye.X) <= RANGE && Math.Abs(y + 0.5 - eye.Z) <= RANGE;

        foreach (var w in state.wanderers)
            if (Near(w.x, w.y)) Stand($"o:{w.id}", w.x + 0.5f, w.y + 0.5f, IdHash.Hash01(w.id, 21) * Math.Tau);
        foreach (var q in state.prisoners)
            if (Near(q.x, q.y)) Stand($"o:{q.id}", q.x + 0.5f, q.y + 0.5f, IdHash.Hash01(q.id, 21) * Math.Tau);
        foreach (var t in state.traders)
        {
            if (!Near(t.x, t.y)) continue;
            // The goods lie on the tile's south half; the trader stands behind them, facing over them.
            float x = t.x + 0.5f, z = t.y + 0.5f;
            props.Add("stall", new Transform3D(Basis.Identity, new Vector3(x, Terrain.Height(x, z + 0.15f) + 0.01f, z + 0.15f)));
            Stand($"o:{t.id}", x, z - 0.38f, Math.PI / 2);
        }
        foreach (var c in Caravans.caravansOf(state))
        {
            if (!Near(c.x, c.y)) continue;
            var p = Follow($"c:{c.id}", c.x + 0.5f, c.y + 0.5f, delta);
            props.Add("wagon", ModelSet.Facing(p.at.X, Terrain.Height(p.at.X, p.at.Y), p.at.Y, Math.Cos(p.heading), Math.Sin(p.heading)));
            // One pulls between the shafts, the rest walk behind the cart, two abreast.
            int n = Math.Min(MAX_PARTY, (int)c.people);
            for (int k = 0; k < n; k++)
                Walk($"o:{c.id}:{k}", p, k == 0 ? 1.25f : -1.5f - 0.9f * ((k - 1) / 2), k == 0 ? 0 : (k % 2 == 0 ? 0.32f : -0.32f));
        }
        foreach (var r in Runs.runsOf(state))
        {
            if (!Near(r.x, r.y)) continue;
            var p = Follow($"r:{r.id}", r.x + 0.5f, r.y + 0.5f, delta);
            int n = Math.Min(MAX_PARTY, (int)r.people);
            for (int k = 0; k < n; k++) Walk($"o:{r.id}:{k}", p, -0.85f * k, k % 2 == 0 ? 0.22f : -0.22f);
        }
        props.End(ModelSet.Around(eye, RANGE + 16));

        // Forget whoever wasn't here this frame, so one coming back in range is put down afresh.
        Forget(parties);
        Forget(bodies);
    }

    void Forget<T>(Dictionary<string, T> of)
    {
        List<string>? gone = null;
        foreach (var id in of.Keys)
            if (!live.Contains(id)) (gone ??= new()).Add(id);
        if (gone != null) foreach (var id in gone) of.Remove(id);
    }

    /// <summary>Someone standing still at a point (m), facing `heading` (radians: 0 east, pi/2 south).</summary>
    void Stand(string id, float x, float z, double heading) => Put(id, x, z, heading, 0);

    /// <summary>A member of a party: `ahead` m along its heading (behind it if negative), `aside` m to its right.</summary>
    void Walk(string id, Party p, float ahead, float aside)
    {
        float cos = Mathf.Cos(p.heading), sin = Mathf.Sin(p.heading);
        Put(id, p.at.X + cos * ahead - sin * aside, p.at.Y + sin * ahead + cos * aside, p.heading, p.speed);
    }

    void Put(string id, float x, float z, double heading, double speed)
    {
        live.Add(id);
        if (!bodies.TryGetValue(id, out var body)) bodies[id] = body = new Resident { id = id };
        // A body's place is in SUB units (256 a tile); `was` the same, so the tick's blend leaves it where it is.
        body.at = new Point((int)Math.Round(x * Motion.SUB), (int)Math.Round(z * Motion.SUB));
        body.was = body.at;
        body.pos = new Point((int)Math.Floor(x), (int)Math.Floor(z));
        Shown.Add(new StandIn(id, body, new PoseIntent { speed = speed, heading = heading }));
    }

    /// <summary>A party's smoothed place: it moves towards its tile's centre, and faces the way it moves.</summary>
    Party Follow(string id, float x, float z, double delta)
    {
        live.Add(id);
        var to = new Vector2(x, z);
        if (!parties.TryGetValue(id, out var p)) parties[id] = p = new Party();
        if (!p.seen || p.at.DistanceTo(to) > SNAP)
        {
            p.at = to;
            p.seen = true;
            p.heading = (float)(IdHash.Hash01(id, 22) * Math.Tau);
            p.speed = 0;
            return p;
        }
        var gap = to - p.at;
        float dist = gap.Length();
        float step = Math.Min(dist, Math.Min(PACE, dist * FOLLOW) * (float)delta);
        if (dist > 0.02f)
        {
            p.at += gap / dist * step;
            p.heading = Mathf.LerpAngle(p.heading, Mathf.Atan2(gap.Y, gap.X), (float)Math.Min(1, delta * 4));
        }
        p.speed = delta > 0 ? step / (float)delta : 0;
        if (p.speed < 0.15f) p.speed = 0;
        return p;
    }
}
