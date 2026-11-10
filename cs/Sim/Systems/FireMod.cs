// Ported from src/sim/fire.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Fire.
//
// A fire sits on a tile and burns for a while: FIRE_BURN ticks on anything that burns (walls, doors, barricades and
// all furniture are wood), less on bare ground. Rain and snow burn it down faster. While it burns it hurts anyone
// standing in it, survivor or zombie, and every SPREAD_EVERY ticks may catch on each burnable neighbour (a hash roll,
// not the sim RNG). When it burns out, what it was on is gone: a wall becomes floor, furniture and items are ash.
//
// Fires start from a stove now and then, from raiders' firebombs, from a heater, from lightning, on purpose (the Burn
// order), and (since 2026-11-10) from a spark off a fuelled brazier left standing out in dry weather. Survivors with
// firefighting on put them out (jobs.ts).
//
// WILDFIRE (2026-11-10). What a fire can burn is its FUEL (fuelAt), read from the tile each time, never stored:
//   Structure   a wall, door, barricade or piece of furniture (as before: the old rules, unchanged);
//   Tree        a tree (furniture of kind "tree": green wood, catches slowly, burns long);
//   Undergrowth forest ground with no tree on it (ferns and bushes: the game draws them, the sim has the ground);
//   Grass       open grass or a field.
// Roads, paving, dirt, bridges, indoor floors, water and farm plots don't burn, so a fire stops at them. A tile that
// has burnt keeps a scar (state.burnt, by "x,y", the tick and what it was) and bears no grass fire again until it has
// regrown (SCAR_DAYS). A vegetation fire catches a neighbour by a chance of what the neighbour is, what the fire is,
// the season and the weather (dryness): dry summer grass readily, a green tree slowly, wet or snowy ground barely.
// settings.wildfire scales it all (0 = vegetation never burns, as before; 1 = the default; 2 = twice the chance).
//
// BOUNDED. Fires are capped (MAX_FIRES); at most SPREAD_BUDGET spread checks run in a tick however many fires burn;
// fires nobody is near (no survivor within a chunk of them) don't spread, hurt or get stepped each tick: they burn
// down in batches every FAR_EVERY ticks, so a wildfire cannot run unseen across a region and costs nothing there.
// Survivors fight vegetation fires only where they threaten a building (worthFighting).
#nullable disable warnings
using System.Runtime.CompilerServices;
namespace LoonieRoo.Sim;

public static partial class FireMod
{

    public const int FIRE_BURN = 300;

    public const int GROUND_BURN = 100;

    public const int SPREAD_EVERY = 20;

    /// <summary>Vegetation looks for neighbours to catch every second (buildings: SPREAD_EVERY, as before): a grass fire is quick.</summary>
    public const int VEG_SPREAD_EVERY = 10;

    public const double SPREAD_CHANCE = 0.2;

    /// <summary>Health lost per tick standing in a fire.</summary>
    public const int FIRE_DAMAGE = 5;

    /// <summary>Ticks of work to put a fire out.</summary>
    public const int DOUSE_WORK = 30;

    /// <summary>Chance a hot meal sets the stove's tile alight.</summary>
    public const double STOVE_FIRE_CHANCE = 0.02;

    /// <summary>Chance a raider's blow on a wall or door is a firebomb instead.</summary>
    public const double FIREBOMB_CHANCE = 0.05;

    /// <summary>Safety cap (not a balance lever). Was 150 when only buildings burned; a wildfire in a wood needs room (a 38 x 38 stand), and every per-tick cost below is bounded by budgets, not by this.</summary>
    public const int MAX_FIRES = 1500;

    // ---- wildfire: what burns, for how long ----

    /// <summary>Ticks a grass fire burns (4 s): a flash that covers four spread checks, short enough to run past. (60 in the first cut: a front that long percolated across whole worlds in the balance harness.)</summary>
    public const int GRASS_BURN = 40;

