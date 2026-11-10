// A car's body (2026-11-11, VehicleBody.cs): it is as long and wide as the car drawn (the sedan 4.2 x 1.7 m), not
// the 0.43 m circle it was. What that means at a wall, in a gap, against another car, in a corridor, when it starts
// out stuck, and for getting in and out.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class VehicleBodyTest
{
    const int SUB = Motion.SUB;

    /// <summary>A field 40 x 7 with `wall` put on each (x, y) listed; you at (2, 3) under control, a car facing east at (3, 3).</summary>
    static (SimState state, Survivor s, Vehicle car) field(params (int x, int y)[] walls)
    {
        var rows = new List<char[]>();
        for (int y = 0; y < 7; y++) rows.Add(new string('.', 40).ToCharArray());
        foreach (var (x, y) in walls) rows[y][x] = '#';
        var f = Ported.VehiclesTest.field(rows.Select((r) => new string(r)).ToList());
        return (f.state, f.s, f.car);
    }

    static void run(SimState state, int n)
    {
        for (int i = 0; i < n; i++) Tick.tick(state);
    }

    static void steer(SimState state, int dx, int dy) => Commands.applyCommand(state, new Command { type = "steer", dx = dx, dy = dy });

    static void getIn(SimState state)
    {
        expect(Commands.applyCommand(state, new Command { type = "use", x = 3, y = 3 }).ok).toBe(true);
        run(state, Vehicles.GET_IN_TICKS);
    }

    static (int x, int y)[] column(int x, params int[] gaps) =>
        Enumerable.Range(0, 7).Where((y) => !gaps.Contains(y)).Select((y) => (x, y)).ToArray();

    [Fact(DisplayName = "car body > a sedan is 4.2 m long and 1.7 m wide: a row of circles its own width, no further apart than a radius")]
    public void The_shape()
    {
        var shape = Vehicles.shapeOf("car");
        expect(Math.Round(2.0 * shape.halfLength / SUB, 1)).toBe(4.2);
        expect(Math.Round(2.0 * shape.halfWidth / SUB, 1)).toBe(1.7);
        var at = Motion.centre(new Point { x = 10, y = 3 });
        var east = Vehicles.circles(shape, at, 0);
        expect(east[0].x).toBe(at.x - (shape.halfLength - shape.halfWidth));   // the tail's circle reaches the back bumper
        expect(east[^1].x).toBe(at.x + (shape.halfLength - shape.halfWidth));  // and the nose's the front one
        for (int i = 1; i < east.Count; i++) expect(east[i].x - east[i - 1].x).toBeLessThanOrEqual(shape.halfWidth);
        foreach (var c in east) expect(c.y).toBe(at.y);
        // Facing south, the row runs down the map.
        var south = Vehicles.circles(shape, at, 8);
        expect(south[^1].y).toBe(at.y + (shape.halfLength - shape.halfWidth));
        expect(south[^1].x).toBe(at.x);
        // A pickup is longer and wider.
        expect(Vehicles.shapeOf("truck").halfLength).toBeGreaterThan(shape.halfLength);
        expect(Vehicles.shapeOf("truck").halfWidth).toBeGreaterThan(shape.halfWidth);
    }

    [Fact(DisplayName = "car body > its nose stops at a wall: the centre stays half a car's length short of it")]
    public void Nose_at_a_wall()
    {
        var (state, s, car) = field(column(20));
        getIn(state);
        steer(state, 0, -1);
        run(state, 80);
        int nose = car.at.x + Vehicles.shapeOf(car).halfLength;
        expect(nose).toBeLessThanOrEqual(20 * SUB);          // the front bumper is at the wall's face, not in it
        expect(nose).toBeGreaterThan(20 * SUB - 16);         // and right up to it
        expect(car.speed).toBe(0);
        expect(car.health).toBeLessThan(Vehicles.VEHICLE_HEALTH); // it hit it at speed
        expect(Vehicles.stuck(state, Vehicles.shapeOf(car), car.at, car.heading, car)).toBe(0);
    }

    [Fact(DisplayName = "car body > a gap one tile wide is too narrow for it; three tiles wide, it drives through")]
    public void Gaps()
    {
        var (narrow, _, car) = field(column(20, 3));
        getIn(narrow);
        steer(narrow, 0, -1);
        run(narrow, 100);
        expect(car.at.x + Vehicles.shapeOf(car).halfLength).toBeLessThanOrEqual(20 * SUB);

        var (wide, _, car2) = field(column(20, 2, 3, 4));
        getIn(wide);
        steer(wide, 0, -1);
        run(wide, 100);
        expect(car2.pos.x).toBeGreaterThan(24);
    }

    [Fact(DisplayName = "car body > another car is solid: it stops bumper to bumper, and the parked one doesn't move")]
    public void Another_car()
    {
        var (state, _, car) = field();
        var parked = Vehicles.addVehicle(state, 16, 3);
        var where = parked.at.x;
        getIn(state);
        steer(state, 0, -1);
        run(state, 80);
        var shape = Vehicles.shapeOf(car);
        expect(car.at.x + shape.halfLength).toBeLessThanOrEqual(parked.at.x - shape.halfLength);
        expect(car.at.x + shape.halfLength).toBeGreaterThan(parked.at.x - shape.halfLength - 16);
        expect(parked.at.x).toBe(where);
        expect(car.speed).toBe(0);
        // A car can't be put down where one stands, or where its body would reach into one.
        expect(Vehicles.parkable(state, new Point { x = 16, y = 3 })).toBe(false);
        expect(Vehicles.parkable(state, new Point { x = 19, y = 3 })).toBe(false);
        expect(Vehicles.parkable(state, new Point { x = 24, y = 3 })).toBe(true);
    }

    [Fact(DisplayName = "car body > in a street three tiles wide it never enters the walls, however you steer, and can't turn round")]
    public void Corridor()
    {
        var walls = Enumerable.Range(0, 40).SelectMany((x) => new[] { (x, 1), (x, 5) }).ToArray();
        var (state, _, car) = field(walls);
        getIn(state);
        var shape = Vehicles.shapeOf(car);
        for (int t = 0; t < 400; t++)
        {
            // Forward, weaving: hard left for a while, hard right for a while, and a spell in reverse.
            int dx = (t / 25) % 2 == 0 ? -1 : 1;
            int dy = (t / 90) % 3 == 2 ? 1 : -1;
            steer(state, dx, dy);
            Tick.tick(state);
            expect(Vehicles.stuck(state, shape, car.at, car.heading, car), $"tick {t}, at {car.at.x},{car.at.y} heading {car.heading}").toBe(0);
            // Across the street (headings 6 to 10 and 22 to 26) the body is longer than the street is wide.
            expect(car.heading is (>= 6 and <= 10) or (>= 22 and <= 26)).toBe(false);
        }
    }

    [Fact(DisplayName = "car body > it turns about its rear axle: the nose swings, the back wheels stay where they were")]
    public void Rear_axle()
    {
        var (state, _, car) = field();
        var shape = Vehicles.shapeOf(car);
        double axle = Math.Truncate(shape.halfLength * Vehicles.AXLE);
        Point rear() { var d = Vehicles.HEADING_DIRS[(int)car.heading]; return new Point { x = (int)(car.at.x - d[0] * axle / 256), y = (int)(car.at.y - d[1] * axle / 256) }; }
        car.at = Motion.centre(new Point { x = 20, y = 3 });
        var before = rear();
        expect(Vehicles.turn(state, car, 1)).toBe(true);
        expect(car.heading).toBe(1);
        expect(Math.Abs(rear().x - before.x)).toBeLessThanOrEqual(2);
        expect(Math.Abs(rear().y - before.y)).toBeLessThanOrEqual(2);
        expect(car.at.y).toBeGreaterThan(before.y); // the centre (and the nose) swung south, the way it turned
    }

    [Fact(DisplayName = "car body > a car left with its tail in a wall (an old save) can still drive out, and then can't go back in")]
    public void Stuck()
    {
        var (state, _, car) = field(column(1));
        var shape = Vehicles.shapeOf(car);
        expect(Vehicles.stuck(state, shape, car.at, car.heading, car)).toBeGreaterThan(0); // at (3, 3) its tail reaches x = 1.4, into the wall
        getIn(state);
        steer(state, 0, -1);
        run(state, 30);
        expect(Vehicles.stuck(state, shape, car.at, car.heading, car)).toBe(0);
        expect(car.pos.x).toBeGreaterThan(6);
        // Back towards the wall: it stops with its tail at the wall's face.
        steer(state, 0, 1);
        run(state, 120);
        expect(car.at.x - shape.halfLength).toBeGreaterThanOrEqual(2 * SUB);
        expect(Vehicles.stuck(state, shape, car.at, car.heading, car)).toBe(0);
    }

    [Fact(DisplayName = "car body > you get in from anywhere beside its body, and get out by the driver's door, clear of it")]
    public void In_and_out()
    {
        var (state, s, car) = field();
        // Standing off the back bumper, using the tile the boot is over.
        Vehicles.unpark(state, car); // moved by hand: its tiles go with it (VehicleParking.cs)
        car.at = Motion.centre(new Point { x = 8, y = 3 });
        car.pos = new Point { x = 8, y = 3 };
        Vehicles.park(state, car);
        s.pos = new Point { x = 5, y = 3 };
        s.at = Motion.centre(s.pos);
        expect(Vehicles.gap(car, s.at)).toBeGreaterThan(0);
        expect(Vehicles.vehicleNear(state, s.at)).toBe(car);
        expect(Commands.applyCommand(state, new Command { type = "use", x = 6, y = 3 }).ok).toBe(true);
        run(state, Vehicles.GET_IN_TICKS);
        expect(Vehicles.vehicleOf(state, s)).toBe(car);
        // Out again: beside the driver's seat, on the car's left (north of a car facing east), not inside the body.
        expect(Commands.applyCommand(state, new Command { type = "use", x = 8, y = 3 }).ok).toBe(true);
        run(state, Vehicles.GET_OUT_TICKS);
        expect(Vehicles.vehicleOf(state, s)).toBeUndefined();
        expect(Vehicles.gap(car, s.at)).toBeGreaterThanOrEqual(Motion.BODY_RADIUS);
        expect(s.pos.y).toBeLessThan(car.pos.y);
        expect(Math.Abs(s.pos.x - car.pos.x)).toBeLessThanOrEqual(1);
        // From three tiles off its side there is no car beside you.
        s.pos = new Point { x = 8, y = 0 };
        s.at = Motion.centre(s.pos);
        expect(Vehicles.vehicleNear(state, s.at)).toBeUndefined();
    }

    [Fact(DisplayName = "car body > it runs down the dead its body reaches, not only those at its centre")]
    public void Ramming()
    {
        var (state, s, car) = field();
        getIn(state);
        // One 0.7 m off the car's line (inside its half width, well outside the old 0.43 m circle), one 1.5 m off.
        var grazed = Zombies.addZombie(state, 16, 3, 200, "static");
        grazed.at = new Point { x = 16 * SUB + SUB / 2, y = 3 * SUB + SUB / 2 + 180 };
        var missed = Zombies.addZombie(state, 16, 5, 200, "static");
        missed.at = new Point { x = 16 * SUB + SUB / 2, y = 3 * SUB + SUB / 2 + 384 };
        // The car alone moves (Vehicles.drive, not whole ticks), so neither of the dead walks into its way.
        for (int t = 0; t < 80 && car.at.x < 19 * SUB; t++) Vehicles.drive(state, s, 0, -1);
        expect(car.at.x).toBeGreaterThanOrEqual(19 * SUB);
        expect(state.zombies.includes(grazed) && grazed.health == 200).toBe(false);
        expect(missed.health).toBe(200);
    }
}
