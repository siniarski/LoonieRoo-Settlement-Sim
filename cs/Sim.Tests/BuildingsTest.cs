// Building plans as real places (2026-11-10, the buildings session: docs/buildings.md). TownsTest has the older
// checks (walled all round, reachable from outside, furniture on floor, turning, every building in its lot).
#nullable disable warnings
using LoonieRoo.Sim;
using LoonieRoo.Sim.Tests.Ported;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class BuildingsTest
{
    static readonly List<BuildingsPlan> plans = TownsTest.allPlans();

    static IEnumerable<int> roomsOf(BuildingsPlan p) => Enumerable.Range(0, p.rooms.Count).Where(r => p.roomOf.Contains(r));

    /// <summary>The rooms a room has a door to (-1: outside).</summary>
    static List<int> doorsOf(BuildingsPlan p, int r)
    {
        var list = new List<int>();
        foreach (var e in p.edges)
        {
            if (!e.door) continue;
            int a = Buildings.roomAt(p, e.ax, e.ay), b = Buildings.roomAt(p, e.bx, e.by);
            if (a == r && !list.Contains(b)) list.Add(b);
            if (b == r && !list.Contains(a)) list.Add(a);
        }
        return list;
    }

    /// <summary>Rooms reached from the front door's room through doors (and round the outside through outer doors),
    /// never entering a room for which `skip` is true.</summary>
    static HashSet<int> fromFront(BuildingsPlan p, Func<int, bool> skip = null)
    {
        var seen = new HashSet<int>();
        int start = Buildings.roomAt(p, p.front.ax, p.front.ay);
        var queue = new Queue<int>();
        queue.Enqueue(start);
        seen.Add(start);
        while (queue.Count > 0)
        {
            int r = queue.Dequeue();
            foreach (int b in doorsOf(p, r))
            {
                if (seen.Contains(b) || (b >= 0 && skip != null && skip(b))) continue;
                seen.Add(b);
                queue.Enqueue(b);
            }
            if (r == -1) continue;
        }
        return seen;
    }

    static string Show(BuildingsPlan p) => "\n" + Buildings.ascii(p);

    [Fact(DisplayName = "buildings > every plan has a front door facing the street, and every room has a door and is reached from it")]
    public void FrontDoor()
    {
        foreach (var p in plans)
        {
            expect(p.front, Show(p)).toBeDefined();
            expect(p.front.by, Show(p)).toBe(p.front.ay + 1);
            var seen = fromFront(p);
            foreach (int r in roomsOf(p))
            {
                expect(doorsOf(p, r).Count > 0, $"{p.rooms[r]} has no door{Show(p)}").toBe(true);
                expect(seen.Contains(r), $"{p.rooms[r]} not reached from the front door{Show(p)}").toBe(true);
            }
        }
    }

    [Fact(DisplayName = "buildings > windows only in outer walls, never in a door's place or between two rooms")]
    public void Windows()
    {
        int windows = 0;
        foreach (var p in plans)
            foreach (var e in p.edges)
            {
                if (e.window != true) continue;
                windows++;
                expect(Buildings.cellAt(p, e.bx, e.by), Show(p)).toBe(Buildings.OUTSIDE);
                expect(e.door, Show(p)).toBe(false);
            }
        expect(windows).toBeGreaterThan(plans.Count * 2);
        // Houses: every bedroom and living room with an outer wall has a window in it.
        foreach (var p in plans.Where(p => p.kind == "house"))
            foreach (int r in roomsOf(p).Where(r => p.rooms[r] is "living" or "cabin"))
                expect(p.edges.Any(e => e.window == true && Buildings.roomAt(p, e.ax, e.ay) == r), $"{p.rooms[r]} without a window{Show(p)}").toBe(true);
    }

    [Fact(DisplayName = "buildings > furniture never stands beside a door, and leaves the way between a room's doors and to every container clear")]
    public void ClearWays()
    {
        foreach (var p in plans)
        {
            var taken = new HashSet<int>(p.furniture.Select(f => f.y * p.w + f.x));
            bool wallBetween(int x, int y, int nx, int ny) => TownsTest.edgeOf(p, x, y, nx, ny) is { } e && !e.door;
            foreach (int r in roomsOf(p))
            {
                // Cells beside a door into this room.
                var byDoor = new List<int>();
                foreach (var e in p.edges)
                {
                    if (!e.door) continue;
                    if (Buildings.roomAt(p, e.ax, e.ay) == r) byDoor.Add(e.ay * p.w + e.ax);
                    if (Buildings.roomAt(p, e.bx, e.by) == r) byDoor.Add(e.by * p.w + e.bx);
                }
                foreach (int i in byDoor) expect(taken.Contains(i), $"furniture in a doorway at {i % p.w},{i / p.w}{Show(p)}").toBe(false);
                if (byDoor.Count == 0) continue;
                var seen = new HashSet<int> { byDoor[0] };
                var stack = new Stack<int>(seen);
                while (stack.Count > 0)
                {
                    int i = stack.Pop(), x = i % p.w, y = i / p.w;
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = x + dx, ny = y + dy, j = ny * p.w + nx;
                        if (Buildings.roomAt(p, nx, ny) != r || taken.Contains(j) || seen.Contains(j) || wallBetween(x, y, nx, ny)) continue;
                        seen.Add(j);
                        stack.Push(j);
                    }
                }
                foreach (int i in byDoor) expect(seen.Contains(i), $"{p.rooms[r]}'s doors cut off from each other{Show(p)}").toBe(true);
                foreach (var f in p.furniture.Where(f => f.table != null && Buildings.roomAt(p, f.x, f.y) == r))
                {
                    bool reach = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d => seen.Contains((f.y + d.Item2) * p.w + f.x + d.Item1) && Buildings.roomAt(p, f.x + d.Item1, f.y + d.Item2) == r && !wallBetween(f.x, f.y, f.x + d.Item1, f.y + d.Item2));
                    expect(reach, $"{f.look} at {f.x},{f.y} can't be got at{Show(p)}").toBe(true);
                }
            }
        }
    }

    [Fact(DisplayName = "buildings > a house is entered by a hall or the living room; the kitchen opens on the living room and has a back door; bedrooms off the hall; the bathroom reached without a bedroom")]
    public void HouseLayout()
    {
        var styles = new HashSet<string>();
        foreach (var p in plans.Where(p => p.kind == "house"))
        {
            styles.Add(p.style);
            int entry = Buildings.roomAt(p, p.front.ax, p.front.ay);
            expect(p.rooms[entry] is "hall" or "living" or "cabin", $"entered by the {p.rooms[entry]}{Show(p)}").toBe(true);
            int kitchen = p.rooms.IndexOf("kitchen");
            if (kitchen >= 0)
            {
                var k = doorsOf(p, kitchen);
                expect(k.Contains(-1), $"kitchen without a back door{Show(p)}").toBe(true);
                expect(k.Any(b => b >= 0 && p.rooms[b] == "living"), $"kitchen not opening on the living room{Show(p)}").toBe(true);
            }
            bool openPlan = p.style is "cabin" or "tiny";
            foreach (int r in roomsOf(p).Where(r => p.rooms[r] == "bedroom"))
                foreach (int b in doorsOf(p, r))
                    expect(b >= 0 && (p.rooms[b] == "hall" || openPlan), $"a bedroom off {(b < 0 ? "the outside" : p.rooms[b])}{Show(p)}").toBe(true);
            var noBeds = fromFront(p, r => p.rooms[r] == "bedroom");
            foreach (int r in roomsOf(p).Where(r => p.rooms[r] == "bathroom"))
                expect(noBeds.Contains(r), $"the bathroom only through a bedroom{Show(p)}").toBe(true);
        }
        foreach (var s in new[] { "cottage", "bungalow", "family", "farmhouse", "cabin", "trailer" }) expect(styles.Contains(s), s).toBe(true);
    }

    [Fact(DisplayName = "buildings > house rooms are big enough for what's in them, and have it: no corridor-shaped bedrooms")]
    public void RoomSizes()
    {
        var need = new Dictionary<string, (int side, int area, string look)>
        {
            ["bedroom"] = (3, 9, "bed"), ["bathroom"] = (2, 4, "toilet"), ["kitchen"] = (2, 8, "oven"), ["living"] = (3, 12, "sofa"), ["garage"] = (3, 12, null),
        };
        foreach (var p in plans.Where(p => p.kind == "house"))
            foreach (int r in roomsOf(p))
            {
                if (!need.TryGetValue(p.rooms[r], out var n)) continue;
                var cells = Enumerable.Range(0, p.w * p.h).Where(i => p.roomOf[i] == r).ToList();
                int w = cells.Max(i => i % p.w) - cells.Min(i => i % p.w) + 1, h = cells.Max(i => i / p.w) - cells.Min(i => i / p.w) + 1;
                string what = $"{p.rooms[r]} {w}x{h} ({cells.Count}){Show(p)}";
                expect(Math.Min(w, h), what).toBeGreaterThanOrEqual(n.side);
                expect(cells.Count, what).toBeGreaterThanOrEqual(n.area);
                if (p.rooms[r] == "bedroom") expect(Math.Max(w, h), what).toBeLessThanOrEqual(2 * Math.Min(w, h));
                if (n.look != null) expect(p.furniture.Any(f => f.look == n.look && Buildings.roomAt(p, f.x, f.y) == r), $"no {n.look}: {what}").toBe(true);
            }
    }

    [Fact(DisplayName = "buildings > wide doors on garages, workshops, warehouses and barns; a back door on shops")]
    public void WideDoors()
    {
        foreach (var p in plans)
        {
            int wideIn(string room) => p.edges.Count(e => e.door && Buildings.cellAt(p, e.bx, e.by) == Buildings.OUTSIDE && p.rooms[Buildings.roomAt(p, e.ax, e.ay)] == room);
            if (p.rooms.Contains("garage")) expect(wideIn("garage"), Show(p)).toBeGreaterThanOrEqual(3);
            if (p.kind is "warehouse" or "factory") expect(wideIn("hall-big"), Show(p)).toBeGreaterThanOrEqual(6);
            if (p.kind == "workshop") expect(wideIn("workshop"), Show(p)).toBeGreaterThanOrEqual(3);
            if (p.kind is "grocery" or "pharmacy" or "hardware" or "station" or "bar" or "diner")
                expect(p.edges.Count(e => e.door && e.by == e.ay - 1 && Buildings.cellAt(p, e.bx, e.by) == Buildings.OUTSIDE), $"no back door{Show(p)}").toBeGreaterThanOrEqual(1);
        }
    }

    /// <summary>A world plan's lots' layouts, made afresh (not from the cache).</summary>
    static IEnumerable<(Lot lot, LotLayout layout)> layouts(double seed, int size)
    {
        var p = Worldplan.planWorld(seed, size, "mainland");
        foreach (var lot in p.lots) yield return (lot, Lots.makeLayout(seed, lot));
    }

    [Fact(DisplayName = "buildings > lots: no side window within a tile of the lot's edge; paths, driveways and fences inside their lot")]
    public void LotRules()
    {
        int paths = 0, drives = 0, fences = 0, sheds = 0;
        foreach (var seed in new double[] { 1, 2, 3 })
            foreach (var (lot, l) in layouts(seed, 256))
            {
                var F = (int)Lots.frame(lot).F;
                foreach (var b in l.buildings)
                    foreach (var e in b.plan.edges)
                    {
                        if (e.window != true || e.ay != e.by) continue;
                        // The tile beyond the one outside the window is still on the lot.
                        var o = Lots.planToMap(b, e.bx, e.by);
                        var two = Lots.planToMap(b, 2 * e.bx - e.ax, e.by);
                        var u = Lots.uv(lot, two).u;
                        expect(u >= 0 && u < F, $"window at {o.x},{o.y} on lot {Js.Json_stringify(lot)}").toBe(true);
                    }
                for (int v = 0; v < Lots.frame(lot).D; v++)
                    for (int u = 0; u < F; u++)
                    {
                        string g = l.yard(u, v);
                        if (g == "paving" && lot.zone is "residential" or "cabin") paths++;
                        if (g == "concrete" && lot.zone == "residential") drives++;
                    }
                fences += l.fences.Count > 0 ? 1 : 0;
                sheds += l.buildings.Count(b => b.plan.kind == "shed");
                foreach (var e in l.fences)
                {
                    bool aIn = e.ax >= lot.x && e.ay >= lot.y && e.ax < lot.x + lot.w && e.ay < lot.y + lot.h;
                    expect(aIn, $"fence off its lot {Js.Json_stringify(lot)}").toBe(true);
                }
                if (l.drive != null) expect(l.yard(Lots.uv(lot, l.drive).u, Lots.uv(lot, l.drive).v)).toBe("concrete");
            }
        expect(paths).toBeGreaterThan(50);
        expect(drives).toBeGreaterThan(5);
        expect(fences).toBeGreaterThan(5);
        expect(sheds).toBeGreaterThan(0);
    }

    [Fact(DisplayName = "buildings > the same seed lays out the same town twice")]
    public void SameTwice()
    {
        string all(double seed) => string.Join("\n", layouts(seed, 256).Select(t => Js.Json_stringify(t.layout.buildings) + Js.Json_stringify(t.layout.fences) + Js.Json_stringify(t.layout.decor)));
        expect(all(5) == all(5)).toBe(true);
        expect(all(5) == all(6)).toBe(false);
    }

    [Fact(DisplayName = "buildings > a save made by another world generation version is refused, with a message")]
    public void GenerationVersion()
    {
        var state = Worldgen.createWorld(3, 128);
        expect(state.world.gen).toBe(Worldgen.GEN_VERSION);
        var text = Savefile.toSaveFile(state);
        expect(Savefile.loadFile(text).ok).toBe(true);
        var node = System.Text.Json.Nodes.JsonNode.Parse(text)!;
        node["state"]!["world"]!.AsObject().Remove("gen");
        var old = Savefile.loadFile(node.ToJsonString());
        expect(old.ok).toBe(false);
        expect(old.error).toContain("generation version 1");
        node["state"]!["world"]!["gen"] = Worldgen.GEN_VERSION + 1;
        expect(Savefile.loadFile(node.ToJsonString()).ok).toBe(false);
        var bug = System.Text.Json.Nodes.JsonNode.Parse(Savefile.toBugReport(state, state))!;
        bug["initial"]!["world"]!.AsObject().Remove("gen");
        expect(Savefile.loadFile(bug.ToJsonString()).ok).toBe(false);
    }

    /// <summary>PLANS=path writes one plan of each kind and style as text (for the session log and docs).</summary>
    [Fact(DisplayName = "buildings > PLANS=path writes one plan of each kind and style (a no-op without it)")]
    public void Print()
    {
        var path = System.Environment.GetEnvironmentVariable("PLANS");
        if (string.IsNullOrEmpty(path)) return;
        var seen = new HashSet<string>();
        var sb = new System.Text.StringBuilder();
        foreach (var p in plans)
            if (seen.Add(p.kind + "/" + p.style)) sb.Append(Buildings.ascii(p)).Append('\n');
        File.WriteAllText(path, sb.ToString());
    }
}
