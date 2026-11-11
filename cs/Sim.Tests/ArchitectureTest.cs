// The architecture rules (CLAUDE.md), checked against the source files themselves, so a change that breaks one
// fails the check instead of waiting for a reviewer:
//   - the sim has no Godot, no wall clock and no unseeded randomness (rules 1 to 3);
//   - every sim file sits in one area folder, opens with a comment, and is on the map (docs/modules.md);
//   - what is saved is declared in State/ (rule 7): no system's file adds a property to a state type;
//   - the game changes the sim only through commands, ticks and Staging (rule 5).
// These read text, not the compiler's model: they are tripwires for the common slips, not proofs. Comments are
// stripped first (a file may say "no Godot here").
#nullable disable warnings
using System.Text.RegularExpressions;
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class ArchitectureTest
{
    static readonly string ROOT = Path.GetDirectoryName(Js.RepoFile("CLAUDE.md"))!;

    /// <summary>The sim's folders: an area each (docs/modules.md has a section for every one).</summary>
    static readonly string[] AREAS = ["Js", "State", "Core", "Save", "Grid", "Debug", "Movement", "Worldgen", "World", "Combat",
        "Dead", "Items", "Colony", "Survivors", "You", "Others"];

    static List<string> Sources(string folder) =>
        Directory.GetFiles(Path.Combine(ROOT, folder), "*.cs", SearchOption.AllDirectories)
            .Select((f) => Path.GetRelativePath(ROOT, f).Replace('\\', '/'))
            .Where((f) => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("/.godot/"))
            .OrderBy((f) => f, StringComparer.Ordinal)
            .ToList();

    /// <summary>A file's code without its comments (line comments and doc comments; the sim has no block comments to speak of).</summary>
    static string Code(string file)
    {
        var lines = File.ReadAllLines(Path.Combine(ROOT, file));
        return string.Join("\n", lines.Select((l) =>
        {
            int i = l.IndexOf("//", StringComparison.Ordinal);
            return i < 0 ? l : l[..i];
        }));
    }

    static List<string> Offences(IEnumerable<string> files, params (string what, string pattern)[] rules)
    {
        var found = new List<string>();
        foreach (var f in files)
        {
            var code = Code(f);
            foreach (var (what, pattern) in rules)
            {
                var m = Regex.Match(code, pattern);
                if (m.Success) found.Add($"{f}: {what} ({m.Value.Trim()})");
            }
        }
        return found;
    }

    [Fact(DisplayName = "architecture > the sim has no Godot, no wall clock and no unseeded randomness")]
    public void Sim_is_pure()
    {
        var found = Offences(Sources("cs/Sim"),
            ("Godot in the sim", @"\busing\s+Godot\b|\bGodot\.[A-Z]"),
            ("unseeded randomness (use Rng.cs or Hash.hashRoll)", @"\bSystem\.Random\b|\bnew\s+Random\s*\(|\bRandom\.Shared\b|\bGuid\.NewGuid\b"),
            ("the wall clock (time is ticks)", @"\bDateTime\.(Now|UtcNow|Today)\b|\bStopwatch\b|\bEnvironment\.TickCount"));
        expect(string.Join("\n", found)).toBe("");
    }

    [Fact(DisplayName = "architecture > every sim file is in an area folder, opens with a comment and is in docs/modules.md")]
    public void Sim_files_are_on_the_map()
    {
        var map = File.ReadAllText(Path.Combine(ROOT, "docs/modules.md"));
        var found = new List<string>();
        foreach (var f in Sources("cs/Sim"))
        {
            var parts = f.Split('/'); // cs, Sim, Area, Name.cs
            if (parts.Length != 4 || !AREAS.Contains(parts[2])) { found.Add($"{f}: not in an area folder (cs/Sim/<Area>/<Name>.cs)"); continue; }
            var name = Path.GetFileNameWithoutExtension(f);
            if (!Regex.IsMatch(map, $@"\b{Regex.Escape(name)}\b")) found.Add($"{f}: not in docs/modules.md");
            if (!File.ReadLines(Path.Combine(ROOT, f)).First().StartsWith("//")) found.Add($"{f}: no opening comment saying what it is");
        }
        foreach (var area in AREAS)
            if (!map.Contains($"### `{area}/`")) found.Add($"docs/modules.md has no section for {area}/");
        expect(string.Join("\n", found)).toBe("");
    }

    [Fact(DisplayName = "architecture > what is saved is declared in State/, not added to a state type from a system's file")]
    public void Saved_state_is_declared_in_State()
    {
        // A state type is a partial class, so any file can add a saved property to it, where nobody reading
        // StateTypes.cs would see it (and where its place in the JSON hangs on the order files are compiled in).
        var found = new List<string>();
        foreach (var f in Sources("cs/Sim").Where((f) => !f.StartsWith("cs/Sim/State/")))
        {
            var code = Code(f);
            foreach (Match m in Regex.Matches(code, @"(?<!static )\bpartial class (\w+)[^{;]*\{"))
            {
                int depth = 1, i = m.Index + m.Length;
                while (i < code.Length && depth > 0) { if (code[i] == '{') depth++; else if (code[i] == '}') depth--; i++; }
                var body = code[(m.Index + m.Length)..i];
                var saved = Regex.Match(body, @"^(?!.*\[JsonIgnore\]).*\bpublic\b[^(\n]*\{ get; set; \}", RegexOptions.Multiline);
                if (saved.Success) found.Add($"{f}: {m.Groups[1].Value} gains a saved property outside State/ ({saved.Value.Trim()})");
            }
        }
        expect(string.Join("\n", found)).toBe("");
    }

    [Fact(DisplayName = "architecture > the game changes the sim only through commands, ticks and Staging")]
    public void Game_does_not_write_state()
    {
        // The self-test sets its fixtures on the state (it is a test) and the labs and benches build worlds of their own.
        var game = Sources("godot").Where((f) => !f.StartsWith("godot/SelfTest/") && !f.StartsWith("godot/Labs/"));
        var found = Offences(game,
            ("the game writes a state field (make it a command, or a Staging call for a debug scene)", @"\bstate\.[A-Za-z_.]+(\[[^\]\n]*\])?\s*(\+\+|--|[+\-*/]?=(?![=>]))"),
            ("the game changes a state list", @"\bstate\.[A-Za-z_]+\.(Add|Remove|RemoveAt|RemoveAll|Clear|Insert|push|pop|splice|shift|unshift)\s*\("),
            ("the game calls a sim function that changes the world", @"\b(Items\.spawnItem|FurnitureMod\.addFurniture|Vehicles\.addVehicle|FireMod\.ignite|Inventory\.stow|Zombies\.spawnZombie)\s*\("));
        expect(string.Join("\n", found)).toBe("");
    }
}