    /// <summary>Ticks a fire in forest undergrowth burns: ferns and duff smoulder twice as long as grass (and a little under).</summary>
    public const int UNDERGROWTH_BURN = 90;

    /// <summary>A tree burns as long as any wood does (FIRE_BURN): a tree fire is the long, hot one.</summary>
    public const int TREE_BURN = FIRE_BURN;

    /// <summary>Health lost per tick standing in a burning grass or undergrowth tile: a flash fire burns less than a burning house (a survivor crossing one loses 10 to 20, not 100).</summary>
    public const int FLASH_DAMAGE = 3;

    /// <summary>Wet weather burns a vegetation fire down this much faster than a dry one (structures: 2, as before). Rain puts a grass fire out in about 10 ticks.</summary>
    public const int WET_VEG_RATE = 6;

    /// <summary>The chance, per spread check, that a fire catches a neighbour of this kind, on a day of dryness 1 (a summer heatwave) from a fire of intensity 1, checked every second. Grass: 0.5 (0.4 from a grass fire, whose heat is 0.8): a front runs about 0.4 tiles a second in a heatwave, and a tile that burns four checks passes it on four times in five. On a clear summer day the dryness is 0.4, so 0.16 a check: the edge of spreading. Undergrowth: 0.3, ferns and duff are damper. Tree: 0.15, and it needs dry weather twice over (dryness squared: green wood), so a wood burns in a heatwave and hardly otherwise (the old structure rule was 0.2 in every weather). Structure from vegetation: 0.02: a house in a burning yard catches in a heatwave, rarely on an ordinary summer day, never when it is wet; a wall that catches burns down the way walls always did.</summary>
    public const double CATCH_GRASS = 0.5, CATCH_UNDERGROWTH = 0.3, CATCH_TREE = 0.15, CATCH_STRUCTURE = 0.02;

    /// <summary>How hard a burning tile of each kind throws fire at its neighbours (1 = a burning house or tree). Grass is a low flame, undergrowth in between.</summary>
    public const double HEAT_GRASS = 0.8, HEAT_UNDERGROWTH = 0.9;

    /// <summary>No chance goes above this, however dry (a heatwave in a dry summer).</summary>
    public const double CATCH_MAX = 0.95;

    // ---- wildfire: bounds ----

    /// <summary>Most spread checks (one burning tile looking at its four neighbours) in one tick. A check is a few dictionary lookups (about 2 microseconds), so a tick costs at most 0.3 ms in checks. A tile skipped this tick is checked at its next turn: a big fire spreads a little slower, never costs more.</summary>
    public const int SPREAD_BUDGET = 150;

    /// <summary>Fires this many chunks from every survivor are "far": not stepped each tick (see FAR_EVERY). One chunk (32 tiles) each way is always generated (Worldgen.GENERATE_RADIUS is 2), so a near fire never spreads onto ungenerated ground.</summary>
    public const int NEAR_CHUNKS = 1;

    /// <summary>A far fire is aged in one batch every this many ticks (10 s): no spread, no damage, no per-tick work.</summary>
    public const int FAR_EVERY = 100;

    /// <summary>Survivors fight a vegetation fire within this many tiles of them, and only where it is within WATCH_RADIUS of a building (a wall, door, barricade or furniture that isn't a tree): the colonists defend the house, not the wood.</summary>
    public const int FIGHT_RADIUS = 12;
    public const int WATCH_RADIUS = 4;

    /// <summary>Most firefight jobs open for vegetation fires at once.</summary>
    public const int FIGHT_JOBS_MAX = 12;

    // ---- wildfire: scars ----

    /// <summary>Days a burnt tile bears no grass fire and shows its scar: about half a year (a year is 20 days), so the next spring's green-up. The hook below hands the tile to the foliage's regrowth sooner or later.</summary>
    public const int SCAR_DAYS = 10;

