// Ported from src/sim/work.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Doing jobs: what a survivor does each tick while holding one (Jobs.cs has the
// queue). Each work function ends with finishJob or releaseSurvivor; nobody gets stuck.
// Carrying things (to storage, fuel to what burns it, loading a cart) is WorkHaul.cs, a part of this class.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Work
{

    /// <summary>Runs when the survivor is standing still: continue the current job, or look for one.</summary>
    public static void updateWork(SimState state, Survivor s)
    {
        if (!Js.T(s.jobId))
        {
            if (state.tick < s.nextSeekTick)
            {
                return;
            }
            if (JobChoice.seekJob(state, s))
            {
                return;
            }
            s.nextSeekTick = state.tick + Jobs.IDLE_RETRY_TICKS;
            if ((!Js.T(s.order) && !Towers.keepWatch(state, s)) && !Books.startReading(state, s))
            {
                Leisure.startLeisure(state, s);
            }
            return;
        }
        var job = Jobs.getJob(state, s.jobId);
        if (!Js.T(job) || (job.reservedBy != s.id))
        {
            Jobs.releaseSurvivor(state, s, "job vanished");
            return;
        }
        switch (job.type)
        {
            case "clean":
                {
                    { workClean(state, s, job); return; }
                }
            case "build":
                {
                    { workBuild(state, s, job); return; }
                }
            case "haul":
                {
                    { workHaul(state, s, job); return; }
                }
            case "scavenge":
                {
                    { workScavenge(state, s, job); return; }
                }
            case "craft":
            case "cook":
                {
                    { workCraft(state, s, job); return; }
                }
            case "doctor":
                {
                    { workDoctor(state, s, job); return; }
                }
            case "scout":
                {
                    { workScout(state, s, job); return; }
                }
            case "guard":
                {
                    { if (Js.T(job.animalId)) { workHunt(state, s, job); } else { workGuard(state, s, job); } return; }
                }
            case "firefight":
                {
                    { workFirefight(state, s, job); return; }
                }
            case "hunt":
                {
                    { workHunt(state, s, job); return; }
                }
            case "research":
                {
                    { workResearch(state, s, job); return; }
                }
            case "farm":
                {
                    { workFarm(state, s, job); return; }
                }
        }
    }

    /// <summary>Sow an empty plot or harvest a ripe one, standing on it.</summary>
    public static void workFarm(SimState state, Survivor s, Job job)
    {
        var plot = Js.T(job.plotId) ? Farming.getPlot(state, job.plotId) : null;
        if (!Js.T(plot) || !Farming.needsWork(plot))
        {
            { Jobs.releaseSurvivor(state, s, "plot no longer needs work"); return; }
        }
        if (!Space.samePoint(s.pos, plot))
        {
            { goTo(state, s, job, [Farming.plotPoint(plot)]); return; }
        }
        var step = Skills.workStep(state, s, "farm");
        if (step == 0)
        {
            return;
        }
        job.progress += step;
        if (job.progress < Farming.FARM_WORK)
        {
            return;
        }
        if (Farming.isRipe(plot))
        {
            Farming.harvest(state, plot);
            finishJob(state, s, job, $"harvested {Js.Str((plot.crop == "herb") ? Js.Str("herbs") : Js.Str((plot.crop == "wheat") ? "wheat" : "food"))} at ({Js.Str(plot.x)}, {Js.Str(plot.y)})");
        }
        else
        {
            plot.planted = true;
            plot.growth = 0;
            finishJob(state, s, job, $"sowed ({Js.Str(plot.x)}, {Js.Str(plot.y)})");
        }
    }

    /// <summary>Hunt a zombie: close in, then hit it every GUARD_ATTACK_EVERY ticks until it drops.</summary>
    public static void workGuard(SimState state, Survivor s, Job job)
    {
        var z = Js.T(job.zombieId) ? Zombies.getZombie(state, job.zombieId) : null;
        if (!Js.T(z))
        {
            { Jobs.releaseSurvivor(state, s, "zombie gone"); return; }
        }
        var gun = Weapons.loadedGun(state, s);
        var spec = Weapons.meleeSpec(state, s);
        var inReach = Motion.distance2(s.at, z.at) <= ((double)(spec.reach) * (spec.reach));
        if (Js.T(gun) && !inReach)
        {
            // A shot, if the gun is loaded and the line clear (Weapons.guardShoot: its own range, friends in the way, the gun's rate).
            var extra = Towers.onTower(state, s) ? Towers.TOWER_RANGE_BONUS : 0;
            if (Weapons.guardShoot(state, s, gun, z, extra))
            {
                if (Skills.workStep(state, s, "guard") == 0)
                {
                    return;
                }
                if (Zombies.getZombie(state, z.id) == null)
                {
                    { finishJob(state, s, job, $"shot a zombie at ({Js.Str(z.pos.x)}, {Js.Str(z.pos.y)})"); return; }
                }
                return;
            }
        }
        if (!inReach)
        {
            { goTo(state, s, job, Grids.neighbours4(state.grid, z.pos.x, z.pos.y), Js.T(gun) ? 1 : null); return; }
        }
        if (Skills.workStep(state, s, "guard") == 0)
        {
            return;
        }
        job.progress += 1;
        if ((job.progress % spec.swing) != 1)
        {
            return;
        }
        Noise.makeNoise(state, s.pos, "fighting", spec.noise);
        BodyMod.exert(s, spec.stamina / 2);
        var blow = Weapons.meleeBlowAtDead(state, s);
        if (!Zombies.damageZombie(state, z, Power.inLight(state, s.pos) ? JsMath.round(((double)(((double)(blow) * (Power.LIGHT_DAMAGE_PCT))) / (100))) : blow, s))
        {
            return;
        }
        Weapons.wearWeapon(state, s);
        finishJob(state, s, job, $"killed a zombie at ({Js.Str(z.pos.x)}, {Js.Str(z.pos.y)})");
    }

    /// <summary>Bring a sample to the lab bench (it holds one), then study it.</summary>
    public static void workResearch(SimState state, Survivor s, Job job)
    {
        var lab = Js.T(job.furnitureId) ? FurnitureMod.getFurniture(state, job.furnitureId) : null;
        if (!Js.T(lab) || (lab.kind != "lab"))
        {
            { Jobs.releaseSurvivor(state, s, "lab gone"); return; }
        }
        var site = Grids.neighbours4(state.grid, lab.x, lab.y);
        if (lab.stored == 0)
        {
            var item = Js.T(job.itemId) ? Items.getItem(state, job.itemId) : null;
            if (!Js.T(item))
            {
                item = freeItemFor(state, "sample", s.pos, site);
                if (!Js.T(item))
                {
                    { Jobs.releaseSurvivor(state, s, "no samples to study"); return; }
                }
                job.itemId = item.id;
                item.reservedBy = s.id;
            }
            if (s.carrying != item.id)
            {
                if (!Js.T(item.pos))
                {
                    { Jobs.releaseSurvivor(state, s, "item taken by someone else"); return; }
                }
                if (!Space.samePoint(s.pos, item.pos))
                {
                    { goTo(state, s, job, [item.pos]); return; }
                }
                item.pos = null;
                item.carriedBy = s.id;
                s.carrying = item.id;
            }
            if (Space.manhattan(s.pos, lab) != 1)
            {
                { goTo(state, s, job, site); return; }
            }
            Items.removeItem(state, item);
            s.carrying = null;
            job.itemId = null;
            lab.stored = 1;
            lab.storedKind = "sample";
            s.note = "brought a sample to the lab";
            return;
        }
        if (Space.manhattan(s.pos, lab) != 1)
        {
            { goTo(state, s, job, site); return; }
        }
        var step = Skills.workStep(state, s, "research");
        if (step == 0)
        {
            return;
        }
        job.progress += step;
        if (job.progress < EndgameMod.RESEARCH_WORK)
        {
            return;
        }
        lab.stored = 0;
        lab.storedKind = null;
        EndgameMod.studied(state);
        finishJob(state, s, job, $"studied a sample ({Js.Str(state.endgame.cure)}/{Js.Str(EndgameMod.CURE_SAMPLES)})");
    }

    /// <summary>Close in on the animal a few tiles at a time (it moves), then take it down.</summary>
    public static void workHunt(SimState state, Survivor s, Job job)
    {
        var a = Js.T(job.animalId) ? Animals.getAnimal(state, job.animalId) : null;
        if (!Js.T(a))
        {
            { Jobs.releaseSurvivor(state, s, "the animal got away"); return; }
        }
        if (Space.manhattan(s.pos, a.pos) > 1)
        {
            { goTo(state, s, job, Grids.neighbours4(state.grid, a.pos.x, a.pos.y), 3); return; }
        }
        if (AnimalCars.hidden(a))
        {
            return; // under a car (AnimalCars.cs): out of reach until it comes out
        }
        if ((a.kind == "wolf") || (a.kind == "bear"))
        {
            if ((state.tick % Jobs.GUARD_ATTACK_EVERY) != 0)
            {
                return;
            }
            Noise.makeNoise(state, a.pos, "fighting");
            var blow = Weapons.meleeBlow(state, s);
            if (Handiwork.hitAnimal(state, s, a.id, blow) == "brought it down")
            {
                finishJob(state, s, job, $"killed a {Js.Str(a.kind)} at ({Js.Str(a.pos.x)}, {Js.Str(a.pos.y)})");
            }
            return;
        }
        var step = Skills.workStep(state, s, "hunt");
        if (step == 0)
        {
            return;
        }
        job.progress += step;
        if (job.progress < Animals.HUNT_WORK)
        {
            return;
        }
        Animals.butcher(state, a);
        Noise.makeNoise(state, a.pos, "fighting");
        finishJob(state, s, job, $"hunted a {Js.Str(a.kind)} at ({Js.Str(a.pos.x)}, {Js.Str(a.pos.y)})");
    }

    /// <summary>Beat a fire out from next to it.</summary>
    public static void workFirefight(SimState state, Survivor s, Job job)
    {
        var fire = FireMod.fireAt(state, job.x, job.y);
        if (!Js.T(fire))
        {
            { Jobs.releaseSurvivor(state, s, "fire is out"); return; }
        }
        if (Space.manhattan(s.pos, fire) != 1)
        {
            { goTo(state, s, job, Grids.neighbours4(state.grid, fire.x, fire.y)); return; }
        }
        var step = Skills.workStep(state, s, "firefight");
        if (step == 0)
        {
            return;
        }
        job.progress += ((double)(step) * ((Seasons.isWet(state.weather) ? 2 : 1)));
        if (job.progress < FireMod.DOUSE_WORK)
        {
            return;
        }
        FireMod.douse(state, fire);
        finishJob(state, s, job, $"put out a fire at ({Js.Str(fire.x)}, {Js.Str(fire.y)})");
    }

    /// <summary>Search a crate from next to it; each completed search drops one find by the survivor.</summary>
    public static void workScavenge(SimState state, Survivor s, Job job)
    {
        var crate = Js.T(job.furnitureId) ? FurnitureMod.getFurniture(state, job.furnitureId) : null;
        if (!Js.T(crate) || (crate.loot <= 0))
        {
            { Jobs.releaseSurvivor(state, s, "crate is empty"); return; }
        }
        if (Space.manhattan(s.pos, crate) != 1)
        {
            { goTo(state, s, job, Grids.neighbours4(state.grid, crate.x, crate.y)); return; }
        }
        var step = Skills.workStep(state, s, "scavenge");
        if (step == 0)
        {
            return;
        }
        job.progress += step;
        if (job.progress < Jobs.SCAVENGE_WORK)
        {
            return;
        }
        var kind = (crate.kind == "tree") ? "wood" : scavengeFind(Rng.random(state), crate.table);
        Difficulty.spawnFind(state, kind, s.pos.x, s.pos.y);
        crate.loot -= 1;
        if ((crate.loot == 0) && (crate.kind == "crate"))
        {
            FurnitureMod.setScavenge(state, crate, false);
        }
        finishJob(state, s, job, (crate.kind == "tree") ? $"chopped wood from ({Js.Str(crate.x)}, {Js.Str(crate.y)}), {Js.Str(crate.loot)} left" : $"scavenged {Js.Str(kind)} from ({Js.Str(crate.x)}, {Js.Str(crate.y)}), {Js.Str(crate.loot)} left");
    }

    /// <summary>Maps a [0, 1) roll to a find: from the container's loot table (loot.ts), else SCAVENGE_TABLE.</summary>
    public static string scavengeFind(double roll, string? table = null)
    {
        return Loot.pickFrom(Js.T(table) ? Js.Get(Loot.LOOT_TABLES, table) : Jobs.SCAVENGE_TABLE, roll);
    }

    /// <summary>Bring the input to the station one piece at a time, then make the most-wanted thing it makes.</summary>
    public static void workCraft(SimState state, Survivor s, Job job)
    {
        var bench = Js.T(job.furnitureId) ? FurnitureMod.getFurniture(state, job.furnitureId) : null;
        if (!Js.T(bench) || !Crafting.isStation(bench.kind))
        {
            { Jobs.releaseSurvivor(state, s, "station gone"); return; }
        }
        var workshop = Crafting.inWorkshop(state, bench);
        var kind = Crafting.stationCraft(state, bench);
        if (!Js.T(kind))
        {
            { Jobs.releaseSurvivor(state, s, "stocks are full"); return; }
        }
        if ((bench.stored > 0) && (bench.storedKind != Js.Get(Crafting.RECIPES, kind).input))
        {
            for (var n = 0; n < bench.stored; n++)
            {
                Items.spawnItem(state, bench.storedKind ?? "wood", bench.x, bench.y);
            }
            bench.stored = 0;
            bench.storedKind = null;
        }
        var site = Grids.neighbours4(state.grid, bench.x, bench.y);
        var recipe = Js.Get(Crafting.RECIPES, kind);
        var cost = recipe.count;
        var station = (bench.kind == "stove") ? "the stove" : "the workbench";
        if (bench.stored < cost)
        {
            var item = Js.T(job.itemId) ? Items.getItem(state, job.itemId) : null;
            if (!Js.T(item))
            {
                item = freeItemFor(state, recipe.input, s.pos, site);
                if (!Js.T(item))
                {
                    { Jobs.releaseSurvivor(state, s, $"no {Js.Str(recipe.input)} for {Js.Str(station)}"); return; }
                }
                job.itemId = item.id;
                item.reservedBy = s.id;
            }
            if (s.carrying != item.id)
            {
                if (!Js.T(item.pos))
                {
                    { Jobs.releaseSurvivor(state, s, "item taken by someone else"); return; }
                }
                if (!Space.samePoint(s.pos, item.pos))
                {
                    { goTo(state, s, job, [item.pos]); return; }
                }
                item.pos = null;
                item.carriedBy = s.id;
                s.carrying = item.id;
                s.note = $"picked up {Js.Str(recipe.input)} for {Js.Str(station)}";
            }
            if (Space.manhattan(s.pos, bench) != 1)
            {
                { goTo(state, s, job, site); return; }
            }
            Items.removeItem(state, item);
            s.carrying = null;
            job.itemId = null;
            bench.stored += 1;
            bench.storedKind = recipe.input;
            s.note = $"brought {Js.Str(recipe.input)} to {Js.Str(station)} ({Js.Str(bench.stored)}/{Js.Str(cost)})";
            return;
        }
        if (Space.manhattan(s.pos, bench) != 1)
        {
            { goTo(state, s, job, site); return; }
        }
        var step = Skills.workStep(state, s, job.type);
        if (step == 0)
        {
            return;
        }
        job.progress += step;
        if (job.progress < (workshop ? Crafting.WORKSHOP_CRAFT_WORK : Crafting.CRAFT_WORK))
        {
            return;
        }
        bench.stored -= cost;
        if (bench.stored == 0)
        {
            bench.storedKind = null;
        }
        Items.spawnItem(state, kind, s.pos.x, s.pos.y);
        if (((kind == "meal") && (Hash.hashRoll(bench.x, bench.y, state.tick) < FireMod.STOVE_FIRE_CHANCE)) && FireMod.ignite(state, bench.x, bench.y))
        {
            Events.logEvent(state, $"The stove at ({Js.Str(bench.x)}, {Js.Str(bench.y)}) caught fire!");
        }
        var made = (kind == "meal") ? "cooked a hot meal" : ((kind == "medicine") ? "made medicine" : $"crafted {Js.Str(((kind == "armour") || (kind == "boots")) ? Js.Str("") : Js.Str("a "))}{Js.Str(kind)}");
        finishJob(state, s, job, $"{Js.Str(made)} at ({Js.Str(bench.x)}, {Js.Str(bench.y)})");
    }

    /// <summary>A guard on the way to their zombie stops walking once it's within swing reach (zombies close in off the grid: motion.ts), so the fight starts there rather than at the tile they were heading for. A step already under way lands first.</summary>
    public static void interruptForFight(SimState state, Survivor s)
    {
        if (!Js.T(s.goal) || !Js.T(s.jobId))
        {
            return;
        }
        var job = Jobs.getJob(state, s.jobId);
        if ((job?.type != "guard") || !Js.T(job.zombieId))
        {
            return;
        }
        var z = Zombies.getZombie(state, job.zombieId);
        if (!Js.T(z) || (Motion.distance2(s.at, z.at) > ((double)(Motion.SWING_REACH) * (Motion.SWING_REACH))))
        {
            return;
        }
        s.goal = null;
        s.path = [];
        s.pathVersion = -1;
    }

    public static void interruptScouting(SimState state, Survivor s)
    {
        if ((Js.T(s.jobId) && Fog.scoutingOver(state.tick)) && (Jobs.getJob(state, s.jobId)?.type == "scout"))
        {
            headHome(state, s);
        }
    }

    /// <summary>Evening: drop the scouting and walk back to your own bed, or to the others.</summary>
    public static void headHome(SimState state, Survivor s)
    {
        Jobs.releaseSurvivor(state, s, "heading home before dark");
        s.note = "heading home before dark";
        var bed = FurnitureMod.ofKind(state, "bed").find((f) => f.owner == s.id);
        var others = state.survivors.filter((o) => o != s);
        var home = bed ?? ((others.length > 0) ? new Point { x = Js.I(JsMath.round(((double)(others.reduce((a, o) => a + o.pos.x, 0.0)) / (others.length)))), y = Js.I(JsMath.round(((double)(others.reduce((a, o) => a + o.pos.y, 0.0)) / (others.length)))) } : null);
        if (!Js.T(home))
        {
            return;
        }
        var spot = Space.nearestTile(state.grid, home, (x, y) => Grids.isWalkable(state.grid, x, y) && Pathfinding.canReach(state.grid, state.walkabilityVersion, s.pos, new List<Point> { new Point { x = Js.I(x), y = Js.I(y) } }));
        if (Js.T(spot) && !Space.samePoint(spot, s.pos))
        {
            Survivors.setGoal(s, spot);
        }
    }

    public static void workScout(SimState state, Survivor s, Job job)
    {
        var area = Js.T(job.areaId) ? Fog.getExploreArea(state, job.areaId) : null;
        if (!Js.T(area))
        {
            { Jobs.releaseSurvivor(state, s, "area explored"); return; }
        }
        if (Fog.scoutingOver(state.tick))
        {
            { headHome(state, s); return; }
        }
        var target = Fog.isExplored(state, job.x, job.y) ? Fog.scoutTarget(state, area, s.pos) : new Point { x = Js.I(job.x), y = Js.I(job.y) };
        if (!Js.T(target))
        {
            { finishJob(state, s, job, $"scouted ({Js.Str(area.x0)}, {Js.Str(area.y0)})-({Js.Str(area.x1)}, {Js.Str(area.y1)}) as far as it goes"); return; }
        }
        job.x = Js.I(target.x);
        job.y = Js.I(target.y);
        if (!Space.samePoint(s.pos, target))
        {
            { goTo(state, s, job, [target]); return; }
        }
    }

    /// <summary>Fetch a bandage, carry it to the patient's bedside, and treat them.</summary>
    public static void workDoctor(SimState state, Survivor s, Job job)
    {
        var patient = Js.T(job.patientId) ? Survivors.getSurvivor(state, job.patientId) : null;
        if (!Js.T(patient) || !Medicine.isPatient(patient))
        {
            { Jobs.releaseSurvivor(state, s, "patient is up and about"); return; }
        }
        var site = Grids.neighbours4(state.grid, patient.pos.x, patient.pos.y);
        var item = Js.T(job.itemId) ? Items.getItem(state, job.itemId) : null;
        if (!Js.T(item))
        {
            item = Medicine.remediesFor(patient).map((k) => freeItemFor(state, k, s.pos, site)).find((i) => i != null);
            if (!Js.T(item))
            {
                { Jobs.releaseSurvivor(state, s, "nothing to treat them with"); return; }
            }
            job.itemId = item.id;
            item.reservedBy = s.id;
        }
        if (s.carrying != item.id)
        {
            if (!Js.T(item.pos))
            {
                { Jobs.releaseSurvivor(state, s, "item taken by someone else"); return; }
            }
            if (!Space.samePoint(s.pos, item.pos))
            {
                { goTo(state, s, job, [item.pos]); return; }
            }
            item.pos = null;
            item.carriedBy = s.id;
            s.carrying = item.id;
            s.note = $"picked up {Js.Str((item.kind == "medicine") ? Js.Str("medicine") : Js.Str("a bandage"))} for {Js.Str(patient.name)}";
        }
        if (Space.manhattan(s.pos, patient.pos) != 1)
        {
            { goTo(state, s, job, site); return; }
        }
        var step = Skills.workStep(state, s, "doctor");
        if (step == 0)
        {
            return;
        }
        job.progress += step;
        if (job.progress < Health.TREAT_TICKS)
        {
            return;
        }
        Items.removeItem(state, item);
        s.carrying = null;
        Medicine.applyRemedy(patient, item.kind);
        finishJob(state, s, job, (item.kind == "medicine") ? $"cured {Js.Str(patient.name)}'s infection" : $"treated {Js.Str(patient.name)}'s wounds");
    }

    public static void workClean(SimState state, Survivor s, Job job)
    {
        Point target = new Point { x = Js.I(job.x), y = Js.I(job.y) };
        if (!Space.samePoint(s.pos, target))
        {
            { goTo(state, s, job, [target]); return; }
        }
        var step = Skills.workStep(state, s, "clean");
        if (step == 0)
        {
            return;
        }
        job.progress += step;
        if (job.progress < Jobs.CLEAN_WORK)
        {
            return;
        }
        Grids.setDirty(state.grid, job.x, job.y, false);
        finishJob(state, s, job, $"cleaned ({Js.Str(job.x)}, {Js.Str(job.y)})");
    }

    public static void workBuild(SimState state, Survivor s, Job job)
    {
        if (Jobs.isRepairJob(job))
        {
            { workRepair(state, s, job); return; }
        }
        var bp = Js.T(job.blueprintId) ? Designations.getBlueprint(state, job.blueprintId) : null;
        if (!Js.T(bp))
        {
            { Jobs.releaseSurvivor(state, s, "designation cancelled"); return; }
        }
        if (Js.T(bp.side))
        {
            List<Point> sides = [new Point { x = Js.I(bp.x), y = Js.I(bp.y) }, (bp.side == "n") ? new Point { x = Js.I(bp.x), y = Js.I(bp.y - 1) } : new Point { x = Js.I(bp.x - 1), y = Js.I(bp.y) }];
            if (!sides.some((p) => Space.samePoint(p, s.pos)))
            {
                { goTo(state, s, job, sides); return; }
            }
        }
        else if (Space.manhattan(s.pos, bp) != 1)
        {
            { goTo(state, s, job, Grids.neighbours4(state.grid, bp.x, bp.y)); return; }
        }
        var step = Skills.workStep(state, s, "build");
        if (step == 0)
        {
            return;
        }
        bp.workDone += step;
        if (bp.workDone < bp.workRequired)
        {
            return;
        }
        Designations.completeBlueprint(state, bp);
        Noise.makeNoise(state, bp, "building");
        finishJob(state, s, job, (bp.target == "floor") ? $"removed structure at ({Js.Str(bp.x)}, {Js.Str(bp.y)})" : $"built {Js.Str(bp.target)} at ({Js.Str(bp.x)}, {Js.Str(bp.y)})");
    }

    /// <summary>Patch up a damaged wall or door from a tile next to it.</summary>
    public static void workRepair(SimState state, Survivor s, Job job)
    {
        if (job.edge != null)
        {
            { workRepairEdge(state, s, job, Js.NN(job.edge)); return; }
        }
        if (Space.manhattan(s.pos, job) != 1)
        {
            { goTo(state, s, job, Grids.neighbours4(state.grid, job.x, job.y)); return; }
        }
        var step = Skills.workStep(state, s, "build");
        if (step == 0)
        {
            return;
        }
        if (Breach.repairStructure(state, job.x, job.y, ((double)(Breach.REPAIR_PER_TICK) * (step))) > 0)
        {
            return;
        }
        Noise.makeNoise(state, job, "building");
        finishJob(state, s, job, $"repaired {Js.Str(Grids.getTile(state.grid, job.x, job.y))} at ({Js.Str(job.x)}, {Js.Str(job.y)})");
    }

    /// <summary>Mends an edge wall or door, standing on either side of it.</summary>
    public static void workRepairEdge(SimState state, Survivor s, Job job, int e)
    {
        List<Point> sides = [new Point { x = Js.I(job.x), y = Js.I(job.y) }, ((e % 2) == 0) ? new Point { x = Js.I(job.x), y = Js.I(job.y - 1) } : new Point { x = Js.I(job.x - 1), y = Js.I(job.y) }];
        if (!sides.some((p) => Space.samePoint(p, s.pos)))
        {
            { goTo(state, s, job, sides); return; }
        }
        var step = Skills.workStep(state, s, "build");
        if (step == 0)
        {
            return;
        }
        if (Breach.repairEdge(state, e, ((double)(Breach.REPAIR_PER_TICK) * (step))) > 0)
        {
            return;
        }
        Noise.makeNoise(state, job, "building");
        finishJob(state, s, job, ((((("repaired the " + ((Grids.edgeKind(state.grid, e) == Grids.EDGE_WALL) ? "wall" : "door")) + " at (") + job.x) + ", ") + job.y) + ")");
    }
    public static void workRepairEdge(SimState state, Survivor s, Job job, double e) => workRepairEdge(state, s, job, Js.I(e));

    /// <summary>Walk towards the goals (only the first `steps` tiles, to look again sooner).</summary>
    public static void goTo(SimState state, Survivor s, Job job, List<Point> goals, double? steps = default)
    {
        var path = Pathfinding.findPathToAny(state.grid, s.pos, goals);
        if (!Js.T(path) || (path.length == 0))
        {
            Jobs.releaseSurvivor(state, s, $"can't reach {Js.Str(job.type)} job at ({Js.Str(job.x)}, {Js.Str(job.y)})");
            return;
        }
        Survivors.walk(s, Js.T(steps) ? path.slice(0, steps) : path, state.walkabilityVersion);
    }

    public static void finishJob(SimState state, Survivor s, Job job, string note)
    {
        var i = state.jobs.indexOf(job);
        if (i >= 0)
        {
            state.jobs.splice(i, 1);
        }
        s.jobId = null;
        s.note = note;
    }
}
