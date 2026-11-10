// Parked cars are in the way (2026-11-11, VehicleParking.cs): the tiles under a car nobody is driving can't be
// walked on, so people's paths go round it and the dead stop at it. Driving it off frees them; the marks aren't
// saved but come back the same when a state is read.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class VehicleParkingTest
{
    const int SUB = Motion.SUB;

    /// <summary>A field 40 x 7; you under control (moved off the car's tiles as it is put down), a car facing east at (3, 3).</summary>
    static (SimState state, Survivor s, Vehicle car) field()
    {
        var f = Ported.VehiclesTest.field();
        return (f.state, f.s, f.car);
    }

    static void run(SimState state, int n)
    {
        for (int i = 0; i < n; i++) Tick.tick(state);
    }

    static string marks(SimState state)
    {
        var g = state.grid;
        var rows = new List<string>();
        for (int y = 0; y < g.height; y++)
        {
            var row = new char[g.width];
            for (int x = 0; x < g.width; x++) row[x] = Grids.isParked(g, x, y) ? 'c' : '.';
            rows.Add(new string(row));
        }
        return string.Join("\n", rows);
    }

    [Fact(DisplayName = "parked cars > the five tiles under a parked sedan can't be walked on by anyone on foot, and a path goes round it")]
    public void Blocks()
    {
        var (state, s, car) = field();
        var g = state.grid;
        for (int x = 0; x < 8; x++)
        {
            bool under = x >= 1 && x <= 5;
            expect(Grids.isParked(g, x, 3), $"tile {x},3").toBe(under);
            foreach (var mover in new[] { Pathfinding.SURVIVOR, Pathfinding.FREE, Pathfinding.ZOMBIE, Pathfinding.BREACHER, Pathfinding.RAIDER, Pathfinding.CRAWLER, Pathfinding.ANIMAL })
                expect(Pathfinding.isPassable(g, x, 3, mover), $"{mover} at {x},3").toBe(!under);
            // The rows beside it stay open (it only overhangs them), and another car's tile test doesn't change.
            expect(Pathfinding.isPassable(g, x, 2)).toBe(true);
            expect(Pathfinding.isPassable(g, x, 4)).toBe(true);
            expect(Pathfinding.isPassable(g, x, 3, Pathfinding.VEHICLE)).toBe(true);
        }
        // You were standing where it was put down: moved off it, onto a tile you can walk from.
        expect(Grids.isParked(g, s.pos.x, s.pos.y)).toBe(false);
        expect(Motion.fits(g, s.at, Pathfinding.FREE)).toBe(true);
        // Straight across is five tiles; round the car's end is more, and no step is on it.
        var path = Pathfinding.findPath(g, new Point { x = 3, y = 1 }, new Point { x = 3, y = 5 });
        expect(path != null).toBe(true);
        expect(path.Count).toBeGreaterThan(4);
        foreach (var p in path) expect(Grids.isParked(g, p.x, p.y), $"step {p.x},{p.y}").toBe(false);
        expect(Pathfinding.canReach(g, state.walkabilityVersion, new Point { x = 3, y = 1 }, [new Point { x = 3, y = 3 }])).toBe(false);
    }

    [Fact(DisplayName = "parked cars > getting in frees its tiles, getting out blocks them where it stands now, and you can walk away")]
    public void In_and_out()
    {
        var (state, s, car) = field();
        var g = state.grid;
        int v0 = state.walkabilityVersion;
        expect(Commands.applyCommand(state, new Command { type = "use", x = 3, y = 3 }).ok).toBe(true);
        run(state, Vehicles.GET_IN_TICKS);
        expect(Vehicles.vehicleOf(state, s)).toBe(car);
        expect(marks(state).Contains('c')).toBe(false);
        expect(state.walkabilityVersion).toBeGreaterThan(v0); // paths made round it are stale
        int v1 = state.walkabilityVersion;
        // Drive a way, stop, get out.
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = -1 });
        run(state, 20);
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 0 });
        for (int t = 0; t < 80 && car.speed != 0; t++) Tick.tick(state); // it rolls to a stop
        expect(car.speed).toBe(0);
        expect(marks(state).Contains('c')).toBe(false); // a car with you in it is in nobody's way
        expect(Commands.applyCommand(state, new Command { type = "use", x = car.pos.x, y = car.pos.y }).ok).toBe(true);
        run(state, Vehicles.GET_OUT_TICKS);
        expect(Vehicles.vehicleOf(state, s)).toBeUndefined();
        expect(state.walkabilityVersion).toBeGreaterThan(v1);
        foreach (var i in Vehicles.tilesUnder(g, car)) expect(Grids.isParked(g, i % g.width, i / g.width)).toBe(true);
        expect(Grids.isParked(g, 3, 3)).toBe(false); // where it was
        // You're beside it, on open ground, and can walk off.
        expect(Vehicles.overlapsParked(g, s.at)).toBe(false);
        var from = s.at;
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = -1 });
        run(state, 5);
        expect(s.at.y).toBeLessThan(from.y);
    }

    [Fact(DisplayName = "parked cars > one of the dead coming for you goes round a car in its way, never through it")]
    public void The_dead_go_round()
    {
        var (state, s, car) = field();
        var g = state.grid;
        // You north of the car's middle, it south of it: the straight line between is through the car.
        s.pos = new Point { x = 3, y = 1 };
        s.at = Motion.centre(s.pos);
        var z = Zombies.addZombie(state, 3, 5, 200, "static");
        z.targetId = s.id;
        bool reached = false;
        for (int t = 0; t < 300 && !reached; t++)
        {
            Tick.tick(state);
            if (!state.zombies.includes(z)) break;
            expect(Vehicles.overlapsParked(g, z.at), $"tick {t}: the dead at {z.at.x},{z.at.y}").toBe(false);
            reached = Motion.inReach(s, z, Motion.REACH, g);
        }
        expect(reached).toBe(true);
    }

    [Fact(DisplayName = "parked cars > on foot you stop at the car's body, not a foot inside its flank, and can walk along it")]
    public void You_stop_at_the_body()
    {
        var (state, s, car) = field();
        var shape = Vehicles.shapeOf(car);
        // From two tiles north of its middle, straight at it.
        s.pos = new Point { x = 3, y = 1 };
        s.at = Motion.centre(s.pos);
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 1 });
        run(state, 40);
        expect(Vehicles.gap(car, s.at)).toBeGreaterThanOrEqual(Motion.BODY_RADIUS - 2);  // your body is against its side
        expect(Vehicles.gap(car, s.at)).toBeLessThan(Motion.BODY_RADIUS + 24);           // and right up to it
        expect(s.at.y).toBeLessThan(car.at.y - shape.halfWidth);
        // Along its side to past its nose, then round the front of it.
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 1 });
        run(state, 80);
        expect(s.at.x).toBeGreaterThan(car.at.x + shape.halfLength);
        expect(s.at.y).toBeGreaterThan(car.at.y);
    }

    [Fact(DisplayName = "parked cars > whoever is where a car comes to rest is moved clear of it")]
    public void Evicts()
    {
        var (state, s, car) = field();
        var g = state.grid;
        // One of the dead standing right where a second car is put down, and a colonist's tile under it.
        var z = Zombies.addZombie(state, 20, 3, 200, "static");
        z.at = new Point { x = 20 * SUB + 200, y = 3 * SUB + 30 };
        var other = Commands.spawnSurvivor(state, 21, 3);
        expect(other.ok).toBe(true);
        var colonist = state.survivors[^1];
        var second = Vehicles.addVehicle(state, 20, 3);
        expect(Grids.isParked(g, 20, 3)).toBe(true);
        expect(Vehicles.overlapsParked(g, z.at)).toBe(false);
        expect(Grids.isParked(g, z.pos.x, z.pos.y)).toBe(false);
        expect(Grids.isParked(g, colonist.pos.x, colonist.pos.y)).toBe(false);
    }

    [Fact(DisplayName = "parked cars > a tile two parked cars are over stays blocked when one of them is driven off")]
    public void Shared_tiles()
    {
        var (state, s, car) = field();
        var g = state.grid;
        // A second car across the first one's nose (an old save's cars can overlap): they share tiles.
        var second = Vehicles.addVehicle(state, 5, 3, 8);
        var shared = Vehicles.tilesUnder(g, car).Intersect(Vehicles.tilesUnder(g, second)).ToList();
        expect(shared.Count).toBeGreaterThan(0);
        Vehicles.unpark(state, second);
        foreach (var i in Vehicles.tilesUnder(g, car)) expect(Grids.isParked(g, i % g.width, i / g.width)).toBe(true);
        expect(Grids.isParked(g, 5, 5)).toBe(false); // the second one's own tile, freed
        Vehicles.park(state, second);
        Vehicles.remove(state, second);
        expect(marks(state).Count((c) => c == 'c')).toBe(5);
        Vehicles.remove(state, car);
        expect(marks(state).Contains('c')).toBe(false);
    }

    [Fact(DisplayName = "parked cars > the marks aren't saved: a state read back has the same ones, and plays on the same")]
    public void Read_back()
    {
        var (state, s, car) = field();
        Vehicles.addVehicle(state, 20, 5, 4);
        var z = Zombies.addZombie(state, 30, 3, 200, "static");
        z.targetId = s.id;
        var copy = Js.DeepClone(state);
        expect(marks(copy)).toBe(marks(state));
        expect(marks(state).Count((c) => c == 'c')).toBeGreaterThan(8);
        expect(copy.walkabilityVersion).toBe(state.walkabilityVersion);
        // The same play on both: in, off, out, and the dead after you all the while.
        foreach (var world in new[] { state, copy })
        {
            Commands.applyCommand(world, new Command { type = "use", x = 3, y = 3 });
            run(world, Vehicles.GET_IN_TICKS);
            Commands.applyCommand(world, new Command { type = "steer", dx = 1, dy = -1 });
            run(world, 25);
            Commands.applyCommand(world, new Command { type = "steer", dx = 0, dy = 0 });
            run(world, 40);
            var c = world.vehicles[0];
            Commands.applyCommand(world, new Command { type = "use", x = c.pos.x, y = c.pos.y });
            run(world, 60);
        }
        expect(Js.Json_stringify(copy)).toBe(Js.Json_stringify(state));
        expect(marks(copy)).toBe(marks(state));
        // And what the marks are is what the cars say, however they got there.
        string kept = marks(state);
        Vehicles.remask(state);
        expect(marks(state)).toBe(kept);
    }

    [Fact(DisplayName = "parked cars > every car a town is made with is in the way from the start")]
    public void Generated()
    {
        var state = Worldgen.createWorld(5, 256);
        for (var y = 0; y < 256; y += Worldgen.CHUNK)
            for (var x = 0; x < 256; x += Worldgen.CHUNK)
                Worldgen.ensureGeneratedAround(state, new Point { x = x, y = y });
        var g = state.grid;
        var cars = state.vehicles ?? [];
        expect(cars.Count).toBeGreaterThan(4);
        var under = new HashSet<int>();
        foreach (var v in cars)
        {
            var tiles = Vehicles.tilesUnder(g, v);
            expect(tiles.Count).toBeGreaterThanOrEqual(4);
            foreach (var i in tiles)
            {
                under.Add(i);
                expect(Grids.isParked(g, i % g.width, i / g.width), $"{v.id} at {v.pos.x},{v.pos.y}").toBe(true);
            }
        }
        expect(marks(state).Count((c) => c == 'c')).toBe(under.Count);
    }
}