    /// <summary>Most scars kept (a 200 x 200 burn). At the cap grass stops catching: a hard end to a runaway fire and to the size of the save.</summary>
    public const int MAX_SCARS = 40000;

    /// <summary>What a tile was when it burnt (the scar's low two bits).</summary>
    public const int SCAR_GRASS = 0, SCAR_UNDERGROWTH = 1, SCAR_TREE = 2, SCAR_OTHER = 3;

    // ---- other causes ----

    /// <summary>Chance per in-game hour, at dryness 1 (a summer heatwave), that a fuelled brazier standing out of doors throws a spark on a neighbouring tile that can burn: times the dryness, about one day in ten in a heatwave, one in twenty on a clear summer day, almost never in a damp spring or in rain: a brazier left out through a dry summer is likely to start something eventually.</summary>
    public const double BRAZIER_SPARK_CHANCE = 0.005;

    /// <summary>Debug counters, never read by the sim: what the last updateFires did.</summary>
    public static int lastSpreadChecks, lastNearFires, lastFarFires;

    public enum Fuel { None, Structure, Tree, Undergrowth, Grass }

    // ---- derived data (outside state, rebuilt when stale) ----

    sealed class FireIndex
    {
        public readonly Dictionary<long, Fire> at = new();
        public int count;
        public readonly HashSet<long> near = new();
    }

    static readonly ConditionalWeakTable<List<Fire>, FireIndex> indexes = new();

    static long key(int x, int y) => ((long)y << 32) | (uint)x;

    /// <summary>The fires by tile. Valid while the list is the one indexed and has as many fires as it had; a list changed behind our back (a test pushing straight on) is re-indexed.</summary>
    static FireIndex index(SimState state)
    {
        var list = state.fires;
        if (!indexes.TryGetValue(list, out var ix))
        {
            ix = new FireIndex { count = -1 };
            indexes.Add(list, ix);
        }
        if (ix.count != list.Count)
        {
            ix.at.Clear();
            foreach (var f in list) ix.at.TryAdd(key(f.x, f.y), f);
            ix.count = list.Count;
        }
        return ix;
    }

    public static Fire? fireAt(SimState state, int x, int y)
    {
        return index(state).at.TryGetValue(key(x, y), out var f) ? f : null;
    }
    public static Fire? fireAt(SimState state, double x, double y) => fireAt(state, Js.I(x), Js.I(y));

    // ---- what a tile is made of ----

    /// <summary>The ground of a tile ("grass", "forest", "road"...), or null off the map. The one place fire reads the ground layer.</summary>
    static string groundAt(SimState state, int x, int y)
    {
        var grid = state.grid;
        if (!Grids.inBounds(grid, x, y)) return null;
        var g = grid.ground[Grids.tileIndex(grid, x, y)];
        return (g >= 0) && (g < Grids.GROUNDS.Length) ? Grids.GROUNDS[g] : null;
    }

    /// <summary>settings.wildfire: 0 vegetation never burns, 1 the default, 2 twice the chances. An older save has no such setting and reads 1.</summary>
    public static double wildfireScale(SimState state) => state.settings.wildfire;

    /// <summary>Is the tile's chunk generated (a hand-made map has no chunks: all of it is)?</summary>
    static bool isMade(SimState state, int x, int y)
    {
        var world = state.world;
        if (world == null) return true;
        var cx = x / Worldgen.CHUNK;
        var cy = y / Worldgen.CHUNK;
        return Js.Get(world.generated, ((double)cy * world.chunks) + cx) == 1;
    }

