// Ported from src/sim/tick.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// The fixed-timestep tick. Speed controls change how many ticks run per
// frame, never what a tick means.
//
// tick() is the order everything happens in, and the order is part of the game: same seed and commands, same
// world, only while it stays as it is (the systems draw from one RNG in turn). Its phases are marked below.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Tick
{

    public const int TICKS_PER_SECOND = 10;

    public static void tick(SimState state)
    {
        // The clock runs at this world's day length (a process-wide setting: Time.cs).
        Time.setDayScale(state.settings.dayScale);
        // 1. Where every mover is now, for the renderer to blend from (`was`). Over the movers' lists in turn (no
        //    combined list: nothing here adds or removes one).
        markWas(state.survivors);
        markWas(state.zombies);
        markWas(state.dogs);
        markWas(state.animals);
        markWas(state.residents);
        // 2. The world, a system at a time: the dead first (tonight's arrivals, the dawn retreat, every zombie's move
        //    or bite), then fire, power and water, animals, the storyteller, everyone else's lives, the weather,
        //    cars, the farm. Each update* looks at the clock itself and does nothing on most ticks.
        Zombies.updateZombies(state);
        FireMod.updateFires(state);
        Corpses.updateCorpses(state);
        Water.updateCollectors(state);
        Power.updatePower(state);
        Power.updateHeaters(state);
        Animals.updateAnimals(state);
        Director.updateDirector(state);
        Prisoners.updatePrisoners(state);
        Radio.updateRadio(state);
        EndgameMod.updateEndgame(state);
        Escape.updateHelicopter(state);
        Policies.updateLeader(state);
        Defences.updateDefences(state);
        Outposts.updateOutposts(state);
        Power.updateSpoilage(state);
        Dogs.updateDogs(state);
        Talk.updateTasks(state);
        Settlements.updateSettlements(state);
        Colonies.updateColonies(state);
        Caravans.updateCaravans(state);
        Runs.updateRuns(state);
        Errands.updateErrands(state);
        Residents.updateResidents(state);
        Family.updateFamily(state);
        Herds.updateHerds(state);
        Seasons.updateWeather(state);
        TimelineMod.updateTimeline(state);
        Vehicles.updateVehicles(state);
        Relationships.updateRelationships(state);
        Wellbeing.updateSocial(state);
        if ((state.tick % Time.DAY_LENGTH) == 0)
        {
            Handiwork.forgetForaging(state);
        }
        Wishes.updateWishes(state);
        Expeditions.updateExpeditions(state);
        Visitors.updateVisitors(state);
        Farming.updateFarm(state);
        Wanderers.updateWanderers(state);
        Fog.reconcileExploreAreas(state);
        Fog.reconcilePointsOfInterest(state);
        // 3. Sleepers the world just woke (hurt this tick, or guards sleeping light), and those whose resolve gave
        //    out at dawn, before anyone is handed work.
        foreach (var s in Js.Iter(state.survivors))
        {
            if (Js.T(s.sleep) && (s.hurtTick == state.tick))
            {
                Needs.wake(state, s, "woken by a zombie!");
            }
        }
        Needs.wakeLightSleepers(state);
        foreach (var s in Js.Iter(Resolve.dawnResolve(state)))
        {
            Needs.killSurvivor(state, s, "walked away", true);
        }
        // 4. The job queue made to match the world as it now is (docs/sim-model.md), and reservations nobody holds
        //    any more let go.
        Jobs.reconcileJobs(state);
        Needs.reconcileNeeds(state);
        // 5. Each survivor's turn: you by your held commands, the rest by themselves (behave); then what a tick does
        //    to a body. Whoever it kills is noted, not removed: the list is being walked.
        List<TickDying> dying = [];
        foreach (var s in Js.Iter(state.survivors))
        {
            if (ControlMod.isControlled(state, s))
            {
                ControlMod.updateControlled(state, s);
            }
            else
            {
                behave(state, s);
            }
            Needs.updateRest(state, s);
            Needs.updateSatiety(state, s);
            Wounds.updateWounds(state, s);
            Water.updateHydration(state, s);
            BodyMod.updateBody(state, s);
            Climate.updateRadiation(state, s);
            Towers.countWatch(state, s);
            Mood.forgetThoughts(state, s);
            var cause = Needs.updateHealth(state, s) ?? Infection.updateInfection(state, s);
            if (Js.T(cause))
            {
                dying.push(new() { s = s, cause = cause });
            }
        }
        // 6. The dying die; one the infection took gets up again.
        foreach (var __o0 in Js.Iter(dying))
        {
            var s = __o0.s;
            var cause = __o0.cause;
            Needs.killSurvivor(state, s, cause);
            if (cause == Infection.CAUSE)
            {
                Zombies.addZombie(state, s.pos.x, s.pos.y, Zombies.ZOMBIE_HEALTH, "static");
            }
        }
        // 7. Put right what the turns and the deaths left dangling (rooms, reservations, wanderers, who you are).
        Rooms.reconcileRoomDesignations(state);
        Needs.reconcileNeeds(state);
        Wanderers.reconcileWanderers(state);
        ControlMod.reconcileControl(state);
        // 8. Doors swing shut behind whoever passed; tile walkers' precise positions follow their steps (free movers
        //    own theirs); tracks in the snow, what has been seen; and the clock moves on.
        Doors.closeBehind(state);
        syncSteps(state, state.survivors);
        syncSteps(state, state.zombies);
        syncSteps(state, state.dogs);
        syncSteps(state, state.animals);
        syncSteps(state, state.residents);
        Snow.tick(state);
        Fog.updateFog(state);
        state.tick = Js.I(state.tick + 1);
    }

    /// <summary>What a survivor does on their own: needs first, then leisure, then work.</summary>
    public static void behave(SimState state, Survivor s)
    {
        Leisure.updateBreakdown(state, s);
        Needs.interruptForNeeds(state, s);
        Work.interruptScouting(state, s);
        Work.interruptForFight(state, s);
        Survivors.updateMovement(state, s);
        if (Js.T(Js.T(s.move) || Js.T(s.goal)))
        {
            return;
        }
        var needs = (((((Needs.updateMeal(state, s) || Forage.updateForage(state, s, () => (Needs.nearestFreeItem(state, s, "food") != null) || (Needs.nearestFreeItem(state, s, "meal") != null))) || Water.updateDrink(state, s)) || Needs.updateTreatment(state, s)) || Weapons.updateArming(state, s)) || Clothing.updateDressing(state, s)) || Needs.updateSleep(state, s);
        if (Js.T(needs))
        {
            s.leisure = null;
            Books.stopReading(state, s, s.note ?? "");
        }
        else if (((!Js.T(s.breakdown) && !Orders.keepOrder(state, s)) && !Leisure.updateLeisure(state, s)) && !Books.updateReading(state, s))
        {
            Work.updateWork(state, s);
        }
    }

    static void markWas<T>(List<T> list) where T : Body
    {
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            e.was = new Point { x = Js.I(e.at.x), y = Js.I(e.at.y) };
        }
    }

    static void syncSteps<T>(SimState state, List<T> list) where T : Body
    {
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (!isFree(state, e))
            {
                Motion.syncStep(state, e);
            }
        }
    }

    /// <summary>Everything with a precise position (Motion.cs), as one list. For the harness's profiler: tick() walks
    /// the lists themselves.</summary>
    public static List<Body> movers(SimState state)
    {
        return (List<Body>)[.. state.survivors, .. state.zombies, .. state.dogs, .. state.animals, .. state.residents];
    }

    /// <summary>Moving under its own steering, not tile steps (Motion.cs): the dead (their ids begin with z), animals
    /// and dogs (Walkers.cs), and you.</summary>
    public static bool isFree(SimState state, Body e)
    {
        var id = ((IHasId)(e)).id ?? "";
        return id.startsWith("z") || (e is Animal) || (e is Dog) || ((state.control != null) && (id == state.control.survivorId));
    }

    public static void step(SimState state, double ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            tick(state);
        }
    }
}
