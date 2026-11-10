// Snow on the ground (2026-11-10): falls, lies, is trodden, melts. A few numbers and one capped list in state
// (`state.snow`, null until the first flake: SnowState in StateTypes.cs); nothing per tile, nothing swept over the map.
//
//   depth     cm of snow on open ground, one number per climate region [cold, mild, hot]. Hand-made maps are one
//             region, mild. It rises while the day's weather is snow (SNOW_FALL cm an hour) or a blizzard
//             (BLIZZARD_FALL) and the air is at or under SNOW_BELOW degrees (Weather.cs: so the hot south gets
//             none, and the mild middle only at night, but a blizzard is cold everywhere); it falls in thaw:
//             MELT cm per hour per degree above freezing, and rain on snow melts it RAIN_MELT times faster, plus
//             RAIN_BASE an hour. Never under 0, never over MAX_DEPTH. It steps STEPS_PER_HOUR times an hour, scaled
//             to the day length (Time.TICKS_PER_HOUR), with no RNG: the dawn roll in Seasons is untouched.
//   tiles     a tile's depth is the region's (depthAt): none indoors (any enclosed room: a roof), half under a tree
//             (the canopy holds and shades it: TREE_SHARE), and where someone has trodden, the lesser of the
//             region's and the track's.
//   tracks    a trodden tile: its depth when last trodden (d, cm), and what the region had fallen and melted then
//             (f, m), so the track's depth now is d + FILL times what has fallen since - what has melted since
//             (never over the region's: the snow beside it has caught up; never under 0: bare). A lane trodden
//             LANE_N times or more fills at LANE_FILL, slower. A step takes a tile down to PACK of its depth (never under
//             FLOOR), a car's wheels to RUT_PACK (k = 1 + heading: two ruts to draw). Recorded when a mover
//             enters a tile on a tick the region has snow, for movers within TRACK_RANGE tiles of a survivor
//             (the far dead, herds and neighbours leave none) and survivors not in a car. At most TRACK_CAP
//             tracks: past it the oldest TRIM (by tick, then y, x) go. Each hour, filled and melted-out tracks go.
//   walking   deep untrodden snow slows the step (walkPct, paced) from DEEP_FROM cm to a third off at DEEP_FULL
//             and more, if `slowsWalking`. A trodden path is shallow, so it does not. Applied to survivors and to
//             you (Survivors.stepPace, ControlMod.stepLength); the dead, animals and dogs are not slowed (an option).
#nullable disable warnings
using System.Runtime.CompilerServices;

namespace LoonieRoo.Sim;

public static class Snow
{
    public const double SNOW_FALL = 0.8;
    public const double BLIZZARD_FALL = 2.0;
    /// <summary>Snow falls only when the air is this cold or colder (degrees).</summary>
    public const double SNOW_BELOW = 1;
    public const double MAX_DEPTH = 80;
    /// <summary>cm an hour, per degree above freezing.</summary>
    public const double MELT = 0.25;
    public const double RAIN_MELT = 2.5;
    public const double RAIN_BASE = 0.3;
    public const int STEPS_PER_HOUR = 20;
    /// <summary>Depth is not "lying" under this (cm): not drawn, not trodden.</summary>
    public const double MIN_LYING = 1;
    public const double TREE_SHARE = 0.5;

    public const int TRACK_CAP = 2048;
    public const int TRIM = 256;
    public const double FLOOR = 0.5;
    public const double PACK = 0.65;
    public const double RUT_PACK = 0.35;
    public const int LANE_N = 6;
    /// <summary>A trodden hollow catches snow from its sides as well as the sky: it fills this many cm for each cm that falls, a lane (LANE_N treads) LANE_FILL.</summary>
    public const double FILL = 2;
    public const double LANE_FILL = 1.25;
    /// <summary>Movers further than this (tiles) from every survivor leave no tracks.</summary>
    public const int TRACK_RANGE = 40;

    public const double DEEP_FROM = 5;
    public const double DEEP_FULL = 35;
    public const double MAX_SLOW = 0.3;
    /// <summary>The walking effect, on or off (balance comparisons; not part of state).</summary>
    public static bool slowsWalking = true;

    /// <summary>0 cold, 1 mild, 2 hot.</summary>
    public static int regionOf(string climate) => climate == "cold" ? 0 : climate == "hot" ? 2 : 1;

    public static int regionAt(SimState state, int x, int y) => regionOf(Climate.climateAt(state, x, y));

    static bool falling(SimState state) => (state.weather == "snow") || (state.weather == "blizzard");

    static bool raining(SimState state) => (state.weather == "rain") || (state.weather == "storm");

    // ---- depth ----

    /// <summary>Each tick: the depth steps, and tiles are trodden. A handful of arithmetic on a snowless day.</summary>
    public static void tick(SimState state)
    {
        stepDepth(state);
        var sn = state.snow;
        if (sn == null)
        {
            return;
        }
        if (lying(sn))
        {
            trample(state, sn);
        }
        if ((state.tick % Time.TICKS_PER_HOUR) == 0)
        {
            tidy(state, sn);
        }
    }