    /// <summary>What a fire on this tile would burn (None: nothing, so it burns as bare ground).</summary>
    public static Fuel fuelAt(SimState state, int x, int y)
    {
        var t = Grids.getTile(state.grid, x, y);
        if ((t == null) || (t == "water")) return Fuel.None;
        if (Defences.isReinforced(state, x, y)) return Fuel.None;
        if ((t == "wall") || (t == "door") || (t == "barricade")) return Fuel.Structure;
        if (t != "floor") return Fuel.None;
        var scale = wildfireScale(state);
        var furniture = FurnitureMod.furnitureAt(state, x, y);
        if (furniture != null) return ((furniture.kind == "tree") && (scale > 0)) ? Fuel.Tree : Fuel.Structure;
        if (scale <= 0) return Fuel.None;
        var ground = groundAt(state, x, y);
        Fuel fuel;
        if ((ground == "grass") || (ground == "field")) fuel = Fuel.Grass;
        else if (ground == "forest") fuel = Fuel.Undergrowth;
        else return Fuel.None;
        if (scarred(state, x, y) || Rooms.isIndoors(state, x, y)) return Fuel.None;
        foreach (var plot in state.farm) if ((plot.x == x) && (plot.y == y)) return Fuel.None;
        return fuel;
    }
    public static Fuel fuelAt(SimState state, double x, double y) => fuelAt(state, Js.I(x), Js.I(y));

    static bool isVegetation(Fuel f) => (f == Fuel.Grass) || (f == Fuel.Undergrowth) || (f == Fuel.Tree);

    /// <summary>Can a fire here keep going (and catch from a neighbour)? A wall, door, barricade, furniture or tree: the old meaning.</summary>
    public static bool burnable(SimState state, int x, int y)
    {
        var f = fuelAt(state, x, y);
        return (f == Fuel.Structure) || (f == Fuel.Tree);
    }
    public static bool burnable(SimState state, double x, double y) => burnable(state, Js.I(x), Js.I(y));

    /// <summary>Can a fire here burn at all: any fuel, vegetation included?</summary>
    public static bool flammable(SimState state, int x, int y) => fuelAt(state, x, y) != Fuel.None;

    public static int burnTicks(Fuel f) => f switch
    {
        Fuel.Structure => FIRE_BURN,
        Fuel.Tree => TREE_BURN,
        Fuel.Undergrowth => UNDERGROWTH_BURN,
        Fuel.Grass => GRASS_BURN,
        _ => GROUND_BURN,
    };

    static int damageOf(Fuel f) => (f == Fuel.Grass) || (f == Fuel.Undergrowth) ? FLASH_DAMAGE : FIRE_DAMAGE;

    /// <summary>Ticks of burning lost per tick: wet weather doubles it for buildings and bare ground (as before), and sextuples it for vegetation.</summary>
    static int burnRate(Fuel f, bool wet) => !wet ? 1 : (isVegetation(f) ? WET_VEG_RATE : 2);

    /// <summary>How dry the world is, 0 to about 1: the season times the weather times the hour. A summer heatwave day is about 1; a clear summer afternoon 0.4; autumn 0.3; a damp spring 0.1; winter 0.05; rain a tenth of a dry day, snow almost nothing. Night is cooler and damper (down to 0.4 of the day's). In a nuclear winter it is never summer. These are tuned against the balance harness (docs/log): with a grass fire's four checks a tile, a clear summer day is about the edge where a fire spreads (most die out as small burns, a few run), a heatwave is clearly past it, autumn and spring are clearly under.</summary>
    public static double dryness(SimState state)
    {
        var season = Seasons.seasonOf(state.tick);
        double s = season == "summer" ? 0.4 : season == "autumn" ? 0.3 : season == "spring" ? 0.1 : 0.05;
        if (state.settings.winter) s = JsMath.min(s, 0.05);
        double w = state.weather switch
        {
            "heatwave" => 2.2,
            "rain" => 0.1,
            "storm" => 0.15,
            "snow" => 0.02,
            "blizzard" => 0.0,
            _ => 1.0,
        };
        return s * w * (1 - (0.6 * Time.darkness(state.tick)));
    }

    // ---- scars ----

    static string scarKey(int x, int y) => $"{x},{y}";

