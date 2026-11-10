// The grid's readers one at a time (2026-11-10, the grid in chunks): each operation repeated on one fixed state, the
// best of several rounds, so two builds can be compared on a busy machine where whole ticks are too noisy to call a
// few percent. It uses only calls both the old and the new grid have (so the same file builds against either).
//
//   dotnet test cs/Harness -c Release --filter "DisplayName=micro"     (SCENE=colony-512, ROUNDS=7; out/micro.txt)
using System.Diagnostics;
using LoonieRoo.Sim;

namespace LoonieRoo.Harness;

public class Micro
{
    static string? Env(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    [Fact(DisplayName = "micro")]
    public void Run()
    {
        var name = Env("SCENE") ?? "colony-512";
        int rounds = int.Parse(Env("ROUNDS") ?? "7");
        var state = Profile.SCENES.First((x) => x.name == name).build();
        Pathfinding.warmReach(state.grid, state.walkabilityVersion);
        for (int t = 0; t < 60; t++) Tick.tick(state);
        var grid = state.grid;
        var home = state.survivors[0].pos;
        int w = grid.width;
        var out_ = new List<string> { $"{name}: size {w}, survivors {state.survivors.Count}, zombies {state.zombies.Count}, rooms {Rooms.getRoomMap(state).rooms.Count}, tick {state.tick}" };
        double sink = 0;

        // The best of `rounds` runs of `f` (which does `n` operations): nanoseconds an operation.
        void T(string what, int n, Action f)
        {
            f();
            double best = double.MaxValue;
            for (int r = 0; r < rounds; r++)
            {
                long t0 = Stopwatch.GetTimestamp();
                f();
                best = Math.Min(best, (Stopwatch.GetTimestamp() - t0) * 1e9 / Stopwatch.Frequency / n);
            }
            out_.Add($"   {what,-46} {best,10:0.0} ns");
        }

        // A 48 x 48 box round the camp: what the colony's systems ask about.
        int x0 = Math.Max(0, home.x - 24), y0 = Math.Max(0, home.y - 24), box = 48;
        T("Grids.getTile (box)", box * box, () => { for (int y = y0; y < y0 + box; y++) for (int x = x0; x < x0 + box; x++) if (Grids.getTile(grid, x, y) == "wall") sink++; });
        T("Grids.isWalkable (box)", box * box, () => { for (int y = y0; y < y0 + box; y++) for (int x = x0; x < x0 + box; x++) if (Grids.isWalkable(grid, x, y)) sink++; });
        T("Pathfinding.isPassable zombie (box)", box * box, () => { for (int y = y0; y < y0 + box; y++) for (int x = x0; x < x0 + box; x++) if (Pathfinding.isPassable(grid, x, y, "zombie")) sink++; });
        T("Pathfinding.crossable east+south (box)", 2 * box * box, () =>
        {
            for (int y = y0; y < y0 + box; y++)
                for (int x = x0; x < x0 + box; x++)
                {
                    if (Pathfinding.crossable(grid, x, y, x + 1, y)) sink++;
                    if (Pathfinding.crossable(grid, x, y, x, y + 1, "zombie")) sink++;
                }
        });
        T("Rooms.labelAt (box)", box * box, () => { for (int y = y0; y < y0 + box; y++) for (int x = x0; x < x0 + box; x++) sink += Rooms.labelAt(state, x, y); });
        T("Rooms.isIndoors (survivors)", state.survivors.Count, () => { foreach (var s in state.survivors) if (Rooms.isIndoors(state, s.pos.x, s.pos.y)) sink++; });
        T("Fog.isExplored (box)", box * box, () => { for (int y = y0; y < y0 + box; y++) for (int x = x0; x < x0 + box; x++) if (Fog.isExplored(state, x, y)) sink++; });
        T("Grids.isDirty (box)", box * box, () => { for (int y = y0; y < y0 + box; y++) for (int x = x0; x < x0 + box; x++) if (Grids.isDirty(grid, x, y)) sink++; });
        T("Sight.sightLine (to 64 tiles round)", 64, () =>
        {
            for (int k = 0; k < 64; k++)
            {
                var b = new Point(Math.Clamp(home.x + (k % 8 - 4) * 3, 0, w - 1), Math.Clamp(home.y + (k / 8 - 4) * 3, 0, grid.height - 1));
                if (Sight.sightLine(grid, home, b)) sink++;
            }
        });
        T("Sight.sightOf (each survivor)", state.survivors.Count, () => { foreach (var s in state.survivors) sink += Sight.sightOf(state, s).Count; });
        T("Sight.zombieSees (zombies x 4 survivors)", state.zombies.Count * 4, () =>
        {
            foreach (var z in state.zombies)
                for (int k = 0; k < 4 && k < state.survivors.Count; k++) if (Sight.zombieSees(state, z.pos, state.survivors[k].pos)) sink++;
        });
        var targets = Enumerable.Range(0, 16).Select((k) => new Point(Math.Clamp(home.x + (k % 4 - 2) * 9 + 3, 1, w - 2), Math.Clamp(home.y + (k / 4 - 2) * 9 + 3, 1, grid.height - 2))).ToList();
        T("Pathfinding.findPath (16 goals round the camp)", 16, () => { foreach (var g in targets) sink += Pathfinding.findPath(grid, home, g)?.Count ?? 0; });
        T("Pathfinding.findPathToAny zombie, 400 expands", 16, () => { foreach (var g in targets) sink += Pathfinding.findPathToAny(grid, g, [home], "zombie", 400)?.Count ?? 0; });
        T("Pathfinding.canReach survivor (16 goals)", 16, () => { foreach (var g in targets) if (Pathfinding.canReach(grid, state.walkabilityVersion, home, [g])) sink++; });
        T("Pathfinding.canReach zombie (zombies -> camp)", state.zombies.Count, () => { foreach (var z in state.zombies) if (Pathfinding.canReach(grid, state.walkabilityVersion, z.pos, [home], "zombie")) sink++; });
        T("Space.nearestTile (walkable, limit 200)", 16, () => { foreach (var g in targets) sink += Space.nearestTile(grid, g, (x, y) => (x + y) % 37 == 0, 200)?.x ?? 0; });
        T("Motion.inWindowFrame (movers)", state.zombies.Count, () => { foreach (var z in state.zombies) if (Motion.inWindowFrame(grid, z.at)) sink++; });

        // A wall put up and taken down beside the camp: what each change costs the derived data.
        var spot = Space.nearestTile(grid, new Point(home.x + 6, home.y + 6), (x, y) => Grids.getTile(grid, x, y) == "floor" && FurnitureMod.furnitureAt(state, x, y) == null) ?? home;
        bool up = false;
        void Toggle()
        {
            up = !up;
            Grids.setTile(grid, spot.x, spot.y, up ? "wall" : "floor");
            state.walkabilityVersion++;
        }
        T("a wall changed, then Rooms.getRoomMap", 1, () => { Toggle(); sink += Rooms.getRoomMap(state).rooms.Count; });
        T("a wall changed, then canReach (survivor)", 1, () => { Toggle(); if (Pathfinding.canReach(grid, state.walkabilityVersion, home, [targets[0]])) sink++; });
        T("a wall changed, then canReach (six movers)", 1, () =>
        {
            Toggle();
            foreach (var m in new[] { "survivor", "free", "zombie", "breacher", "raider", "crawler" }) if (Pathfinding.canReach(grid, state.walkabilityVersion, home, [targets[0]], m)) sink++;
        });
        if (up) Toggle();
        T("Tick.tick", 1, () => Tick.tick(state));
        T("Savefile.toSaveFile", 1, () => sink += Savefile.toSaveFile(state).Length);
        out_.Add($"   (sink {sink})");
        string path = Path.Combine(Node.Root, Env("OUT") ?? "cs/Harness/out/micro.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", out_) + "\n");
    }
}
