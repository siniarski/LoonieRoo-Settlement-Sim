// Ported from src/sim/jobChoice.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Choosing work: which job an idle survivor takes (by priority, then distance), and
// planning the walk there. Pure apart from claim(). Also explainIdle for the debug panel.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class JobChoice
{

    /// <summary>Highest-priority job type first (ties: build, haul, clean), then the nearest open job of that type that is actually doable. Returns true if a job was claimed.</summary>
    public static bool seekJob(SimState state, Survivor s)
    {
        var choice = chooseJob(state, s);
        if (!Js.T(choice))
        {
            return false;
        }
        claim(state, s, choice.job, choice.plan, choice.why);
        return true;
    }

    /// <summary>The job this survivor would take right now, without claiming it. Pure.</summary>
    public static ChooseJobResult? chooseJob(SimState state, Survivor s)
    {
        foreach (var type in Js.Iter(typesByPriority(s)))
        {
            // By distance, then place in the list, taken lazily nearest first off a heap (NearestFirst): the
            // first MAX_CANDIDATES that pass the (pure) filter are the ones a full sort and filter would give.
            var open = state.jobs.filter((j) => (j.type == type) && (j.reservedBy == null));
            var curfew = (((Policies.policyOn(state, "curfew") && Time.isNight(state.tick)) && (type != "guard")) && (type != "firefight")) && (type != "doctor");
            var nearest = new NearestFirst(open, s.pos);
            var taken = 0;
            while ((taken < Jobs.MAX_CANDIDATES) && nearest.next() is { } job)
            {
                if (!((!Js.T(curfew) || Rooms.isIndoors(state, job.x, job.y)) && Orders.withinOrder(state, s, job)))
                {
                    continue;
                }
                taken++;
                var plan = planJob(state, s, job);
                if (Js.T(plan))
                {
                    return new ChooseJobResult { job = job, plan = plan, why = $"priority {Js.Str(Js.Get(s.priorities, type))}, nearest of {Js.Str(open.length)} open" };
                }
            }
        }
        return null;
    }

    public static List<string> typesByPriority(Survivor s)
    {
        if (s.childUntil != null)
        {
            return (List<string>)[];
        }
        return JobTypes.JOB_TYPES.filter((t) => (Js.Get(s.priorities, t) > 0) && (!Js.T(s.order) || Orders.ORDER_JOBS.includes(t))).sort((a, b) => ((Js.Get(s.priorities, a) - Js.Get(s.priorities, b)) is var __or0 && Js.T(__or0) ? __or0 : (JobTypes.JOB_TYPES.indexOf(a) - JobTypes.JOB_TYPES.indexOf(b))));
    }

    /// <summary>Open jobs of a type nearest first, by the key manhattan * 2^20 + place in the list (distinct), off a
    /// binary min-heap: popping all gives the full sort's order, and stopping early skips the rest of the sort.</summary>
    sealed class NearestFirst
    {
        readonly List<Job> jobs;
        readonly double[] keys;
        int n;

        public NearestFirst(List<Job> jobs, Point from)
        {
            this.jobs = jobs;
            n = jobs.Count;
            keys = new double[n];
            for (var i = 0; i < n; i++)
            {
                keys[i] = ((double)(Space.manhattan(from, jobs[i])) * (1048576)) + i;
            }
            for (var i = n / 2 - 1; i >= 0; i--)
            {
                down(i);
            }
        }

        public Job? next()
        {
            if (n == 0)
            {
                return null;
            }
            var k = keys[0];
            keys[0] = keys[--n];
            down(0);
            return jobs[(int)(k % 1048576)];
        }

        void down(int i)
        {
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < n && keys[l] < keys[m]) m = l;
                if (r < n && keys[r] < keys[m]) m = r;
                if (m == i) return;
                (keys[i], keys[m]) = (keys[m], keys[i]);
                i = m;
            }
        }
    }

    public static JobChoicePlan? planJob(SimState state, Survivor s, Job job)
    {
        var __d1 = state;
        var grid = __d1.grid;
        var v = __d1.walkabilityVersion;
        switch (job.type)
        {
            case "clean":
            case "farm":
                {
                    {
                    List<Point> goal = new List<Point> { new Point { x = Js.I(job.x), y = Js.I(job.y) } };
                    if (!Pathfinding.canReach(grid, v, s.pos, goal))
                    {
                        return null;
                    }
                    return pathPlan(state, s.pos, goal, null);
                }
                }
            case "build":
            case "scavenge":
            case "firefight":
            case "hunt":
            case "guard":
                {
                    {
                    if ((job.type == "guard") && !Defences.seen(state, job))
                    {
                        return null;
                    }
                    var goals = Grids.neighbours4(grid, job.x, job.y);
                    if (!Pathfinding.canReach(grid, v, s.pos, goals))
                    {
                        return null;
                    }
                    return pathPlan(state, s.pos, goals, null);
                }
                }
            case "scout":
                {
                    {
                    if (Fog.scoutingOver(state.tick))
                    {
                        return null;
                    }
                    var area = Js.T(job.areaId) ? Fog.getExploreArea(state, job.areaId) : null;
                    var target = Js.T(area) ? Fog.scoutTarget(state, area, s.pos) : null;
                    if (!Js.T(target))
                    {
                        return null;
                    }
                    var plan = pathPlan(state, s.pos, [target], null);
                    if (Js.T(plan))
                    {
                        plan.moveTo = new Point { x = Js.I(target.x), y = Js.I(target.y) };
                    }
                    return plan;
                }
                }
            case "doctor":
                {
                    {
                    var patient = Js.T(job.patientId) ? Survivors.getSurvivor(state, job.patientId) : null;
                    if (!Js.T(patient))
                    {
                        return null;
                    }
                    var site = Grids.neighbours4(grid, patient.pos.x, patient.pos.y).filter((p) => Grids.isWalkable(grid, p.x, p.y));
                    var remedy = Medicine.remediesFor(patient).map((k) => Work.freeItemFor(state, k, s.pos, site)).find((i) => i != null);
                    return Js.T(remedy) ? pathPlan(state, s.pos, [remedy.pos], remedy.id) : null;
                }
                }
            case "research":
                {
                    {
                    var lab = Js.T(job.furnitureId) ? FurnitureMod.getFurniture(state, job.furnitureId) : null;
                    if (!Js.T(lab))
                    {
                        return null;
                    }
                    var site = Grids.neighbours4(grid, lab.x, lab.y).filter((p) => Grids.isWalkable(grid, p.x, p.y));
                    if (lab.stored > 0)
                    {
                        return Pathfinding.canReach(grid, v, s.pos, site) ? pathPlan(state, s.pos, site, null) : null;
                    }
                    var sample = Work.freeItemFor(state, "sample", s.pos, site);
                    return Js.T(sample) ? pathPlan(state, s.pos, [sample.pos], sample.id) : null;
                }
                }
            case "craft":
            case "cook":
                {
                    {
                    var bench = Js.T(job.furnitureId) ? FurnitureMod.getFurniture(state, job.furnitureId) : null;
                    if (!Js.T(bench) || !Crafting.isStation(bench.kind))
                    {
                        return null;
                    }
                    var kind = Crafting.stationCraft(state, bench);
                    if (!Js.T(kind))
                    {
                        return null;
                    }
                    var site = Grids.neighbours4(grid, bench.x, bench.y).filter((p) => Grids.isWalkable(grid, p.x, p.y));
                    if ((bench.stored >= Js.Get(Crafting.RECIPES, kind).count) && (bench.storedKind == Js.Get(Crafting.RECIPES, kind).input))
                    {
                        return Pathfinding.canReach(grid, v, s.pos, site) ? pathPlan(state, s.pos, site, null) : null;
                    }
                    var input = Work.freeItemFor(state, Js.Get(Crafting.RECIPES, kind).input, s.pos, site);
                    return Js.T(input) ? pathPlan(state, s.pos, [input.pos], input.id) : null;
                }
                }
            case "haul":
                {
                    {
                    if (Jobs.isStoreJob(job))
                    {
                        var item = Js.T(job.itemId) ? Items.getItem(state, job.itemId) : null;
                        if ((!Js.T(Js.T(item?.pos)) || (item.reservedBy != null)) || !Pathfinding.canReach(grid, v, s.pos, [item.pos]))
                        {
                            return null;
                        }
                        var dest = Work.pickStorageTile(state, item.pos, job, Rooms.homeFor(Rooms.completeStoreRooms(state), item.kind));
                        return Js.T(dest) ? pathPlan(state, s.pos, [item.pos], item.id, dest) : null;
                    }
                    var site = Grids.neighbours4(grid, job.x, job.y).filter((p) => Grids.isWalkable(grid, p.x, p.y));
                    if (site.length == 0)
                    {
                        return null;
                    }
                    if (Jobs.isFuelJob(job))
                    {
                        var f = Js.T(job.furnitureId) ? FurnitureMod.getFurniture(state, job.furnitureId) : null;
                        var fuel = (Js.T(f) && (Js.In(f.kind, Jobs.FUEL))) ? Js.Get(Jobs.FUEL, f.kind) : "wood";
                        var item = Work.freeItemFor(state, fuel, s.pos, site);
                        return Js.T(item) ? pathPlan(state, s.pos, [item.pos], item.id) : null;
                    }
                    var bp = Js.T(job.blueprintId) ? Designations.getBlueprint(state, job.blueprintId) : null;
                    var material = Js.T(bp) ? Designations.materialFor(bp.target) : "wood";
                    var free = Items.freeOfKind(state, material).sort((a, b) => Space.manhattan(s.pos, a.pos) - Space.manhattan(s.pos, b.pos));
                    foreach (var item in Js.Iter(free.slice(0, Jobs.MAX_CANDIDATES)))
                    {
                        if (!Pathfinding.canReach(grid, v, s.pos, [item.pos]) || !Pathfinding.canReach(grid, v, item.pos, site))
                        {
                            continue;
                        }
                        return pathPlan(state, s.pos, [item.pos], item.id);
                    }
                    return null;
                }
                }
        }
        throw new InvalidOperationException("unreachable: every case returns");
    }

    public static JobChoicePlan? pathPlan(SimState state, Point from, List<Point> goals, string? itemId, Point? dest = null)
    {
        var path = Pathfinding.findPathToAny(state.grid, from, goals);
        return Js.T(path) ? new() { path = path, itemId = itemId, dest = dest } : null;
    }

    public static void claim(SimState state, Survivor s, Job job, JobChoicePlan plan, string why)
    {
        job.reservedBy = s.id;
        s.jobId = job.id;
        job.dest = plan.dest;
        if (Js.T(plan.moveTo))
        {
            job.x = Js.I(plan.moveTo.x);
            job.y = Js.I(plan.moveTo.y);
        }
        if (Js.T(plan.itemId))
        {
            job.itemId = plan.itemId;
            var item = Items.getItem(state, plan.itemId);
            if (Js.T(item))
            {
                item.reservedBy = s.id;
            }
        }
        if (!(Js.T((job.type == "guard") && Js.T(Weapons.loadedGun(state, s)))))
        {
            Survivors.walk(s, plan.path, state.walkabilityVersion);
        }
        s.note = $"took {Js.Str(job.type)} job at ({Js.Str(job.x)}, {Js.Str(job.y)}): {Js.Str(why)}";
    }

    /// <summary>Why an idle survivor isn't working. Pure: safe to call from the renderer.</summary>
    public static string explainIdle(SimState state, Survivor s)
    {
        var types = typesByPriority(s);
        if (types.length == 0)
        {
            return "all job types are off";
        }
        var open = state.jobs.filter((j) => (j.reservedBy == null) && types.includes(j.type));
        if (open.length == 0)
        {
            return "no open jobs of enabled types";
        }
        var haulOnly = open.every((j) => j.type == "haul");
        var freeWood = state.items.some((i) => ((i.kind == "wood") && (i.pos != null)) && (i.reservedBy == null));
        if (haulOnly && !freeWood)
        {
            return $"{Js.Str(open.length)} haul job(s) waiting for wood";
        }
        return $"{Js.Str(open.length)} open job(s), none reachable right now";
    }
}
