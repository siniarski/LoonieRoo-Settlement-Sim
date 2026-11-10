// What generated buildings hold (2026-11-10, the buildings session): every chunk of a few worlds generated, then per
// kind of building (its lot's first building's plan kind) the lots, containers and loot found, and the loot by table.
// For keeping loot per lot of each kind close when building plans change.
//   SEEDS=1,2,3 SIZE=512 dotnet test cs/Harness -c Release --filter "DisplayName=building loot"
//   Report: cs/Harness/out/building-loot.txt (OUT= to change)
using LoonieRoo.Sim;

namespace LoonieRoo.Harness;

public class BuildingLoot
{
    static string? Env(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    [Fact(DisplayName = "building loot")]
    public void Run()
    {
        int size = int.Parse(Env("SIZE") ?? "512");
        var seeds = (Env("SEEDS") ?? "1,2,3").Split(',').Select(double.Parse).ToList();
        var lots = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var boxes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var loot = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var byTable = new SortedDictionary<string, SortedDictionary<string, double>>(StringComparer.Ordinal);
        var sleepers = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var seed in seeds)
        {
            var state = Worldgen.createWorld(seed, size, Difficulty.NORMAL);
            for (int y = 0; y < size; y += Worldgen.CHUNK)
                for (int x = 0; x < size; x += Worldgen.CHUNK)
                    Worldgen.ensureGeneratedAround(state, new Point(x, y));
            var l = Worldgen.layout(seed, size, state.settings.map);
            string KindAt(int x, int y)
            {
                var lot = l.regions.LotAt(x, y);
                if (lot == null) return null!;
                var b = Lots.layoutLot(seed, lot).buildings;
                return b.Count == 0 ? lot.zone : b[0].plan.kind;
            }
            var seen = new HashSet<double>();
            for (int cy = 0; cy < size / Worldgen.CHUNK; cy++)
                for (int cx = 0; cx < size / Worldgen.CHUNK; cx++)
                    foreach (var id in l.regions.LotsInChunk(cx, cy))
                    {
                        if (!seen.Add(id)) continue;
                        var lot = l.regions.Lot(id);
                        var b = Lots.layoutLot(seed, lot).buildings;
                        string k = b.Count == 0 ? lot.zone : b[0].plan.kind;
                        lots[k] = lots.GetValueOrDefault(k) + 1;
                    }
            foreach (var f in state.furniture)
            {
                if (f.kind != "crate" || f.table == null) continue;
                var k = KindAt(f.x, f.y);
                if (k == null) continue;
                boxes[k] = boxes.GetValueOrDefault(k) + 1;
                loot[k] = loot.GetValueOrDefault(k) + f.loot;
                if (!byTable.TryGetValue(k, out var t)) byTable[k] = t = new(StringComparer.Ordinal);
                t[f.table] = t.GetValueOrDefault(f.table) + f.loot;
            }
            foreach (var d in state.dormant)
                if (KindAt(d.x, d.y) is { } k) sleepers[k] = sleepers.GetValueOrDefault(k) + 1;
        }
        var out_ = new List<string> { $"building loot: seeds {string.Join(",", seeds)} at {size}, every chunk; per lot of each kind" };
        foreach (var (k, n) in lots)
        {
            double per(double v) => v / n;
            var t = byTable.GetValueOrDefault(k);
            out_.Add($"{k,-10} lots {n,4}  containers {per(boxes.GetValueOrDefault(k)),5:0.0}  loot {per(loot.GetValueOrDefault(k)),6:0.0}  sleepers {per(sleepers.GetValueOrDefault(k)),4:0.00}" +
                (t == null ? "" : "  | " + string.Join(" ", t.Select(p => $"{p.Key} {per(p.Value):0.0}"))));
        }
        Directory.CreateDirectory(Path.Combine(Node.Root, "cs/Harness/out"));
        File.WriteAllText(Path.Combine(Node.Root, Env("OUT") ?? "cs/Harness/out/building-loot.txt"), string.Join("\n", out_) + "\n");
    }
}
