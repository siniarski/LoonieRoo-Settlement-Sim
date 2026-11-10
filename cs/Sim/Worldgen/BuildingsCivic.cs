// Civic and working buildings (docs/buildings.md): police stations, clinics, schools and office blocks round a corridor
// from a front lobby, a church (porch, nave, vestry), warehouses and factories (a hall behind loading doors, an office
// in the corner), barns.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Buildings
{
    /// <summary>The rooms down each side of a civic building's corridor, from the back, in this order.</summary>
    public static readonly JsRecord<List<string>> ROLES = new()
    {
        ["police"] = ["desks", "lockers", "cells", "armoury", "toilets", "desks"],
        ["clinic"] = ["ward", "stores", "exam", "exam", "toilets", "desks", "ward"],
        ["school"] = ["classroom", "classroom", "toilets", "desks", "classroom", "stores", "classroom"],
        ["office"] = ["desks", "desks", "toilets", "stores", "desks", "desks"],
    };

    static readonly JsRecord<List<int>> DEPTHS = new()
    {
        ["classroom"] = [5, 6], ["ward"] = [4, 5], ["cells"] = [5, 6], ["desks"] = [3, 5], ["toilets"] = [3, 3], ["stores"] = [3, 4],
        ["lockers"] = [3, 3], ["armoury"] = [3, 3], ["exam"] = [3, 4],
    };

    /// <summary>A corridor up the middle from a lobby across the front (a school's corridor runs to its front door);
    /// rooms either side, each with a door onto it; the back door at the corridor's end.</summary>
    public static BuildingsPlan civicPlan(Func<double, double> r, string kind, double maxW, double maxH)
    {
        int W = Math.Min((int)maxW, (int)between(r(2), 16, 22)), H = Math.Min((int)maxH, (int)between(r(3), 13, 19));
        var d = new Draft(W, H, kind, kind == "police" || kind == "clinic" ? 3 : 2) { style = kind };
        string floor = kind == "clinic" ? "tiles" : "boards";
        string table = kind == "police" ? "police" : kind == "clinic" ? "clinic" : kind == "school" ? "school" : "office";
        int c0 = W / 2 - 1;
        int lb = kind == "school" ? 0 : 4;
        int lobby = lb > 0 ? d.add("lobby", floor, 0, H - lb, W - 1, H - 1) : -1;
        int corridor = d.add("corridor", kind == "clinic" ? "tiles" : "concrete", c0, 0, c0 + 1, H - 1 - lb);
        if (lobby >= 0)
        {
            d.frontDoor(lobby, 0.5, 2);
            d.join(corridor, lobby, 0.5, -1, 2);
        }
        else d.frontDoor(corridor, 0.5, 2);
        d.join(corridor, -1, 0.5, 0);
        var roles = ROLES[kind];
        int n = 0;
        var fits = new List<(int room, string role)>();
        foreach (var (x0, x1, left) in new[] { (0, c0 - 1, true), (c0 + 2, W - 1, false) })
        {
            int y = 0, end = H - 1 - lb;
            while (y <= end)
            {
                string role = roles[n % roles.Count];
                var dep = DEPTHS[role];
                int depth = (int)between(r(40 + n), dep[0], dep[1]);
                if (end - (y + depth - 1) < 3) depth = end - y + 1;
                int y1 = y + depth - 1;
                if (role == "cells" && depth >= 2 && x1 - x0 >= 3) cellBlock(d, x0, y, x1, y1, left, corridor);
                else
                {
                    int room = d.add(role == "desks" ? "office" : role == "toilets" ? "toilet" : role == "stores" ? "store" : role, role == "toilets" ? "tiles" : floor, x0, y, x1, y1);
                    d.join(room, corridor, r(50 + n));
                    fits.Add((room, role));
                }
                n++;
                y = y1 + 1;
            }
        }
        d.windows();
        if (lobby >= 0)
        {
            d.againstWall(lobby, "desk", table, r(60), (x, y) => y == H - lb && Math.Abs(x - c0) > 1);
            for (int i = 0; i < 2; i++) d.againstWall(lobby, "bench", null, r(61 + i));
        }
        int k = 70;
        foreach (var (room, role) in fits) { fitOut(d, room, role, table, r, k); k += 6; }
        return d.finish();
    }

    /// <summary>Police cells: a walkway beside the corridor, cells two deep off it, each with a cot and a toilet.</summary>
    static void cellBlock(Draft d, int x0, int y0, int x1, int y1, bool left, int corridor)
    {
        int wx = left ? x1 : x0;
        int walk = d.add("cells", "concrete", wx, y0, wx, y1);
        d.join(walk, corridor, 0.5);
        int cx0 = left ? x0 : x0 + 1, cx1 = left ? x1 - 1 : x1;
        for (int y = y0; y + 1 <= y1; y += 2)
        {
            int yb = y + 3 > y1 ? y1 : y + 1;
            int cell = d.add("cell", "concrete", cx0, y, cx1, yb);
            d.join(cell, walk, 0.5);
            d.againstWall(cell, "cot", null, 0.0);
            d.inCorner(cell, "toilet", null, 0.5);
            if (yb == y1) break;
        }
    }

    static void fitOut(Draft d, int room, string role, string table, Func<double, double> r, int k)
    {
        var cells = d.cellsOf(room);
        int x0 = cells.Min(c => c.x), x1 = cells.Max(c => c.x), y0 = cells.Min(c => c.y), y1 = cells.Max(c => c.y);
        switch (role)
        {
            case "desks":
                for (int y = y0 + 1; y < y1; y += 2) for (int x = x0 + 1; x < x1; x += 2) d.put(x, y, "desk", "office");
                d.againstWall(room, "cabinet", "office", r(k));
                d.againstWall(room, "bin", "trash", r(k + 1));
                break;
            case "lockers":
                d.run(room, Enumerable.Repeat(("locker", table), x1 - x0).ToList(), r(k), 0);
                break;
            case "armoury":
                d.run(room, Enumerable.Repeat(("locker", table), x1 - x0).ToList(), r(k), 0);
                d.run(room, Enumerable.Repeat(("locker", table), 2).ToList(), r(k + 1), 2);
                break;
            case "toilets":
                for (int x = x0; x <= x1; x += 2) d.put(x, y0, "toilet", null);
                d.againstWall(room, "sink", "bathroom", r(k));
                d.againstWall(room, "cabinet", "bathroom", r(k + 1));
                break;
            case "ward":
                for (int x = x0; x <= x1; x += 2) d.put(x, y0, "cot", null);
                if (y1 - y0 >= 3) for (int x = x0; x <= x1; x += 2) d.put(x, y1, "cot", null);
                d.againstWall(room, "cabinet", table, r(k));
                d.againstWall(room, "cabinet", table, r(k + 1));
                break;
            case "exam":
                d.bed(room, r(k), null);
                d.furniture[^1].look = d.furniture[^1].look == "bed" ? "cot" : d.furniture[^1].look;
                d.againstWall(room, "cabinet", table, r(k + 1));
                d.againstWall(room, "sink", table, r(k + 2));
                break;
            case "stores":
                d.run(room, new List<(string, string)> { ("cabinet", table), ("shelf", table), ("shelf", table) }, r(k), 0);
                break;
            case "classroom":
                // Pupils' desks in rows facing the front (the corridor side is the back), the teacher's desk ahead.
                // (Pupils' desks are tables: only the teacher's desk and the shelves hold anything.)
                for (int y = y0 + 1; y < y1 - 1; y += 2) for (int x = x0 + 1; x < x1; x += 2) d.put(x, y, "table", null);
                d.againstWall(room, "desk", "school", r(k), (x, y) => y == y1);
                d.againstWall(room, "shelf", "school", r(k + 1));
                d.againstWall(room, "shelf", "school", r(k + 2));
                break;
        }
    }

    /// <summary>A church: a porch room behind the double front door, the nave with two blocks of pews either side of
    /// the aisle facing the altar, a vestry in a back corner with its own door out.</summary>
    public static BuildingsPlan churchPlan(Func<double, double> r, double maxW, double maxH)
    {
        int W = Math.Min((int)maxW, (int)between(r(2), 11, 15)), H = Math.Min((int)maxH, (int)between(r(3), 15, 20));
        var d = new Draft(W, H, "church", 1) { style = "church" };
        int porch = d.add("lobby", "boards", 0, H - 3, W - 1, H - 1);
        int nave = d.add("nave", "boards", 0, 0, W - 1, H - 4);
        int vestry = d.add("vestry", "boards", 0, 0, 3, 3);
        d.frontDoor(porch, 0.5, 2);
        d.join(nave, porch, 0.5, -1, 2);
        d.join(vestry, nave, 0.5, 1);
        d.join(vestry, -1, 0.5, 0);
        d.windows();
        int mid = W / 2;
        d.put(mid, 1, "altar", null);
        for (int y = 5; y <= H - 5; y += 2)
            for (int x = 1; x < W - 1; x++)
                if (x < mid - 1 || x > mid) d.put(x, y, "pew", null);
        d.againstWall(vestry, "cabinet", "church", r(5));
        d.againstWall(vestry, "shelf", "church", r(6));
        d.againstWall(porch, "bench", null, r(7));
        return d.finish();
    }

    /// <summary>A warehouse (racking in rows with aisles a vehicle wide) or a factory (machines on the floor, stores
    /// along a wall), behind loading doors three tiles wide at the front and back; an office and a toilet in a front
    /// corner, the office with its own door and a window on the yard.</summary>
    public static BuildingsPlan warehousePlan(Func<double, double> r, string kind, double maxW, double maxH)
    {
        int W = Math.Min((int)maxW, (int)between(r(2), 16, 24)), H = Math.Min((int)maxH, (int)between(r(3), 12, 18));
        var d = new Draft(W, H, kind, 2) { style = kind };
        int hall = d.add("hall-big", "concrete", 0, 0, W - 1, H - 1);
        int office = d.add("office", "boards", 0, H - 4, 4, H - 1);
        int toilet = d.add("toilet", "tiles", 0, H - 6, 1, H - 5);
        d.frontDoor(office, 0.5);
        d.join(hall, -1, 0.6, 2, 3);
        if (W >= 20) d.join(hall, -1, 0.95, 2, 3);
        d.join(hall, -1, 0.3, 0, 3);
        d.join(office, hall, 0.5, 0);
        d.join(toilet, hall, 0.5);
        d.windows();
        d.againstWall(office, "desk", "office", r(5));
        d.againstWall(office, "cabinet", "office", r(6));
        d.inCorner(toilet, "toilet", null, r(7));
        // Rows along the depth, two tiles of aisle between, clear of the loading area at the front and a cross-aisle
        // at the back.
        int racks = 0, most = kind == "factory" ? 14 : 21;
        int mid = (H - 4) / 2 + 1;
        for (int x = 7; x < W - 2; x += 3)
            for (int y = 2; y < H - 4; y++)
            {
                if (kind == "factory") { if (y % 3 == 0) d.put(x, y, "machine", null); else if (y % 3 == 1 && racks < most && d.put(x, y, "shelf", kind)) racks++; }
                else if (y != mid && racks < most && d.put(x, y, r(10 + x * 31 + y) < 0.5 ? "pallet" : "shelf", kind)) racks++;
            }
        return d.finish();
    }

    /// <summary>A barn: wide doors front and back, hay along the walls, a feed room in a back corner (sacks, tools),
    /// sometimes a tractor in the middle.</summary>
    public static BuildingsPlan barnPlan(Func<double, double> r, double maxW, double maxH)
    {
        int W = Math.Min((int)maxW, (int)between(r(2), 9, 12)), H = Math.Min((int)maxH, (int)between(r(3), 8, 10));
        var d = new Draft(W, H, "barn", 1) { style = "barn" };
        int barn = d.add("barn", "concrete", 0, 0, W - 1, H - 1);
        int feed = d.add("store", "boards", 0, 0, 2, 2);
        d.frontDoor(barn, 0.5, 3);
        d.join(barn, -1, 0.6, 0, 2);
        d.join(feed, barn, 0.5);
        d.windows();
        for (int i = 0; i < 4; i++) d.againstWall(barn, "hay", null, r(10 + i), (x, y) => y > 2 || x > 3);
        d.againstWall(feed, "sack", "barn", r(20));
        d.againstWall(feed, "sack", "barn", r(21));
        d.againstWall(feed, "toolbox", "barn", r(30));
        if (r(31) < 0.5) d.inMiddle(barn, "tractor", null);
        return d.finish();
    }
}
