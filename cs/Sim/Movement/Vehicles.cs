// Ported from src/sim/vehicles.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Vehicles: cars you get into and drive.
//
//   where       generated towns leave cars parked in the street in front of some houses and
//               shops (lots.ts parkCar), with a little fuel and some wear, by hash.
//   getting in  use a car next to you (control.ts use): with a fuel can in your bag and room in
//               the tank, you fill it up (FUEL_PER_CAN); otherwise you get in. Use again to get
//               out, beside it. Letting go of control, or dying, puts you out too.
//   driving     W accelerates (ACCEL a tick, up to MAX_SPEED), S brakes then reverses (down to
//               -REVERSE_MAX), and nothing held lets it roll to a stop (FRICTION). A and D turn
//               one of HEADINGS steps every TURN_EVERY ticks, only while it's moving (backwards,
//               the other way), about the rear axle. Its body is as long and wide as the car
//               drawn (VehicleBody.cs: a row of circles), and slides on walls like any mover
//               ('vehicle': floor only, no doors, no windows, no barricades). Other cars are solid.
//   fuel        a point a tick moving, a point every IDLE_BURN ticks idling; empty, it won't go.
//   noise       the engine (noise 'engine') every ENGINE_NOISE_EVERY ticks while you're in it.
//   crashes     hitting a wall faster than CRASH_SPEED stops you dead and dents it
//               ((speed - CRASH_SPEED) * CRASH_DAMAGE); at 0 health it's wrecked: it won't go.
//   off-road    on grass, dirt, fields and forest floor it won't go past OFFROAD_MAX. Trees and
//               furniture block it (all but traps and pits): it hits them as it would a wall. A tree's
//               trunk (Trunks.cs) is solid to the car's body: clipped, the car scrapes past along
//               one axis; head on, it loses its speed and crashes like a wall.
//   the boot    a car carries BOOT_MAX (a truck TRUCK_BOOT_MAX) of things: items with `inside`
//               set to its id, like a container (inventory.ts stash, take, takeAll).
//   the truck   a generated world leaves one out past the camp; expeditions take it
//               (outposts.ts). It drives like a car, slower, with a big boot.
//   the dead    run one down at RAM_SPEED or more and it takes RAM_DAMAGE per unit of speed and
//               is knocked aside; the car slows by a quarter and takes RAM_WEAR. The dead can't
//               bite you through it: they batter the car instead (BATTER_DAMAGE a bite).
// Positions are fixed point (motion.ts SUB), headings index HEADING_DIRS (integers, |v| = 256).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Vehicles
{

    public const int HEADINGS = 32;

    /// <summary>cos and sin of each heading, times 256, rounded: integers, the same everywhere.</summary>
    public static readonly List<List<double>> HEADING_DIRS = [[256, 0], [251, 50], [237, 98], [213, 142], [181, 181], [142, 213], [98, 237], [50, 251], [0, 256], [-50, 251], [-98, 237], [-142, 213], [-181, 181], [-213, 142], [-237, 98], [-251, 50], [-256, 0], [-251, -50], [-237, -98], [-213, -142], [-181, -181], [-142, -213], [-98, -237], [-50, -251], [0, -256], [50, -251], [98, -237], [142, -213], [181, -181], [213, -142], [237, -98], [251, -50]];

    public const int MAX_SPEED = 160;

    public const int REVERSE_MAX = 48;

    public const int ACCEL = 8;

    public const int BRAKE = 16;

    public const int FRICTION = 3;

    public const int TURN_EVERY = 2;

    /// <summary>Slower than this, it doesn't turn.</summary>
    public const int TURN_MIN_SPEED = 8;

    public const int FUEL_MAX = 3000;

    public const int FUEL_PER_CAN = 1500;

    public const int IDLE_BURN = 4;

    public const int VEHICLE_HEALTH = 1000;

    public const int CRASH_SPEED = 40;

    public const int CRASH_DAMAGE = 2;

    public const int RAM_SPEED = 24;

    public const int RAM_DAMAGE = 3;

    public const int RAM_WEAR = 10;

    public const int BATTER_DAMAGE = 20;

    public const int ENGINE_NOISE_EVERY = 20;

    /// <summary>You get into a car whose body is this close to you (SUB: a little over a metre).</summary>
    public const int ENTER_REACH = 290;

    /// <summary>Top speed off the road (grass, dirt, fields, forest).</summary>
    public const int OFFROAD_MAX = 80;

    /// <summary>The truck's top speed.</summary>
    public const int TRUCK_MAX = 120;

    /// <summary>What a boot holds (tenths of a kilo, as inventory.ts weighs things).</summary>
    public const int BOOT_MAX = 400;

    public const int TRUCK_BOOT_MAX = 1200;

    /// <summary>Ground a car goes at full speed on.</summary>
    public static readonly List<string> PAVED = ["road", "bridge", "paving", "concrete", "tiles", "boards"];

    /// <summary>The cars on the map (older saves have none).</summary>
    public static List<Vehicle> vehiclesOf(SimState state)
    {
        return state.vehicles ?? [];
    }

    public static Vehicle addVehicle(SimState state, int x, int y, double heading = 0, double fuel = FUEL_MAX, double health = VEHICLE_HEALTH, string kind = "car")
    {
        state.vehicles ??= [];
        var at = Motion.centre(new Point { x = Js.I(x), y = Js.I(y) });
        Vehicle v = new Vehicle { id = $"v{Js.Str(state.nextId++)}", at = at, was = Js.Clone(at), pos = new Point { x = Js.I(x), y = Js.I(y) }, heading = ((heading % HEADINGS) + HEADINGS) % HEADINGS, speed = 0, fuel = fuel, health = health, driverId = null, kind = kind };
        state.vehicles.push(v);
        return v;
    }
    public static Vehicle addVehicle(SimState state, double x, double y, double heading = 0, double fuel = FUEL_MAX, double health = VEHICLE_HEALTH, string kind = "car") => addVehicle(state, Js.I(x), Js.I(y), heading, fuel, health, kind);

    /// <summary>The car this survivor is driving, if any.</summary>
    public static Vehicle? vehicleOf(SimState state, Survivor s)
    {
        return vehiclesOf(state).find((v) => v.driverId == s.id);
    }

    /// <summary>The nearest car whose body is within reach of a point (SUB units), or undefined.</summary>
    public static Vehicle? vehicleNear(SimState state, Point at, double reach = ENTER_REACH)
    {
        Vehicle? best = null;
        double bestGap = double.PositiveInfinity;
        foreach (var v in Js.Iter(vehiclesOf(state)))
        {
            // A cheap look first: nothing of a car is further from its centre than half its length.
            double far = reach + shapeOf(v).halfLength;
            if (Motion.distance2(v.at, at) > far * far) continue;
            var g = gap(v, at);
            if ((g <= reach) && (g < bestGap))
            {
                best = v;
                bestGap = g;
            }
        }
        return best;
    }

    /// <summary>Use a car: fill it from a fuel can in your bag, or get in. Returns what happened, or why not.</summary>
    public static string useVehicle(SimState state, Survivor s, Vehicle v)
    {
        if (Js.T(v.driverId))
        {
            return "someone is driving it";
        }
        return fillFromCan(state, s, v) ?? enterVehicle(state, s, v);
    }

    /// <summary>A fuel can from your bag into the tank, if there's one and room. Returns what happened, or null.</summary>
    public static string? fillFromCan(SimState state, Survivor s, Vehicle v)
    {
        var can = s.pack.map((id) => Items.getItem(state, id)).find((i) => i?.kind == "fuel");
        if (Js.T(can) && (v.fuel <= (FUEL_MAX - ((double)(FUEL_PER_CAN) / (2)))))
        {
            v.fuel = JsMath.min(FUEL_MAX, v.fuel + FUEL_PER_CAN);
            Inventory.unpack(s, can.id);
            Items.removeItem(state, can);
            return "filled the tank";
        }
        return null;
    }

    /// <summary>In behind the wheel (the end of getting in: vehicleActions.ts).</summary>
    public static string enterVehicle(SimState state, Survivor s, Vehicle v)
    {
        if (Js.T(v.driverId))
        {
            return "someone is driving it";
        }
        v.driverId = s.id;
        s.move = null;
        s.at = Js.CopyAs<Point>(v.at);
        s.pos = Js.CopyAs<Point>(v.pos);
        return (v.health <= 0) ? "got in: it's a wreck, it won't go" : ((v.fuel <= 0) ? "got in: the tank is empty" : "got in");
    }

    /// <summary>Out of the car, by the driver's door if there's room there, else onto the nearest free tile clear of its body.</summary>
    public static string? leaveVehicle(SimState state, Survivor s)
    {
        var v = vehicleOf(state, s);
        if (!Js.T(v))
        {
            return null;
        }
        v.driverId = null;
        v.speed = 0;
        var beside = anchorAt(v, "driverDoor").at;
        var door = Motion.tileOf(beside);
        bool clear(int x, int y)
        {
            var c = Motion.centre(new Point { x = Js.I(x), y = Js.I(y) });
            return Motion.fits(state.grid, c, "free") && (gap(v, c) >= Motion.BODY_RADIUS);
        }
        // Right where the door is, if a body fits there; else the nearest tile clear of the car.
        if (Grids.inBounds(state.grid, door.x, door.y) && Motion.fits(state.grid, beside, "free"))
        {
            s.at = beside;
            s.pos = door;
            return "got out";
        }
        var spot = (Grids.inBounds(state.grid, door.x, door.y) ? Space.nearestTile(state.grid, door, clear) : null) ?? Space.nearestTile(state.grid, v.pos, clear) ?? v.pos;
        s.at = Motion.centre(spot);
        s.pos = spot;
        return "got out";
    }

    /// <summary>One tick behind the wheel: the held direction is the pedals and the wheel (control.ts dx, dy).</summary>
    public static void drive(SimState state, Survivor s, int dx, int dy)
    {
        var v = vehicleOf(state, s);
        if (!Js.T(v))
        {
            return;
        }
        var runs = (v.fuel > 0) && (v.health > 0);
        var top = topSpeed(state, v);
        if (Js.T(runs) && (dy < 0))
        {
            v.speed = (v.speed < 0) ? JsMath.min(0, v.speed + BRAKE) : JsMath.min(top, v.speed + ACCEL);
        }
        else if (Js.T(runs) && (dy > 0))
        {
            v.speed = (v.speed > 0) ? JsMath.max(0, v.speed - BRAKE) : JsMath.max(-REVERSE_MAX, v.speed - ACCEL);
        }
        else
        {
            v.speed = (v.speed > 0) ? JsMath.max(0, v.speed - FRICTION) : JsMath.min(0, v.speed + FRICTION);
        }
        if (v.speed > top)
        {
            v.speed = JsMath.max(top, v.speed - BRAKE);
        }
        if (((dx != 0) && (JsMath.abs(v.speed) >= TURN_MIN_SPEED)) && ((state.tick % TURN_EVERY) == 0))
        {
            turn(state, v, ((double)(dx) * (JsMath.sign(v.speed))));
        }
        if (Js.T(runs))
        {
            if (v.speed != 0)
            {
                v.fuel -= 1;
            }
            else if ((state.tick % IDLE_BURN) == 0)
            {
                v.fuel -= 1;
            }
            if (v.fuel <= 0)
            {
                Events.logEvent(state, $"{Js.Str(s.name)}'s car ran out of fuel");
            }
            if ((state.tick % ENGINE_NOISE_EVERY) == 0)
            {
                Noise.makeNoise(state, v.pos, "engine");
            }
        }
        if (v.speed != 0)
        {
            move(state, s, v);
        }
        s.at = Js.CopyAs<Point>(v.at);
        s.pos = Js.CopyAs<Point>(v.pos);
    }
    public static void drive(SimState state, Survivor s, double dx, double dy) => drive(state, s, Js.I(dx), Js.I(dy));

    public static void move(SimState state, Survivor s, Vehicle v)
    {
        var (cx, cy) = Js.NN(Js.Get(HEADING_DIRS, v.heading));
        Point want = new Point { x = Js.I(JsMath.trunc(((double)(((double)(cx) * (v.speed))) / (256)))), y = Js.I(JsMath.trunc(((double)(((double)(cy) * (v.speed))) / (256)))) };
        var to = advance(state, v, want.x, want.y);
        Point got = new Point { x = Js.I(to.x - v.at.x), y = Js.I(to.y - v.at.y) };
        v.at = to;
        v.pos = Motion.tileOf(to);
        var lost = ((double)(Motion.distance2(want, got)) * (9)) > (((double)(want.x) * (want.x)) + ((double)(want.y) * (want.y)));
        if (lost)
        {
            var fast = JsMath.abs(v.speed);
            if (fast > CRASH_SPEED)
            {
                dent(state, v, ((double)((fast - CRASH_SPEED)) * (CRASH_DAMAGE)));
                Events.logEvent(state, $"{Js.Str(s.name)} crashed the car");
            }
            v.speed = 0;
            return;
        }
        if (JsMath.abs(v.speed) < RAM_SPEED)
        {
            return;
        }
        double far = shapeOf(v).halfLength + Motion.BODY_RADIUS;
        foreach (var z in Js.Iter((state.zombies).ToList()))
        {
            // Anything of the body touching it (a cheap look at the centres first).
            if ((Motion.distance2(z.at, v.at) > far * far) || (gap(v, z.at) > Motion.BODY_RADIUS))
            {
                continue;
            }
            var killed = Zombies.damageZombie(state, z, ((double)(JsMath.abs(v.speed)) * (RAM_DAMAGE)), s);
            if (!killed)
            {
                Zombies.knockBack(state, z, v.at);
            }
            v.speed = JsMath.trunc(((double)(((double)(v.speed) * (3))) / (4)));
            dent(state, v, RAM_WEAR);
            if (JsMath.abs(v.speed) < RAM_SPEED)
            {
                break;
            }
        }
    }

    /// <summary>Top speed here: the truck is slower; off the road, slower still.</summary>
    public static double topSpeed(SimState state, Vehicle v)
    {
        var ground = Js.At(Grids.GROUNDS, Js.NN(Grids.groundAt(state.grid, ((double)(v.pos.y) * (state.grid.width)) + v.pos.x)), "grass");
        var @base = (v.kind == "truck") ? TRUCK_MAX : MAX_SPEED;
        return PAVED.includes(ground) ? @base : JsMath.min(@base, OFFROAD_MAX);
    }

    /// <summary>How much a vehicle's boot holds.</summary>
    public static double bootMax(Vehicle v)
    {
        return (v.kind == "truck") ? TRUCK_BOOT_MAX : BOOT_MAX;
    }

    public static void dent(SimState state, Vehicle v, double amount)
    {
        var was = v.health;
        v.health = JsMath.max(0, v.health - amount);
        if ((was > 0) && (v.health == 0))
        {
            Events.logEvent(state, $"The car at ({Js.Str(v.pos.x)}, {Js.Str(v.pos.y)}) is wrecked");
        }
    }

    /// <summary>A zombie's bite at someone in a car lands on the car. True if they were in one.</summary>
    public static bool batterVehicle(SimState state, Survivor s)
    {
        var v = vehicleOf(state, s);
        if (!Js.T(v))
        {
            return false;
        }
        dent(state, v, BATTER_DAMAGE);
        return true;
    }

    /// <summary>Every tick: blending positions, and drivers who are gone (dead, or no longer yours) get out.</summary>
    public static void updateVehicles(SimState state)
    {
        foreach (var v in Js.Iter(vehiclesOf(state)))
        {
            v.was = Js.CopyAs<Point>(v.at);
            if (!Js.T(v.driverId) || v.driverId.startsWith("x:"))
            {
                continue;
            }
            var s = state.survivors.find((x) => x.id == v.driverId);
            if (!Js.T(s))
            {
                v.driverId = null;
                v.speed = 0;
            }
            else if (state.control?.survivorId != s.id)
            {
                leaveVehicle(state, s);
            }
        }
    }

    /// <summary>A heading pointing along (dx, dy) (one of the four ways).</summary>
    public static double headingOf(int dx, int dy)
    {
        return (dx > 0) ? 0 : ((dy > 0) ? 8 : ((dx < 0) ? 16 : 24));
    }
    public static double headingOf(double dx, double dy) => headingOf(Js.I(dx), Js.I(dy));

    /// <summary>Can a car be parked with its centre on this tile, on this heading (a floor tile, and its whole body fits)?</summary>
    public static bool parkable(SimState state, Point p, double heading = 0, string kind = "car", Vehicle? self = null)
    {
        return (Grids.getTile(state.grid, p.x, p.y) == "floor") && fitsAt(state, shapeOf(kind), Motion.centre(p), ((heading % HEADINGS) + HEADINGS) % HEADINGS, self);
    }
}
