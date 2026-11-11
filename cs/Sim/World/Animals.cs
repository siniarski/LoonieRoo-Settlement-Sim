// Ported from src/sim/animals.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Wild animals: deer to hunt, and a small food chain round them.
//
//   deer    most mornings (a hash of the day) a deer wanders in at the edge of explored ground,
//           browses about (a tile every DEER_STEP_EVERY ticks on average), keeps clear of people, and leaves
//           after DEER_STAYS. VENISON when brought down.
//   rabbits nibble about on open ground near your people and bolt from anything that moves near
//           (people, wolves, bears, the dead) within FLEE. Where there are two or more close
//           together they breed (a chance each hour), up to RABBITS_NEAR round each of yours.
//   wolves  come in packs (WOLF_PACK) out of the wild, and get hungry (hunger, on slowTick ticks).
//           Hungry, they hunt the nearest rabbit or deer (killing it feeds them); starving with
//           nothing to hunt, they come for people. Anyone who gets within WOLF_CLOSE is attacked
//           whatever their hunger (they don't like being crowded), and so is anyone who hurts one.
//           A pack follows its leader; a wolf badly hurt runs.
//   bears   rare (BEAR_CHANCE a day), slow and very tough. They wander, eat what they catch, and
//           maul anyone who comes within BEAR_CLOSE.
// They move freely (Walkers.cs: continuous positions at any angle, sliding along walls, A* only where
// the straight way is blocked); PACE is a walking speed (ticks a tile), and reach is between `at`s.
// Only near your people: animals turn up round them (a hash of the hour and the survivor) and are
// gone once nobody of yours is within WILD_RADIUS (they wandered off), so a bigger world costs
// nothing extra. Rolls are hashes (Hash.cs), not the sim RNG, so adding wildlife shifts nothing else.
// An animal that attacks someone is marked for hunting (the Hunt order's mark): hunters go for it.
// Hunting and hitting them: Handiwork.cs hitAnimal (yours), Work.cs (colonists'): MEAT by kind.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Animals
{
    static Animals()
    {
        Time.onClock(() => DEER_STAYS = ((double)(2) * (Time.DAY_LENGTH)));
        Time.onClock(() =>
        {
            WOLF_HUNGRY = ((double)(Time.DAY_LENGTH) / (2));
            WOLF_STARVING = ((double)(2) * (Time.DAY_LENGTH));
            PACK_STAYS = Time.DAY_LENGTH;
        });
    }

    /// <summary>How long after biting someone a wolf or bear counts as a menace to fight off.</summary>
    public const int MENACE_TICKS = 300;

    /// <summary>A wolf or bear that's bitten someone lately, or is out for someone: colonists on guard go for it (Jobs.cs).</summary>
    public static bool isMenace(SimState state, Animal a)
    {
        if ((a.kind != "wolf") && (a.kind != "bear"))
        {
            return false;
        }
        return !!Js.T(a.angryAt) || ((a.bitTick != null) && ((state.tick - a.bitTick) <= MENACE_TICKS));
    }

    public const double DEER_CHANCE = 0.6;

    public static double DEER_STAYS = ((double)(2) * (Time.DAY_LENGTH));

    public const int DEER_STEP_EVERY = 20;

    public const int DEER_TICKS_PER_TILE = 5;

    /// <summary>Deer keep this far from survivors when they can (they're wary, not fast).</summary>
    public const int DEER_WARY = 3;

    public const int HUNT_WORK = 20;

    public const int VENISON = 3;

    /// <summary>Food from each kind brought down.</summary>
    public static readonly JsRecord<double> MEAT = new() { ["deer"] = VENISON, ["rabbit"] = 1, ["wolf"] = 2, ["bear"] = 6 };

    public static readonly JsRecord<double> HEALTH = new() { ["rabbit"] = 20, ["wolf"] = 150, ["bear"] = 700 };

    /// <summary>Ticks a tile, by kind: how fast each walks (Walkers.speedOf).</summary>
    public static readonly JsRecord<double> PACE = new() { ["deer"] = DEER_TICKS_PER_TILE, ["rabbit"] = 3, ["wolf"] = 4, ["bear"] = 7 };

    public const int WILD_RADIUS = 70;

    /// <summary>Rabbits, wolves and bears only on maps at least this big (a yard isn't the wild).</summary>
    public const int WILD_MAP = 64;

    /// <summary>Animals turn up at least this far from all your people.</summary>
    public const int SPAWN_CLEAR = 14;

    public const int FLEE = 4;

    public const int RABBITS_NEAR = 4;

    /// <summary>How far round someone (or a rabbit) the rabbits are counted for those caps.</summary>
    public const int RABBIT_RANGE = 40;

    public const double RABBIT_CHANCE = 0.5;

    public const double BREED_CHANCE = 0.15;

    public const int WOLF_PACK = 3;

    /// <summary>Ticks a tile for a wolf running something down (quicker than a rabbit).</summary>
    public const int WOLF_SPRINT = 2;

    /// <summary>Chance an hour (at night and in winter: twice each) that a pack comes in near your people with none about.</summary>
    public const double WOLF_CHANCE = 0.01;

    /// <summary>Hunger (slowTick ticks) at which wolves hunt prey, and at which they'll come for people. (A bear hunts
    /// at WOLF_STARVING: chooseTarget.)</summary>
    public static double WOLF_HUNGRY = ((double)(Time.DAY_LENGTH) / (2));

    public static double WOLF_STARVING = ((double)(2) * (Time.DAY_LENGTH));

    /// <summary>A pack moves on after this long.</summary>
    public static double PACK_STAYS = Time.DAY_LENGTH;

    public const int WOLF_CLOSE = 2;

    /// <summary>Wolves that aren't starving keep this far from people.</summary>
    public const int WOLF_WARY = 6;

    /// <summary>After a warning bite, how long it keeps clear.</summary>
    public const int BACK_OFF = 80;

    public const int WOLF_DAMAGE = 30;

    public const int WOLF_BITE_EVERY = 12;

    public const double BEAR_CHANCE = 0.08;

    public const int BEAR_CLOSE = 3;

    public const int BEAR_DAMAGE = 90;

    public const int BEAR_BITE_EVERY = 16;

    /// <summary>How far a predator looks for prey, or a starving one for people.</summary>
    public const int HUNT_SIGHT = 15;

    /// <summary>An animal brought down: gone, and its meat (MEAT by kind) lies where it fell.</summary>
    public static void butcher(SimState state, Animal a)
    {
        state.animals.splice(state.animals.indexOf(a), 1);
        for (var n = 0; n < Js.Get(MEAT, a.kind); n++)
        {
            Items.spawnItem(state, "food", a.pos.x, a.pos.y);
        }
    }

    public static Animal? getAnimal(SimState state, string id)
    {
        return state.animals.find((a) => a.id == id);
    }

    public static Animal addDeer(SimState state, int x, int y)
    {
        Animal a = new Animal { id = $"d{Js.Str(state.nextId++)}", kind = "deer", pos = new Point { x = Js.I(x), y = Js.I(y) }, move = null, hunted = false, leavesTick = state.tick + DEER_STAYS, at = Motion.placedAt(new Point { x = Js.I(x), y = Js.I(y) }).at, was = Motion.placedAt(new Point { x = Js.I(x), y = Js.I(y) }).was };
        state.animals.push(a);
        return a;
    }
    public static Animal addDeer(SimState state, double x, double y) => addDeer(state, Js.I(x), Js.I(y));

    /// <summary>A rabbit, wolf or bear at a tile.</summary>
    public static Animal addAnimal(SimState state, string kind, int x, int y, string? packId = default)
    {
        var placed = Motion.placedAt(new Point { x = x, y = y });
        Animal a = new Animal { id = $"w{Js.Str(state.wildId = (state.wildId ?? 0) + 1)}", kind = kind, pos = new Point { x = x, y = y }, at = placed.at, was = placed.was, move = null, hunted = false, leavesTick = JsNumber.MAX_SAFE_INTEGER, health = HEALTH[kind], hunger = 0, targetId = null, nextBite = 0 };
        if (Js.T(packId)) a.packId = packId;
        if ((kind == "wolf") && !Js.T(packId))
        {
            a.packId = a.id;
        }
        state.animals.push(a);
        return a;
    }
    public static Animal addAnimal(SimState state, string kind, double x, double y, string? packId = default) => addAnimal(state, kind, Js.I(x), Js.I(y), packId);

    /// <summary>Open ground an animal can stand on.</summary>
    public static bool ground(SimState state, int x, int y)
    {
        return Grids.getTile(state.grid, x, y) == "floor";
    }
    public static bool ground(SimState state, double x, double y) => ground(state, Js.I(x), Js.I(y));

    /// <summary>A* plans animals may make in one tick, all told (rule 14; most walks are straight and need none).</summary>
    public const int PLANS_PER_TICK = 4;

    public static void updateAnimals(SimState state)
    {
        var day = Time.dayOf(state.tick);
        if (((Time.tickOfDay(state.tick) == ((double)(2) * (Time.TICKS_PER_HOUR))) && (Hash.hashRoll(day, 57063.0) < DEER_CHANCE)) && (state.survivors.length > 0))
        {
            var spot = Wanderers.frontierSpot(state, new JsSet<string>());
            if (Js.T(spot))
            {
                addDeer(state, spot.x, spot.y);
                Events.logEvent(state, $"Deer seen at ({Js.Str(spot.x)}, {Js.Str(spot.y)}) (the Hunt order marks them)");
            }
        }
        if (((((state.tick % Time.TICKS_PER_HOUR) == 0) && (state.survivors.length > 0)) && (state.grid.width >= WILD_MAP)) && (state.grid.height >= WILD_MAP))
        {
            hourly(state);
        }
        var budget = new WalkBudget { plans = PLANS_PER_TICK };
        foreach (var a in Js.Iter((state.animals).ToList()))
        {
            if (!state.animals.includes(a))
            {
                continue;
            }
            if (state.tick >= a.leavesTick)
            {
                state.animals.splice(state.animals.indexOf(a), 1);
                continue;
            }
            if ((a.hunger != null) && Time.slowTick(state.tick))
            {
                a.hunger += 1;
            }
            // Under or over a parked car (AnimalCars.cs): that is all it does until it is down on the far side.
            if (AnimalCars.step(state, a))
            {
                continue;
            }
            // A free mover (Walkers.cs): if something moved it by tile (an eviction), `at` follows.
            a.move = null;
            Motion.followTile(a);
            if (!ground(state, a.pos.x, a.pos.y) && !offGround(state, a))
            {
                continue;
            }
            if (a.kind == "deer")
            {
                deer(state, a, budget);
            }
            else if (a.kind == "rabbit")
            {
                rabbit(state, a, budget);
            }
            else
            {
                predator(state, a, budget);
            }
        }
    }

    /// <summary>Built over (or a wall dropped on it): out to the nearest open ground, or gone (false).</summary>
    public static bool offGround(SimState state, Animal a)
    {
        var @out = Space.nearestTile(state.grid, a.pos, (x, y) => ground(state, x, y), 60);
        if (!Js.T(@out))
        {
            state.animals.splice(state.animals.indexOf(a), 1);
            return false;
        }
        a.pos = @out;
        Js.Object_assign(a, Motion.placedAt(@out));
        a.goal = null;
        a.path = [];
        return true;
    }

    /// <summary>Its walking speed, SUB units a tick (PACE ticks a tile).</summary>
    public static double speedOf(Animal a)
    {
        return Walkers.speedOf(Js.Get(PACE, a.kind));
    }

    /// <summary>Walk on to its goal, if it has one; it stands again (no goal) once there, or if it can't get on.</summary>
    public static void walkOn(SimState state, Animal a, double speed, WalkBudget budget)
    {
        if (!Js.T(a.goal))
        {
            return;
        }
        var was = a.at;
        if (Walkers.walkTo(state, a, a.goal, speed, Pathfinding.ANIMAL, budget, 0, true) || ((a.at.x == was.x) && (a.at.y == was.y)))
        {
            a.goal = null;
        }
    }

    /// <summary>Away from a point (SUB units), `tiles` at a time: a new leg when it has none, or every `every` ticks (the threat moves).</summary>
    public static void flee(SimState state, Animal a, Point from, double tiles, double every, WalkBudget budget)
    {
        if (!Js.T(a.goal) || ((state.tick % every) == 0))
        {
            a.goal = Walkers.fleeLeg(state, a, from, tiles, Pathfinding.ANIMAL);
            a.path = [];
        }
        a.restUntil = state.tick;
        walkOn(state, a, speedOf(a), budget);
    }

    /// <summary>Wander (graze, roam): once it's stood long enough, a walk 1 to 3 tiles off at any angle, then a stand, so it goes a tile every `every` ticks on average (as the tile steps did).</summary>
    public static void wander(SimState state, Animal a, double every, WalkBudget budget)
    {
        if (!Js.T(a.goal) && (state.tick >= a.restUntil))
        {
            a.goal = Walkers.wanderLeg(state, a, Pathfinding.ANIMAL);
            a.path = [];
            a.restUntil = state.tick + (Js.T(a.goal) ? JsMath.floor(((double)(JsMath.sqrt(Motion.distance2(a.goal, a.at))) * (every)) / Motion.SUB) : every);
        }
        walkOn(state, a, speedOf(a), budget);
    }

    /// <summary>Deer browse about, and every DEER_STEP_EVERY ticks with someone within DEER_WARY walk a tile away from them (wary, not fast).</summary>
    public static void deer(SimState state, Animal a, WalkBudget budget)
    {
        if ((state.tick % DEER_STEP_EVERY) == 0)
        {
            var near = state.survivors.find((s) => Space.manhattan(s.pos, a.pos) <= DEER_WARY);
            if (Js.T(near))
            {
                a.goal = Walkers.fleeLeg(state, a, near.at, 1, Pathfinding.ANIMAL);
                a.path = [];
                a.restUntil = state.tick + DEER_STEP_EVERY;
            }
        }
        wander(state, a, DEER_STEP_EVERY, budget);
    }

    /// <summary>What a rabbit bolts from: someone, a wolf or bear, or one of the dead within FLEE tiles. Where it is, SUB units.</summary>
    public static Point? rabbitThreat(SimState state, Animal a)
    {
        return (state.survivors.find((s) => Space.manhattan(s.pos, a.pos) <= FLEE)?.at ?? state.animals.find((o) => ((o.kind == "wolf") || (o.kind == "bear")) && (Space.manhattan(o.pos, a.pos) <= FLEE))?.at) ?? ((Zombies.zombiesWithin(state, a.pos, FLEE) > 0) ? state.zombies.find((z) => Space.manhattan(z.pos, a.pos) <= FLEE)?.at : null);
    }

    public static void rabbit(SimState state, Animal a, WalkBudget budget)
    {
        var threat = rabbitThreat(state, a);
        if (Js.T(threat))
        {
            flee(state, a, threat, 3, 6, budget);
            return;
        }
        wander(state, a, 12, budget);
    }

    public static void predator(SimState state, Animal a, WalkBudget budget)
    {
        var hp = Js.Q(a.health, 0);
        var hurt = hp < ((double)(Js.Get(HEALTH, a.kind)) * (0.3));
        var nearest = closestSurvivor(state, a.pos);
        if ((Js.T(hurt && Js.T(nearest))) && (Space.manhattan(nearest.pos, a.pos) <= 8))
        {
            flee(state, a, nearest.at, 3, 8, budget);
            return;
        }
        if ((Js.T((((Js.Q(a.backOffUntil, 0)) > state.tick) && !Js.T(a.angryAt)) && Js.T(nearest))) && (Space.manhattan(nearest.pos, a.pos) <= 8))
        {
            flee(state, a, nearest.at, 3, 8, budget);
            return;
        }
        var target = chooseTarget(state, a, nearest);
        a.targetId = target?.id ?? null;
        if ((Js.T((!Js.T(target) && (a.kind == "wolf")) && Js.T(nearest))) && (Space.manhattan(nearest.pos, a.pos) <= WOLF_WARY))
        {
            flee(state, a, nearest.at, 3, 8, budget);
            return;
        }
        if (!Js.T(target))
        {
            var leader = (Js.T(a.packId) && (a.packId != a.id)) ? state.animals.find((o) => o.id == a.packId) : null;
            if (Js.T(leader) && (Space.manhattan(leader.pos, a.pos) > 2))
            {
                a.goal = null;
                Walkers.walkTo(state, a, leader.at, speedOf(a), Pathfinding.ANIMAL, budget, (double)(2) * (Motion.SUB));
                return;
            }
            wander(state, a, (a.kind == "bear") ? 25 : 15, budget);
            return;
        }
        a.goal = null;
        if (Motion.inReach(a, target, Motion.REACH, state.grid) || besideCar(state, a, target))
        {
            attack(state, a, target);
            return;
        }
        // Run it down: straight at it (A* round what's in the way), stopping just inside reach, and
        // bite as it gets there (else a fleeing rabbit is always a step out of reach by its next turn).
        Walkers.walkTo(state, a, target.at, (a.kind == "wolf") ? Walkers.speedOf(WOLF_SPRINT) : speedOf(a), Pathfinding.ANIMAL, budget, Motion.REACH - 16);
        if (Motion.inReach(a, target, Motion.REACH, state.grid) || besideCar(state, a, target))
        {
            attack(state, a, target);
        }
    }

    /// <summary>Is its target sitting in a car whose side is within a bite of it (VehicleCabin.cs)?</summary>
    static bool besideCar(SimState state, Animal a, IBody target)
    {
        return target is Survivor s && Vehicles.vehicleOf(state, s) is { } v && Vehicles.occupied(v) && Vehicles.gap(v, a.at) <= Vehicles.CABIN_REACH;
    }

    public static Survivor? closestSurvivor(SimState state, Point p)
    {
        Survivor? best = null;
        foreach (var s in Js.Iter(state.survivors))
        {
            if (!Js.T(best) || (Space.manhattan(s.pos, p) < Space.manhattan(best.pos, p)))
            {
                best = s;
            }
        }
        return best;
    }

    /// <summary>Who a wolf or bear goes for: whoever it's out to get (they hurt it, or it's starving), anyone who comes too close (a warning: one bite, then it backs off), prey when hungry.</summary>
    public static IBody? chooseTarget(SimState state, Animal a, Survivor? nearest)
    {
        var close = (a.kind == "bear") ? BEAR_CLOSE : WOLF_CLOSE;
        var after = Js.T(a.angryAt) ? state.survivors.find((s) => s.id == a.angryAt) : null;
        if (Js.T(after) && (Space.manhattan(after.pos, a.pos) <= HUNT_SIGHT))
        {
            return after;
        }
        a.angryAt = null;
        if (Js.T(nearest) && (Space.manhattan(nearest.pos, a.pos) <= close))
        {
            return nearest;
        }
        var hunger = Js.Q(a.hunger, 0);
        if (Js.T((a.kind == "wolf") ? (hunger >= WOLF_HUNGRY) : (hunger >= WOLF_STARVING)))
        {
            Animal? prey = null;
            foreach (var o in Js.Iter(state.animals))
            {
                if (((o.kind != "rabbit") && (o.kind != "deer")) || (Space.manhattan(o.pos, a.pos) > HUNT_SIGHT) || AnimalCars.hidden(o))
                {
                    continue;
                }
                if (!Js.T(prey) || (Space.manhattan(o.pos, a.pos) < Space.manhattan(prey.pos, a.pos)))
                {
                    prey = o;
                }
            }
            if (Js.T(prey))
            {
                return prey;
            }
        }
        if ((Js.T(((a.kind == "wolf") && (hunger >= WOLF_STARVING)) && Js.T(nearest))) && (Space.manhattan(nearest.pos, a.pos) <= HUNT_SIGHT))
        {
            a.angryAt = nearest.id;
            return nearest;
        }
        return null;
    }

    public static void attack(SimState state, Animal a, IBody target)
    {
        if (state.tick < (Js.Q(a.nextBite, 0)))
        {
            return;
        }
        var bear = a.kind == "bear";
        a.nextBite = state.tick + (bear ? BEAR_BITE_EVERY : WOLF_BITE_EVERY);
        if (Js.In("kind", target))
        {
            state.animals.splice(state.animals.indexOf((Animal)target), 1);
            a.hunger = 0;
            return;
        }
        var s = (Survivor)target;
        if (Vehicles.batterVehicle(state, s))
        {
            return;  // in a car that isn't a wreck: the car takes it (VehicleCabin.cs)
        }
        s.health = JsMath.max(0, s.health - (bear ? BEAR_DAMAGE : WOLF_DAMAGE));
        a.bitTick = state.tick;
        s.lastHurtBy = bear ? "a bear" : "wolves";
        s.hurtTick = state.tick;
        s.note = $"attacked by a {Js.Str(a.kind)} at ({Js.Str(a.pos.x)}, {Js.Str(a.pos.y)})";
        Mood.addThought(state, s, "attacked");
        BodyMod.frighten(s, BodyMod.HIT_PANIC);
        BodyMod.startle(state, s);
        if (!Js.T(a.angryAt))
        {
            a.backOffUntil = state.tick + BACK_OFF;
        }
        if (!a.hunted)
        {
            a.hunted = true;
            Events.logEvent(state, $"{Js.Str(bear ? Js.Str("A bear") : Js.Str("Wolves"))} attacked {Js.Str(s.name)} at ({Js.Str(a.pos.x)}, {Js.Str(a.pos.y)})!");
        }
    }

    /// <summary>Someone of yours hurt it: it goes for them (wolves and bears), and its pack too.</summary>
    public static void provoke(SimState state, Animal a, Survivor by)
    {
        if ((a.kind != "wolf") && (a.kind != "bear"))
        {
            return;
        }
        foreach (var o in Js.Iter(state.animals))
        {
            if ((o != a) && !(Js.T(a.packId) && (o.packId == a.packId)))
            {
                continue;
            }
            o.angryAt = by.id;
            o.backOffUntil = 0;
        }
    }

    /// <summary>Every hour: rabbits about your people, breeding; now and then a wolf pack or (rarely) a bear; and animals far from everyone gone.</summary>
    public static void hourly(SimState state)
    {
        var hour = JsMath.floor(((double)(state.tick) / (Time.TICKS_PER_HOUR)));
        var winter = Seasons.seasonOf(state.tick) == "winter";
        state.animals = state.animals.filter((a) => (a.kind == "deer") || state.survivors.some((s) => Space.manhattan(s.pos, a.pos) <= WILD_RADIUS));
        state.survivors.forEach((s, k) =>
        {
            double roll(double salt)
            {
                return Hash.hashRoll(hour, Hash.idNumber(s.id), salt);
            }
            List<Animal> near(string kind, double r)
            {
                return state.animals.filter((a) => (a.kind == kind) && (Space.manhattan(a.pos, s.pos) <= r));
            }
            if ((near("rabbit", RABBIT_RANGE).length < (winter ? 2 : RABBITS_NEAR)) && (roll(1) < RABBIT_CHANCE))
            {
                spawnNear(state, s.pos, "rabbit", roll(2), roll(3));
            }
            var wolfChance = ((double)(((double)(WOLF_CHANCE) * ((Time.isNight(state.tick) ? 2 : 1)))) * ((winter ? 2 : 1)));
            if (((k == 0) && (near("wolf", 60).length == 0)) && (roll(4) < wolfChance))
            {
                var lead = spawnNear(state, s.pos, "wolf", roll(5), roll(6));
                if (Js.T(lead))
                {
                    for (var n = 1; n < WOLF_PACK; n++)
                    {
                        var p = Space.nearestTile(state.grid, lead.pos, (x, y) => ground(state, x, y) && !state.animals.some((o) => (o.pos.x == x) && (o.pos.y == y)), 50);
                        if (Js.T(p))
                        {
                            addAnimal(state, "wolf", p.x, p.y, lead.id);
                        }
                    }
                    foreach (var o in Js.Iter(state.animals))
                    {
                        if (o.packId != lead.id)
                        {
                            continue;
                        }
                        o.hunger = winter ? (WOLF_STARVING - WOLF_HUNGRY) : WOLF_HUNGRY;
                        o.leavesTick = state.tick + PACK_STAYS;
                    }
                    Events.logEvent(state, $"Wolves howling out past ({Js.Str(lead.pos.x)}, {Js.Str(lead.pos.y)})");
                }
            }
            if ((((k == 0) && (Time.tickOfDay(state.tick) == ((double)(6) * (Time.TICKS_PER_HOUR)))) && !state.animals.some((a) => a.kind == "bear")) && (roll(7) < BEAR_CHANCE))
            {
                var b = spawnNear(state, s.pos, "bear", roll(8), roll(9));
                if (Js.T(b))
                {
                    Events.logEvent(state, $"A bear's tracks near ({Js.Str(b.pos.x)}, {Js.Str(b.pos.y)}). Keep your distance");
                }
            }
        });
        foreach (var a in Js.Iter((state.animals).ToList()))
        {
            if ((a.kind != "rabbit") || winter)
            {
                continue;
            }
            var close = state.animals.filter((o) => (o.kind == "rabbit") && (Space.manhattan(o.pos, a.pos) <= 8)).length;
            var about = state.animals.filter((o) => (o.kind == "rabbit") && (Space.manhattan(o.pos, a.pos) <= RABBIT_RANGE)).length;
            if (((close < 2) || (about >= ((double)(RABBITS_NEAR) * (2)))) || (Hash.hashRoll(hour, Hash.idNumber(a.id), 0xb7) >= BREED_CHANCE))
            {
                continue;
            }
            var p = Space.nearestTile(state.grid, a.pos, (x, y) => ground(state, x, y) && !((x == a.pos.x) && (y == a.pos.y)), 30);
            if (Js.T(p))
            {
                addAnimal(state, "rabbit", p.x, p.y);
            }
        }
    }

    /// <summary>An animal out in the open 18-34 tiles from a point (by hash), or null.</summary>
    public static Animal? spawnNear(SimState state, Point from, string kind, double r1, double r2)
    {
        var angle = ((double)(((double)(r1) * (JsMath.PI))) * (2));
        var dist = 18 + JsMath.floor(((double)(r2) * (16)));
        var __d0 = state.grid;
        var width = __d0.width;
        var height = __d0.height;
        Point want = new Point { x = Js.I(JsMath.max(0, JsMath.min(width - 1, JsMath.round(from.x + ((double)(JsMath.cos(angle)) * (dist)))))), y = Js.I(JsMath.max(0, JsMath.min(height - 1, JsMath.round(from.y + ((double)(JsMath.sin(angle)) * (dist)))))) };
        bool clear(double x, double y)
        {
            return state.survivors.every((s) => Space.manhattan(s.pos, new Point { x = Js.I(x), y = Js.I(y) }) >= SPAWN_CLEAR);
        }
        var p = Space.nearestTile(state.grid, want, (x, y) => ((ground(state, x, y) && clear(x, y)) && (Rooms.labelAt(state, x, y) == Rooms.OUTDOORS)) && !state.animals.some((o) => (o.pos.x == x) && (o.pos.y == y)), 200);
        return Js.T(p) ? addAnimal(state, kind, p.x, p.y) : null;
    }
}
