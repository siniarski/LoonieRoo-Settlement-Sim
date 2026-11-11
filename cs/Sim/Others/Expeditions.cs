// Ported from src/sim/expeditions.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Expeditions: survivors sent off the map to a point of interest.
//
// Sending takes them out of the colony (state.survivors) into an expedition
// record until it's due back: the walk there and back plus SEARCH_TICKS.
// On return (sim RNG), what the ruin's crates held comes back with them, up
// to CARRY each, rolled like scavenging; the crates keep the rest. Sleeping
// zombies near the ruin make it dangerous: members may come back hurt, or
// not at all. The ruin's sleepers are cleared either way. Abstract on
// purpose: nobody walks the route, which is what keeps far trips cheap.
//
// The truck, though, is seen on the road (when it's a vehicle: Outposts.cs): it
// drives there in a straight line, waits while they search, and drives back,
// and nobody else can take it meanwhile. Each hour on the road there's a
// chance the dead catch it (ROAD_AMBUSH): someone's hurt, or killed if they
// were hurt already; lose everyone and the truck stands where it stopped. An
// outpost's truck stays parked at the outpost.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Expeditions
{
    /// <summary>Someone who doesn't come back (killed at a ruin or an outpost): all they wore and carried is gone
    /// with them, and their bed is anyone's.</summary>
    public static void lose(SimState state, Survivor s)
    {
        Inventory.loseAll(state, s);
        foreach (var f in Js.Iter(FurnitureMod.ofKind(state, "bed")))
        {
            if (f.owner == s.id)
            {
                f.owner = null;
            }
        }
    }


    public const int SEARCH_TICKS = 400;

    public const int CARRY = 4;

    /// <summary>Crates and sleepers within this of the point of interest count as the ruin's.</summary>
    public const int RUIN_RADIUS = 10;

    public const double INJURY_CHANCE = 0.3;

    public const int INJURY = 300;

    /// <summary>Chance an hour on the road that the dead catch the truck.</summary>
    public const double ROAD_AMBUSH = 0.03;

    public static string? sendExpedition(SimState state, List<Survivor> members, string poiId, bool outpost = false)
    {
        var poi = state.pointsOfInterest.find((p) => p.id == poiId);
        if (!Js.T(poi))
        {
            return $"no point of interest {Js.Str(poiId)}";
        }
        if (members.length == 0)
        {
            return "nobody to send";
        }
        var home = Js.Clone(Js.NN(Js.Get(members, 0)).pos);
        var distance = Space.manhattan(home, poi);
        foreach (var s in Js.Iter(members))
        {
            state.survivors.splice(state.survivors.indexOf(s), 1);
        }
        var truckId = Outposts.truckVehicleId(state);
        var inTruck = Outposts.takeTruck(state);
        var id = $"x{Js.Str(state.nextId)}";
        var v = (Js.T(inTruck && Js.T(truckId))) ? Vehicles.vehiclesOf(state).find((q) => q.id == truckId) : null;
        if (Js.T(v))
        {
            Vehicles.unpark(state, v); // off with the expedition: it's no longer standing in anyone's way
            v.driverId = $"x:{Js.Str(id)}";
        }
        state.expeditions.push(Js.With(new Expedition { id = $"x{Js.Str(state.nextId++)}", members = members, target = new() { x = Js.I(poi.x), y = Js.I(poi.y), label = poi.label }, home = home, returnTick = Js.T(outpost) ? Outposts.NEVER : ((state.tick + ((double)(((double)((inTruck ? 1 : 2)) * (distance))) * (Survivors.TICKS_PER_TILE))) + SEARCH_TICKS), truck = inTruck, outpost = outpost }, __s0 => { if (Js.T(v)) { __s0.truckId = v.id; __s0.leftTick = state.tick; } else {  } }));
        Events.logEvent(state, $"{Js.Str(members.map((s) => s.name).join(", "))} set out{Js.Str(inTruck ? Js.Str(" in the truck") : Js.Str(""))} for the {Js.Str(poi.label)} at ({Js.Str(poi.x)}, {Js.Str(poi.y)}){Js.Str(Js.T(outpost) ? Js.Str(" to hold it as an outpost") : Js.Str(""))}");
        return null;
    }

    /// <summary>Brings back any expedition that's due.</summary>
    public static void updateExpeditions(SimState state)
    {
        foreach (var x in Js.Iter((state.expeditions).ToList()))
        {
            if (Js.T(x.truckId) && onTheRoad(state, x))
            {
                continue;
            }
            if (state.tick >= x.returnTick)
            {
                returnHome(state, x);
            }
        }
    }

    /// <summary>The truck on its way: where it is along the road (out, waiting at the ruin, back), and the dead on the way. Returns true if the expedition ended on the road.</summary>
    public static bool onTheRoad(SimState state, Expedition x)
    {
        var v = Vehicles.vehiclesOf(state).find((q) => q.id == x.truckId);
        if (!Js.T(v) || (x.leftTick == null))
        {
            return false;
        }
        var leg = JsMath.max(1, JsMath.floor(((double)(((double)(Space.manhattan(x.home, x.target)) * (Survivors.TICKS_PER_TILE))) / (2))));
        var e = state.tick - x.leftTick;
        var driving = (e < leg) || (!x.outpost && (e >= (leg + SEARCH_TICKS)));
        var f = (e < leg) ? ((double)(e) / (leg)) : ((x.outpost || (e < (leg + SEARCH_TICKS))) ? 1 : JsMath.max(0, 1 - ((double)(((e - leg) - SEARCH_TICKS)) / (leg))));
        Point p = new Point { x = Js.I(JsMath.round(x.home.x + ((double)((x.target.x - x.home.x)) * (f)))), y = Js.I(JsMath.round(x.home.y + ((double)((x.target.y - x.home.y)) * (f)))) };
        v.pos = p;
        v.at = Motion.centre(p);
        if (x.outpost && (e >= leg))
        {
            v.driverId = null;
            x.truckId = null;
            Vehicles.park(state, v);
            return false;
        }
        if (!Js.T(driving) || ((state.tick % Time.TICKS_PER_HOUR) != 0))
        {
            return false;
        }
        if (Hash.hashRoll(Hash.idNumber(x.id), state.tick, 0x70ad) >= ((double)(((double)(ROAD_AMBUSH) * (state.settings.zombies))) / (100)))
        {
            return false;
        }
        var s = Js.NN(Js.Get(x.members, JsMath.floor(((double)(Hash.hashRoll(Hash.idNumber(x.id), state.tick, 1)) * (x.members.length)))));
        if (s.health > (Health.HEALTH_MAX - INJURY))
        {
            s.health -= INJURY;
            Events.logEvent(state, $"The dead caught the truck on the road near ({Js.Str(p.x)}, {Js.Str(p.y)}): {Js.Str(s.name)} was hurt");
            return false;
        }
        x.members.splice(x.members.indexOf(s), 1);
        Mood.mournDeath(state, s);
        Events.logEvent(state, $"The dead caught the truck on the road near ({Js.Str(p.x)}, {Js.Str(p.y)}): {Js.Str(s.name)} was killed");
        if (x.members.length > 0)
        {
            return false;
        }
        state.expeditions.splice(state.expeditions.indexOf(x), 1);
        v.driverId = null;
        Vehicles.park(state, v);
        Events.logEvent(state, $"Nobody from the expedition to the {Js.Str(x.target.label)} is coming back. The truck stands empty near ({Js.Str(p.x)}, {Js.Str(p.y)})");
        return true;
    }

    public static void returnHome(SimState state, Expedition x)
    {
        state.expeditions.splice(state.expeditions.indexOf(x), 1);
        var v = Js.T(x.truckId) ? Vehicles.vehiclesOf(state).find((q) => q.id == x.truckId) : null;
        if (Js.T(v))
        {
            var spot = Vehicles.parkable(state, x.home, v.heading, v.kind, v) ? x.home : (Space.nearestTile(state.grid, x.home, (px, py) => Vehicles.parkable(state, new Point { x = Js.I(px), y = Js.I(py) }, v.heading, v.kind, v), 400) ?? x.home);
            v.pos = Js.CopyAs<Point>(spot);
            v.at = Motion.centre(spot);
            v.was = Js.CopyAs<Point>(v.at);
            v.driverId = null;
            Vehicles.park(state, v);
        }
        bool near(Point p)
        {
            return Space.manhattan(p, x.target) <= RUIN_RADIUS;
        }
        var sleepers = state.dormant.filter((__f0) => near(__f0));
        state.dormant = state.dormant.filter((d) => !near(d));
        var deathChance = JsMath.min(0.5, 0.05 + ((double)(((double)(0.08) * (sleepers.length))) / (x.members.length)));
        List<Survivor> back = [];
        List<Survivor> lost = [];
        List<Survivor> hurt = [];
        foreach (var s in Js.Iter(x.members))
        {
            if (Rng.random(state) < deathChance)
            {
                lost.push(s);
                lose(state, s);
                continue;
            }
            if ((sleepers.length > 0) && (Rng.random(state) < INJURY_CHANCE))
            {
                s.health = JsMath.max(1, s.health - INJURY);
                hurt.push(s);
            }
            s.health = JsMath.min(Health.HEALTH_MAX, s.health);
            s.pos = Js.CopyAs<Point>(x.home);
            s.note = $"back from the {Js.Str(x.target.label)}";
            back.push(s);
        }
        state.survivors.pushAll(back);
        double room = ((double)(((double)(CARRY) * (back.length))) * ((x.truck ? 2 : 1)));
        double found = 0;
        foreach (var crate in Js.Iter(FurnitureMod.ofKind(state, "crate").filter((f) => (f.loot > 0) && near(f))))
        {
            while ((crate.loot > 0) && (room > 0))
            {
                crate.loot -= 1;
                room -= 1;
                found += 1;
                Difficulty.spawnFind(state, Work.scavengeFind(Rng.random(state)), x.home.x, x.home.y);
            }
            if (crate.loot == 0)
            {
                FurnitureMod.setScavenge(state, crate, false);
            }
        }
        state.pointsOfInterest = state.pointsOfInterest.filter((p) => !((p.x == x.target.x) && (p.y == x.target.y)));
        foreach (var s in Js.Iter(lost))
        {
            Mood.mournDeath(state, s);
        }
        List<string> parts = [$"brought back {Js.Str(found)} find{Js.Str((found == 1) ? Js.Str("") : Js.Str("s"))}"];
        if (Js.T(hurt.length))
        {
            parts.push($"{Js.Str(hurt.map((s) => s.name).join(", "))} hurt");
        }
        if (Js.T(lost.length))
        {
            parts.push($"{Js.Str(lost.map((s) => s.name).join(", "))} didn't come back");
        }
        if ((x.target.label == Radio.DISTRESS_LABEL) && (back.length > 0))
        {
            var n = 1 + JsMath.floor(((double)(Hash.hashRoll(Hash.idNumber(x.id), 0xd15)) * (2)));
            List<string> names = [];
            for (var k = 0; k < n; k++)
            {
                var s = Survivors.addSurvivor(state, x.home.x, x.home.y);
                s.traits = Skills.rollTraits(s.id);
                names.push(s.name);
            }
            parts.push($"and brought in {Js.Str(names.join(" and "))}");
        }
        if ((back.length > 0) && (Hash.hashRoll(Hash.idNumber(x.id), 0xd09) < Dogs.EXPEDITION_DOG_CHANCE))
        {
            var dog = Dogs.addDog(state, x.home.x, x.home.y);
            parts.push($"and a dog, {Js.Str(dog.name)}, followed them home");
        }
        Events.logEvent(state, $"The expedition to the {Js.Str(x.target.label)} is back: {Js.Str(parts.join("; "))}");
    }

    /// <summary>Everyone in the colony or away on an expedition (their ids stay in use).</summary>
    public static List<Survivor> everyone(SimState state)
    {
        return (List<Survivor>)[.. state.survivors, .. state.expeditions.flatMap((x) => x.members)];
    }
}
