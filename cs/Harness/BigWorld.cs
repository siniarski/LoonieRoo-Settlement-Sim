// A big world made, played a little, saved and loaded: time and managed memory at each step (2026-11-10, the grid in
// chunks). What the game does at the scale target, on one state, so the numbers include what play allocates on top
// of the world (pathfinding's and reachability's per-tile data, rooms, sight), not only createWorld.
//
//   SIZES=2048,10000 TICKS=200 dotnet test cs/Harness -c Release --filter "DisplayName=big world"
//   Report: cs/Harness/out/big-world.txt (OUT= another path). BOUND_MB=<n> fails the run if the heap after
//   createWorld at the last size is over n megabytes (the memory bound the grid in chunks is held to).
using System.Diagnostics;
using LoonieRoo.Sim;

namespace LoonieRoo.Harness;

public class BigWorld
{
    static string? Env(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    static double Mb() => GC.GetTotalMemory(true) / 1e6;

    [Fact(DisplayName = "big world")]
    public void Run()
    {
        var out_ = new List<string>();
        string path = Path.Combine(Node.Root, Env("OUT") ?? "cs/Harness/out/big-world.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        int ticks = int.Parse(Env("TICKS") ?? "200");
        double last = 0;
        foreach (var size in (Env("SIZES") ?? "2048,10000").Split(',').Select(int.Parse))
        {
            GC.Collect();
            double m0 = Mb();
            var line = new List<string>();
            var t = Stopwatch.StartNew();
            void Lap(string what, string more = "")
            {
                double s = t.Elapsed.TotalSeconds;
                line.Add($"{what} {(s < 1 ? $"{s * 1000:0} ms" : $"{s:0.00} s")}, heap {Mb() - m0:0} MB{more}");
                t.Restart();
            }
            void Write() => File.WriteAllText(path, string.Join("\n", out_.Append($"size {size}: " + string.Join("; ", line))) + "\n");
            try
            {
                var s = Worldgen.createWorld(42, size, Difficulty.NORMAL);
                last = Mb() - m0;
                int Planned() => Worldplan.regions(42, size, Difficulty.NORMAL.map).Planned;
                Lap("createWorld", $" ({s.world!.generated.Count((g) => g == 1)} chunks, {Planned()} regions planned)");
                Write();
                Tick.tick(s);
                Lap("the first tick");
                Write();
                for (int i = 1; i < ticks; i++) Tick.tick(s);
                double perTick = t.Elapsed.TotalMilliseconds / Math.Max(1, ticks - 1);
                Lap($"{ticks - 1} more ticks", $" ({perTick:0.000} ms a tick; {Planned()} regions planned)");
                Write();
                // A walk away from the start, a chunk at a time: new chunks made, paths and rooms asked on new land.
                var you = s.survivors[0];
                int far = Math.Min(size - 2, you.pos.x + 6 * Worldgen.CHUNK);
                for (int x = you.pos.x; x <= far; x += Worldgen.CHUNK)
                {
                    Worldgen.ensureGeneratedAround(s, new Point(x, you.pos.y));
                    for (int i = 0; i < 5; i++) Tick.tick(s);
                }
                Lap("6 chunks east, 5 ticks each", $" ({s.world.generated.Count((g) => g == 1)} chunks, {Planned()} regions planned)");
                Write();
                string text = "";
                double save = double.MaxValue;
                for (int r = 0; r < 3; r++)
                {
                    var sw = Stopwatch.StartNew();
                    text = Savefile.toSaveFile(s);
                    save = Math.Min(save, sw.Elapsed.TotalMilliseconds);
                }
                t.Restart();
                line.Add($"save {save:0} ms (best of 3), {text.Length / 1e6:0.00} MB");
                Write();
                s = null!;
                GC.Collect();
                double m1 = Mb();
                t.Restart();
                var back = Savefile.loadFile(text);
                double load = t.Elapsed.TotalMilliseconds;
                line.Add($"load {load:0} ms ({(back.ok ? "ok" : back.error)}), the loaded state's heap {Mb() - m1:0} MB");
                Write();
                if (back.ok)
                {
                    t.Restart();
                    for (int i = 0; i < 20; i++) Tick.tick(back.state);
                    line.Add($"20 ticks on it {t.Elapsed.TotalMilliseconds:0} ms, heap {Mb() - m1:0} MB");
                }
                back = null!;
            }
            catch (OutOfMemoryException) { line.Add("OUT OF MEMORY"); }
            out_.Add($"size {size}: " + string.Join("; ", line));
            File.WriteAllText(path, string.Join("\n", out_) + "\n");
            GC.Collect();
        }
        using var p = Process.GetCurrentProcess();
        out_.Add($"peak working set {p.PeakWorkingSet64 / 1e6:0} MB");
        File.WriteAllText(path, string.Join("\n", out_) + "\n");
        if (Env("BOUND_MB") is { } bound) Assert.True(last <= double.Parse(bound), $"heap after createWorld {last:0} MB, over the bound of {bound} MB");
    }
}
