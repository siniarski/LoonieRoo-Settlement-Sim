// Ported from src/sim/rooms.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Rooms: flood fill over floor tiles, with walls and doors as boundaries.
// A region touching the map edge is outdoors, not a room.
//
// Rooms are derived from the grid, not stored: getRoomMap memoises per Grid
// and walkabilityVersion (every wall/door change bumps it), so they are
// recomputed only when walls or doors change.
//
// The player's room purposes are stored as designations anchored to a tile.
// A designation applies to whatever room contains its anchor, so purposes
// survive walls being moved around. If two designations end up in one room,
// the older one wins (reconcileRoomDesignations drops the newer).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Rooms
{

    public static readonly List<string> ROOM_PURPOSES = ["housing", "storage", "armory", "messhall", "recroom", "infirmary", "barracks", "workshop", "graveyard", "greenhouse", "cell"];

    public static readonly List<string> STORE_CATEGORIES = ["food", "materials", "medical", "gear"];

    public static string? categoryOf(string kind)
    {
        if ((kind == "food") || (kind == "meal"))
        {
            return "food";
        }
        if (((kind == "wood") || (kind == "cloth")) || (kind == "scrap"))
        {
            return "materials";
        }
        if ((kind == "bandage") || (kind == "medicine"))
        {
            return "medical";
        }
        return (kind == "corpse") ? null : "gear";
    }

    /// <summary>Label for floor tiles that are outside: see computeRooms.</summary>
    public const double OUTDOORS = -1;

    /// <summary>A region of floor bigger than this is outdoors, however it is bounded. With "touches water", this keeps "outdoors" from depending on the map edge, which a big world can't afford to flood-fill out to.</summary>
    public const int MAX_ROOM_TILES = 600;

    /// <summary>Label for walls and doors.</summary>
    public const double NOT_FLOOR = -2;

    public static readonly List<List<double>> DIRS = [[0, -1], [1, 0], [0, 1], [-1, 0]];

    // Per grid, and gone with it (a JsMap here kept every grid ever made alive, labels and all).
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Grid, CacheEntry> cache = new();

    /// <summary>Memoised per grid + walkabilityVersion. Derived data, outside sim state. When walls change, the rooms are updated round the changed tiles (updateRooms) if the grid knows which they were (Grid.cs takeChanges), else worked out in full. Either way the result is the same.</summary>
    public static RoomMap getRoomMap(SimState state)
    {
        cache.TryGetValue(state.grid, out var cached);
        if (Js.T(cached) && (cached.version == state.walkabilityVersion))
        {
            return cached.map;
        }
        var changed = Grids.takeChanges(state.grid);
        var map = (Js.T(Js.T(cached) && Js.T(changed))) ? updateRooms(state.grid, cached.map, changed.Select(v => (double)v).ToList()) : computeRooms(state.grid);
        cache.AddOrUpdate(state.grid, new() { version = state.walkabilityVersion, map = map });
        return map;
    }

    public static double labelAt(SimState state, int x, int y)
    {
        if ((((x < 0) || (y < 0)) || (x >= state.grid.width)) || (y >= state.grid.height))
        {
            return NOT_FLOOR;
        }
        return getRoomMap(state).labels.at(x, y);
    }
    public static double labelAt(SimState state, double x, double y) => labelAt(state, Js.I(x), Js.I(y));

    /// <summary>The enclosed room containing (x, y), or null for outdoors/walls/doors.</summary>
    public static Room? roomAt(SimState state, int x, int y)
    {
        var label = labelAt(state, x, y);
        return (label >= 0) ? Js.NN(Js.Get(getRoomMap(state).rooms, label)) : null;
    }
    public static Room? roomAt(SimState state, double x, double y) => roomAt(state, Js.I(x), Js.I(y));

    public static bool isIndoors(SimState state, int x, int y)
    {
        return labelAt(state, x, y) >= 0;
    }
    public static bool isIndoors(SimState state, double x, double y) => isIndoors(state, Js.I(x), Js.I(y));

    /// <summary>The designation that applies to a room (first in state order), if any.</summary>
    public static RoomDesignation? designationOf(SimState state, Room room)
    {
        return state.roomDesignations.find((d) => labelAt(state, d.x, d.y) == room.index);
    }

    /// <summary>Keeps anchors on floor and at most one designation per room. Runs every tick (cheap).</summary>
    public static void reconcileRoomDesignations(SimState state)
    {
        foreach (var d in Js.Iter(state.roomDesignations))
        {
            if (Grids.getTile(state.grid, d.x, d.y) == "floor")
            {
                continue;
            }
            var next = DIRS.map((__p1) =>
        {
            var (dx, dy) = __p1;
            return new Point { x = Js.I(d.x + dx), y = Js.I(d.y + dy) };
        }).find((p) => Grids.getTile(state.grid, p.x, p.y) == "floor");
            if (Js.T(next))
            {
                d.x = Js.I(next.x);
                d.y = Js.I(next.y);
            }
        }
        var claimed = new JsSet<double>();
        state.roomDesignations = state.roomDesignations.filter((d) =>
        {
            if (Grids.getTile(state.grid, d.x, d.y) != "floor")
            {
                return false;
            }
            var label = labelAt(state, d.x, d.y);
            if (label < 0)
            {
                return true;
            }
            if (claimed.has(label))
            {
                return false;
            }
            claimed.add(label);
            return true;
        });
    }

    /// <summary>Tiles of floor per bed for housing not to be cramped.</summary>
    public const int TILES_PER_BED = 4;

    public const int MIN_STORAGE_TILES = 4;

    /// <summary>Requirements for a purpose. `room` is null when the anchor is outdoors.</summary>
    public static RoomStatus evaluateRoom(SimState state, Room? room, string purpose)
    {
        var size = Js.Q(room?.tiles.length, 0);
        double count(string kind)
        {
            return Js.T(room) ? FurnitureMod.ofKind(state, kind).filter((f) => labelAt(state, f.x, f.y) == room.index).length : 0;
        }
        var beds = count("bed");
        var tables = count("table");
        List<Requirement> requirements = [new() { label = "enclosed by walls", met = room != null }, new() { label = "has a door", met = (Js.Q(room?.doors.length, 0)) > 0 }];
        if ((purpose == "housing") || (purpose == "barracks"))
        {
            requirements.push(new() { label = "at least 1 bed", met = beds >= 1 });
            requirements.push(new() { label = $"{Js.Str(TILES_PER_BED)} tiles per bed", met = (beds == 0) || (size >= ((double)(beds) * (TILES_PER_BED))) });
        }
        else if (purpose == "infirmary")
        {
            requirements.push(new() { label = "at least 1 bed", met = beds >= 1 });
        }
        else if (purpose == "workshop")
        {
            requirements.push(new() { label = "a workbench", met = count("workbench") >= 1 });
        }
        else if (purpose == "messhall")
        {
            requirements.push(new() { label = "at least 1 table", met = tables >= 1 });
        }
        else
        {
            requirements.push(new() { label = $"at least {Js.Str(MIN_STORAGE_TILES)} tiles", met = size >= MIN_STORAGE_TILES });
        }
        return new RoomStatus { purpose = purpose, complete = requirements.every((r) => r.met), requirements = requirements, beds = beds, tables = tables, size = size };
    }

    /// <summary>Status for a designation (finds its room).</summary>
    public static RoomStatus designationStatus(SimState state, RoomDesignation d)
    {
        return evaluateRoom(state, roomAt(state, d.x, d.y), d.purpose);
    }

    /// <summary>Room indices of complete rooms with this purpose.</summary>
    public static JsSet<double> completeRooms(SimState state, string purpose)
    {
        var result = new JsSet<double>();
        foreach (var d in Js.Iter(state.roomDesignations))
        {
            if (d.purpose != purpose)
            {
                continue;
            }
            var room = roomAt(state, d.x, d.y);
            if (Js.T(room) && evaluateRoom(state, room, purpose).complete)
            {
                result.add(room.index);
            }
        }
        return result;
    }

    public static StoreRooms completeStoreRooms(SimState state)
    {
        var storage = completeRooms(state, "storage");
        JsSet<double> refusing(string c)
        {
            return Js.Set(state.roomDesignations.filter((d) => Js.T((d.purpose == "storage") && Js.T(d.refuses?.includes(c)))).map((d) => labelAt(state, d.x, d.y)));
        }
        var storageFor = Js.AsRecord<JsSet<double>>(Js.Object_fromEntries(STORE_CATEGORIES.map((c) =>
        {
            var no = refusing(c);
            return (c, Js.Set(Js.ListOf(storage).filter((r) => !no.has(r))));
        })));
        return new StoreRooms { storage = storage, armory = completeRooms(state, "armory"), graveyard = completeRooms(state, "graveyard"), storageFor = storageFor };
    }

    /// <summary>Where an item belongs: the dead in a graveyard; weapons, guns and ammo in an armory if there is one, everything else (and those otherwise) in storage that takes it.</summary>
    public static JsSet<double> homeFor(StoreRooms rooms, string kind)
    {
        if (kind == "corpse")
        {
            return rooms.graveyard;
        }
        if (Weapons.isArmoryKind(kind) && (rooms.armory.size > 0))
        {
            return rooms.armory;
        }
        return Js.Get(rooms.storageFor, categoryOf(kind) ?? "gear");
    }
}
