// The bits of Node the harness files translated from TypeScript use (fs, performance, process.env), in C#, and what
// every harness file shares: the repo's root (paths are relative to it) and the reports' folder, made on first use
// (cs/Harness/out is not in git, so a fresh checkout has none).

using System.Diagnostics;
using LoonieRoo.Sim;

// One at a time, as the sim's tests are: the sim has process-wide state (the clock's scale, caches), and timings
// taken beside another run mean nothing. (A bare `dotnet test cs/Harness` would otherwise run its classes together.)
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace LoonieRoo.Harness;

public static class Node
{
    /// <summary>The repo root: the nearest folder up from the build output with a CLAUDE.md.</summary>
    public static readonly string Root = FindRoot();

    static Node() => Directory.CreateDirectory(Path.Combine(Root, "cs/Harness/out"));

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
        public string? SNOW_SLOW => Get("SNOW_SLOW");
        public string? SEEDS => Get("SEEDS");
        public string? PRESET => Get("PRESET");
        public string? TRACE => Get("TRACE");
        public string? TRACE_EVERY => Get("TRACE_EVERY");
        public string? NO_BARRICADE => Get("NO_BARRICADE");
        public string? LOG => Get("LOG");
        public string? LOG_AT => Get("LOG_AT");
        public string? WILDFIRE => Get("WILDFIRE");
    }

    public static class process
    {
        public static readonly Env env = new();
    }
}
