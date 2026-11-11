// Ported from src/sim/furniture.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Furniture: things on floor tiles that don't change walkability.
// Survivors can walk over them; items can't be dropped on them.
//   bed    built by survivors; slept in (see Needs.cs)
//   crate  found in ruins; holds loot that survivors scavenge (see Jobs.cs)
//   table  built by survivors; makes a mess hall (see Mess.cs)
//   workbench  built by survivors; crafting happens here (see Crafting.cs)
//   stove  built by survivors; cooking happens here (see Crafting.cs)
//   trap   spikes: hurts a zombie that steps on it, TRAP_CHARGES times (see Zombies.cs)
//   tower  a watchtower: see further and shoot further from it (see Towers.cs)
//   well, collector  clean water (see Water.cs); a collector's loot is the water in it
//   generator, lamp, fridge  power (see Power.cs); a generator's stored is its fuel, its loot the burn timer
//   heater a brazier: warmth outdoors on cold nights, fuelled like a generator (Power.cs)
//   coop   chickens: lays food (Farming.cs)
//   turret, lure, pit  defences (Defences.cs); a turret's stored is ammo boxes, its loot the rounds left in one
//   truck  found out in a generated world; expeditions drive it (Outposts.cs)
//   lab, beacon  the endgame: research a cure, or call for evacuation (EndgameMod.cs)
//   radio  powered: hears distress calls and neighbours' asks (Radio.cs)
//   tree   marked like crates; chopping yields wood, and it grows back (TREE_WOOD)
//   decor  generated fittings (a sofa, a toilet, a pew, a wrecked car): `look` says which. Only for show.
// Generated containers are crates with a `look` (a kitchen counter, a wardrobe, a police locker)
// and a loot `table` (Loot.cs): what's in them depends on the room they're in.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class FurnitureMod
{

    /// <summary>Finds in a fresh crate. Each scavenge job yields one.</summary>
    public const int CRATE_LOOT = 5;

    /// <summary>Spikes left on a new trap.</summary>
    public const int TRAP_CHARGES = 3;

    /// <summary>Wood on a grown tree. A chopped tree grows one back every TREE_REGROW_TICKS (three days), up to this. Was 3 every 2 days: generated forests flooded the map with wood.</summary>
    public const int TREE_WOOD = 2;

    /// <summary>In slow ticks (Time.slowEvery): three days at any day length.</summary>
    public static readonly double TREE_REGROW_TICKS = ((double)(3) * (Time.BASE_TICKS_PER_HOUR * Time.HOURS_PER_DAY));

    /// <summary>Containers (crates) and decor in generated buildings (Buildings.cs).</summary>
    public static readonly List<string> CONTAINER_LOOKS = ["counter", "icebox", "shelf", "wardrobe", "dresser", "cabinet", "toolbox", "locker", "desk", "pallet", "sack", "crate", "sink", "bin", "nightstand", "bookcase"];

    public static readonly List<string> DECOR_LOOKS = ["sofa", "bed", "toilet", "bath", "table", "oven", "tv", "car", "pew", "altar", "hay", "tractor", "pump", "bench", "cot", "register", "machine", "silo", "shower", "armchair", "washer"];

    /// <summary>Furniture that isn't a searchable container but plainly holds things (2026-11-12: the player found
    /// washers, tables and tills with no inventory; an oven cooks and a bath is got into): generated as decor with nothing in it, opened like an
    /// emptied cupboard, yours to put things in.</summary>
    public static readonly List<string> HOLDING_LOOKS = ["washer", "table", "register", "altar", "hay", "machine"];

    /// <summary>Can things be put in and taken out of this piece? A crate (any container look), or decor that holds.</summary>
    public static bool holds(Furniture? f) => f != null && (f.kind == "crate" || (f.kind == "decor" && f.look != null && HOLDING_LOOKS.includes(f.look)));

    /// <summary>What each look is called (an icebox is a house's fridge; the fridge you build is furniture kind 'fridge').</summary>
    public static readonly JsRecord<string> LOOK_NAMES = new() { ["counter"] = "counter", ["icebox"] = "fridge", ["shelf"] = "shelf", ["wardrobe"] = "wardrobe", ["dresser"] = "dresser", ["cabinet"] = "cabinet", ["toolbox"] = "toolbox", ["locker"] = "locker", ["desk"] = "desk", ["pallet"] = "pallet", ["sack"] = "sacks", ["crate"] = "crate", ["sink"] = "sink", ["bin"] = "bin", ["nightstand"] = "nightstand", ["bookcase"] = "bookcase", ["sofa"] = "sofa", ["bed"] = "bed", ["toilet"] = "toilet", ["bath"] = "bath", ["table"] = "table", ["oven"] = "oven", ["tv"] = "television", ["car"] = "car", ["pew"] = "pew", ["altar"] = "altar", ["hay"] = "hay bales", ["tractor"] = "tractor", ["pump"] = "fuel pump", ["bench"] = "bench", ["cot"] = "cot", ["register"] = "till", ["machine"] = "machine", ["silo"] = "silo", ["shower"] = "shower", ["armchair"] = "armchair", ["washer"] = "washing machine" };

    /// <summary>Small containers hold fewer finds (Lots.cs caps a new one's loot at this).</summary>
    public static readonly JsRecord<double> LOOK_CAPACITY = new() { ["bin"] = 2, ["nightstand"] = 2, ["sink"] = 2, ["cabinet"] = 3, ["toolbox"] = 3 };

    /// <summary>A container's name from its look and what it holds: a medicine cabinet, a filing cabinet, a kitchen sink.</summary>
    public static string containerName(Furniture f)
    {
        var look = f.look ?? "crate";
        if ((look == "cabinet") && (((f.table == "bathroom") || (f.table == "clinic")) || (f.table == "pharmacy")))
        {
            return "medicine cabinet";
        }
        if ((look == "cabinet") && (((f.table == "office") || (f.table == "police")) || (f.table == "school")))
        {
            return "filing cabinet";
        }
        if (look == "sink")
        {
            return (f.table == "kitchen") ? "kitchen sink" : "bathroom sink";
        }
        if (look == "bin")
        {
            return "rubbish bin";
        }
        return Js.Get(LOOK_NAMES, look);
    }

    /// <summary>Furniture by tile ("x,y") and by id, derived and cached outside sim state. Furniture never moves, and only addFurniture/removeFurniture change the list, so they drop the cache. (A generated world has thousands of trees: scanning the list per tile was the cost.)</summary>
    /// <summary>Each furniture list's index, kept while the list is (a chunk's scratch list goes with it: Worldgen.makeChunk).</summary>
    public static readonly System.Runtime.CompilerServices.ConditionalWeakTable<List<Furniture>, IndexesEntry> indexes = new();

    static IndexesEntry? Ix(List<Furniture> list) => indexes.TryGetValue(list, out var e) ? e : null;

    public static IndexesEntry? index(SimState state)
    {
        var found = Ix(state.furniture);
        if (!Js.T(found))
        {
            found = new IndexesEntry { byTile = new JsMap<string, Furniture>(), byId = new JsMap<string, Furniture>(), byKind = new JsMap<string, List<Furniture>>(), marked = null };
            foreach (var f in Js.Iter(state.furniture))
            {
                found.seq[f] = found.nextSeq++;
                if (!found.byTile.has($"{Js.Str(f.x)},{Js.Str(f.y)}"))
                {
                    found.byTile.set($"{Js.Str(f.x)},{Js.Str(f.y)}", f);
                }
                found.byId.set(f.id, f);
                var same = found.byKind.get(f.kind);
                if (Js.T(same))
                {
                    same.push(f);
                }
                else
                {
                    found.byKind.set(f.kind, [f]);
                }
            }
            indexes.AddOrUpdate(state.furniture, found);
        }
        return found;
    }

    public static Furniture? furnitureAt(SimState state, int x, int y)
    {
        return index(state).byTile.get($"{Js.Str(x)},{Js.Str(y)}");
    }
    public static Furniture? furnitureAt(SimState state, double x, double y) => furnitureAt(state, Js.I(x), Js.I(y));

    /// <summary>Every piece of one kind, in list order (so results come out as a scan of the list would). Shared with the index, and kept up to date in place: read it, never change it, and don't add or remove furniture of that kind while looping over it. (A world has thousands of trees and fittings; scanning them all each tick for the one generator was a steady cost.)</summary>
    public static List<Furniture> ofKind(SimState state, string kind)
    {
        return index(state).byKind.get(kind) ?? NONE;
    }

    public static readonly List<Furniture> NONE = [];

    /// <summary>Everything marked for scavenging, in list order: worked out once and kept until a mark changes (setScavenge) or furniture comes or goes. (A 1024 world has 88,000 pieces; the job sweep scanned them all every tick for the few that are marked.) Read it, never change it.</summary>
    public static List<Furniture> markedForScavenge(SimState state)
    {
        var ix = index(state);
        ix.marked ??= state.furniture.filter((f) => f.scavenge);
        return ix.marked;
    }

    /// <summary>Marks (or unmarks) a piece for scavenging. Always this way, never f.scavenge = ..., or the marked list goes stale.</summary>
    public static void setScavenge(SimState state, Furniture f, bool on)
    {
        if (f.scavenge == on)
        {
            return;
        }
        f.scavenge = on;
        var ix = Ix(state.furniture);
        if (Js.T(ix))
        {
            ix.marked = null;
        }
    }

    /// <summary>Pieces of several kinds, in list order (furniture is only ever appended, so that's id order).</summary>
    public static List<Furniture> ofKinds(SimState state, List<string> kinds)
    {
        // List order: by when each was added (the index's count), not by id (generated ids come from their chunk).
        var ix = index(state);
        return kinds.flatMap((k) => ofKind(state, k)).sort((a, b) => ix.seq[a] - ix.seq[b]);
    }

    public static Furniture? getFurniture(SimState state, string id)
    {
        return index(state).byId.get(id);
    }

    public static Furniture addFurniture(SimState state, string kind, int x, int y)
    {
        Furniture f = new Furniture { id = $"f{Js.Str(state.nextId++)}", kind = kind, x = Js.I(x), y = Js.I(y), occupant = null, owner = null, loot = (kind == "crate") ? CRATE_LOOT : ((kind == "tree") ? TREE_WOOD : ((kind == "trap") ? TRAP_CHARGES : 0)), scavenge = false, stored = 0, storedKind = null, look = null, table = null };
        return adopt(state, f);
    }

    /// <summary>Puts a piece made elsewhere (a generated chunk's: Worldgen.makeChunk) at the end of the list, indexed.</summary>
    public static Furniture adopt(SimState state, Furniture f)
    {
        state.furniture.push(f);
        var ix = Ix(state.furniture);
        if (Js.T(ix))
        {
            ix.seq[f] = ix.nextSeq++;
            var key = $"{Js.Str(f.x)},{Js.Str(f.y)}";
            if (!ix.byTile.has(key))
            {
                ix.byTile.set(key, f);
            }
            ix.byId.set(f.id, f);
            var same = ix.byKind.get(f.kind);
            if (Js.T(same))
            {
                same.push(f);
            }
            else
            {
                ix.byKind.set(f.kind, [f]);
            }
        }
        return f;
    }
    public static Furniture addFurniture(SimState state, string kind, double x, double y) => addFurniture(state, kind, Js.I(x), Js.I(y));

    // Trees coming back (new ones from saplings, and wood on chopped ones) is Growth.cs's: Growth.update, Growth.regrowWood.

    public static void removeFurniture(SimState state, Furniture f)
    {
        var i = state.furniture.indexOf(f);
        if (i < 0)
        {
            return;
        }
        state.furniture.splice(i, 1);
        var ix = Ix(state.furniture);
        if (Js.T(ix))
        {
            var key = $"{Js.Str(f.x)},{Js.Str(f.y)}";
            if (ix.byTile.get(key) == f)
            {
                var next = state.furniture.find((g) => (g.x == f.x) && (g.y == f.y));
                if (Js.T(next))
                {
                    ix.byTile.set(key, next);
                }
                else
                {
                    ix.byTile.delete(key);
                }
            }
            ix.byId.delete(f.id);
            var same = ix.byKind.get(f.kind);
            if (Js.T(same))
            {
                var k = same.indexOf(f);
                if (k >= 0)
                {
                    same.splice(k, 1);
                }
            }
            if (Js.T(ix.marked))
            {
                ix.marked = ix.marked.filter((g) => g != f);
            }
        }
        for (var n = 0; n < f.stored; n++)
        {
            Items.spawnItem(state, f.storedKind ?? "wood", f.x, f.y);
        }
    }
}
