// Dwellings (kind "house"), by style (docs/buildings.md):
//   cottage    8-9 x 7-8: a hall from the front door; the kitchen at the back (back door) opening into the living
//              room; one bedroom, the bathroom and maybe a utility room off the hall
//   bungalow   9-10 x 8-9 (+3 for a garage): the same with two bedrooms; often an attached garage beside the
//              kitchen, a bay on the living room or a kitchen wing at the back
//   family     12-13 x 9-10: three bedrooms, two at the back and one at the front, off an L-shaped hall; a utility
//              room behind the kitchen; often an L (the living room wing reaching forward) or a bay
//   farmhouse  a bungalow or family house on a farm: always a utility room (a mudroom), never a garage
//   cabin      out of town: one main room (kitchen, stove, table) with a bedroom and a bathroom off it, a lean-to
//   trailer    out of town: a long single-wide: living room and kitchen, a short hall to the bathroom and bedroom
// The plan is laid out as columns, the front at the bottom: on the left the kitchen (back) over the living room
// (front), a one-tile hall up the middle from the front door, bedrooms and the bathroom on the right; a garage, if
// any, to the left of the kitchen. Mirroring puts it the other way round.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Buildings
{
    public static BuildingsPlan housePlan(Func<double, double> r, HouseOptions o)
    {
        int maxW = (int)o.maxW, maxH = (int)o.maxH;
        string where = string.IsNullOrEmpty(o.where) ? "town" : o.where;
        if (where == "wild")
            return r(1) < 0.35 && maxW >= 10 && maxH >= 4 ? trailerPlan(r, maxW) : cabinPlan(r, maxW, maxH);
        string style = maxW >= 12 && maxH >= 9 && r(1) < (where == "farm" ? 0.6 : 0.4) ? "family"
            : maxW >= 9 && maxH >= 8 && r(5) < 0.75 ? "bungalow" : "cottage";
        return columnHouse(r, maxW, maxH, style, where, o.garage);
    }

    static BuildingsPlan columnHouse(Func<double, double> r, int maxW, int maxH, string style, string where, bool garageOk)
    {
        bool family = style == "family", cottage = style == "cottage";
        int Rw = family ? (int)between(r(2), 6, 7) : cottage ? 3 : 4;
        int Lw = family ? (int)between(r(3), 5, 6) : (int)between(r(3), 4, 5);
        // Narrow lots: the left column gives first, then the right.
        while (Lw + 1 + Rw > maxW && Lw > 4) Lw--;
        while (Lw + 1 + Rw > maxW && Rw > (family ? 6 : 3)) Rw--;
        if (Lw + 1 + Rw > maxW)
            return family ? columnHouse(r, maxW, maxH, "bungalow", where, garageOk) : cottage ? tinyHouse(r, maxW, maxH) : columnHouse(r, maxW, maxH, "cottage", where, garageOk);
        int minH = family ? 9 : cottage ? 7 : 8;
        int H = Math.Min(maxH, family ? (int)between(r(4), 9, 10) : cottage ? (int)between(r(4), 7, 8) : (int)between(r(4), 8, 9));
        if (H < minH) return family ? columnHouse(r, maxW, maxH, "bungalow", where, garageOk) : cottage ? tinyHouse(r, maxW, maxH) : columnHouse(r, maxW, maxH, "cottage", where, garageOk);
        bool garage = garageOk && where == "town" && !family && Lw + 1 + Rw + 3 <= maxW && r(6) < 0.55;
        int spare = maxH - H;
        // Shape: a living-room wing reaching forward (an L), a bay, or a kitchen wing at the back (a T), as room allows.
        string shape = "box";
        double sr = r(7);
        if (!garage && spare >= 2 && sr < (family ? 0.45 : 0.25)) shape = "wing";
        else if (spare >= 1 && sr < 0.6) shape = "bay";
        else if (spare >= 2 && sr < 0.8 && Lw >= 4) shape = "rear";
        int gx = garage ? 3 : 0, by = shape == "rear" ? 2 : 0;
        int W = gx + Lw + 1 + Rw, Hh = by + H + (shape == "wing" ? 2 : shape == "bay" ? 1 : 0);
        var d = new Draft(W, Hh, "house", 1) { style = where == "farm" ? "farmhouse" : style };
        int X(int x) => gx + x;
        int Y(int y) => by + y;

        int kh = H == 8 && !cottage && !family ? 2 : family ? Math.Min(4, (int)between(r(8), 3, 4)) : 3;
        bool utilityRoom = family ? Lw >= 6 || where == "farm" : where == "farm" && Lw >= 5;
        int kitchen = d.add("kitchen", "tiles", X(utilityRoom ? 2 : 0), Y(0), X(Lw), Y(kh - 1));
        int utility = utilityRoom ? d.add("utility", "tiles", X(0), Y(0), X(1), Y(kh - 1)) : -1;
        int living = d.add("living", "boards", X(0), Y(kh), X(Lw - 1), Y(H - 1));
        bool intoLiving = !family && r(9) < 0.3;
        int hallEnd = intoLiving ? H - 2 : H - 1;
        int hall = d.add("hall", "boards", X(Lw), Y(kh), X(Lw), Y(hallEnd));
        if (intoLiving) d.extend(living, X(Lw), Y(H - 1), X(Lw), Y(H - 1));
        if (shape == "wing") d.extend(living, X(0), Y(H), X(Lw - 1), Y(H + 1));
        if (shape == "bay")
        {
            int bw = Lw >= 5 ? 3 : 2, b0 = (Lw - bw) / 2;
            d.extend(living, X(b0), Y(H), X(b0 + bw - 1), Y(H));
        }
        if (shape == "rear") d.extend(kitchen, X(utilityRoom ? 2 : 0), Y(-2), X(Lw - 1), Y(-1));

        var beds = new List<int>();
        int bath;
        int R0 = Lw + 1, R1 = Lw + Rw;
        if (family)
        {
            int bd = Math.Max(kh, 3);
            int m = R0 + Rw / 2 - 1;
            beds.Add(d.add("bedroom", "boards", X(R0), Y(0), X(m), Y(bd - 1)));
            beds.Add(d.add("bedroom", "boards", X(m + 1), Y(0), X(R1), Y(bd - 1)));
            d.extend(hall, X(R0), Y(bd), X(R1), Y(bd));
            int bw = Rw >= 7 ? 3 : 2;
            bath = d.add("bathroom", "tiles", X(R0), Y(bd + 1), X(R0 + bw - 1), Y(H - 1));
            beds.Add(d.add("bedroom", "boards", X(R0 + bw), Y(bd + 1), X(R1), Y(H - 1)));
        }
        else
        {
            int b1 = Math.Max(kh + 1, 3);
            beds.Add(d.add("bedroom", "boards", X(R0), Y(0), X(R1), Y(b1 - 1)));
            if (cottage)
            {
                int rest = H - b1;
                int bh = rest >= 5 ? 2 : rest;
                bath = d.add("bathroom", "tiles", X(R0), Y(b1), X(R1), Y(b1 + bh - 1));
                if (rest > bh) utility = utility >= 0 ? utility : d.add("utility", "tiles", X(R0), Y(b1 + bh), X(R1), Y(H - 1));
            }
            else
            {
                bath = d.add("bathroom", "tiles", X(R0), Y(b1), X(R1), Y(b1 + 1));
                beds.Add(d.add("bedroom", "boards", X(R0), Y(b1 + 2), X(R1), Y(H - 1)));
            }
        }
        int garageRoom = garage ? d.add("garage", "concrete", 0, Y(kh - 1), 2, Y(H - 1)) : -1;

        // Doors: in from the front, the kitchen to the living room and the hall, out at the back; every other room off
        // the hall (a utility room off the kitchen, the garage into the kitchen).
        d.frontDoor(intoLiving ? living : hall, r(10));
        d.join(hall, living, r(11));
        d.join(hall, kitchen, 0.5);
        d.join(kitchen, living, r(12));
        d.join(kitchen, -1, r(13), 0);
        foreach (var b in beds) d.join(b, hall, r(14 + beds.IndexOf(b)));
        d.join(bath, hall, r(17));
        if (utility >= 0 && !d.join(utility, kitchen, r(18))) d.join(utility, hall, r(18));
        if (garageRoom >= 0)
        {
            d.join(garageRoom, kitchen, r(19));
            d.join(garageRoom, -1, 0.5, 2, 3);
            d.join(garageRoom, -1, r(20), 0);
        }
        d.windows();
        furnishHouse(d, r, kitchen, living, bath, beds, utility, garageRoom);
        return d.finish();
    }

    /// <summary>Too small a lot for a cottage: two rooms (living-kitchen and a bedroom) and a bathroom.</summary>
    static BuildingsPlan tinyHouse(Func<double, double> r, int maxW, int maxH)
    {
        int W = Math.Max(6, Math.Min(maxW, 8)), H = Math.Max(6, Math.Min(maxH, 7));
        var d = new Draft(W, H, "house", 1) { style = "tiny" };
        int bed = d.add("bedroom", "boards", 0, 0, 2, 2);
        int bath = d.add("bathroom", "tiles", 3, 0, 4, 1);
        int kitchen = d.add("kitchen", "tiles", 5, 0, W - 1, 2);
        d.extend(kitchen, 3, 2, 4, 2);
        int living = d.add("living", "boards", 0, 3, W - 1, H - 1);
        d.frontDoor(living, r(10));
        d.join(kitchen, living, r(12));
        d.join(kitchen, -1, r(13), 0);
        d.join(bed, living, r(14));
        if (!d.join(bath, kitchen, r(17))) d.join(bath, living, r(17));
        d.windows();
        furnishHouse(d, r, kitchen, living, bath, new List<int> { bed }, -1, -1);
        return d.finish();
    }

    /// <summary>A cabin: a main room (kitchen along a wall, a stove, a table, a sofa) with a bedroom and a bathroom
    /// off it, and sometimes a lean-to woodshed on one side with its own door.</summary>
    public static BuildingsPlan cabinPlan(Func<double, double> r, int maxW, int maxH)
    {
        int W = Math.Min(maxW, (int)between(r(2), 7, 8)), H = Math.Min(maxH, (int)between(r(3), 6, 7));
        bool lean = W + 2 <= maxW && r(4) < 0.5;
        var d = new Draft(W + (lean ? 2 : 0), H, "house", 1) { style = "cabin" };
        int bed = d.add("bedroom", "boards", 0, 0, 2, 2);
        int bath = d.add("bathroom", "tiles", 3, 0, 4, 1);
        int main = d.add("cabin", "boards", 0, 3, W - 1, H - 1);
        d.extend(main, 5, 0, W - 1, 2);
        d.extend(main, 3, 2, 4, 2);
        d.floorAs(5, 0, W - 1, 2, "tiles");
        int leanTo = lean ? d.add("lean-to", "concrete", W, 0, W + 1, 2) : -1;
        d.frontDoor(main, r(10));
        d.join(main, -1, r(13), 0);
        d.join(bed, main, r(14));
        d.join(bath, main, 0.5);
        if (leanTo >= 0) d.join(leanTo, -1, r(15), 1);
        d.windows();
        // The kitchen corner (the tiled part), the stove, a table; the bedroom and bathroom as in a house.
        d.run(main, new List<(string, string)> { ("counter", "kitchen"), ("sink", "kitchen"), ("oven", null), ("icebox", "kitchen") }, r(20), 0);
        d.inMiddle(main, "table", null);
        d.againstWall(main, "sofa", null, r(21), (x, y) => y >= 3);
        d.againstWall(main, "shelf", "living", r(22));
        d.againstWall(main, "bin", "trash", r(23));
        furnishBedroom(d, bed, r, 0);
        furnishBathroom(d, bath, r);
        if (leanTo >= 0)
        {
            d.againstWall(leanTo, "toolbox", "shed", r(24));
            d.againstWall(leanTo, "sack", "shed", r(25));
        }
        return d.finish();
    }

    /// <summary>A trailer: a single-wide (4 deep), the door on its long side. Bedroom at one end, a short hall to it and
    /// the bathroom, the living room and the kitchen (back door) at the other end.</summary>
    public static BuildingsPlan trailerPlan(Func<double, double> r, int maxW)
    {
        int W = Math.Min(maxW, (int)between(r(2), 10, 12));
        var d = new Draft(W, 4, "house", 1) { style = "trailer" };
        int bed = d.add("bedroom", "boards", 0, 0, 2, 3);
        int bath = d.add("bathroom", "tiles", 3, 0, 4, 1);
        int hall = d.add("hall", "boards", 3, 2, 4, 3);
        int kw = W >= 11 ? 3 : 2;
        int living = d.add("living", "boards", 5, 0, W - 1 - kw, 3);
        int kitchen = d.add("kitchen", "tiles", W - kw, 0, W - 1, 3);
        d.frontDoor(living, r(10));
        d.join(hall, living, 0.5);
        d.join(bath, hall, 0.5);
        d.join(bed, hall, 0.5);
        d.join(kitchen, living, r(12));
        d.join(kitchen, -1, r(13), 0);
        d.windows();
        furnishHouse(d, r, kitchen, living, bath, new List<int> { bed }, -1, -1);
        return d.finish();
    }

    static void furnishHouse(Draft d, Func<double, double> r, int kitchen, int living, int bath, List<int> beds, int utility, int garage)
    {
        // Kitchen: the counters, sink, stove and fridge in a run along the back wall (or the longest free wall), a table
        // if there's room to sit, a bin.
        var kcells = d.cellsOf(kitchen);
        int kw = kcells.Max(c => c.x) - kcells.Min(c => c.x) + 1, kd = kcells.Max(c => c.y) - kcells.Min(c => c.y) + 1;
        // The stove, sink and fridge first (a small kitchen may have room for no more), then counters.
        var pieces = new List<(string, string)> { ("oven", null), ("sink", "kitchen"), ("icebox", "kitchen"), ("counter", "kitchen") };
        if (r(30) < 0.5 && kcells.Count >= 12) pieces.Add(("counter", "kitchen"));
        d.run(kitchen, pieces, r(31), 0);
        if (Math.Min(kw, kd) >= 3 && kcells.Count >= 12) d.inMiddle(kitchen, "table", null);
        d.againstWall(kitchen, "bin", "trash", r(32));
        // Living room: a television against a wall and the sofa facing it, an armchair, shelves; a dining table when
        // the room is deep and the kitchen too small for one.
        var lcells = d.cellsOf(living);
        if (d.againstWall(living, "tv", null, r(33), (x, y) => d.walls(x, y, true) != 0 && !d.window[(y * d.w + x) * 4 + 2]))
        {
            if (!d.facing(living, d.last, "sofa", null)) d.againstWall(living, "sofa", null, r(34));
        }
        else d.againstWall(living, "sofa", null, r(34));
        d.againstWall(living, "armchair", null, r(35));
        d.againstWall(living, "shelf", "living", r(36));
        if (r(37) < 0.6) d.againstWall(living, "bookcase", "living", r(38));
        if (!(Math.Min(kw, kd) >= 3 && kcells.Count >= 12) && lcells.Count >= 24) d.inMiddle(living, "table", null);
        furnishBathroom(d, bath, r);
        for (int i = 0; i < beds.Count; i++) furnishBedroom(d, beds[i], r, i);
        if (utility >= 0)
        {
            d.againstWall(utility, "washer", null, r(40));
            d.againstWall(utility, "shelf", "garage", r(41));
        }
        if (garage >= 0)
        {
            d.againstWall(garage, "toolbox", "garage", r(42));
            d.againstWall(garage, "shelf", "garage", r(43));
            if (r(44) < 0.5) d.inMiddle(garage, "car", null);
        }
    }

    static void furnishBedroom(Draft d, int room, Func<double, double> r, int i)
    {
        d.bed(room, r(50 + i), i == 0 || r(57 + i) < 0.5 ? "nightstand" : null);
        if (i < 2) d.againstWall(room, i == 0 ? "wardrobe" : "dresser", "bedroom", r(54 + i));
    }

    static void furnishBathroom(Draft d, int room, Func<double, double> r)
    {
        var cells = d.cellsOf(room);
        d.inCorner(room, "toilet", null, r(60));
        d.againstWall(room, "sink", "bathroom", r(61));
        if (cells.Count >= 6) d.againstWall(room, "bath", null, r(62));
        else d.againstWall(room, "shower", null, r(62));
        d.againstWall(room, "cabinet", "bathroom", r(63));
    }

    public static BuildingsPlan shedPlan(Func<double, double> r)
    {
        int W = r(3) < 0.5 ? 2 : 3;
        var d = new Draft(W, 2, "shed", 0) { style = "shed" };
        int s = d.add("shed", "boards", 0, 0, W - 1, 1);
        d.frontDoor(s, 0.5);
        d.againstWall(s, "toolbox", "shed", r(1));
        d.againstWall(s, "shelf", "shed", r(2));
        return d.finish();
    }

    /// <summary>The start's shelter (Worldgen.startCamp): one room of boards, eight by six, walled on its edges like any
    /// generated building: the door in the middle of the front, windows, a stove, a table, shelves and a bin (all
    /// empty: what you start with lies outside). The back row is kept clear for the beds, which startCamp adds
    /// (real beds, not a plan's). The same for every seed.</summary>
    public static BuildingsPlan startPlan()
    {
        var d = new Draft(8, 6, "house", 0) { style = "cabin" };
        int main = d.add("cabin", "boards", 0, 0, 7, 5);
        d.putDoor(3, 5, 2);
        d.front = new PlanEdge { ax = 3, ay = 5, bx = 3, by = 6, door = true };
        d.windows();
        for (int x = 0; x < 8; x++) d.keep[x] = true;
        d.againstWall(main, "oven", null, 0, (x, y) => x == 0 && y >= 2);
        d.againstWall(main, "shelf", "living", 0, (x, y) => x == 7 && y >= 2);
        d.againstWall(main, "bin", "trash", 0, (x, y) => y == 5 && x >= 5);
        d.inMiddle(main, "table", null);
        return d.finish();
    }
}
