// Ported from src/sim/dogs.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Dogs. One sometimes comes back with an expedition, and strays wander in now
// and then (a hash of the day, not the sim RNG). A dog stays near the
// colony, following whoever is nearest. When a zombie comes within
// DOG_SENSE it barks (once in a while: it wakes the guards) and goes for it,
// biting for DOG_BITE. Zombies with nobody else to hit go for the dog.
// Being near a dog cheers people up.
// Dogs move freely (Walkers.cs: straight at the zombie or whoever they follow, at any angle, sliding
// along walls; A* round what's in the way), DOG_TICKS_PER_TILE a tile, biting by reach between `at`s.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Dogs
{

    public const int DOG_HEALTH = 300;

    public const int DOG_SENSE = 10;

    public const int DOG_BITE = 40;

    public const int DOG_BITE_EVERY = 10;

    /// <summary>How fast a dog goes: ticks a tile.</summary>
    public const int DOG_TICKS_PER_TILE = 3;

    public const int DOG_FOLLOW = 3;

    public const int BARK_EVERY = 400;

    public const int PET_EVERY = 200;

    /// <summary>Each day at noon, the chance a stray turns up.</summary>
    public const double STRAY_CHANCE = 0.12;

    /// <summary>The chance an expedition comes home with a dog.</summary>
    public const double EXPEDITION_DOG_CHANCE = 0.25;

    public static readonly List<string> DOG_NAMES = ["Rex", "Bess", "Scout", "Moss", "Patch", "Juno", "Biscuit", "Tank"];

    public static Dog addDog(SimState state, int x, int y)
    {
        var n = state.nextId++;
        Dog dog = new Dog { id = $"g{Js.Str(n)}", name = Js.NN(Js.Get(DOG_NAMES, n % DOG_NAMES.length)), pos = new Point { x = Js.I(x), y = Js.I(y) }, move = null, path = [], health = DOG_HEALTH, cooldown = 0, nextThinkTick = 0, barkedTick = -BARK_EVERY, waiting = false, at = Motion.placedAt(new Point { x = Js.I(x), y = Js.I(y) }).at, was = Motion.placedAt(new Point { x = Js.I(x), y = Js.I(y) }).was };
        state.dogs.push(dog);
        return dog;
    }
    public static Dog addDog(SimState state, double x, double y) => addDog(state, Js.I(x), Js.I(y));

    public static void updateDogs(SimState state)
    {
        var day = Time.dayOf(state.tick);
        if ((((day >= 2) && (Time.tickOfDay(state.tick) == ((double)(6) * (Time.TICKS_PER_HOUR)))) && (Hash.hashRoll(day, 0xd06) < STRAY_CHANCE)) && (state.survivors.length > 0))
        {
            var spot = Wanderers.frontierSpot(state, new JsSet<string>()) ?? Js.NN(Js.Get(state.survivors, 0)).pos;
            var dog = addDog(state, spot.x, spot.y);
            Events.logEvent(state, $"A stray dog, {Js.Str(dog.name)}, has wandered in at ({Js.Str(spot.x)}, {Js.Str(spot.y)})");
        }
        var budget = new WalkBudget { plans = PLANS_PER_TICK };
        foreach (var dog in Js.Iter(state.dogs))
        {
            updateDog(state, dog, budget);
        }
        if (((state.tick % PET_EVERY) == 0) && (state.dogs.length > 0))
        {
            foreach (var s in Js.Iter(state.survivors))
            {
                if (!Js.T(s.sleep?.asleep) && state.dogs.some((d) => Space.manhattan(d.pos, s.pos) <= DOG_FOLLOW))
                {
                    Mood.addThought(state, s, "dog");
                }
            }
        }
    }

    public static Dog? getDogById(SimState state, string id)
    {
        return state.dogs.find((d) => d.id == id);
    }

    /// <summary>A* plans dogs may make in one tick, all told (rule 14).</summary>
    public const int PLANS_PER_TICK = 2;

    /// <summary>Following someone, a dog walks until it's this near them (SUB units, centre to centre).</summary>
    public const int DOG_HEEL = 320;

    public static void updateDog(SimState state, Dog dog, WalkBudget budget)
    {
        dog.cooldown = JsMath.max(0, dog.cooldown - 1);
        if (dog.waiting)
        {
            var found = state.survivors.find((s) => Space.manhattan(s.pos, dog.pos) <= 2);
            if (!Js.T(found))
            {
                return;
            }
            dog.waiting = false;
            Events.logEvent(state, $"{Js.Str(found.name)} found {Js.Str(dog.name)}, waiting at the house");
        }
        // Under or over a parked car (AnimalCars.cs): that is all it does until it is down on the far side.
        if (AnimalCars.step(state, dog))
        {
            return;
        }
        // A free mover (Walkers.cs): if something moved it by tile, `at` follows.
        dog.move = null;
        Motion.followTile(dog);
        var zombie = Js.Get(state.zombies.filter((z) => Space.manhattan(z.pos, dog.pos) <= DOG_SENSE).sort((a, b) => Space.manhattan(a.pos, dog.pos) - Space.manhattan(b.pos, dog.pos)), 0);
        if (Js.T(zombie) && ((state.tick - dog.barkedTick) >= BARK_EVERY))
        {
            bark(state, dog, zombie);
        }
        if (Js.T(zombie) && Motion.inReach(zombie, dog, Motion.REACH, state.grid))
        {
            if (dog.cooldown == 0)
            {
                bite(state, dog, zombie);
            }
            return;
        }
        var speed = Walkers.speedOf(DOG_TICKS_PER_TILE);
        if (Js.T(zombie))
        {
            dog.goal = null;
            Walkers.walkTo(state, dog, zombie.at, speed, Pathfinding.SURVIVOR, budget, Motion.REACH - 16);
            return;
        }
        // Else after the nearest survivor once they're more than DOG_FOLLOW tiles off, until it's at their heel.
        var owner = Js.Get(state.survivors.slice().sort((a, b) => Space.manhattan(a.pos, dog.pos) - Space.manhattan(b.pos, dog.pos)), 0);
        if (!Js.T(owner))
        {
            dog.goal = null;
            return;
        }
        if (!Js.T(dog.goal) && (Space.manhattan(owner.pos, dog.pos) <= DOG_FOLLOW))
        {
            return;
        }
        dog.goal = Js.CopyAs<Point>(owner.at);
        var was = dog.at;
        if (Walkers.walkTo(state, dog, owner.at, speed, Pathfinding.SURVIVOR, budget, DOG_HEEL) || ((dog.at.x == was.x) && (dog.at.y == was.y) && (dog.path.length == 0)))
        {
            dog.goal = null;
        }
    }

    public static void bark(SimState state, Dog dog, Zombie z)
    {
        dog.barkedTick = state.tick;
        double woke = 0;
        foreach (var s in Js.Iter(state.survivors))
        {
            if ((!Js.T(s.sleep?.asleep) || !Medicine.isGuard(s)) || (Space.manhattan(s.pos, dog.pos) > ((double)(2) * (DOG_SENSE))))
            {
                continue;
            }
            Needs.wake(state, s, $"woken by {Js.Str(dog.name)}'s barking");
            woke += 1;
        }
        Events.logEvent(state, $"{Js.Str(dog.name)} is barking: something's coming near ({Js.Str(z.pos.x)}, {Js.Str(z.pos.y)}){Js.Str(Js.T(woke) ? Js.Str($"; {Js.Str(woke)} guard{Js.Str((woke == 1) ? Js.Str("") : Js.Str("s"))} up") : Js.Str(""))}");
    }

    public static void bite(SimState state, Dog dog, Zombie z)
    {
        dog.cooldown = DOG_BITE_EVERY;
        z.health = JsMath.max(0, z.health - DOG_BITE);
        if (z.health > 0)
        {
            return;
        }
        Zombies.removeZombie(state, z);
        Corpses.leaveCorpse(state, z.pos, null);
        Events.logEvent(state, $"{Js.Str(dog.name)} brought down a zombie");
    }

    /// <summary>A zombie's blow on a dog. Returns true if the dog died.</summary>
    public static bool hitDog(SimState state, Dog dog, double damage)
    {
        // Under a car (AnimalCars.cs): the blow lands on the car.
        if (AnimalCars.hidden(dog))
        {
            return false;
        }
        dog.health = JsMath.max(0, dog.health - damage);
        if (dog.health > 0)
        {
            return false;
        }
        state.dogs.splice(state.dogs.indexOf(dog), 1);
        Events.logEvent(state, $"{Js.Str(dog.name)} the dog was killed");
        return true;
    }
}