    public static bool lying(SnowState sn) => (sn.depth[0] >= MIN_LYING) || (sn.depth[1] >= MIN_LYING) || (sn.depth[2] >= MIN_LYING);

    static SnowState make() => new() { depth = [0, 0, 0], fallen = [0, 0, 0], melted = [0, 0, 0] };

    static void stepDepth(SimState state)
    {
        var step = Math.Max(1, Time.TICKS_PER_HOUR / STEPS_PER_HOUR);
        if ((state.tick % step) != 0)
        {
            return;
        }
        var snowing = falling(state);
        var sn = state.snow;
        if ((sn == null) && !snowing)
        {
            return;
        }
        var dt = (double)step / Time.TICKS_PER_HOUR;
        var rain = raining(state);
        for (var r = (state.world == null) ? 1 : 0; r <= ((state.world == null) ? 1 : 2); r++)
        {
            var t = Weather.temperatureIn(state, Weather.CLIMATES[r]);
            var d = sn?.depth[r] ?? 0;
            var fall = (snowing && (t <= SNOW_BELOW)) ? (state.weather == "blizzard" ? BLIZZARD_FALL : SNOW_FALL) : 0;
            var melt = 0.0;
            if ((d > 0) && (t > Weather.FREEZING))
            {
                melt = t * MELT;
                if (rain)
                {
                    melt = (melt * RAIN_MELT) + RAIN_BASE;
                }
            }
            if ((fall == 0) && (melt == 0))
            {
                continue;
            }
            sn ??= (state.snow = make());
            var gain = Math.Min(fall * dt, MAX_DEPTH - d);
            var loss = Math.Min(melt * dt, d + gain);
            var nd = d + gain - loss;
            sn.fallen[r] += gain;
            sn.melted[r] += loss;
            sn.depth[r] = (nd < 0.01) ? 0 : nd;
        }
    }

    /// <summary>The region's depth on open ground here (cm).</summary>
    public static double openDepth(SimState state, int x, int y)
    {
        var sn = state.snow;
        return (sn == null) ? 0 : sn.depth[regionAt(state, x, y)];
    }

    /// <summary>The depth at a tile (cm): none indoors, half under a tree, the lesser of the region's and the track's where trodden. Under MIN_LYING it is 0.</summary>
    public static double depthAt(SimState state, int x, int y)
    {
        var sn = state.snow;
        if (sn == null)
        {
            return 0;
        }
        var d = sn.depth[regionAt(state, x, y)];
        if (d < MIN_LYING)
        {
            return 0;
        }
        if (Rooms.isIndoors(state, x, y))
        {
            return 0;
        }
        if (Trunks.treeAt(state, x, y))
        {
            d *= TREE_SHARE;
        }
        var tr = find(sn, x, y);
        if (tr != null)
        {
            d = Math.Min(d, trackDepth(sn, tr));
        }
        return d;
    }
    public static double depthAt(SimState state, double x, double y) => depthAt(state, Js.I(x), Js.I(y));

    // ---- tracks ----

    /// <summary>What a track's depth is now (cm): d, plus what has fallen since (FILL times, a lane LANE_FILL), minus what has melted since; not under 0.</summary>
    public static double trackDepth(SnowState sn, SnowTrack t)
    {
        var fill = (t.n >= LANE_N) ? LANE_FILL : FILL;
        return Math.Max(0, t.d + (fill * (sn.fallen[t.r] - t.f)) - (sn.melted[t.r] - t.m));
    }

    sealed class Index
    {
        public readonly Dictionary<long, SnowTrack> map = new();
    }

    static readonly ConditionalWeakTable<SnowState, Index> indexes = new();

    static long key(int x, int y) => ((long)y << 32) | (uint)x;

    /// <summary>The tracks by tile: derived, outside state; rebuilt if the list was changed behind its back (its count differs).</summary>
    static Index index(SnowState sn)
    {
        if (!indexes.TryGetValue(sn, out var ix) || (ix.map.Count != sn.tracks.Count))
        {
            ix = new Index();
            foreach (var t in sn.tracks)
            {
                ix.map[key(t.x, t.y)] = t;
            }
            indexes.AddOrUpdate(sn, ix);
        }
        return ix;
    }

    public static SnowTrack find(SnowState sn, int x, int y) => (sn.tracks.Count == 0) ? null : index(sn).map.GetValueOrDefault(key(x, y));