    /// <summary>The scar on a tile, if it has one that hasn't regrown: its tick and kind (SCAR_*).</summary>
    public static bool scarOf(SimState state, int x, int y, out int tick, out int kind)
    {
        tick = 0;
        kind = 0;
        if (state.burnt.Count == 0) return false;
        if (!state.burnt.TryGetValue(scarKey(x, y), out var v)) return false;
        var value = (long)v;
        tick = (int)(value >> 2);
        kind = (int)(value & 3);
        return state.tick - tick < SCAR_DAYS * Time.DAY_LENGTH;
    }

    public static bool scarred(SimState state, int x, int y) => scarOf(state, x, y, out _, out _);

    static void scar(SimState state, int x, int y, int kind)
    {
        state.burnt[scarKey(x, y)] = (((long)state.tick) << 2) | (long)kind;
    }

    /// <summary>Forgets scars that have regrown (once a day). The order of the walk doesn't matter: it only collects keys to drop.</summary>
    static void pruneScars(SimState state)
    {
        var limit = SCAR_DAYS * Time.DAY_LENGTH;
        List<string> gone = null;
        foreach (var kv in state.burnt)
        {
            if (state.tick - (int)((long)kv.Value >> 2) >= limit) (gone ??= new()).Add(kv.Key);
        }
        if (gone == null) return;
        foreach (var k in gone) state.burnt.Remove(k);
    }

    // ---- lighting and putting out ----

    /// <summary>Sets a tile alight. Returns false if it can't burn (water, already alight, too many fires).</summary>
    public static bool ignite(SimState state, int x, int y)
    {
        if (!Grids.inBounds(state.grid, x, y) || (Grids.getTile(state.grid, x, y) == "water") || (state.fires.Count >= MAX_FIRES))
        {
            return false;
        }
        var ix = index(state);
        if (ix.at.ContainsKey(key(x, y)))
        {
            return false;
        }
        var fire = new Fire { x = x, y = y, left = burnTicks(fuelAt(state, x, y)) };
        state.fires.Add(fire);
        ix.at[key(x, y)] = fire;
        ix.count++;
        return true;
    }
    public static bool ignite(SimState state, double x, double y) => ignite(state, Js.I(x), Js.I(y));

    /// <summary>Takes a fire off the list and the index (nothing else).</summary>
    static void remove(SimState state, Fire f)
    {
        var ix = index(state);
        var i = state.fires.IndexOf(f);
        if (i >= 0)
        {
            state.fires.RemoveAt(i);
            ix.at.Remove(key(f.x, f.y));
            ix.count--;
        }
    }

    /// <summary>Puts a fire out. A vegetation fire leaves its scar (it had begun to burn), so the tile doesn't catch again at once.</summary>
    public static void douse(SimState state, Fire f)
    {
        var fuel = fuelAt(state, f.x, f.y);
        if (fuel == Fuel.Grass) scar(state, f.x, f.y, SCAR_GRASS);
        else if (fuel == Fuel.Undergrowth) scar(state, f.x, f.y, SCAR_UNDERGROWTH);
        else if (fuel == Fuel.Tree) scar(state, f.x, f.y, SCAR_GRASS);
        remove(state, f);
    }

    // ---- who stands in it ----

    static void hurt(SimState state, FireIndex ix)
    {
        foreach (var s in state.survivors)
        {
            if (!ix.at.TryGetValue(key(s.pos.x, s.pos.y), out var f)) continue;
            s.health = JsMath.max(0, s.health - damageOf(fuelAt(state, f.x, f.y)));
            s.lastHurtBy = "burns";
            s.hurtTick = state.tick;
        }
        List<Zombie> dead = null;
        foreach (var z in state.zombies)
        {
            if (!ix.at.TryGetValue(key(z.pos.x, z.pos.y), out var f)) continue;
            z.health = JsMath.max(0, z.health - damageOf(fuelAt(state, f.x, f.y)));
            if (z.health <= 0) (dead ??= new()).Add(z);
        }
        if (dead == null) return;
        foreach (var z in dead)
        {
            Zombies.removeZombie(state, z);
            Corpses.leaveCorpse(state, z.pos, null);
        }
    }

