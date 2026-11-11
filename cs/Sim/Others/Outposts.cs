// Ported from src/sim/outposts.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Vehicles and outposts.
//
// The truck: a generated world leaves one somewhere out past the camp
// (worldgen): a vehicle you can drive too (Vehicles.cs). Once someone has seen
// it, expeditions take it when it has half a tank (TRIP_FUEL), or there's
// TRUCK_FUEL free fuel to burn: there and back in half the time, carrying
// twice as much (Expeditions.cs). (Hand-made maps can have a truck as furniture.)
//
// Outposts: an expedition can stay where it went (claimOutpost). Each dawn
// an outpost sends home OUTPOST_FINDS per member (rolled like scavenging, by
// hash) and runs a risk (OUTPOST_DANGER a day, by hash) that someone there is
// hurt, or killed if they were hurt already. Recalling one sets it on its way
// home like any expedition.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Outposts
{

    public const int TRUCK_FUEL = 2;

    public const int OUTPOST_FINDS = 1;

    public const double OUTPOST_DANGER = 0.15;

    public const int OUTPOST_HURT = 350;

    /// <summary>An outpost isn't due back until it's recalled.</summary>
    public static readonly double NEVER = JsNumber.MAX_SAFE_INTEGER;

    /// <summary>The colony's truck, if anyone's seen it.</summary>
    public static Point? truck(SimState state)
    {
        var v = truckVehicle(state);
        if (Js.T(v))
        {
            return v.pos;
        }
        return FurnitureMod.ofKind(state, "truck").find((f) => Fog.isExplored(state, f.x, f.y));
    }

    /// <summary>The truck as a vehicle, seen and parked (nobody driving it).</summary>
    public static Vehicle? truckVehicle(SimState state)
    {
        return Vehicles.vehiclesOf(state).find((v) => (((v.kind == "truck") && (v.health > 0)) && (v.driverId == null)) && Fog.isExplored(state, v.pos.x, v.pos.y));
    }

    /// <summary>The truck vehicle a trip would take now (seen, parked), if there is one.</summary>
    public static string? truckVehicleId(SimState state)
    {
        return truckVehicle(state)?.id;
    }

    /// <summary>Fuel a trip takes from the truck's own tank.</summary>
    public const int TRIP_FUEL = 1500;

    /// <summary>Takes the truck (and its fuel) for a trip, if it can go.</summary>
    public static bool takeTruck(SimState state)
    {
        if (!Js.T(truck(state)))
        {
            return false;
        }
        var v = truckVehicle(state);
        if (Js.T(v) && (v.fuel >= TRIP_FUEL))
        {
            v.fuel -= TRIP_FUEL;
            return true;
        }
        var fuel = state.items.filter((i) => ((i.kind == "fuel") && (i.pos != null)) && (i.reservedBy == null));
        if (fuel.length < TRUCK_FUEL)
        {
            return false;
        }
        foreach (var f in Js.Iter(fuel.slice(0, TRUCK_FUEL)))
        {
            state.items.splice(state.items.indexOf(f), 1);
        }
        return true;
    }

    public static void updateOutposts(SimState state)
    {
        if (((state.tick % Time.DAY_LENGTH) != 0) || (state.tick == 0))
        {
            return;
        }
        foreach (var x in Js.Iter(state.expeditions))
        {
            if (!x.outpost)
            {
                continue;
            }
            for (var k = 0; k < ((double)(x.members.length) * (OUTPOST_FINDS)); k++)
            {
                Difficulty.spawnFind(state, Work.scavengeFind(Hash.hashRoll(Hash.idNumber(x.id), state.tick, k)), x.home.x, x.home.y);
            }
            if ((Hash.hashRoll(Hash.idNumber(x.id), state.tick, 0xd4) >= OUTPOST_DANGER) || (x.members.length == 0))
            {
                continue;
            }
            var s = Js.NN(Js.Get(x.members, JsMath.floor(((double)(Hash.hashRoll(Hash.idNumber(x.id), state.tick, 1)) * (x.members.length)))));
            if (s.health > (Health.HEALTH_MAX - OUTPOST_HURT))
            {
                s.health -= OUTPOST_HURT;
                Events.logEvent(state, $"Trouble at the {Js.Str(x.target.label)} outpost: {Js.Str(s.name)} was hurt");
                continue;
            }
            x.members.splice(x.members.indexOf(s), 1);
            foreach (var id in Js.Iter((List<string>)[s.weapon, s.body, s.feet, s.cart, s.tool, s.carrying]))
            {
                var item = Js.T(id) ? state.items.find((i) => i.id == id) : null;
                if (Js.T(item))
                {
                    state.items.splice(state.items.indexOf(item), 1);
                }
            }
            foreach (var f in Js.Iter(FurnitureMod.ofKind(state, "bed")))
            {
                if (f.owner == s.id)
                {
                    f.owner = null;
                }
            }
            Events.logEvent(state, $"{Js.Str(s.name)} was killed at the {Js.Str(x.target.label)} outpost");
            Mood.mournDeath(state, s);
            if (x.members.length == 0)
            {
                state.expeditions.splice(state.expeditions.indexOf(x), 1);
            }
        }
        foreach (var x in Js.Iter(state.expeditions))
        {
            if (x.outpost)
            {
                foreach (var s in Js.Iter(x.members))
                {
                    s.health = JsMath.min(Health.HEALTH_MAX, s.health + 100);
                }
            }
        }
    }

    /// <summary>Recall an outpost: they set off home. Returns an error, or null.</summary>
    public static string? recallOutpost(SimState state, string id)
    {
        var x = state.expeditions.find((e) => e.id == id);
        if (!Js.T(x?.outpost))
        {
            return "no such outpost";
        }
        x.outpost = false;
        x.returnTick = state.tick + ((double)(Space.manhattan(x.home, x.target)) * (Survivors.TICKS_PER_TILE));
        Events.logEvent(state, $"The {Js.Str(x.target.label)} outpost is packing up and heading home");
        return null;
    }
}
