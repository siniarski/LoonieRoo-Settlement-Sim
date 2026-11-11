// A read-only observer for the balance players (2026-11-10, balance check): every event (the state keeps only
// the last 50), each death with the dying one's last moments, drinks by source, prey caught and meat it gave.
// Set PROBE=path to write it (path.txt the summary and deaths, path.events-s<seed>.txt every event). It never changes
// the state, so a run with it is the same run as without.
#nullable disable warnings
using LoonieRoo.Sim;
namespace LoonieRoo.Harness;

public sealed class Probe
{
    sealed class Snap
    {
        public string id, name, note, job, drink;
        public Point pos;
        public double hydration, satiety, health, tick;
        public double? sick;
        public readonly List<(double tick, string note)> notes = new();
        public double lastDrank = -1, thirstySince = -1, dryFrom = -1;
        public readonly Dictionary<string, int> drinks = new();
    }

    readonly string path;
    readonly double seed;
    readonly Point start;
    readonly List<string> events = new();
    readonly List<string> deaths = new();
    readonly Dictionary<string, Snap> byId = new();
    readonly SortedDictionary<string, int> deathsBy = new(StringComparer.Ordinal);
    readonly SortedDictionary<string, int> drinks = new(StringComparer.Ordinal);
    readonly SortedDictionary<string, int> caught = new(StringComparer.Ordinal);
    readonly SortedDictionary<string, int> meat = new(StringComparer.Ordinal);
    int riverSick, wolfAttacks, deerSeen;
    // Thirst against the other needs, over everyone: a meal started while thirsty, a drink under way dropped for
    // a meal, bed while thirsty, run dry asleep.
    int ateThirsty, drinkDropped, bedThirsty, dryAsleep;
    readonly Dictionary<string, (bool drink, bool meal, bool sleep, double hydration)> was = new();
    GameEvent last;
    object lastBusy;
    List<(string id, string kind, Point pos)> animals = new();
    double nextIdBefore;

    Probe(string path, double seed, Point start)
    {
        this.path = path;
        this.seed = seed;
        this.start = start;
    }

    /// <summary>A probe for this run when PROBE is set, else null (every call below is then a no-op).</summary>
    public static Probe begin(SimState state, double seed, Point start)
    {
        var p = Environment.GetEnvironmentVariable("PROBE");
        if (string.IsNullOrEmpty(p))
        {
            return null;
        }
        var probe = new Probe(p, seed, start);
        probe.last = state.events.Count > 0 ? state.events[^1] : null;
        return probe;
    }

    /// <summary>Call just before Tick.tick.</summary>
    public void before(SimState state)
    {
        foreach (var s in state.survivors)
        {
            if (!byId.TryGetValue(s.id, out var snap))
            {
                byId[s.id] = snap = new Snap { id = s.id, name = s.name };
            }
            if (snap.note != s.note)
            {
                snap.notes.Add((state.tick, s.note));
                if (snap.notes.Count > 40)
                {
                    snap.notes.RemoveAt(0);
                }
                if (s.note != null && s.note.StartsWith("drinking from "))
                {
                    var src = s.note.Substring("drinking from ".Length);
                    drinks[src] = (drinks.TryGetValue(src, out var n) ? n : 0) + 1;
                    snap.drinks[src] = (snap.drinks.TryGetValue(src, out var m) ? m : 0) + 1;
                }
            }
            if (s.hydration > snap.hydration && snap.tick > 0)
            {
                snap.lastDrank = state.tick;
                snap.thirstySince = -1;
                snap.dryFrom = -1;
            }
            if (s.hydration < Water.THIRSTY && snap.thirstySince < 0)
            {
                snap.thirstySince = state.tick;
            }
            if (s.hydration == 0 && snap.dryFrom < 0)
            {
                snap.dryFrom = state.tick;
            }
            snap.note = s.note;
            snap.pos = new Point { x = s.pos.x, y = s.pos.y };
            snap.hydration = s.hydration;
            snap.satiety = s.satiety;
            snap.health = s.health;
            snap.sick = s.sick;
            snap.tick = state.tick;
            snap.job = s.jobId == null ? "-" : (state.jobs.find((j) => j.id == s.jobId)?.type ?? "?");
            snap.drink = s.drink == null ? "-" : $"{s.drink.source}@{s.drink.x},{s.drink.y}";
        }
        // You (direct control) drink by "use": count each drink once, by what's at its target.
        var busy = state.control?.busy;
        if (busy != null && busy.what == "drink" && !ReferenceEquals(busy, lastBusy))
        {
            lastBusy = busy;
            var xy = busy.targetId.Split(',');
            var src = Water.sourceAt(state, int.Parse(xy[0]), int.Parse(xy[1])) ?? "nothing";
            src = src == "river" ? "the river" : $"a {src}";
            drinks[src] = (drinks.TryGetValue(src, out var n) ? n : 0) + 1;
        }
        was.Clear();
        foreach (var s in state.survivors)
        {
            was[s.id] = (s.drink != null, s.meal != null, s.sleep != null, s.hydration);
        }
        animals = state.animals.Select((a) => (a.id, a.kind, new Point { x = a.pos.x, y = a.pos.y })).ToList();
        nextIdBefore = state.nextId;
    }