    // ---- near and far ----

    /// <summary>The chunks within NEAR_CHUNKS of a survivor, rebuilt every tick from where they stand (a pure function of the state, so a loaded save agrees with a continued run).</summary>
    static HashSet<long> nearChunks(SimState state, FireIndex ix)
    {
        var near = ix.near;
        near.Clear();
        foreach (var s in state.survivors)
        {
            int cx = s.pos.x / Worldgen.CHUNK, cy = s.pos.y / Worldgen.CHUNK;
            for (var dy = -NEAR_CHUNKS; dy <= NEAR_CHUNKS; dy++)
                for (var dx = -NEAR_CHUNKS; dx <= NEAR_CHUNKS; dx++) near.Add(key(cx + dx, cy + dy));
        }
        return near;
    }

    static bool isNear(HashSet<long> near, Fire f) => near.Contains(key(f.x / Worldgen.CHUNK, f.y / Worldgen.CHUNK));

    // ---- the tick ----

    public static void updateFires(SimState state)
    {
        if ((state.tick % Time.TICKS_PER_HOUR) == 0) sparks(state);
        if (((state.tick % Time.DAY_LENGTH) == 0) && (state.burnt.Count > 0)) pruneScars(state);
        var fires = state.fires;
        lastSpreadChecks = lastNearFires = lastFarFires = 0;
        if (fires.Count == 0)
        {
            return;
        }
        var ix = index(state);
        var near = nearChunks(state, ix);
        var wet = Seasons.isWet(state.weather);
        var dry = dryness(state);
        var scale = wildfireScale(state);
        hurt(state, ix);
        var snapshot = fires.ToArray();
        var n = snapshot.Length;
        // With more fires than the budget, start at a different place each tick so none is always last in line.
        var start = (n <= SPREAD_BUDGET) ? 0 : (int)((state.tick * 7919L) % n);
        var budget = SPREAD_BUDGET;
        var farTick = (state.tick % FAR_EVERY) == 0;
        var anyEnded = false;
        var vegCount = 0;
        for (var k = 0; k < n; k++)
        {
            var f = snapshot[(start + k) % n];
            if (!isNear(near, f))
            {
                // A far fire costs one set lookup a tick, and a batch every FAR_EVERY ticks.
                lastFarFires++;
                if (farTick)
                {
                    var farFuel = fuelAt(state, f.x, f.y);
                    f.left -= FAR_EVERY * burnRate(farFuel, wet);
                    if (f.left <= 0) { consume(state, f, farFuel); anyEnded = true; }
                }
                continue;
            }
            var fuel = fuelAt(state, f.x, f.y);
            lastNearFires++;
            if (isVegetation(fuel)) vegCount++;
            if ((fuel != Fuel.None) && (budget > 0) && due(state, f, fuel))
            {
                budget--;
                lastSpreadChecks++;
                spread(state, ix, f, fuel, dry, scale);
            }
            f.left -= burnRate(fuel, wet);
            if (f.left <= 0) { consume(state, f, fuel); anyEnded = true; }
        }
        if (anyEnded)
        {
            fires.RemoveAll((f) => f.left <= 0);
            ix.at.Clear();
            foreach (var f in fires) ix.at.TryAdd(key(f.x, f.y), f);
            ix.count = fires.Count;
        }
    }

    /// <summary>Buildings spread on one shared beat, as they always did; vegetation on a beat of its own per tile (every VEG_SPREAD_EVERY), so a big fire's checks spread over the ticks instead of all landing on one.</summary>
    static bool due(SimState state, Fire f, Fuel fuel)
    {
        if (fuel == Fuel.Structure) return (state.tick % SPREAD_EVERY) == 0;
        var beat = (((state.tick + (f.x * 7) + (f.y * 13)) % VEG_SPREAD_EVERY) + VEG_SPREAD_EVERY) % VEG_SPREAD_EVERY;
        return beat == 0;
    }

