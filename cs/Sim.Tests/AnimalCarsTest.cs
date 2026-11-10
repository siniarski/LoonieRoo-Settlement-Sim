// Animals and parked cars (2026-11-11, AnimalCars.cs): the small go under, the rest go over, round when round is
// short; a crossing lands only where the body fits, replays from a save, and nobody presses against a car for ever.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class AnimalCarsTest
{
    const int SUB = Motion.SUB;
    static readonly string[] CARS = ["car", "truck"];

    /// <summary>A map w x h from a function of the tile ('.' floor, '#' wall); you at (1, 1); no zombies, 08:00.</summary>
    static SimState map(int w, int h, Func<int, int, char> at)
    {
        var rows = new List<string>();
        for (int y = 0; y < h; y++) rows.Add(new string(Enumerable.Range(0, w).Select((x) => x == 1 && y == 1 ? 's' : at(x, y)).ToArray()));
        var state = ScenarioMod.loadScenario(new Scenario { seed = 5, map = rows });
        state.settings = Js.With(Js.CopyAs<Settings>(state.settings), __s0 => { __s0.zombies = 0; });
        state.tick = Js.I(8 * Time.TICKS_PER_HOUR + 1);
        return state;
    }

    /// <summary>A parked car centred at (cx, cy) in tiles, halves allowed; heading 0 is along x, 8 along y.</summary>
    static Vehicle park(SimState state, string kind, double cx, double cy, int heading = 0)
    {
        var v = Vehicles.addVehicle(state, (int)cx, (int)cy, heading, Vehicles.FUEL_MAX, Vehicles.VEHICLE_HEALTH, kind);
        v.at = new Point(Js.I(cx * SUB + SUB / 2), Js.I(cy * SUB + SUB / 2));
        v.pos = Motion.tileOf(v.at);
        Vehicles.remask(state, true);
        return v;
    }

    /// <summary>An alley running north and south, `w` tiles wide from x = 8, walled from row 0 to the bottom (a dead
    /// end there), with a car of this kind parked across it at row 5: as wide as the car, so the car is the only way
    /// out. The sedan's alley is 5 wide, the pickup's 6.</summary>
    static (SimState state, Vehicle car, int mid) alley(string kind, int h = 16)
    {
        int w = kind == "truck" ? 6 : 5;
        var state = map(24, h, (x, y) => (x == 7 || x == 8 + w || y == h - 1) && x >= 7 && x <= 8 + w ? '#' : '.');
        var car = park(state, kind, 8 + (w - 1) / 2.0, 5);
        for (int x = 8; x < 8 + w; x++) expect(Grids.isParked(state.grid, x, 5), $"{kind}: tile {x},5 under it").toBe(true);
        return (state, car, 8 + w / 2);
    }

    /// <summary>A walker's body where it can be: clear of walls and cars, or crossing a car.</summary>
    static void sound(SimState state, IWalker w, string when)
    {
        if (w.cross != null)
        {
            expect(Motion.fits(state.grid, w.cross.to, AnimalCars.moverOf(w)), $"{when}: lands clear\n{DebugText.dumpText(state)}").toBe(true);
            return;
        }
        expect(Motion.fits(state.grid, w.at, AnimalCars.moverOf(w)), $"{when}: {w.id} at {w.at.x},{w.at.y}\n{DebugText.dumpText(state)}").toBe(true);
        expect(Space.samePoint(w.pos, Motion.tileOf(w.at))).toBe(true);
    }

    static void tickAnimals(SimState state)
    {
        state.tick = Js.I(state.tick + 1);
        Animals.updateAnimals(state);
        Dogs.updateDogs(state);
    }

    [Fact(DisplayName = "animals and cars > the table: rabbits under both, a dog under the truck and over the sedan, deer leap, wolves and bears climb")]
    public void Table()
    {
        expect(AnimalCars.wayOf("rabbit", "car").how).toBe(AnimalCars.UNDER);
        expect(AnimalCars.wayOf("rabbit", "truck").how).toBe(AnimalCars.UNDER);
        expect(AnimalCars.wayOf("dog", "car").how).toBe(AnimalCars.OVER);
        expect(AnimalCars.wayOf("dog", "truck").how).toBe(AnimalCars.UNDER);
        expect(AnimalCars.wayOf("deer", "car").how).toBe(AnimalCars.LEAP);
        expect(AnimalCars.wayOf("deer", "truck").how).toBe(AnimalCars.LEAP);
        expect(AnimalCars.wayOf("wolf", "truck").how).toBe(AnimalCars.OVER);
        expect(AnimalCars.wayOf("bear", "car").how).toBe(AnimalCars.OVER);
        // A deer's bound is quick, a bear's climb slow, the pickup slower than the sedan to climb.
        expect(AnimalCars.wayOf("deer", "car").ticks).toBeLessThan(AnimalCars.wayOf("wolf", "car").ticks);
        expect(AnimalCars.wayOf("wolf", "car").ticks).toBeLessThan(AnimalCars.wayOf("bear", "car").ticks);
        expect(AnimalCars.wayOf("bear", "car").ticks).toBeLessThan(AnimalCars.wayOf("bear", "truck").ticks);
    }

    [Fact(DisplayName = "animals and cars > each kind gets across a sedan and a pickup across its only way, as the table says, landing clear")]
    public void Each_kind_each_car()
    {
        foreach (var carKind in CARS)
        {
            foreach (var kind in new[] { "rabbit", "deer", "wolf", "bear", "dog" })
            {
                var (state, car, mid) = alley(carKind);
                IWalker w = kind == "dog" ? Dogs.addDog(state, mid, 9) : Animals.addAnimal(state, kind, mid, 9);
                var goal = Motion.centre(new Point(mid, 1));
                var budget = new WalkBudget();
                double speed = w is Animal a ? Animals.speedOf(a) : Walkers.speedOf(Dogs.DOG_TICKS_PER_TILE);
                string how = null;
                double ticks = 0;
                bool there = false;
                for (int t = 0; t < 400 && !there; t++)
                {
                    state.tick = Js.I(state.tick + 1);
                    budget.plans = 1;
                    if (!AnimalCars.step(state, w)) there = Walkers.walkTo(state, w, goal, speed, AnimalCars.moverOf(w), budget);
                    if (w.cross != null) { how ??= w.cross.how; ticks = w.cross.ticks; }
                    sound(state, w, $"{kind} at the {carKind}, tick {t}");
                }
                expect(there, $"{kind} over the {carKind}: at {w.at.x},{w.at.y}").toBe(true);
                expect(how, $"{kind} at the {carKind}").toBe(AnimalCars.wayOf(kind, carKind).how);
                // About the table's ticks (the crossing here is about a car's width).
                expect(ticks, $"{kind} at the {carKind}").toBeGreaterThanOrEqual(AnimalCars.wayOf(kind, carKind).ticks - 2);
                expect(ticks, $"{kind} at the {carKind}").toBeLessThanOrEqual(AnimalCars.wayOf(kind, carKind).ticks * 2);
            }
        }
    }

    [Fact(DisplayName = "animals and cars > a rabbit fleeing goes under a car, hides there while you are near, and comes out the other side")]
    public void Rabbit_under()
    {
        foreach (var carKind in CARS)
        {
            var state = map(30, 16, (x, y) => '.');
            var s = state.survivors[0];
            Staging.place(s, 10, 10);
            park(state, carKind, 10, 6);
            var r = Animals.addAnimal(state, "rabbit", 10, 8);
            bool hid = false;
            for (int t = 0; t < 400 && !(hid && r.pos.y < 6 && r.cross == null); t++)
            {
                tickAnimals(state);
                sound(state, r, $"{carKind} tick {t}");
                if (AnimalCars.hidden(r))
                {
                    hid = true;
                    // Out of reach: no blow, no shot finds it.
                    expect(AnimalCars.inTheOpen(state.animals).includes(r)).toBe(false);
                    expect(Motion.nearestInArc(AnimalCars.inTheOpen(state.animals), s.at, new Point(0, -1), 6 * SUB)).toBeNull();
                }
            }
            expect(hid, carKind).toBe(true);
            expect(r.pos.y, $"{carKind}: the rabbit came out north of the car").toBeLessThan(6);
        }
    }

    [Fact(DisplayName = "animals and cars > your swing misses a rabbit under a car, and the same swing hits it in the open")]
    public void Swing_under()
    {
        var state = map(16, 8, (x, y) => '.');
        var s = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        park(state, "car", 6, 3);
        Staging.place(s, 5, 4);
        var r = Animals.addAnimal(state, "rabbit", 5, 3);
        r.cross = new Crossing { how = AnimalCars.UNDER, from = r.at.Copy(), to = Motion.centre(new Point(5, 1)), ticks = 8, done = 4 };
        double hp = Js.Q(r.health, 0);
        Commands.applyCommand(state, new Command { type = "swing", x = 5, y = 3 });
        expect(state.animals.includes(r), "under the car: missed").toBe(true);
        expect(r.health).toBe(hp);
        // Out from under it, where it stood: the same swing finds it.
        r.cross = null;
        state.control.nextSwing = 0;
        Commands.applyCommand(state, new Command { type = "swing", x = 5, y = 3 });
        expect(!state.animals.includes(r) || Js.Q(r.health, 0) < hp, "in the open: hit").toBe(true);
    }

    [Fact(DisplayName = "animals and cars > a wolf after a rabbit loses it when it goes under a car")]
    public void Chase_broken()
    {
        var state = map(30, 16, (x, y) => '.');
        Staging.place(state.survivors[0], 1, 1);
        park(state, "car", 12, 6);
        var r = Animals.addAnimal(state, "rabbit", 12, 6);
        r.cross = new Crossing { how = AnimalCars.UNDER, from = r.at.Copy(), to = Motion.centre(new Point(12, 4)), ticks = 8, done = 4 };
        var wolf = Animals.addAnimal(state, "wolf", 12, 10);
        wolf.hunger = Animals.WOLF_HUNGRY;
        expect(Animals.chooseTarget(state, wolf, null)).toBeNull();
        r.cross = null;
        r.at = Motion.centre(new Point(12, 4));
        r.pos = new Point(12, 4);
        expect(Animals.chooseTarget(state, wolf, null)).toBe(r);
    }

    [Fact(DisplayName = "animals and cars > a deer fleeing with a car across its only way out bounds over it")]
    public void Deer_cornered()
    {
        foreach (var carKind in CARS)
        {
            var (state, car, mid) = alley(carKind);
            var s = state.survivors[0];
            var d = Animals.addDeer(state, mid, 8);
            bool leapt = false;
            for (int t = 0; t < 600 && d.pos.y > 4; t++)
            {
                // You come on behind it, two tiles back.
                if (t % 10 == 0 && d.cross == null) Staging.place(s, mid, Math.Min(14, d.pos.y + 2));
                tickAnimals(state);
                sound(state, d, $"{carKind} tick {t}");
                leapt |= d.cross?.how == AnimalCars.LEAP;
            }
            expect(leapt, carKind).toBe(true);
            expect(d.pos.y, $"{carKind}: the deer is out past the car").toBeLessThan(5);
        }
    }

    /// <summary>A wolf out for you: from (wx, 10) to you at (sx, 2), a car across between at row 6 (tiles 8-12).</summary>
    static (SimState state, Animal wolf, Survivor s) hunted(string carKind, int wx, int sx)
    {
        var state = map(30, 14, (x, y) => '.');
        var s = state.survivors[0];
        Staging.place(s, sx, 2);
        park(state, carKind, 10, 6);
        var wolf = Animals.addAnimal(state, "wolf", wx, 10);
        wolf.angryAt = s.id;
        return (state, wolf, s);
    }

    [Fact(DisplayName = "animals and cars > a wolf hunting you gets over a car between you, and can't bite on top of it")]
    public void Wolf_over()
    {
        foreach (var carKind in CARS)
        {
            var (state, wolf, s) = hunted(carKind, 10, 10);
            bool over = false, bit = false;
            for (int t = 0; t < 200 && !bit; t++)
            {
                double hp = s.health;
                bool crossing = wolf.cross != null;
                tickAnimals(state);
                sound(state, wolf, $"{carKind} tick {t}");
                over |= wolf.cross?.how == AnimalCars.OVER;
                if (crossing && wolf.cross != null) expect(s.health, "no bite from on top of the car").toBe(hp);
                bit = s.health < hp;
            }
            expect(over, carKind).toBe(true);
            expect(bit, $"{carKind}: it got to you").toBe(true);
        }
    }

    [Fact(DisplayName = "animals and cars > a wolf goes round a car's end when round is short")]
    public void Wolf_round()
    {
        foreach (var carKind in CARS)
        {
            var (state, wolf, s) = hunted(carKind, 12, 12);
            bool bit = false;
            for (int t = 0; t < 200 && !bit; t++)
            {
                double hp = s.health;
                tickAnimals(state);
                expect(wolf.cross, $"{carKind} tick {t}: round, not over").toBeNull();
                sound(state, wolf, $"{carKind} tick {t}");
                bit = s.health < hp;
            }
            expect(bit, carKind).toBe(true);
        }
    }

    [Fact(DisplayName = "animals and cars > a dog following you is not left behind by a car across the way: over the sedan, under the pickup")]
    public void Dog_follows()
    {
        foreach (var carKind in CARS)
        {
            var (state, car, mid) = alley(carKind);
            var s = state.survivors[0];
            Staging.place(s, mid, 1);
            var dog = Dogs.addDog(state, mid, 9);
            string how = null;
            for (int t = 0; t < 300; t++)
            {
                tickAnimals(state);
                sound(state, dog, $"{carKind} tick {t}");
                how ??= dog.cross?.how;
            }
            expect(how, carKind).toBe(carKind == "truck" ? AnimalCars.UNDER : AnimalCars.OVER);
            expect(Space.manhattan(dog.pos, s.pos), $"{carKind}: the dog caught up").toBeLessThanOrEqual(Dogs.DOG_FOLLOW + 1);
        }
    }

    [Fact(DisplayName = "animals and cars > a crossing whose far side is walled off as it goes stops short, on ground it can stand on")]
    public void Far_side_taken()
    {
        var (state, car, mid) = alley("car");
        var bear = Animals.addAnimal(state, "bear", mid, 9);
        var goal = Motion.centre(new Point(mid, 1));
        var budget = new WalkBudget();
        bool stopped = false;
        for (int t = 0; t < 200 && !stopped; t++)
        {
            state.tick = Js.I(state.tick + 1);
            budget.plans = 1;
            if (!AnimalCars.step(state, bear)) Walkers.walkTo(state, bear, goal, Animals.speedOf(bear), Pathfinding.ANIMAL, budget);
            if (bear.cross != null && bear.cross.done == 5)
            {
                var land = Motion.tileOf(bear.cross.to);
                Grids.setTile(state.grid, land.x, land.y, Grids.WALL);
                state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
                tickAnimals(state);
                expect(bear.cross).toBeNull();
                stopped = true;
            }
            sound(state, bear, $"tick {t}");
        }
        expect(stopped).toBe(true);
    }

    [Fact(DisplayName = "animals and cars > nothing crosses where it couldn't come down: a wall right behind the car turns it back")]
    public void Far_side_checked()
    {
        // The alley's car with a wall along its north side: no room beyond it.
        var (state, car, mid) = alley("car");
        for (int x = 8; x <= 12; x++) Grids.setTile(state.grid, x, 4, Grids.WALL);
        var wolf = Animals.addAnimal(state, "wolf", mid, 9);
        var goal = Motion.centre(new Point(mid, 1));
        var budget = new WalkBudget();
        for (int t = 0; t < 120; t++)
        {
            state.tick = Js.I(state.tick + 1);
            budget.plans = 1;
            if (!AnimalCars.step(state, wolf)) Walkers.walkTo(state, wolf, goal, Animals.speedOf(wolf), Pathfinding.ANIMAL, budget);
            expect(wolf.cross, $"tick {t}").toBeNull();
            sound(state, wolf, $"tick {t}");
        }
    }

    [Fact(DisplayName = "animals and cars > a state saved in the middle of a crossing reads back and plays on the same")]
    public void Saved_mid_crossing()
    {
        var (state, wolf, s) = hunted("truck", 10, 10);
        for (int t = 0; t < 200 && !(wolf.cross != null && wolf.cross.done >= 3); t++) tickAnimals(state);
        expect(wolf.cross != null && wolf.cross.done >= 3).toBe(true);
        var copy = Js.DeepClone(state);
        var theirs = copy.animals.find((a) => a.id == wolf.id);
        expect(theirs.cross).toBeDefined();
        expect(theirs.cross.done).toBe(wolf.cross.done);
        expect(Js.Json_stringify(copy)).toBe(Js.Json_stringify(state));
        for (int t = 0; t < 60; t++)
        {
            tickAnimals(state);
            tickAnimals(copy);
        }
        expect(Js.Json_stringify(copy)).toBe(Js.Json_stringify(state));
    }

    [Fact(DisplayName = "animals and cars > among cars and walls for a long while, no animal ends in a wall and none stands still against a car")]
    public void Long_run()
    {
        // A yard split by a wall whose one gap a car stands in, a few more cars about, and you walking round: the
        // wolf after you and the dog after you must get over the car in the gap.
        var state = map(40, 30, (x, y) => x == 0 || y == 0 || x == 39 || y == 29 || (x == 20 && (y < 8 || y > 12)) ? '#' : '.');
        park(state, "car", 20, 10, 8);
        park(state, "truck", 10, 6);
        park(state, "car", 30, 20);
        park(state, "truck", 8, 22);
        var s = state.survivors[0];
        var all = new List<IWalker>();
        foreach (var (kind, x, y) in new[] { ("rabbit", 5, 5), ("rabbit", 25, 22), ("rabbit", 12, 8), ("deer", 15, 15), ("wolf", 30, 5), ("bear", 33, 25), ("rabbit", 9, 20) })
            all.Add(kind == "deer" ? Animals.addDeer(state, x, y) : Animals.addAnimal(state, kind, x, y));
        all.Add(Dogs.addDog(state, 18, 18));
        var still = new Dictionary<string, int>();
        int crossings = 0;
        for (int t = 0; t < 3000; t++)
        {
            // You wander the yard, a step every few ticks (a hash, not the RNG).
            if (t % 40 == 0)
            {
                int nx = 2 + (int)(Hash.hashRoll(t, 11) * 36), ny = 2 + (int)(Hash.hashRoll(t, 12) * 26);
                if (Pathfinding.isPassable(state.grid, nx, ny, Pathfinding.FREE)) Staging.place(s, nx, ny);
            }
            var before = all.ToDictionary((w) => w.id, (w) => w.at.Copy());
            foreach (var a in state.animals)
            {
                if (a.kind is "wolf" or "bear") a.leavesTick = state.tick + 100;
                if (a.kind == "wolf") a.angryAt = s.id;
            }
            tickAnimals(state);
            foreach (var w in all)
            {
                if (w is Animal an && !state.animals.includes(an)) continue;
                sound(state, w, $"tick {t}");
                if (w.cross != null && w.cross.done == 1) crossings++;
                // Wanting to go somewhere, against a car, and not moving.
                var b = before[w.id];
                bool wants = w is Animal a ? Js.T(a.goal) || (a.targetId == s.id && Motion.distance2(a.at, s.at) > 4.0 * Motion.REACH * Motion.REACH) : Js.T(((Dog)w).goal);
                bool against = w.cross == null && Vehicles.overlapsParked(state.grid, w.at, Motion.BODY_RADIUS + 24);
                still[w.id] = wants && against && b.x == w.at.x && b.y == w.at.y ? still.GetValueOrDefault(w.id) + 1 : 0;
                expect(still[w.id], $"tick {t}: {w.id} pressed against a car\n{DebugText.dumpText(state)}").toBeLessThan(80);
            }
        }
        expect(crossings).toBeGreaterThan(10);
    }
}
