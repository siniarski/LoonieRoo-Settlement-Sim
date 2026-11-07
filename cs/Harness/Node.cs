// The bits of Node the translated harness files use (fs, performance, process.env, the inspector), in C#.
// Paths are relative to the repo root, as when `npm run harness` runs from there.

using System.Diagnostics;
using LoonieRoo.Sim;

namespace LoonieRoo.Harness;

public static class Node
{
    /// <summary>The repo root: the nearest folder up from the build output with a CLAUDE.md.</summary>
    public static readonly string Root = FindRoot();

    static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CLAUDE.md"))) return d.FullName;
        return Directory.GetCurrentDirectory();
    }

    static string At(string path) => Path.IsPathRooted(path) ? path : Path.Combine(Root, path);

    public sealed class MkdirOptions { public bool recursive { get; set; } }

    public static void mkdirSync(string path, MkdirOptions? options = null) => Directory.CreateDirectory(At(path));

    /// <summary>Returns null (JS: undefined), so `() => writeFileSync(...)` translates as a function with a value.</summary>
    public static object? writeFileSync(string path, string text)
    {
        File.WriteAllText(At(path), text);
        return null;
    }

    public static object? writeFileSync(object path, string text) => writeFileSync(path.ToString()!, text);

    public static class performance
    {
        static readonly Stopwatch clock = Stopwatch.StartNew();
        public static double now() => clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>process.env: the environment variables the harness files read (null when unset, as JS's undefined).</summary>
    public sealed class Env
    {
        static string? Get(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
        public string? OUT => Get("OUT");
        public string? BUDGET_MS => Get("BUDGET_MS");
        public string? DAYS => Get("DAYS");
        public string? SEEDS => Get("SEEDS");
        public string? PRESET => Get("PRESET");
        public string? TRACE => Get("TRACE");
        public string? TRACE_EVERY => Get("TRACE_EVERY");
        public string? NO_BARRICADE => Get("NO_BARRICADE");
        public string? PROFILE_DAY => Get("PROFILE_DAY");
        public string? ONLY => Get("ONLY");
        public string? TICKS => Get("TICKS");
        public string? LOG => Get("LOG");
        public string? LOG_AT => Get("LOG_AT");
    }

    public static class process
    {
        public static readonly Env env = new();
    }
}
