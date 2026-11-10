// Where a tick's time and allocations go, system by system (no profiler needed): Tick.tick replayed step by step with
// a clock and GC.GetAllocatedBytesForCurrentThread around each step, the survivors' per-survivor steps and `behave`'s
// steps summed over survivors. Also the whole tick's ticks/s and bytes/tick, and sampled allocations by type (the
// runtime's AllocationTick events, about one per 100 KB). Report: cs/Harness/out/profile.txt.
// Keep the replay in step with Tick.tick and Tick.behave (it must call the same things in the same order).
// Env: SCENES=a,b (default all), TICKS (measured ticks per scene, 300), WARM (warm-up ticks, 20: as the stress test).
//
// "tick dumps": the states of fixed scenes (four seeds: a generated 512 world, a crowded 128, a 512 ringed by the dead) at several ticks, as
// JSON files (DUMP=folder), for checking that a speed change moves nothing: compare two runs' files byte for byte.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using LoonieRoo.Sim;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Harness;

sealed class AllocListener : EventListener
{
    public readonly ConcurrentDictionary<string, long> bytes = new();
    public volatile bool on;

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (!on || e.EventName == null || !e.EventName.StartsWith("GCAllocationTick") || e.Payload == null) return;
        int ti = e.PayloadNames!.IndexOf("TypeName"), ai = e.PayloadNames.IndexOf("AllocationAmount64");
        if (ti < 0) return;
        long amount = ai >= 0 ? Convert.ToInt64(e.Payload[ai]) : 100_000;
        bytes.AddOrUpdate((string)e.Payload[ti]!, amount, (_, v) => v + amount);
    }
}

