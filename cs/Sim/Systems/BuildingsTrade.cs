// Trade on commercial lots (docs/buildings.md): shops (grocery, pharmacy, hardware, a petrol station's kiosk), a bar or
// a diner, a workshop, a small office. Each has a front of windows and a double door on the street, the trade's
// room behind, and back rooms (a storeroom with the back door to the yard, a toilet, a kitchen, an office).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Buildings
{
    public static BuildingsPlan shopPlan(Func<double, double> r, string kind, double maxW, double maxH)
    {
        switch (kind)
        {
            case "bar":
            case "diner": return barPlan(r, kind, (int)maxW, (int)maxH);
            case "workshop": return workshopPlan(r, (int)maxW, (int)maxH);
            case "office": return officePlan(r, (int)maxW, (int)maxH);
        }
        bool kiosk = kind == "station";
        int W = Math.Min((int)maxW, kiosk ? (int)between(r(2), 8, 10) : (int)between(r(2), 10, 13));
        int H = Math.Min((int)maxH, kiosk ? (int)between(r(3), 6, 7) : (int)between(r(3), 8, 11));
        int bs = kiosk ? 2 : 3;
        var d = new Draft(W, H, kind, 1) { style = kind };
        int shop = d.add("shop", "tiles", 0, bs, W - 1, H - 1);
        int store = d.add("store", "concrete", 0, 0, W - 3, bs - 1);
        int toilet = d.add("toilet", "tiles", W - 2, 0, W - 1, bs - 1);
        d.frontDoor(shop, 0.5, 2);
        d.join(store, shop, r(4) < 0.5 ? 0.2 : 0.8);
        d.join(store, -1, r(5), 0);
        if (!d.join(toilet, store, 0.5)) d.join(toilet, shop, 0.5);
        d.windows();
        // The till by the door; shelves along the side walls, aisles of shelving between, a cross-aisle at the back;
        // a grocery's fridges along the back wall, a pharmacy's cabinets behind its counter.
        d.againstWall(shop, "counter", kind, 0.0, (x, y) => y >= H - 3 && x >= W - 3);
        d.againstWall(shop, "register", null, 0.0, (x, y) => y >= H - 3 && x >= W - 4);
        if (kind == "grocery") for (int x = 1; x < W - 1; x += 4) d.put(x, bs, "icebox", kind);
        if (kind == "pharmacy") for (int x = 1; x < W - 1; x += 4) d.put(x, bs, "cabinet", kind);
        int top = kind == "grocery" || kind == "pharmacy" ? bs + 2 : bs + 1;
        int shelves = 0, most = kiosk ? 3 : 6;
        for (int y = top; y <= H - 3 && shelves < most; y++)
        {
            if (d.put(0, y, "shelf", kind)) shelves++;
            if (shelves < most && d.put(W - 1, y, "shelf", kind)) shelves++;
        }
        for (int x = 2; x <= W - 4 && !kiosk; x += 3)
            for (int y = top + 1; y <= H - 3 && shelves < most + 1; y++)
                if (d.put(x, y, kind == "hardware" && y % 2 == 0 ? "pallet" : "shelf", kind)) shelves++;
        for (int i = 0; i < (kiosk ? 1 : 2); i++) d.againstWall(store, i % 2 == 0 ? "pallet" : "crate", kind, r(7 + i));
        d.inCorner(toilet, "toilet", null, r(9));
        d.againstWall(toilet, "sink", "bathroom", r(10));
        return d.finish();
    }

    /// <summary>A bar (the counter along a side wall, tables) or a diner (the counter across the back facing the
    /// kitchen hatch, tables along the windows); a kitchen behind with the back door; a toilet off the room.</summary>
    static BuildingsPlan barPlan(Func<double, double> r, string kind, int maxW, int maxH)
    {
        int W = Math.Min(maxW, (int)between(r(2), 10, 13)), H = Math.Min(maxH, (int)between(r(3), 8, 11));
        var d = new Draft(W, H, kind, 1) { style = kind };
        int room = d.add("bar", "boards", 0, 3, W - 1, H - 1);
        int kitchen = d.add("kitchen", "tiles", 0, 0, W - 4, 2);
        int toilet = d.add("toilet", "tiles", W - 3, 0, W - 1, 2);
        d.frontDoor(room, r(4) < 0.5 ? 0.35 : 0.65, 2);
        d.join(kitchen, room, 0.3);
        d.join(kitchen, -1, r(5), 0);
        d.join(toilet, room, 0.5);
        d.windows();
        d.run(kitchen, new List<(string, string)> { ("counter", "bar"), ("oven", null), ("sink", "bar"), ("icebox", "bar"), ("counter", "bar") }, r(6), 0);
        if (kind == "bar")
        {
            int side = r(7) < 0.5 ? 1 : 3;
            d.run(room, new List<(string, string)> { ("counter", "bar"), ("counter", "bar"), ("counter", "bar"), ("register", null) }, r(8), side);
            for (int y = 5; y <= H - 2; y += 2)
                for (int x = side == 3 ? 3 : 1; x <= (side == 3 ? W - 2 : W - 4); x += 3) d.put(x, y, "table", null);
        }
        else
        {
            for (int x = 1; x <= W - 4; x++) if (x != 2) d.put(x, 3, x == W - 4 ? "register" : "counter", "bar");
            for (int x = 1; x <= W - 2; x += 3) d.put(x, H - 1, "table", null);
            for (int x = 2; x <= W - 3; x += 3) d.put(x, H - 3, "table", null);
        }
        d.inCorner(toilet, "toilet", null, r(9));
        d.againstWall(toilet, "sink", "bathroom", r(10));
        d.againstWall(room, "bin", "trash", r(11));
        return d.finish();
    }

    /// <summary>A workshop or car repair garage: a concrete bay behind a wide door, toolboxes and shelving round it,
    /// a car in for repair; a small office at the front with its own door and window, a toilet at the back.</summary>
    static BuildingsPlan workshopPlan(Func<double, double> r, int maxW, int maxH)
    {
        int W = Math.Min(maxW, (int)between(r(2), 10, 13)), H = Math.Min(maxH, (int)between(r(3), 8, 10));
        var d = new Draft(W, H, "workshop", 1) { style = "workshop" };
        int bay = d.add("workshop", "concrete", 0, 0, W - 1, H - 1);
        int office = d.add("office", "boards", 0, H - 3, 2, H - 1);
        int toilet = d.add("toilet", "tiles", 0, 0, 1, 1);
        d.frontDoor(office, 0.5);
        d.join(bay, -1, 0.5, 2, 3);
        d.join(office, bay, 0.5);
        d.join(toilet, bay, 0.5);
        d.join(bay, -1, r(4), 0);
        d.windows();
        d.run(bay, new List<(string, string)> { ("toolbox", "garage"), ("shelf", "garage"), ("toolbox", "garage"), ("shelf", "garage") }, r(5), 0);
        d.againstWall(bay, "machine", null, r(6));
        d.againstWall(bay, "shelf", "garage", r(7));
        if (r(8) < 0.6) d.inMiddle(bay, "car", null);
        d.againstWall(office, "desk", "office", r(9));
        d.againstWall(office, "cabinet", "office", r(10));
        d.inCorner(toilet, "toilet", null, r(11));
        d.againstWall(toilet, "sink", "bathroom", r(12));
        return d.finish();
    }

    /// <summary>A small office: a reception across the front, offices off a corridor behind it, a toilet, a side door.</summary>
    static BuildingsPlan officePlan(Func<double, double> r, int maxW, int maxH)
    {
        int W = Math.Min(maxW, (int)between(r(2), 10, 13)), H = Math.Min(maxH, (int)between(r(3), 8, 10));
        var d = new Draft(W, H, "office", 1) { style = "office" };
        int lobby = d.add("lobby", "boards", 0, H - 3, W - 1, H - 1);
        int corridor = d.add("corridor", "boards", 0, H - 4, W - 1, H - 4);
        var rooms = new List<int>();
        int x0 = 0;
        int toiletW = 2;
        int officeW = (W - toiletW) / 2;
        rooms.Add(d.add("office", "boards", x0, 0, x0 + officeW - 1, H - 5));
        rooms.Add(d.add("office", "boards", x0 + officeW, 0, W - toiletW - 1, H - 5));
        int toilet = d.add("toilet", "tiles", W - toiletW, 0, W - 1, H - 5);
        d.frontDoor(lobby, 0.5, 2);
        d.join(corridor, lobby, r(4));
        foreach (var o in rooms) d.join(o, corridor, 0.5);
        d.join(toilet, corridor, 0.5);
        d.join(corridor, -1, 0.5, r(5) < 0.5 ? 1 : 3);
        d.windows();
        d.againstWall(lobby, "desk", "office", r(6), (x, y) => y == H - 3);
        d.againstWall(lobby, "sofa", null, r(7));
        foreach (var o in rooms)
        {
            d.inMiddle(o, "desk", "office");
            d.againstWall(o, "cabinet", "office", r(8 + rooms.IndexOf(o)));
        }
        d.inCorner(toilet, "toilet", null, r(14));
        d.againstWall(toilet, "sink", "bathroom", r(15));
        d.againstWall(corridor, "bin", "trash", r(16));
        return d.finish();
    }
}
