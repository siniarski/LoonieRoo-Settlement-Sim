// The world's one-off noises (2026-11-09), read from state by what changed since the last look:
//   gunshot  a survivor's gun lost wear (each round fired wears it by 1), or a turret's rounds went down;
//            yours from where you stand, anyone's from where they stand
//   glass    a window edge near you turned from glass to broken (looked at when walkabilityVersion moves)
//   far off  the sim's far gunshots and screams (TimelineMod.farNoiseAt for each tick run): a burst of
//            two to four shots, or a scream, 25-45 tiles off
// A fresh start (a load, time away) only takes stock: nothing plays for what happened meanwhile.

using System;
using System.Collections.Generic;
using LoonieRoo.Sim;
using LoonieRoo.Game.Render;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.Audio;

public sealed class WorldNoises
{
    /// <summary>Windows within this many tiles of you are watched for breaking.</summary>
    const int GLASS_RANGE = 40;
    /// <summary>The box of watched windows is taken again when you've moved this far (tiles) from where it was taken.</summary>
    const int GLASS_MOVE = 8;
    const float GUN_HEIGHT = 1.4f, GLASS_HEIGHT = 1.2f;

    readonly Sounds sounds;
    readonly Dictionary<string, (string item, double wear)> guns = new();
    readonly Dictionary<string, (double loot, double stored)> turrets = new();
    /// <summary>Intact windows in the watched box, by edge index; the version and the spot it was taken at.</summary>
    readonly HashSet<int> windows = new();
    int windowsVersion = -1, windowsX = int.MinValue, windowsY;
    /// <summary>Sounds to come (shots in a burst): when (ms), what, where.</summary>
    readonly List<(ulong at, string kind, P where, double pick)> later = new();

    public WorldNoises(Sounds sounds) { this.sounds = sounds; }

    public void Update(SimState state, double alpha, Survivor you, P me, bool fresh, int lastTick)
    {
        ulong now = Godot.Time.GetTicksMsec();
        if (fresh) later.Clear();
        for (int i = later.Count - 1; i >= 0; i--)
        {
            if (later[i].at > now) continue;
            var (_, kind, where, pick) = later[i];
            later.RemoveAt(i);
            sounds.Play(state, kind, null, where, kind == "gunshot" ? GUN_HEIGHT : 1.5f, me, pick);
        }
        Guns(state, alpha, me, fresh);
        Turrets(state, me, fresh);
        Glass(state, you, me, fresh);
        if (!fresh)
            for (int t = lastTick + 1; t <= state.tick; t++)
            {
                if (TimelineMod.farNoiseAt(state, t) is not var (at, shots)) continue;
                var where = new P(at.x + 0.5, at.y + 0.5);
                if (!shots) { sounds.Play(state, "scream", null, where, 1.5f, me, t); continue; }
                int n = 2 + (int)(Hash.hashRoll(t, 0x5407) * 3);
                ulong gap = 0;
                for (int k = 0; k < n; k++)
                {
                    later.Add((now + gap, "gunshot", where, t * 7 + k));
                    gap += (ulong)(250 + Hash.hashRoll(t, k, 0x5408) * 600);
                }
            }
    }

    /// <summary>Every survivor's gun, by the wear it had: less now is a round fired.</summary>
    void Guns(SimState state, double alpha, P me, bool fresh)
    {
        foreach (var s in state.survivors)
        {
            var gun = string.IsNullOrEmpty(s.weapon) ? null : Items.getItem(state, s.weapon!);
            if (gun == null || !Weapons.isGunKind(gun.kind) || Weapons.specOf(gun)?.cls == "bow") { guns.Remove(s.id); continue; }  // a crossbow's twang is the swing's whoosh (Yours.cs): no report
            if (!fresh && guns.TryGetValue(s.id, out var was) && was.item == gun.id && gun.wear < was.wear)
                sounds.Play(state, "gunshot", s.id, worldPos(s, alpha), GUN_HEIGHT, me, Hash.idNumber(s.id) + state.tick);
            guns[s.id] = (gun.id, gun.wear);
        }
    }

    /// <summary>Turrets: a round fired takes one off `loot`; the last one reloads from `stored`.</summary>
    void Turrets(SimState state, P me, bool fresh)
    {
        foreach (var f in Of(state))
        {
            if (f.kind != "turret") continue;
            if (!fresh && turrets.TryGetValue(f.id, out var was) && (f.loot < was.loot || f.stored < was.stored))
                sounds.Play(state, "gunshot", null, new P(f.x + 0.5, f.y + 0.5), GUN_HEIGHT, me, Hash.idNumber(f.id) + state.tick);
            turrets[f.id] = (f.loot, f.stored);
        }
    }

    /// <summary>Windows breaking near you. The intact ones in a box round you are noted; when walkabilityVersion moves
    /// (it does when glass goes), any of them now broken is glass breaking, at the middle of its edge.</summary>
    void Glass(SimState state, Survivor you, P me, bool fresh)
    {
        var grid = state.grid;
        bool moved = Math.Abs(you.pos.x - windowsX) > GLASS_MOVE || Math.Abs(you.pos.y - windowsY) > GLASS_MOVE;
        if (!fresh && !moved && state.walkabilityVersion == windowsVersion) return;
        if (!fresh && state.walkabilityVersion != windowsVersion)
            foreach (var e in windows)
                if (Grids.edgeKind(grid, e) == Grids.EDGE_WINDOW_BROKEN)
                {
                    int tile = e / 2, x = tile % grid.width, y = tile / grid.width;
                    var at = e % 2 == 0 ? new P(x + 0.5, y) : new P(x, y + 0.5);
                    sounds.Play(state, "glass", null, at, GLASS_HEIGHT, me, e + state.tick);
                }
        windows.Clear();
        int x0 = Math.Max(0, you.pos.x - GLASS_RANGE), x1 = Math.Min(grid.width - 1, you.pos.x + GLASS_RANGE);
        int y0 = Math.Max(0, you.pos.y - GLASS_RANGE), y1 = Math.Min(grid.height - 1, you.pos.y + GLASS_RANGE);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int e = (y * grid.width + x) * 2;
                if (grid.edges[e] == Grids.EDGE_WINDOW) windows.Add(e);
                if (grid.edges[e + 1] == Grids.EDGE_WINDOW) windows.Add(e + 1);
            }
        windowsVersion = state.walkabilityVersion;
        windowsX = you.pos.x;
        windowsY = you.pos.y;
    }

    // The turrets, found again only when the furniture list changes (a world has hundreds of thousands of trees:
    // scanning them all every frame cost ~8 ms on a 2048 world).
    List<Furniture> turretList = new();
    List<Furniture>? seenList;
    int seenCount = -1;

    List<Furniture> Of(SimState state)
    {
        if (!ReferenceEquals(seenList, state.furniture) || seenCount != state.furniture.Count)
        {
            seenList = state.furniture;
            seenCount = state.furniture.Count;
            turretList = state.furniture.FindAll((f) => f.kind == "turret");
        }
        return turretList;
    }
}