public class Profile
{
    static string? Env(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    static Point? Near(SimState s, double x, double y) => Space.nearestTile(s.grid, new Point((int)Math.Max(0, Math.Min(s.grid.width - 1, JsMath.round(x))), (int)Math.Max(0, Math.Min(s.grid.height - 1, JsMath.round(y)))), (a, b) => Grids.isWalkable(s.grid, a, b), 4000);

    static SimState Crowd(int seed, int size, int colonists, int zombies, bool generate)
    {
        var s = Worldgen.createWorld(seed, size);
        if (generate) Scenarios.generateAll(s);
        var home = s.survivors[0].pos;
        for (int i = s.survivors.Count; i < colonists; i++)
        {
            if (Near(s, home.x + i % 10 - 5, home.y + i / 10) is { } p) Survivors.addSurvivor(s, p.x, p.y);
        }
        for (int i = 0; i < zombies; i++)
        {
            double a = (double)i / zombies * Math.PI * 2;
            if (Near(s, home.x + Math.Cos(a) * 25, home.y + Math.Sin(a) * 25) is { } p) Zombies.addZombie(s, p.x, p.y, 200, i % 2 == 0 ? "herd" : "static");
        }
        return s;
    }

    public static readonly (string name, Func<SimState> build)[] SCENES =
    {
        ("512", () => Crowd(7, 512, 3, 0, false)),
        ("horde-512", () => Scenarios.STRESS.First((x) => x.name == "horde-512").build()),
        ("colony-512", () => Scenarios.STRESS.First((x) => x.name == "colony-512").build()),
        ("colony-128", () => Crowd(7, 128, 100, 0, false)),
        ("mixed-128", () => Crowd(7, 128, 50, 200, false)),
    };

    // --- The replayed tick. ---
    sealed class Acc { public double ms; public long bytes; public long calls; }
    readonly Dictionary<string, Acc> acc = new();
    readonly List<string> order = new();

    void S(string name, SimState st, Action<SimState> f)
    {
        long b0 = GC.GetAllocatedBytesForCurrentThread(), t0 = Stopwatch.GetTimestamp();
        f(st);
        Add(name, t0, b0);
    }

    T F<T>(string name, Func<T> f)
    {
        long b0 = GC.GetAllocatedBytesForCurrentThread(), t0 = Stopwatch.GetTimestamp();
        var r = f();
        Add(name, t0, b0);
        return r;
    }

    void Add(string name, long t0, long b0)
    {
        long t1 = Stopwatch.GetTimestamp(), b1 = GC.GetAllocatedBytesForCurrentThread();
        if (!acc.TryGetValue(name, out var a)) { a = acc[name] = new Acc(); order.Add(name); }
        a.ms += (t1 - t0) * 1000.0 / Stopwatch.Frequency;
        a.bytes += b1 - b0;
        a.calls++;
    }

    void ReplayTick(SimState state)
    {
        Time.setDayScale(state.settings.dayScale);
        S("movers.was", state, (st) => { foreach (var e in Js.Iter(Tick.movers(st))) e.was = new Point { x = Js.I(e.at.x), y = Js.I(e.at.y) }; });
        if (Env("DEEP") == "zombies") ReplayZombies(state);
        else S("Zombies.updateZombies", state, Zombies.updateZombies);
        S("FireMod.updateFires", state, FireMod.updateFires);
        S("Corpses.updateCorpses", state, Corpses.updateCorpses);
        S("Water.updateCollectors", state, Water.updateCollectors);
        S("Power.updatePower", state, Power.updatePower);
        S("Power.updateHeaters", state, Power.updateHeaters);
        S("Animals.updateAnimals", state, Animals.updateAnimals);
        S("Director.updateDirector", state, Director.updateDirector);
        S("Prisoners.updatePrisoners", state, Prisoners.updatePrisoners);
        S("Radio.updateRadio", state, Radio.updateRadio);
        S("EndgameMod.updateEndgame", state, EndgameMod.updateEndgame);
        S("Escape.updateHelicopter", state, Escape.updateHelicopter);
        S("Policies.updateLeader", state, Policies.updateLeader);
        S("Defences.updateDefences", state, Defences.updateDefences);
        S("Outposts.updateOutposts", state, Outposts.updateOutposts);
        S("Power.updateSpoilage", state, Power.updateSpoilage);
        S("Dogs.updateDogs", state, Dogs.updateDogs);
        S("Talk.updateTasks", state, Talk.updateTasks);
        S("Settlements.updateSettlements", state, Settlements.updateSettlements);
        S("Colonies.updateColonies", state, Colonies.updateColonies);
        S("Caravans.updateCaravans", state, Caravans.updateCaravans);
        S("Runs.updateRuns", state, Runs.updateRuns);
        S("Errands.updateErrands", state, Errands.updateErrands);
        S("Residents.updateResidents", state, Residents.updateResidents);
        S("Family.updateFamily", state, Family.updateFamily);
        S("Herds.updateHerds", state, Herds.updateHerds);
        S("Seasons.updateWeather", state, Seasons.updateWeather);
        S("TimelineMod.updateTimeline", state, TimelineMod.updateTimeline);
        S("Vehicles.updateVehicles", state, Vehicles.updateVehicles);
        S("Relationships.updateRelationships", state, Relationships.updateRelationships);
        S("Wellbeing.updateSocial", state, Wellbeing.updateSocial);
        if ((state.tick % Time.DAY_LENGTH) == 0) S("Handiwork.forgetForaging", state, Handiwork.forgetForaging);
        S("Wishes.updateWishes", state, Wishes.updateWishes);
        S("Expeditions.updateExpeditions", state, Expeditions.updateExpeditions);
        S("Visitors.updateVisitors", state, Visitors.updateVisitors);
        S("Farming.updateFarm", state, Farming.updateFarm);
        S("Wanderers.updateWanderers", state, Wanderers.updateWanderers);
        S("Fog.reconcileExploreAreas", state, Fog.reconcileExploreAreas);
        S("Fog.reconcilePointsOfInterest", state, Fog.reconcilePointsOfInterest);
        S("wake hurt sleepers", state, (st) => { foreach (var s in Js.Iter(st.survivors)) if (Js.T(s.sleep) && (s.hurtTick == st.tick)) Needs.wake(st, s, "woken by a zombie!"); });
        S("Needs.wakeLightSleepers", state, Needs.wakeLightSleepers);
        S("Resolve.dawnResolve", state, (st) => { foreach (var s in Js.Iter(Resolve.dawnResolve(st))) Needs.killSurvivor(st, s, "walked away", true); });
        S("Jobs.reconcileJobs", state, Jobs.reconcileJobs);
        S("Needs.reconcileNeeds", state, Needs.reconcileNeeds);
        List<TickDying> dying = [];
        foreach (var s in Js.Iter(state.survivors))
        {
            if (ControlMod.isControlled(state, s)) S("s: ControlMod.updateControlled", state, (st) => ControlMod.updateControlled(st, s));
            else Behave(state, s);
            S("s: Needs.updateRest", state, (st) => Needs.updateRest(st, s));
            S("s: Needs.updateSatiety", state, (st) => Needs.updateSatiety(st, s));
            S("s: Wounds.updateWounds", state, (st) => Wounds.updateWounds(st, s));
            S("s: Water.updateHydration", state, (st) => Water.updateHydration(st, s));
            S("s: BodyMod.updateBody", state, (st) => BodyMod.updateBody(st, s));
            S("s: Climate.updateRadiation", state, (st) => Climate.updateRadiation(st, s));
            S("s: Towers.countWatch", state, (st) => Towers.countWatch(st, s));
            S("s: Mood.forgetThoughts", state, (st) => Mood.forgetThoughts(st, s));
            var cause = F("s: Needs.updateHealth", () => Needs.updateHealth(state, s)) ?? F("s: Infection.updateInfection", () => Infection.updateInfection(state, s));
            if (Js.T(cause)) dying.push(new() { s = s, cause = cause });
        }
        S("dying", state, (st) =>
        {
            foreach (var o in Js.Iter(dying))
            {
                Needs.killSurvivor(st, o.s, o.cause);
                if (o.cause == "the infection") Zombies.addZombie(st, o.s.pos.x, o.s.pos.y, Zombies.ZOMBIE_HEALTH, "static");
            }
        });
        S("Rooms.reconcileRoomDesignations", state, Rooms.reconcileRoomDesignations);
        S("Needs.reconcileNeeds (2)", state, Needs.reconcileNeeds);
        S("Wanderers.reconcileWanderers", state, Wanderers.reconcileWanderers);
        S("ControlMod.reconcileControl", state, ControlMod.reconcileControl);
        S("Doors.closeBehind", state, Doors.closeBehind);
        S("movers.syncStep", state, (st) => { foreach (var e in Js.Iter(Tick.movers(st))) if (!Tick.isFree(st, e)) Motion.syncStep(e); });
        S("Fog.updateFog", state, Fog.updateFog);
        state.tick = Js.I(state.tick + 1);
    }

    // Zombies.updateZombies, updateAll and updateZombie step by step (DEEP=zombies).
    void ReplayZombies(SimState state)
    {
        if (!Time.isNight(state.tick))
        {
            S("z: dawn leavers", state, (st) =>
            {
                st.zombiesTonight = null;
                var n = st.zombies.filter((z) => z.kind == "night" || z.kind == "raider").length;
                if (n > 0)
                {
                    st.zombies = st.zombies.filter((z) => !(z.kind == "night" || z.kind == "raider"));
                    Events.logEvent(st, $"{Js.Str(n)} zombie{Js.Str((n == 1) ? Js.Str("") : Js.Str("s"))} shambled off at dawn");
                }
            });
        }
        else
        {
            S("z: night spawns", state, (st) =>
            {
                var night = Js.NN(Time.nightNumber(st.tick));
                if (Time.ticksIntoNight(st.tick) == 0)
                {
                    st.zombiesTonight = JsMath.max(1, Difficulty.zombieScale(st, JsMath.round(((double)(((double)(((double)(Zombies.zombiesForNight(night, st.survivors.length)) * (Director.threatScale(st)))) * (EndgameMod.siegeFactor(st, night)))) * ((st.harsh ? Zombies.groupShare(st.survivors.length) : 1))))));
                    var siege = EndgameMod.siegeFactor(st, night) > 1;
                    Events.logEvent(st, $"Night {Js.Str(night)} falls: {Js.Str(st.zombiesTonight)} zombies expected{Js.Str(siege ? Js.Str(" (a SIEGE night)") : Js.Str(""))}");
                }
                var arriving = JsMath.min(Zombies.spawnsAt(st.tick, Zombies.tonight(st, night)), Zombies.MAX_ZOMBIES - st.zombies.length);
                if (arriving > 0) Zombies.spawnGroup(st, arriving, Zombies.zombieHealthForNight(night));
            });
        }
        S("z: Hordes.updateHordes", state, Hordes.updateHordes);
        var budget = F("z: survivorsNear", () => new Budget { plans = Zombies.PLANS_PER_TICK, near = Zombies.survivorsNear(state) });
        foreach (var z in Js.Iter(state.zombies)) ReplayZombie(state, z, budget);
        S("z: pushApart", state, Zombies.pushApart);
        S("z: headings", state, (st) =>
        {
            foreach (var z in Js.Iter(st.zombies))
                if ((Js.T((z.kind == "static") && Js.T(z.heading))) && (Space.manhattan(z.pos, z.heading) <= 1)) z.heading = null;
            bool gone(Zombie z) => ((Js.T((z.kind == "herd") && Js.T(z.heading))) && (Space.manhattan(z.pos, z.heading) <= 1)) && !Js.T(z.move);
            if (st.zombies.some((z) => gone(z))) st.zombies = st.zombies.filter((z) => !gone(z));
        });
    }

    void ReplayZombie(SimState state, Zombie z, Budget budget)
    {
        z.cooldown = JsMath.max(0, z.cooldown - 1);
        z.move = null;
        Motion.followTile(z, 1);
        if (state.tick < z.stuckUntil || state.tick < z.staggerUntil) return;
        bool done = F("zz: reach and attack", () =>
        {
            Survivor? victim = null, first = null;
            foreach (var s in Js.Iter(budget.near(z.at)))
            {
                if ((JsMath.abs(s.at.x - z.at.x) > Motion.REACH) || (JsMath.abs(s.at.y - z.at.y) > Motion.REACH)) continue;
                if (Motion.inReach(s, z, Motion.REACH, state.grid))
                {
                    first ??= s;
                    if (victim == null && s.id == z.targetId) victim = s;
                }
            }
            victim ??= first;
            if (Js.T(victim))
            {
                if (z.windup > 0)
                {
                    z.windup -= 1;
                    if ((z.windup == 0) && !Vehicles.batterVehicle(state, victim)) Zombies.hit(state, z, victim);
                }
                else if (z.cooldown == 0) z.windup = Zombies.WINDUP_TICKS;
                return true;
            }
            if (z.windup > 0)
            {
                z.windup = 0;
                z.cooldown = Zombies.WINDUP_TICKS;
            }
            Dog? dog = null;
            foreach (var d in state.dogs) if (Motion.inReach(d, z, Motion.REACH, state.grid)) { dog = d; break; }
            if (Js.T(dog))
            {
                if (z.cooldown == 0)
                {
                    Dogs.hitDog(state, dog, (z.kind == "raider") ? Visitors.RAIDER_DAMAGE : Zombies.ZOMBIE_DAMAGE);
                    z.cooldown = Zombies.ZOMBIE_ATTACK_EVERY;
                }
                return true;
            }
            return false;
        });
        if (done) return;
        var speed = JsMath.ceil(((double)(Motion.SUB) / (Js.Get(ZombieKinds.VARIANTS, z.variant).ticksPerTile)));
        if (F("zz: enter", () => Zombies.enter(state, z))) return;
        var target = F("zz: chaseTarget", () => Zombies.chaseTarget(state, z));
        var horde = Hordes.getHorde(state, z.hordeId);
        var planned = false;
        if (Js.T(horde) && (z.path.length == 0))
        {
            if (!Js.T(target))
            {
                var step = F("zz: hordeStep", () => Hordes.hordeStep(state, z, horde));
                z.path = Js.T(step) ? ((List<Point>)[step]) : ((List<Point>)[]);
            }
        }
        else if (((state.tick >= z.nextThinkTick) || ((z.path.length == 0) && !Js.T(target))) && (budget.plans > 0))
        {
            budget.plans -= 1;
            S("zz: plan", state, (st) => Zombies.plan(st, z));
            z.nextThinkTick = state.tick + Zombies.REPATH_EVERY;
            planned = true;
            target = F("zz: chaseTarget", () => Zombies.chaseTarget(state, z));
        }
        if (Js.T(target))
        {
            S("zz: chase", state, (st) => Zombies.chase(st, z, target, speed));
            return;
        }
        if (planned) S("zz: aimAhead", state, (st) => Zombies.aimAhead(st, z));
        S("zz: followPath", state, (st) => Zombies.followPath(st, z, speed));
    }

    void Behave(SimState state, Survivor s)
    {
        S("b: Leisure.updateBreakdown", state, (st) => Leisure.updateBreakdown(st, s));
        S("b: Needs.interruptForNeeds", state, (st) => Needs.interruptForNeeds(st, s));
        S("b: Work.interruptScouting", state, (st) => Work.interruptScouting(st, s));
        S("b: Work.interruptForFight", state, (st) => Work.interruptForFight(st, s));
        S("b: Survivors.updateMovement", state, (st) => Survivors.updateMovement(st, s));
        if (Js.T(Js.T(s.move) || Js.T(s.goal))) return;
        var needs = F("b: Needs.updateMeal", () => Needs.updateMeal(state, s))
            || F("b: Forage.updateForage", () => Forage.updateForage(state, s, () => (Needs.nearestFreeItem(state, s, "food") != null) || (Needs.nearestFreeItem(state, s, "meal") != null)))
            || F("b: Water.updateDrink", () => Water.updateDrink(state, s))
            || F("b: Needs.updateTreatment", () => Needs.updateTreatment(state, s))
            || F("b: Weapons.updateArming", () => Weapons.updateArming(state, s))
            || F("b: Clothing.updateDressing", () => Clothing.updateDressing(state, s))
            || F("b: Needs.updateSleep", () => Needs.updateSleep(state, s));
        if (Js.T(needs))
        {
            s.leisure = null;
            S("b: Books.stopReading", state, (st) => Books.stopReading(st, s, s.note ?? ""));
        }
        else if (!Js.T(s.breakdown) && !F("b: Orders.keepOrder", () => Orders.keepOrder(state, s)) && !F("b: Leisure.updateLeisure", () => Leisure.updateLeisure(state, s)) && !F("b: Books.updateReading", () => Books.updateReading(state, s)))
        {
            S("b: Work.updateWork", state, (st) => Work.updateWork(st, s));
        }
    }

    [Fact(DisplayName = "profile")]
    public void Run()
    {
        int ticks = int.TryParse(Env("TICKS"), out var n) ? n : 300;
        int warm = int.TryParse(Env("WARM"), out var w) ? w : 20;
        var want = Env("SCENES")?.Split(',');
        var out_ = new List<string>();
        using var listener = new AllocListener();
        foreach (var (name, build) in SCENES.Where((x) => want == null || want.Contains(x.name)))
        {
            var state = build();
            Pathfinding.warmReach(state.grid, state.walkabilityVersion);
            for (int t = 0; t < warm; t++) Tick.tick(state);
            // The whole tick, as the game runs it, then the same number of ticks replayed step by step.
            long b0 = GC.GetAllocatedBytesForCurrentThread(), t0 = Stopwatch.GetTimestamp();
            int gc0 = GC.CollectionCount(0);
            listener.bytes.Clear();
            listener.on = true;
            for (int t = 0; t < ticks; t++) Tick.tick(state);
            listener.on = false;
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency / ticks;
            double bytes = (double)(GC.GetAllocatedBytesForCurrentThread() - b0) / ticks;
            int gcs = GC.CollectionCount(0) - gc0;
            Thread.Sleep(500);
            var types = listener.bytes.OrderByDescending((kv) => kv.Value).Take(25).ToList();
            acc.Clear();
            order.Clear();
            for (int t = 0; t < ticks; t++) ReplayTick(state);
            double total = acc.Values.Sum((a) => a.ms);
            long totalB = acc.Values.Sum((a) => a.bytes);
            out_.Add($"== {name}: size {state.grid.width} survivors {state.survivors.Count} zombies {state.zombies.Count} animals {state.animals.Count} items {state.items.Count} jobs {state.jobs.Count} tick {state.tick}");
            out_.Add($"   whole tick: {ms:0.000} ms ({1000 / ms:0} ticks/s), {bytes / 1024:0.0} KB allocated a tick, {gcs} gen0 GCs over {ticks} ticks");
            out_.Add($"   replayed:   {total / ticks:0.000} ms, {(double)totalB / ticks / 1024:0.0} KB a tick (step by step; the clocks add a little)");
            out_.Add($"   {"step",-38} {"ms/tick",8} {"%",5} {"KB/tick",8} {"%",5} {"calls/tick",10}");
            foreach (var k in order.OrderByDescending((k) => acc[k].ms).Where((k) => acc[k].ms / total > 0.002 || (double)acc[k].bytes / Math.Max(1, totalB) > 0.002))
            {
                var a = acc[k];
                out_.Add($"   {k,-38} {a.ms / ticks,8:0.000} {100 * a.ms / total,5:0.0} {(double)a.bytes / ticks / 1024,8:0.0} {100.0 * a.bytes / Math.Max(1, totalB),5:0.0} {(double)a.calls / ticks,10:0.0}");
            }
            out_.Add($"   sampled allocations by type (whole tick, KB/tick):");
            foreach (var kv in types) out_.Add($"     {kv.Value / 1024.0 / ticks,8:0.0}  {kv.Key}");
            out_.Add("");
            var path = Path.Combine(Node.Root, Env("OUT") ?? "cs/Harness/out/profile.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join("\n", out_) + "\n");
        }
    }

    [Fact(DisplayName = "tick dumps")]
    public void Dumps()
    {
        string dir = Env("DUMP") ?? Path.Combine(Node.Root, "cs/Harness/out/dumps");
        Directory.CreateDirectory(dir);
        int[] at = (Env("DUMP_AT") ?? "1,200,700,1500").Split(',').Select(int.Parse).ToArray();
        var seeds = (Env("SEEDS") ?? "1,2,3,4").Split(',').Select(int.Parse).ToArray();
        var kinds = new (string name, Func<int, SimState> build)[]
        {
            ("gen512", (seed) => Crowd(seed, 512, 3, 30, true)),
            ("crowd128", (seed) => Crowd(seed, 128, 50, 150, false)),
            ("ring512", (seed) => Crowd(seed, 512, 30, 400, false)),
        };
        foreach (var (kind, build) in kinds)
            foreach (var seed in seeds)
            {
                var state = build(seed);
                for (int t = 1; t <= at.Max(); t++)
                {
                    Tick.tick(state);
                    if (at.Contains(t)) File.WriteAllText(Path.Combine(dir, $"{kind}-s{seed}-t{t}.json"), StateJson.Write(state));
                }
            }
    }
}
