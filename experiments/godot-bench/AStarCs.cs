// The pathfinding benchmark in C#, inside Godot: the same A* (4 ways, Manhattan, a binary heap of
// f * 2^21 + tile, stale entries skipped) over the same pairs as the TypeScript, GDScript and C++ copies.
// Run: godot --headless --path experiments/godot-bench res://astarcs.tscn -- --size=128|1024
using System;
using System.Diagnostics;
using System.Text.Json;
using Godot;

public partial class AStarCs : Node
{
    const long SHIFT = 2097152;

    public override void _Ready()
    {
        int size = 128;
        foreach (var a in OS.GetCmdlineUserArgs()) if (a.StartsWith("--size=")) size = int.Parse(a.Substring(7));
        using var world = JsonDocument.Parse(Godot.FileAccess.GetFileAsString($"res://data/world-{size}.json"));
        using var pairsDoc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString($"res://data/pairs-{size}.json"));
        string tiles = world.RootElement.GetProperty("tiles").GetString()!;
        int n = size * size;
        var walk = new byte[n];
        for (int i = 0; i < n; i++) { char t = tiles[i]; walk[i] = (byte)(t == '.' || t == 'D' || t == 'B' ? 1 : 0); }
        var list = new System.Collections.Generic.List<int>();
        foreach (var p in pairsDoc.RootElement.GetProperty("pairs").EnumerateArray()) list.Add(p.GetInt32());
        var pairs = list.ToArray();
        var g = new int[n];
        var seen = new int[n];
        var heap = new long[n * 4];
        int stamp = 0;
        for (int k = 0; k + 1 < pairs.Length; k += 2) AStar(walk, size, pairs[k], pairs[k + 1], g, seen, heap, ref stamp); // warm up (the JIT)
        var sw = Stopwatch.StartNew();
        long steps = 0;
        for (int k = 0; k + 1 < pairs.Length; k += 2) steps += AStar(walk, size, pairs[k], pairs[k + 1], g, seen, heap, ref stamp);
        sw.Stop();
        GD.Print($"BENCH {size} cs_astar paths={pairs.Length / 2} ms={sw.Elapsed.TotalMilliseconds:F1} steps={steps}");
        GetTree().Quit();
    }

    static int AStar(byte[] walk, int size, int start, int goal, int[] g, int[] seen, long[] heap, ref int stampRef)
    {
        int stamp = ++stampRef;
        int n = walk.Length;
        int gx = goal % size, gy = goal / size;
        int len = 0;
        g[start] = 0;
        seen[start] = stamp;
        heap[len++] = (Math.Abs(start % size - gx) + Math.Abs(start / size - gy)) * SHIFT + start;
        while (len > 0)
        {
            long key = heap[0];
            len--;
            if (len > 0)
            {
                heap[0] = heap[len];
                int k = 0;
                for (;;)
                {
                    int l = 2 * k + 1, r = l + 1, m = k;
                    if (l < len && heap[l] < heap[m]) m = l;
                    if (r < len && heap[r] < heap[m]) m = r;
                    if (m == k) break;
                    (heap[m], heap[k]) = (heap[k], heap[m]);
                    k = m;
                }
            }
            int i = (int)(key % SHIFT);
            long f = key / SHIFT;
            int x = i % size;
            if (f > g[i] + Math.Abs(x - gx) + Math.Abs(i / size - gy)) continue;
            if (i == goal) return g[i];
            int ng = g[i] + 1;
            for (int d = 0; d < 4; d++)
            {
                int j = d == 0 ? (x > 0 ? i - 1 : -1) : d == 1 ? (x < size - 1 ? i + 1 : -1) : d == 2 ? i - size : i + size;
                if (j < 0 || j >= n || walk[j] == 0) continue;
                if (seen[j] == stamp && g[j] <= ng) continue;
                seen[j] = stamp;
                g[j] = ng;
                int c = len++;
                heap[c] = (ng + Math.Abs(j % size - gx) + Math.Abs(j / size - gy)) * SHIFT + j;
                while (c > 0)
                {
                    int p = (c - 1) >> 1;
                    if (heap[p] <= heap[c]) break;
                    (heap[p], heap[c]) = (heap[c], heap[p]);
                    c = p;
                }
            }
        }
        return -1;
    }
}