    static double heat(Fuel f) => f switch { Fuel.Grass => HEAT_GRASS, Fuel.Undergrowth => HEAT_UNDERGROWTH, _ => 1.0 };

    static double catchBase(Fuel f) => f switch { Fuel.Grass => CATCH_GRASS, Fuel.Undergrowth => CATCH_UNDERGROWTH, Fuel.Tree => CATCH_TREE, _ => CATCH_STRUCTURE };

    static readonly int[] DX = { 0, 1, 0, -1 };
    static readonly int[] DY = { -1, 0, 1, 0 };

    static void spread(SimState state, FireIndex ix, Fire f, Fuel src, double dry, double scale)
    {
        for (var d = 0; d < 4; d++)
        {
            int nx = f.x + DX[d], ny = f.y + DY[d];
            if (!Grids.inBounds(state.grid, nx, ny) || !isMade(state, nx, ny)) continue;
            if (ix.at.ContainsKey(key(nx, ny))) continue;
            var target = fuelAt(state, nx, ny);
            if (target == Fuel.None) continue;
            if (isVegetation(target) && (target != Fuel.Tree) && (state.burnt.Count >= MAX_SCARS)) continue;
            bool caught;
            if ((src == Fuel.Structure) && (target == Fuel.Structure))
            {
                // Building to building: the old rule, unchanged.
                var chance = (state.weather == "heatwave") ? ((double)(2) * (SPREAD_CHANCE)) : SPREAD_CHANCE;
                caught = Hash.hashRoll(nx, ny, state.tick) < chance;
            }
            else
            {
                var chance = JsMath.min(CATCH_MAX, catchBase(target) * heat(src) * ((target == Fuel.Tree) ? (dry * dry) : dry) * scale);
                caught = (chance > 0) && (Hash.hashRoll(nx, ny, f.x, f.y, state.tick) < chance);
            }
            if (caught && ignite(state, nx, ny))
            {
                if ((src == Fuel.Structure) && isVegetation(target) && (state.fires.Count <= 12))
                {
                    Events.logEvent(state, $"The fire at ({f.x}, {f.y}) has spread outdoors, to the {(target == Fuel.Tree ? "trees" : "grass")} at ({nx}, {ny})");
                }
            }
        }
    }

    /// <summary>A fuelled brazier standing out of doors throws a spark, now and then in dry weather, on a neighbour that can burn.</summary>
    static void sparks(SimState state)
    {
        var scale = wildfireScale(state);
        if (scale <= 0) return;
        var braziers = FurnitureMod.ofKind(state, "heater");
        if (braziers.Count == 0) return;
        var dry = dryness(state);
        if (dry <= 0) return;
        foreach (var h in braziers)
        {
            if ((h.stored <= 0) || Rooms.isIndoors(state, h.x, h.y)) continue;
            if (Hash.hashRoll(h.x, h.y, state.tick, 0xb7) >= JsMath.min(CATCH_MAX, BRAZIER_SPARK_CHANCE * dry * scale)) continue;
            var first = (int)JsMath.floor(Hash.hashRoll(h.x, h.y, state.tick, 0xb8) * 4);
            for (var i = 0; i < 4; i++)
            {
                int nx = h.x + DX[(first + i) % 4], ny = h.y + DY[(first + i) % 4];
                if (!Grids.inBounds(state.grid, nx, ny) || !isMade(state, nx, ny)) continue;
                var target = fuelAt(state, nx, ny);
                if (!isVegetation(target)) continue;
                if (ignite(state, nx, ny))
                {
                    Events.logEvent(state, $"A spark from the brazier at ({h.x}, {h.y}) set the {(target == Fuel.Tree ? "tree" : "grass")} at ({nx}, {ny}) alight!");
                    break;
                }
            }
        }
    }