    /// <summary>Call just after Tick.tick (and after the player's commands for the tick, if any).</summary>
    public void after(SimState state)
    {
        foreach (var e in newEvents(state, ref last))
        {
            events.Add($"{e.tick} {e.message}");
            onEvent(state, e.message);
        }
        foreach (var s in state.survivors)
        {
            if (!was.TryGetValue(s.id, out var w))
            {
                continue;
            }
            if (!w.meal && s.meal != null && s.hydration < Water.THIRSTY)
            {
                ateThirsty++;
            }
            if (w.drink && s.drink == null && s.meal != null)
            {
                drinkDropped++;
            }
            if (!w.sleep && s.sleep != null && s.hydration < Water.THIRSTY)
            {
                bedThirsty++;
            }
            if (w.hydration > 0 && s.hydration == 0 && s.sleep != null)
            {
                dryAsleep++;
            }
        }
        // Prey caught: an animal gone this tick with new food beside it.
        var fresh = state.items.Where((it) => it.kind == "food" && idNumber(it.id) >= nextIdBefore).ToList();
        if (fresh.Count > 0)
        {
            foreach (var (id, kind, pos) in animals)
            {
                if (state.animals.Any((a) => a.id == id))
                {
                    continue;
                }
                var near = fresh.Count((it) => it.pos != null && Math.Max(Math.Abs(it.pos.x - pos.x), Math.Abs(it.pos.y - pos.y)) <= 3);
                if (near == 0)
                {
                    continue;
                }
                caught[kind] = (caught.TryGetValue(kind, out var c) ? c : 0) + 1;
                meat[kind] = (meat.TryGetValue(kind, out var m) ? m : 0) + near;
            }
        }
    }

    /// <summary>The events logged since `last` (by reference: the state keeps only the last 50, dropping the oldest); moves `last` on.</summary>
    public static List<GameEvent> newEvents(SimState state, ref GameEvent last)
    {
        var from = last == null ? 0 : state.events.LastIndexOf(last) + 1;
        var fresh = state.events.Skip(from).ToList();
        last = state.events.Count > 0 ? state.events[^1] : null;
        return fresh;
    }

    static double idNumber(string id)
    {
        return double.TryParse(id.AsSpan(1), out var n) ? n : -1;
    }

