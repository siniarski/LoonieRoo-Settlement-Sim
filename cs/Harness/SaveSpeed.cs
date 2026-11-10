// How long saving and loading take, and where the time goes (2026-11-10: a 512 save took ~1 s, ~10 ms in the browser).
//
// For each world (size : "start" for the chunks round the start, as a game has them, or "all" for every chunk), a
// played state (ticked, a few things changed by hand as ChunkSaveTest does), then each of: the chunked save
// (Savefile.toSaveFile of a world) and its load, the whole save (the same state with no world, as a hand-made map is
// saved) and its load. Each is timed REPS times (default 5): the best and the median. Then the save's parts timed on
// their own, so the total can be read as a sum.
//
//   dotnet test cs/Harness -c Release --filter "DisplayName=save speed"
//   SAVES=512:all,2048:all REPS=3 ...  Report: cs/Harness/out/save-speed.txt

using System.Diagnostics;
using System.Text.Json;
using LoonieRoo.Sim;

namespace LoonieRoo.Harness;

public class SaveSpeed
{
    static string? Env(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    /// <summary>A world played a little and changed by hand the ways play changes it.</summary>
    public static SimState Played(int size, bool all)
    {
        var s = Worldgen.createWorld(42, size, Difficulty.NORMAL);
        if (all)
            for (int y = 0; y < size; y += Worldgen.CHUNK)
                for (int x = 0; x < size; x += Worldgen.CHUNK) Worldgen.ensureGeneratedAround(s, new Point(x, y));
        for (int i = 0; i < 200; i++) Tick.tick(s);
        var start = s.survivors[0].pos;
        if (s.furniture.FirstOrDefault(f => f.kind == "tree") is { } tree) FurnitureMod.removeFurniture(s, tree);
        if (s.furniture.FirstOrDefault(f => f.kind == "crate") is { } crate) crate.loot = 0;
        Grids.setTile(s.grid, start.x + 5, start.y + 5, "wall");
        Grids.setDirty(s.grid, start.x + 2, start.y + 6, true);
        if (s.dormant.Count > 0) Dormant.remove(s, s.dormant[0]);
        FurnitureMod.addFurniture(s, "workbench", start.x + 3, start.y + 3);
        return s;
    }

    /// <summary>Best and median of `reps` runs of `f`, in ms.</summary>
    static (double best, double median) Time(int reps, Action f)
    {
        var t = new List<double>();
        for (int i = 0; i < reps; i++)
        {
            var sw = Stopwatch.StartNew();
            f();
            t.Add(sw.Elapsed.TotalMilliseconds);
        }
        t.Sort();
        return (t[0], t[t.Count / 2]);
    }

    static string Ms((double best, double median) t) => $"{t.best,8:0.0} ms (median {t.median:0.0})";

    [Fact(DisplayName = "save speed")]
    public void Measure()
    {
        var out_ = new List<string>();
        int reps = int.Parse(Env("REPS") ?? "5");
        string path = Path.Combine(Node.Root, "cs/Harness/out/save-speed.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        out_.Add($"save speed, seed 42, best of {reps} (median), Release");
        foreach (var spec in (Env("SAVES") ?? "256:start,512:start,512:all,1024:all,2048:start,2048:all").Split(','))
        {
            var p = spec.Split(':');
            int size = int.Parse(p[0]);
            bool all = p[1] == "all";
            var s = Played(size, all);
            int generated = s.world!.generated.Count(g => g == 1);
            int remade = SavefileChunks.changed(s).Count;
            out_.Add($"{size} {(all ? "every chunk" : "the start only")} ({generated} chunks generated, {remade} changed since; furniture {s.furniture.Count:N0}, dormant {s.dormant.Count:N0}, items {s.items.Count:N0}):");

            string chunked = "";
            var save = Time(reps, () => chunked = Savefile.toSaveFile(s));
            LoadResult back = null!;
            var load = Time(reps, () => back = Savefile.loadFile(chunked));
            string again = back.ok ? Savefile.toSaveFile(back.state) : "";
            out_.Add($"   chunked: save {Ms(save)}, load {Ms(load)}, {chunked.Length / 1e6:0.000} MB; {(back.ok ? "loads" : back.error)}{(again == chunked ? ", saves again the same" : ", SAVES AGAIN DIFFERENT")}");

            if (size <= int.Parse(Env("WHOLE_MAX") ?? "2048"))
            {
                var w = s.world;
                s.world = null;
                string whole = "";
                var wsave = Time(reps, () => whole = Savefile.toSaveFile(s));
                LoadResult wback = null!;
                var wload = Time(reps, () => wback = Savefile.loadFile(whole));
                out_.Add($"   whole:   save {Ms(wsave)}, load {Ms(wload)}, {whole.Length / 1e6:0.000} MB; {(wback.ok ? "loads" : wback.error)}");
                s.world = w;
            }

            // The parts, each on its own: the state as plain JSON text and as nodes, chunks made again, the preview.
            var text = Time(reps, () => JsonSerializer.Serialize(s, StateJson.Options));
            var nodes = Time(reps, () => JsonSerializer.SerializeToNode(s, StateJson.Options)!.ToJsonString(StateJson.Options));
            var l = Worldgen.layout(42, size, s.settings.map);
            int n = (int)s.world!.chunks;
            var remake = Time(Math.Min(reps, 2), () =>
            {
                for (int i = 0; i < n * n; i++)
                    if (s.world.generated[i] == 1) Worldgen.makeChunk(s, l, i % n, i / n);
            });
            var preview = Time(reps, () => WorldPreview.ofSave(s));
            // A bug report's start: the packed state (Savefile.bugStart) against the whole copy the game kept before.
            Savefile.BugStart start = null!;
            var packed = Time(reps, () => start = Savefile.bugStart(s));
            var copy = Time(Math.Min(reps, 3), () => States.cloneState(s));
            out_.Add($"   bug report's start: packed {Ms(packed)}, {start.Length / 1e6:0.000} MB; a whole copy (cloneState, as before) {Ms(copy)}, {JsonSerializer.Serialize(s, StateJson.Options).Length / 1e6:0.0} MB as JSON");
            out_.Add($"   parts: state as text {Ms(text)}; as nodes then text {Ms(nodes)}; every generated chunk made again {Ms(remake)}; preview {Ms(preview)}");
            File.WriteAllText(path, string.Join("\n", out_) + "\n");
        }
    }
}