    /// <summary>Tread the tile (x, y): a foot (kind 0) or a car's wheels heading `kind - 1`. Does nothing indoors or where there is nothing to cut.</summary>
    public static void tread(SimState state, int x, int y, int kind)
    {
        var sn = state.snow;
        if ((sn == null) || (sn.depth[regionAt(state, x, y)] < MIN_LYING))
        {
            return;
        }
        var cur = depthAt(state, x, y);
        if (cur <= FLOOR)
        {
            return;
        }
        var r = regionAt(state, x, y);
        var d = Math.Round(Math.Max(kind > 0 ? 0.2 : FLOOR, cur * (kind > 0 ? RUT_PACK : PACK)), 2);
        var tr = find(sn, x, y);
        if (tr == null)
        {
            if (sn.tracks.Count >= TRACK_CAP)
            {
                trim(sn);
            }
            tr = new SnowTrack { x = x, y = y, r = r };
            sn.tracks.Add(tr);
            index(sn).map[key(x, y)] = tr;
        }
        tr.d = d;
        tr.f = sn.fallen[r];
        tr.m = sn.melted[r];
        tr.t = state.tick;
        tr.n += 1;
        tr.k = kind;
    }

    /// <summary>Drop the oldest TRIM tracks (by tick, then y, then x: the same on every run).</summary>
    static void trim(SnowState sn)
    {
        var gone = new HashSet<SnowTrack>(sn.tracks.OrderBy(t => t.t).ThenBy(t => t.y).ThenBy(t => t.x).Take(TRIM));
        sn.tracks.RemoveAll(gone.Contains);
        indexes.Remove(sn);
    }

    /// <summary>Hourly: tracks the snow beside has caught up with, or that melted out, go; and a state with no snow and no tracks is dropped.</summary>
    static void tidy(SimState state, SnowState sn)
    {
        if (sn.tracks.RemoveAll(t => trackDepth(sn, t) >= (sn.depth[t.r] - 0.2)) > 0)
        {
            indexes.Remove(sn);
        }
        if ((sn.tracks.Count == 0) && !lying(sn) && (sn.depth[0] == 0) && (sn.depth[1] == 0) && (sn.depth[2] == 0))
        {
            state.snow = null;
        }
    }

    /// <summary>Who leaves tracks: every mover whose tile changed this tick (its `was` to its `at`), near a survivor.</summary>
    static void trample(SimState state, SnowState sn)
    {
        foreach (var s in state.survivors)
        {
            if (stepped(s.was, s.at, out var to) && (Vehicles.vehicleOf(state, s) == null))
            {
                tread(state, to.x, to.y, 0);
            }
        }
        each(state, state.zombies);
        each(state, state.dogs);
        each(state, state.animals);
        each(state, state.residents);
        if (state.vehicles != null)
        {
            foreach (var v in state.vehicles)
            {
                if (stepped(v.was, v.at, out var to) && near(state, to))
                {
                    tread(state, to.x, to.y, 1 + (int)v.heading);
                }
            }
        }
    }

    static void each<T>(SimState state, List<T> list) where T : Body
    {
        foreach (var e in list)
        {
            if (stepped(e.was, e.at, out var to) && near(state, to))
            {
                tread(state, to.x, to.y, 0);
            }
        }
    }

    /// <summary>Did it cross into a neighbouring tile this tick? (A teleport, a spawn, an eviction is not a step.)</summary>
    static bool stepped(Point was, Point at, out Point to)
    {
        var a = Motion.tileOf(was);
        to = Motion.tileOf(at);
        return ((a.x != to.x) || (a.y != to.y)) && (Math.Abs(a.x - to.x) <= 1) && (Math.Abs(a.y - to.y) <= 1);
    }

    static bool near(SimState state, Point tile)
    {
        var range = (double)TRACK_RANGE;
        foreach (var s in state.survivors)
        {
            if ((Math.Abs(s.pos.x - tile.x) <= range) && (Math.Abs(s.pos.y - tile.y) <= range))
            {
                return true;
            }
        }
        return false;
    }

    // ---- walking ----

    /// <summary>Walking pace at a tile, percent (100 when the snow is shallow, trodden, or the effect is off).</summary>
    public static int walkPct(SimState state, int x, int y)
    {
        if (!slowsWalking || (state.snow == null))
        {
            return 100;
        }
        var d = depthAt(state, x, y);
        if (d <= DEEP_FROM)
        {
            return 100;
        }
        var tr = find(state.snow, x, y);
        if ((tr != null) && (trackDepth(state.snow, tr) < (0.85 * openDepth(state, x, y))))
        {
            return 100;  // trodden: packed, and no deeper than the hollow
        }
        return (int)Math.Round(100 * (1 - (MAX_SLOW * Math.Min(1, (d - DEEP_FROM) / (DEEP_FULL - DEEP_FROM)))));
    }

    /// <summary>A step's ticks, longer in deep snow: base / walkPct, never shorter than base.</summary>
    public static double paced(SimState state, Point next, double pace)
    {
        var pct = walkPct(state, next.x, next.y);
        return (pct == 100) ? pace : Math.Max(pace, JsMath.round(((double)pace * 100) / pct));
    }

    /// <summary>One line for debugging: the depths, the tracks.</summary>
    public static string describe(SimState state)
    {
        var sn = state.snow;
        return (sn == null) ? "no snow" : $"snow cm cold {sn.depth[0]:0.0} mild {sn.depth[1]:0.0} hot {sn.depth[2]:0.0}, {sn.tracks.Count} tracks";
    }
}