    // ---- the end of a fire ----

    /// <summary>A fire on this tile has burnt itself out: what it was on is gone.</summary>
    public static void burnOut(SimState state, Fire f)
    {
        var fuel = fuelAt(state, f.x, f.y);
        remove(state, f);
        consume(state, f, fuel);
    }

    /// <summary>What burning out does to the tile (the fire itself leaves the list by the caller). `fuel` is what the tile was.</summary>
    static void consume(SimState state, Fire f, Fuel fuel)
    {
        var furniture = FurnitureMod.furnitureAt(state, f.x, f.y);
        var what = furniture?.kind ?? Grids.getTile(state.grid, f.x, f.y);
        if (furniture != null)
        {
            furniture.stored = 0;
            FurnitureMod.removeFurniture(state, furniture);
        }
        var t = Grids.getTile(state.grid, f.x, f.y);
        if (((t == "wall") || (t == "door")) || (t == "barricade"))
        {
            Grids.setTile(state.grid, f.x, f.y, "floor");
            state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
        }
        // Things lying on burning grass survive it; on a building's floor or bare ground they are ash, as before.
        if ((fuel != Fuel.Grass) && (fuel != Fuel.Undergrowth))
        {
            var item = Items.itemAt(state, f.x, f.y);
            if (item != null)
            {
                Items.removeItem(state, item);
            }
        }
        var ground = groundAt(state, f.x, f.y);
        scar(state, f.x, f.y, fuel switch
        {
            Fuel.Grass => SCAR_GRASS,
            Fuel.Undergrowth => SCAR_UNDERGROWTH,
            Fuel.Tree => SCAR_TREE,
            _ => SCAR_OTHER,
        });
        // HOOK (foliage regrowth): Growth.clear(state, f.x, f.y); here, for Grass, Undergrowth and Tree, once the sibling's call exists.
        if (what != "floor")
        {
            // A wood on fire would log a line for every tree: say so of lone trees only.
            if ((what != "tree") || (state.fires.Count <= 6))
            {
                Events.logEvent(state, $"A {Js.Str(what)} at ({Js.Str(f.x)}, {Js.Str(f.y)}) burned down");
            }
        }
    }

    // ---- firefighters ----

    /// <summary>Does a fire get a firefight job? Buildings, bare-ground fires and (with the world's wildfire off) trees: always, as before. A vegetation fire: only within FIGHT_RADIUS of a survivor and WATCH_RADIUS of a building, and while fewer than FIGHT_JOBS_MAX firefight jobs are open: the colonists defend the house, they don't walk into a burning wood.</summary>
    public static bool worthFighting(SimState state, Fire f, int openFightJobs)
    {
        var fuel = fuelAt(state, f.x, f.y);
        if (!isVegetation(fuel)) return true;
        if (openFightJobs >= FIGHT_JOBS_MAX) return false;
        var close = false;
        foreach (var s in state.survivors)
        {
            if (Space.manhattan(s.pos, f) <= FIGHT_RADIUS) { close = true; break; }
        }
        if (!close) return false;
        for (var dy = -WATCH_RADIUS; dy <= WATCH_RADIUS; dy++)
            for (var dx = -WATCH_RADIUS; dx <= WATCH_RADIUS; dx++)
            {
                if (JsMath.abs(dx) + JsMath.abs(dy) > WATCH_RADIUS) continue;
                int x = f.x + dx, y = f.y + dy;
                if (!Grids.inBounds(state.grid, x, y)) continue;
                var t = Grids.getTile(state.grid, x, y);
                if ((t == "wall") || (t == "door") || (t == "barricade")) return true;
                if (t == "floor")
                {
                    var furniture = FurnitureMod.furnitureAt(state, x, y);
                    if ((furniture != null) && (furniture.kind != "tree")) return true;
                }
            }
        return false;
    }
}