    void onEvent(SimState state, string msg)
    {
        if (msg.Contains("got sick from river water"))
        {
            riverSick++;
        }
        if (msg.StartsWith("Wolves attacked") || msg.StartsWith("A bear attacked"))
        {
            wolfAttacks++;
        }
        if (msg.StartsWith("Deer seen"))
        {
            deerSeen++;
        }
        var at = msg.IndexOf(" died of ", StringComparison.Ordinal);
        if (at < 0)
        {
            return;
        }
        var name = msg.Substring(0, at);
        var cause = msg.Substring(at + " died of ".Length);
        deathsBy[cause] = (deathsBy.TryGetValue(cause, out var n) ? n : 0) + 1;
        // Names repeat: the one of that name who was here before this tick and isn't now.
        var s = byId.Values.Where((q) => q.name == name && !state.survivors.Any((o) => o.id == q.id)).OrderByDescending((q) => q.tick).FirstOrDefault();
        if (s == null)
        {
            deaths.Add($"  death t{state.tick} day {Time.dayOf(state.tick)} {name}: {cause} (never seen)");
            return;
        }
        var line = $"  death t{state.tick} day {Time.dayOf(state.tick)} {name}: {cause} at {s.pos.x},{s.pos.y} ({Math.Max(Math.Abs(s.pos.x - start.x), Math.Abs(s.pos.y - start.y))} from the start) hp {s.health} water {s.hydration} food {s.satiety} sick {(s.sick == null ? "-" : s.sick.ToString())} job {s.job} drink {s.drink}";
        deaths.Add(line);
        if (cause != "thirst")
        {
            return;
        }
        // Thirst: where the water was and what they tried.
        var d = Water.findWater(state, s.pos);
        var path = d == null ? null : Pathfinding.findPathToAny(state.grid, s.pos, [new Point { x = d.x, y = d.y }]);
        double best = double.MaxValue;
        Point near = null;
        for (var y = 0; y < state.grid.height; y++)
        {
            for (var x = 0; x < state.grid.width; x++)
            {
                if (Grids.getTile(state.grid, x, y) != "water")
                {
                    continue;
                }
                var dd = Math.Sqrt(((double)(x - s.pos.x) * (x - s.pos.x)) + ((double)(y - s.pos.y) * (y - s.pos.y)));
                if (dd < best)
                {
                    best = dd;
                    near = new Point { x = x, y = y };
                }
            }
        }
        var wells = state.furniture.Where((f) => f.kind == "well").Select((f) => $"{f.x},{f.y}");
        var collectors = state.furniture.Where((f) => f.kind == "collector").Select((f) => $"{f.x},{f.y}:{f.loot}");
        var wellPlans = state.blueprints.Count((b) => b.target == "well");
        deaths.Add($"    water: findWater {(d == null ? "none" : $"{d.source}@{d.x},{d.y} path {(path == null ? "none" : path.Count.ToString())}")}; nearest open water {(near == null ? "none" : $"{near.x},{near.y} {best:0} tiles")}; nearest open water from the start {nearestWater(state, start)}; wells [{string.Join(" ", wells)}] (blueprints {wellPlans}) collectors [{string.Join(" ", collectors)}] taps {(TimelineMod.waterOn(state) ? "on" : "off")}");
        deaths.Add($"    last drank t{s.lastDrank} thirsty since t{s.thirstySince} dry from t{s.dryFrom}; drinks {string.Join(" ", s.drinks.Select((kv) => $"{kv.Key} {kv.Value}"))}");
        foreach (var (t, note) in s.notes.Where((n) => n.tick >= s.tick - 2400))
        {
            deaths.Add($"      t{t} {note}");
        }
    }

    static string nearestWater(SimState state, Point from)
    {
        double best = double.MaxValue;
        for (var y = 0; y < state.grid.height; y++)
        {
            for (var x = 0; x < state.grid.width; x++)
            {
                if (Grids.getTile(state.grid, x, y) == "water")
                {
                    best = Math.Min(best, Math.Sqrt(((double)(x - from.x) * (x - from.x)) + ((double)(y - from.y) * (y - from.y))));
                }
            }
        }
        return best == double.MaxValue ? "none" : $"{best:0} tiles";
    }

    /// <summary>Write the run's summary (call once at the end, alive or not).</summary>
    public void end(SimState state, string outcome)
    {
        string fmt(SortedDictionary<string, int> d) => d.Count == 0 ? "-" : string.Join(",", d.Select((kv) => $"{kv.Key} {kv.Value}"));
        var lines = new List<string>
        {
            $"seed {seed} {outcome} day {Time.dayOf(state.tick)} alive {state.survivors.Count} | deaths {fmt(deathsBy)} | caught {fmt(caught)} meat {fmt(meat)} | drinks {fmt(drinks)} riverSick {riverSick} | wolf/bear attacks {wolfAttacks} deerSeen {deerSeen} | start water {nearestWater(state, start)} | thirsty: ate first {ateThirsty} drink dropped for food {drinkDropped} to bed {bedThirsty} ran dry asleep {dryAsleep}",
        };
        lines.AddRange(deaths);
        File.AppendAllText(path + ".txt", string.Join("\n", lines) + "\n");
        File.AppendAllText(path + $".events-s{seed}.txt", string.Join("\n", events) + "\n");
    }
}
