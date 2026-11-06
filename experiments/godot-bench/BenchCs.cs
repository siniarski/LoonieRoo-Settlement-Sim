// The sim benchmark in C#, inside Godot: the same 177 BFS floods over the same exported 128 world as
// bench.gd and harness/godotexport.harness.ts. Prints a BENCH line and quits.
// Run: godot --headless --path experiments/godot-bench res://benchcs.tscn
using System;
using System.Diagnostics;
using System.Text.Json;
using Godot;

public partial class BenchCs : Node
{
    public override void _Ready()
    {
        var text = Godot.FileAccess.GetFileAsString("res://data/world-128.json");
        using var doc = JsonDocument.Parse(text);
        int size = doc.RootElement.GetProperty("size").GetInt32();
        string tiles = doc.RootElement.GetProperty("tiles").GetString()!;
        int n = size * size;
        var walk = new byte[n];
        for (int i = 0; i < n; i++) { char t = tiles[i]; walk[i] = (byte)(t == '.' || t == 'D' || t == 'B' ? 1 : 0); }
        var dist = new int[n];
        var queue = new int[n];
        var starts = new System.Collections.Generic.List<int>();
        for (int i = 0; i < n && starts.Count < 200; i += 81) if (tiles[i] == '.') starts.Add(i);
        for (int k = 0; k < 20; k++) Bfs(walk, size, starts[0], dist, queue); // warm up (the JIT)
        var sw = Stopwatch.StartNew();
        long reached = 0;
        foreach (int s in starts) reached += Bfs(walk, size, s, dist, queue);
        sw.Stop();
        GD.Print($"BENCH cs_bfs_floods={starts.Count} ms={sw.Elapsed.TotalMilliseconds:F1} reached={reached}");
        GetTree().Quit();
    }

    static int Bfs(byte[] walk, int size, int start, int[] dist, int[] queue)
    {
        Array.Fill(dist, -1);
        int n = walk.Length, head = 0, tail = 0;
        queue[tail++] = start;
        dist[start] = 0;
        while (head < tail)
        {
            int c = queue[head++];
            int x = c % size;
            int d = dist[c] + 1;
            if (x > 0) tail = Visit(walk, c - 1, d, dist, queue, tail);
            if (x < size - 1) tail = Visit(walk, c + 1, d, dist, queue, tail);
            if (c >= size) tail = Visit(walk, c - size, d, dist, queue, tail);
            if (c + size < n) tail = Visit(walk, c + size, d, dist, queue, tail);
        }
        return tail;
    }

    static int Visit(byte[] walk, int j, int d, int[] dist, int[] queue, int tail)
    {
        if (dist[j] != -1 || walk[j] == 0) return tail;
        dist[j] = d;
        queue[tail] = j;
        return tail + 1;
    }
}
